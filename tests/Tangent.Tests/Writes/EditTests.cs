using System.Net;
using Koan.Data.Core.Commands;
using Koan.Identity.Roles;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Capture;
using Tangent.Events;
using Tangent.Space;
using Tangent.Tangents;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Posts;

/// <summary>Editing a Post, a change of it at its address: the author edits their own, a moderator anyone's; each edit is one
/// commit holding the pre-edit snapshot in Koan's audit partition, the raised version and <c>post.edited</c>; an edit made from
/// another version is refused as stale, naming the version the Post is at, its form read again with the current text, and writes
/// nothing.</summary>
public sealed class EditTests
{
    [Fact]
    public async Task The_author_edits_their_own_post_by_form_and_a_moderator_edits_anyones_by_json_each_with_its_snapshot_version_and_event()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var root = await Started(Ada, "Lanterns", "How do you hang yours?");
        var reply = await Replied(root, Ada, "From the porch beam.");
        using var ada = await SignedIn(tangentSpace, Ada);
        using var mo = await SignedIn(tangentSpace, Mo);

        var page = await Send(ada.Client, HttpMethod.Get, PostPath(root, root) + "/edit");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains("<input type=\"hidden\" name=\"version\" value=\"1\" />", page.Body);
        Assert.Contains("<input type=\"hidden\" name=\"_method\" value=\"PATCH\" />", page.Body);
        var byForm = await Change(ada, PostPath(root, root) + "/edit", PostPath(root, root),
            new Dictionary<string, string> { ["title"] = "Lanterns, again", ["body"] = "How do you hang yours, and where?", ["version"] = "1" });

        Assert.Equal((HttpStatusCode.SeeOther, AddressOf(root, root)), (byForm.Status, byForm.Location));
        var edited = await Post.Get(root.Id, Cancellation);
        Assert.Equal(("Lanterns, again", "How do you hang yours, and where?", 2L), (edited?.Title, edited?.Body, edited?.Version));
        var snapshot = await AuditPartition.At<Post>(root.Id, 1, Cancellation);
        Assert.NotNull(snapshot);
        Assert.Equal((AuditPartition.Key(root.Id, 1), "Lanterns", "How do you hang yours?", 1L, Ada),
            (snapshot.Id, snapshot.Title, snapshot.Body, snapshot.Version, snapshot.AuthorId));
        var editedEvent = Assert.Single(await Event.Query(e => e.Kind == EventKind.PostEdited, Cancellation));
        Assert.Equal((root.Id, root.Id, Workshop, Ada, 2L), (editedEvent.PostId, editedEvent.TopicId, editedEvent.TangentId, editedEvent.ActorId, editedEvent.Version));

        var byJson = await Patch(mo, PostPath(root, reply) + "/edit", PostPath(root, reply),
            new { body = "From the porch beam, on a hook.", version = 1 });

