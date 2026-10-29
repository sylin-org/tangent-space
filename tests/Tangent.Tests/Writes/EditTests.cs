using System.Net;
using Tangent.Events;
using Tangent.Writer;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Posts;

/// <summary>Editing a Post: the author edits their own, a moderator anyone's; each edit is one commit holding the pre-edit
/// snapshot in Koan's audit partition, the raised version and <c>post.edited</c>; an edit made from another version is
/// a conflict that carries the current text and writes nothing.</summary>
public sealed class EditTests
{
    [Fact]
    public async Task The_author_edits_their_own_post_by_form_and_a_moderator_edits_anyones_by_json_each_in_one_commit_with_its_snapshot_version_and_event()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        var root = await Started(writer, Ada, "Lanterns", "How do you hang yours?");
        var reply = await Replied(writer, root, Ada, "From the porch beam.");
        using var ada = await SignedIn(tangentSpace, Ada);
        using var mo = await SignedIn(tangentSpace, Mo);
        var commits = new List<Committed>();
        writer.Committed += commits.Add;

        var page = await Send(ada.Client, HttpMethod.Get, PostPath(root, root) + "/edit");
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains("<input type=\"hidden\" name=\"version\" value=\"1\" />", page.Body);
        var byForm = await Form(ada, PostPath(root, root) + "/edit", PostPath(root, root) + "/edit",
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
        // One commit carried the snapshot, the row and the event: the writer announced it once, with the event's sequence.
        Assert.Equal(editedEvent.Seq, Assert.Single(commits).Sequences[Event.Log]);
        Assert.IsType<EditPost>(commits[0].Command);

        var byJson = await Json(mo, PostPath(root, reply) + "/edit", PostPath(root, reply) + "/edit",
            new { body = "From the porch beam, on a hook.", version = 1 });

        Assert.Equal((HttpStatusCode.OK, AddressOf(root, reply)), (byJson.Status, byJson.Location));
        Assert.Equal((reply.Id, AddressOf(root, reply), "From the porch beam, on a hook.", 2L, Ada),
            (byJson.Json.GetProperty("id").GetString(), byJson.Json.GetProperty("address").GetString(), byJson.Json.GetProperty("body").GetString(),
                byJson.Json.GetProperty("version").GetInt64(), byJson.Json.GetProperty("authorId").GetString()));
        Assert.Contains("Moderate", byJson.Json.GetProperty("can").EnumerateArray().Select(verb => verb.GetString()));
        Assert.Equal("From the porch beam.", (await AuditPartition.At<Post>(reply.Id, 1, Cancellation))?.Body);
        var moEdit = Assert.Single(await Event.Query(e => e.Kind == EventKind.PostEdited && e.PostId == reply.Id, Cancellation));
        Assert.Equal((Mo, 2L), (moEdit.ActorId, moEdit.Version));
        Assert.Equal(2, commits.Count);
    }

    [Fact]
    public async Task An_edit_from_a_stale_version_is_a_conflict_carrying_the_current_text_as_json_and_on_the_form_and_writes_nothing_while_a_repeat_writes_nothing_and_answers_the_post()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        var root = await Started(writer, Ada, "Lanterns", "How do you hang yours?");
        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new EditPost(root.Id, 1, "Hung from the beam, says the moderator.", null, Mo), Cancellation));
        var before = (await Counted(), await Snapshots());
        using var ada = await SignedIn(tangentSpace, Ada);
        var edit = PostPath(root, root) + "/edit";

        var asJson = await Json(ada, edit, edit, new { title = "Lanterns", body = "Mine hang from the porch.", version = 1 });
        var onForm = await Form(ada, edit, edit, new Dictionary<string, string> { ["title"] = "Lanterns", ["body"] = "Mine hang from the porch.", ["version"] = "1" });
        var repeat = await writer.Send(new EditPost(root.Id, 1, "Hung from the beam, says the moderator.", null, Mo), Cancellation);

        Assert.Equal((HttpStatusCode.Conflict, "application/problem+json"), (asJson.Status, asJson.MediaType));
        var current = asJson.Json.GetProperty("current");
        Assert.Equal(("Hung from the beam, says the moderator.", "Lanterns", 2L),
            (current.GetProperty("body").GetString(), current.GetProperty("title").GetString(), current.GetProperty("version").GetInt64()));
        Assert.Equal(EditPost.Stale(2), asJson.Json.GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.Conflict, onForm.Status);
        Assert.Contains(">Hung from the beam, says the moderator.</textarea>", onForm.Body);
        Assert.Contains("<input type=\"hidden\" name=\"version\" value=\"2\" />", onForm.Body);
        Assert.Contains("readonly>Mine hang from the porch.</textarea>", onForm.Body);
        Assert.Equal(2L, Assert.IsType<Outcome<Post>.Done>(repeat).Result.Version);
        Assert.Equal(before, (await Counted(), await Snapshots()));
        Assert.Equal(("Hung from the beam, says the moderator.", 2L), ((await Post.Get(root.Id, Cancellation))?.Body, (await Post.Get(root.Id, Cancellation))?.Version));
        Assert.Null(await AuditPartition.At<Post>(root.Id, 2, Cancellation));
    }
}
