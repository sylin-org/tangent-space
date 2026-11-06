using System.Net;
using Koan.Data.Core.Commands;
using Koan.Identity.Roles;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Events;
using Tangent.Tangents;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Posts;

/// <summary>Removing a Post: the author removes their own, a moderator anyone's; the row stays in its place as a
/// tombstone without its body, which the snapshot keeps; <c>post.removed</c> is appended; removing again answers the same
/// tombstone; a removed root leaves its replies and closes its Topic; each Post offers its removal in its footer to whoever
/// may remove it.</summary>
public sealed class RemoveTests
{
    [Fact]
    public async Task The_author_removes_by_form_and_a_moderator_by_json_each_leaving_a_tombstone_in_place_its_body_in_the_snapshot_and_post_removed()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var root = await Started(Ada, "Lanterns", "How do you hang yours?");
        var own = await Replied(root, Ada, "From the porch beam.");
        var bea = await Replied(root, Bea, "From a hook by the door.");
        using var ada = await SignedIn(tangentSpace, Ada);
        using var mo = await SignedIn(tangentSpace, Mo);

        var page = await Send(ada.Client, HttpMethod.Get, PostPath(root, own) + "/edit");
        var byForm = await Form(ada, PostPath(root, own) + "/edit", PostPath(root, own) + "/remove", new Dictionary<string, string>());

        Assert.Contains("<details class=\"post-remove remove\">", page.Body);
        Assert.Contains("action=\"" + AddressOf(root, own) + "/remove\"", page.Body);
        Assert.Equal((HttpStatusCode.SeeOther, AddressOf(root, own)), (byForm.Status, byForm.Location));
        var tombstone = await Post.Get(own.Id, Cancellation);
        Assert.NotNull(tombstone);
        Assert.Equal((true, Ada, "", 1L, root.Id, 1L), (tombstone.Removed, tombstone.RemovedBy, tombstone.Body, tombstone.Sequence, tombstone.ParentId, tombstone.Version));
        Assert.NotNull(tombstone.RemovedAt);
        Assert.Equal(("From the porch beam.", false), ((await AuditPartition.At<Post>(own.Id, 1, Cancellation))?.Body, (await AuditPartition.At<Post>(own.Id, 1, Cancellation))?.Removed));
        var removed = Assert.Single(await Event.Query(e => e.Kind == EventKind.PostRemoved, Cancellation));
        Assert.Equal((own.Id, root.Id, Ada, 1L), (removed.PostId, removed.TopicId, removed.ActorId, removed.Version));
        Assert.Equal(2, (await Post.Get(root.Id, Cancellation))?.ReplyCount);

        var byJson = await Json(mo, PostPath(root, bea) + "/edit", PostPath(root, bea) + "/remove", new { });

