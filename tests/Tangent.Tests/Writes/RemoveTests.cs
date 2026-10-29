using System.Net;
using Tangent.Access;
using Tangent.Events;
using Tangent.Writer;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Posts;

/// <summary>Removing a Post: the author removes their own, a moderator anyone's; the row stays in its place as a
/// tombstone without its body, which the snapshot keeps; <c>post.removed</c> is appended; removing again answers the same
/// tombstone; a removed root leaves its replies.</summary>
public sealed class RemoveTests
{
    [Fact]
    public async Task The_author_removes_by_form_and_a_moderator_by_json_each_leaving_a_tombstone_in_place_its_body_in_the_snapshot_and_post_removed()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        var root = await Started(writer, Ada, "Lanterns", "How do you hang yours?");
        var own = await Replied(writer, root, Ada, "From the porch beam.");
        var bea = await Replied(writer, root, Bea, "From a hook by the door.");
        using var ada = await SignedIn(tangentSpace, Ada);
        using var mo = await SignedIn(tangentSpace, Mo);

        var page = await Send(ada.Client, HttpMethod.Get, PostPath(root, own) + "/edit");
        var byForm = await Form(ada, PostPath(root, own) + "/edit", PostPath(root, own) + "/remove", new Dictionary<string, string>());

        Assert.Contains("<details class=\"post-remove\">", page.Body);
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
        Assert.Equal((true, Mo, "", 0), (byJson.Json.GetProperty("removed").GetBoolean(), byJson.Json.GetProperty("removedBy").GetString(),
            byJson.Json.GetProperty("body").GetString(), byJson.Json.GetProperty("can").GetArrayLength()));
        Assert.Equal("From a hook by the door.", (await AuditPartition.At<Post>(bea.Id, 1, Cancellation))?.Body);
        Assert.Equal(2, (await Event.Query(e => e.Kind == EventKind.PostRemoved, Cancellation)).Count);
    }

    [Fact]
    public async Task Removing_twice_answers_the_same_tombstone_and_writes_nothing_and_a_removed_root_leaves_its_replies_where_no_one_replies_again()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        var root = await Started(writer, Ada, "Lanterns", "How do you hang yours?");
        var reply = await Replied(writer, root, Bea, "From a hook by the door.");

        var first = Assert.IsType<Outcome<Post>.Done>(await writer.Send(new RemovePost(root.Id, Ada), Cancellation)).Result;
        var counted = (await Counted(), await Snapshots());
        var again = Assert.IsType<Outcome<Post>.Done>(await writer.Send(new RemovePost(root.Id, Ada), Cancellation)).Result;
        var byModerator = await writer.Send(new RemovePost(root.Id, Mo), Cancellation);
        var answer = await writer.Send(new Reply(Tids.Mint(), root.Id, "Still here?", Bea), Cancellation);

        Assert.Equal((first.Id, first.RemovedAt, first.RemovedBy, first.Body), (again.Id, again.RemovedAt, again.RemovedBy, again.Body));
        Assert.Equal(Capabilities.Removed, Assert.IsType<Outcome<Post>.Refused>(byModerator).Reason);
        Assert.Equal(Capabilities.Removed, Assert.IsType<Outcome<Post>.Refused>(answer).Reason);
        Assert.Equal(counted, (await Counted(), await Snapshots()));
        var stored = await Post.Get(root.Id, Cancellation);
        Assert.Equal((true, "", "Lanterns", "lanterns", 1, reply.Id), (stored?.Removed, stored?.Body, stored?.Title, stored?.Slug, stored?.ReplyCount, stored?.LatestReplyId));
        var kept = await Post.Get(reply.Id, Cancellation);
        Assert.Equal((false, "From a hook by the door.", root.Id), (kept?.Removed, kept?.Body, kept?.TopicId));
        Assert.Single(await Event.Query(e => e.Kind == EventKind.PostRemoved, Cancellation));
    }
}