        Assert.Equal((HttpStatusCode.OK, AddressOf(root, reply)), (byJson.Status, byJson.Location));
        Assert.Equal((reply.Id, AddressOf(root, reply), "From the porch beam, on a hook.", 2L, Ada),
            (byJson.Json.GetProperty("id").GetString(), byJson.Json.GetProperty("address").GetString(), byJson.Json.GetProperty("body").GetString(),
                byJson.Json.GetProperty("version").GetInt64(), byJson.Json.GetProperty("authorId").GetString()));
        Assert.Contains("Moderate", byJson.Json.GetProperty("can").EnumerateArray().Select(verb => verb.GetString()));
        Assert.Equal("From the porch beam.", (await AuditPartition.At<Post>(reply.Id, 1, Cancellation))?.Body);
        var moEdit = Assert.Single(await Event.Query(e => e.Kind == EventKind.PostEdited && e.PostId == reply.Id, Cancellation));
        Assert.Equal((Mo, 2L), (moEdit.ActorId, moEdit.Version));
    }

    [Fact]
    public async Task A_member_whom_an_overwrite_grants_moderate_in_a_tangent_edits_and_removes_anyones_post_there_and_nowhere_else()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await tangentSpace.Services.GetRequiredService<RoleCollection>().Overwrite(Overwrites.Resource(Workshop), OverwriteSubjectKind.Person,
            Bea, [Bits.Token(Bit.Moderate)], [], ct: Cancellation);
        var root = await Started(Ada, "Lanterns", "How do you hang yours?");
        var reply = await Replied(root, Ada, "From the porch beam.");
        var elsewhere = await Started(Ada, "On wicks", "A short piece.", Articles);

        using var bea = await SignedIn(tangentSpace, Bea);

        var edited = await Patch(bea, PostPath(root, root), PostPath(root, root), new { body = "How do you hang yours? (moderated)", version = 1 });
        var removed = await new RemovePost(reply.Id, Bea).Send(Cancellation);
        var notThere = await Patch(bea, PostPath(elsewhere, elsewhere), PostPath(elsewhere, elsewhere), new { body = "Not mine to change.", version = 1 });
        var notThereEither = await new RemovePost(elsewhere.Id, Bea).Send(Cancellation);

        Assert.Equal((HttpStatusCode.OK, 2L, Bea), (edited.Status, edited.Json.GetProperty("version").GetInt64(), edited.Json.GetProperty("editedBy").GetString()));
        Assert.Equal((true, Bea), (Assert.IsType<Outcome<Post>.Done>(removed).Result.Removed, Assert.IsType<Outcome<Post>.Done>(removed).Result.RemovedBy));
        Assert.Equal("How do you hang yours?", (await AuditPartition.At<Post>(root.Id, 1, Cancellation))?.Body);
        Assert.Equal("From the porch beam.", (await AuditPartition.At<Post>(reply.Id, 1, Cancellation))?.Body);
        Assert.Equal((HttpStatusCode.Forbidden, Capabilities.NotYours.Word), (notThere.Status, notThere.Word));
        Assert.Equal(Capabilities.NotYours, Assert.IsType<Outcome<Post>.Refused>(notThereEither).Reason);
        Assert.Equal(("A short piece.", 1L, false), ((await Post.Get(elsewhere.Id, Cancellation))?.Body, (await Post.Get(elsewhere.Id, Cancellation))?.Version,
            (await Post.Get(elsewhere.Id, Cancellation))?.Removed));
    }

    [Fact]
    public async Task An_edit_from_a_stale_version_is_refused_as_stale_naming_the_current_version_its_form_holding_the_current_text_and_writes_nothing_while_a_repeat_writes_nothing_and_answers_the_post()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var root = await Started(Ada, "Lanterns", "How do you hang yours?");
        Assert.IsType<Outcome<Post>.Done>(await new Edited(root.Id, 1, "Hung from the beam, says the moderator.", null, Mo).Send(Cancellation));
        var before = (await Counted(), await Snapshots());
        using var ada = await SignedIn(tangentSpace, Ada);
        var edit = PostPath(root, root) + "/edit";

        var asJson = await Patch(ada, edit, PostPath(root, root), new { title = "Lanterns", body = "Mine hang from the porch.", version = 1 });
        var onForm = await Change(ada, edit, PostPath(root, root), new Dictionary<string, string> { ["title"] = "Lanterns", ["body"] = "Mine hang from the porch.", ["version"] = "1" });
        var repeat = await new Edited(root.Id, 1, "Hung from the beam, says the moderator.", null, Mo).Send(Cancellation);

        Assert.Equal((HttpStatusCode.Conflict, "application/problem+json", Post.StaleWord), (asJson.Status, asJson.MediaType, asJson.Word));
        Assert.Equal(Post.Stale(2).Text, asJson.Json.GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.Conflict, onForm.Status);
        Assert.Contains(">Hung from the beam, says the moderator.</textarea>", onForm.Body);
        Assert.Contains("<input type=\"hidden\" name=\"version\" value=\"2\" />", onForm.Body);
        Assert.Matches("<textarea [^>]*readonly[^>]*>Mine hang from the porch.</textarea>", onForm.Body);
        Assert.Equal(2L, Assert.IsType<Outcome<Post>.Done>(repeat).Result.Version);
        Assert.Equal(before, (await Counted(), await Snapshots()));
        Assert.Equal(("Hung from the beam, says the moderator.", 2L), ((await Post.Get(root.Id, Cancellation))?.Body, (await Post.Get(root.Id, Cancellation))?.Version));
        Assert.Null(await AuditPartition.At<Post>(root.Id, 2, Cancellation));
    }
}