        Assert.Equal((HttpStatusCode.OK, AddressOf(root, bea)), (byJson.Status, byJson.Location));
        Assert.Equal((true, Mo, "", "RemoveOwn"), (byJson.Json.GetProperty("removed").GetBoolean(), byJson.Json.GetProperty("removedBy").GetString(),
            byJson.Json.GetProperty("body").GetString(), string.Join(",", byJson.Json.GetProperty("can").EnumerateArray().Select(bit => bit.GetString()))));
        Assert.Equal("From a hook by the door.", (await AuditPartition.At<Post>(bea.Id, 1, Cancellation))?.Body);
        Assert.Equal(2, (await Event.Query(e => e.Kind == EventKind.PostRemoved, Cancellation)).Count);
    }

    [Fact]
    public async Task Removing_twice_answers_the_same_tombstone_to_anyone_who_may_remove_it_and_writes_nothing_and_a_removed_root_leaves_its_replies()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var root = await Started(Ada, "Lanterns", "How do you hang yours?");
        var reply = await Replied(root, Bea, "From a hook by the door.");

        var first = Assert.IsType<Outcome<Post>.Done>(await new RemovePost(root.Id, Ada).Send(Cancellation)).Result;
        var counted = (await Counted(), await Snapshots());
        var again = Assert.IsType<Outcome<Post>.Done>(await new RemovePost(root.Id, Ada).Send(Cancellation)).Result;
        var byModerator = Assert.IsType<Outcome<Post>.Done>(await new RemovePost(root.Id, Mo).Send(Cancellation)).Result;
        var bySomeoneElse = await new RemovePost(root.Id, Bea).Send(Cancellation);
        var answer = await new Reply(Tids.Mint(), root.Id, "Still here?", Bea).Send(Cancellation);

        Assert.Equal((first.Id, first.RemovedAt, Ada, first.Body), (again.Id, again.RemovedAt, again.RemovedBy, again.Body));
        Assert.Equal((first.Id, first.RemovedAt, Ada, first.Body), (byModerator.Id, byModerator.RemovedAt, byModerator.RemovedBy, byModerator.Body));
        Assert.Equal(Capabilities.Removed, Assert.IsType<Outcome<Post>.Refused>(bySomeoneElse).Reason);
        Assert.Equal(Capabilities.Closed, Assert.IsType<Outcome<Post>.Refused>(answer).Reason);
        Assert.Equal(counted, (await Counted(), await Snapshots()));
        var stored = await Post.Get(root.Id, Cancellation);
        Assert.Equal((true, "", "Lanterns", "lanterns", 1, reply.Id), (stored?.Removed, stored?.Body, stored?.Title, stored?.Slug, stored?.ReplyCount, stored?.LatestReplyId));
        var kept = await Post.Get(reply.Id, Cancellation);
        Assert.Equal((false, "From a hook by the door.", root.Id), (kept?.Removed, kept?.Body, kept?.TopicId));
        Assert.Single(await Event.Query(e => e.Kind == EventKind.PostRemoved, Cancellation));
    }

    [Fact]
    public async Task Remove_stands_in_the_footer_of_each_post_the_reader_may_remove_without_its_edit_and_removing_a_root_says_and_does_close_its_topic()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await tangentSpace.Services.GetRequiredService<RoleCollection>().Overwrite(Overwrites.Resource(Workshop), OverwriteSubjectKind.Person,
            Bea, [], [Bits.Token(Bit.EditOwn)], ct: Cancellation);
        var root = await Started(Ada, "Lanterns", "How do you hang yours?");
        var own = await Replied(root, Bea, "From a hook by the door.");
        using var bea = await SignedIn(tangentSpace, Bea);
        using var mo = await SignedIn(tangentSpace, Mo);

        var asBea = (await Send(bea.Client, HttpMethod.Get, TopicPath(root))).Body;
        var asMo = WebUtility.HtmlDecode((await Send(mo.Client, HttpMethod.Get, TopicPath(root))).Body);

        // Bea may remove her reply and not edit it: its footer offers the one and not the other, and Ada's root neither.
        Assert.Contains("<details class=\"post-remove remove\">", Shown(asBea, own));
        Assert.Contains($"action=\"{AddressOf(root, own)}/remove\"", Shown(asBea, own));
        Assert.DoesNotContain("/edit\"", Shown(asBea, own));
        Assert.DoesNotContain("/remove\"", Shown(asBea, root));
        // A moderator may remove both; the root's confirm says its Topic then takes no reply, the reply's does not.
        Assert.Contains("Its words leave the page and a tombstone stays in their place; the topic keeps its replies and takes no more.", Shown(asMo, root));
        Assert.Contains($"action=\"{AddressOf(root, own)}/remove\"", Shown(asMo, own));
        Assert.DoesNotContain("takes no more", Shown(asMo, own));

        var pressed = await Form(bea, TopicPath(root), PostPath(root, own) + "/remove", new Dictionary<string, string>());

        Assert.Equal((HttpStatusCode.SeeOther, AddressOf(root, own)), (pressed.Status, pressed.Location));
        var tombstone = Shown((await Send(bea.Client, HttpMethod.Get, PostPath(root, own))).Body, own);
        Assert.Contains("Removed by its author", tombstone);
        Assert.DoesNotContain("/remove\"", tombstone);

        var closing = await Form(mo, TopicPath(root), PostPath(root, root) + "/remove", new Dictionary<string, string>());

        Assert.Equal(HttpStatusCode.SeeOther, closing.Status);
        var closed = WebUtility.HtmlDecode((await Send(bea.Client, HttpMethod.Get, TopicPath(root))).Body);
        Assert.Contains($"<p class=\"topic-closed\" data-gate=\"{Capabilities.Closed.Word}\">", closed);
        Assert.Contains(Capabilities.Closed.Text, closed);
        Assert.DoesNotContain("class=\"reply-form reply\"", closed);
    }

    /// <summary>One Post as a page draws it, from its article to the article's end.</summary>
    private static string Shown(string html, Post post)
    {
        var start = html.IndexOf($"<article id=\"{post.Id}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "the page draws the Post");
        return html[start..(html.IndexOf("</article>", start, StringComparison.Ordinal) + "</article>".Length)];
    }
}
