using System.Net;
using System.Text.Json;
using Koan.Data.Core.Commands;
using Tangent.Access;
using Tangent.Capture;
using Tangent.Events;
using Tangent.Participants;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Posts;

/// <summary>Undoing a removal: whoever removed their own Post brings its words back from its tombstone, and only they; a removal
/// by someone else only a moderator of its Tangent undoes. The words, mentions and images come back at the same version from
/// the snapshot, <c>removal.undone</c> is appended for no one, the removal's line leaves its author's door, a removed opening's
/// Topic takes replies again, and the Post's history shows each version once.</summary>
public sealed class UndoRemovalTests
{
    [Fact]
    public async Task An_author_undoes_their_own_removal_and_its_words_mentions_and_images_come_back_at_the_same_version()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var image = await Media.Keep(ArticleMediaTests.Jpeg(), Cancellation);
        Assert.IsType<Outcome<MediaUse>.Done>(await new BringMedia(image.Id, Ada).Send(Cancellation));
        var root = await Started(Bea, "Lanterns", "How do you hang yours?");
        var own = await Replied(root, Ada, "Like [@Bea](/p/" + Bea + ") does. ![Hook](/media/" + image.Id + ")");
        Assert.IsType<Outcome<Post>.Done>(await new RemovePost(own.Id, Ada).Send(Cancellation));
        Assert.Null(await MediaUse.Get(MediaUse.Key(image.Id, own.Id), Cancellation));
        var mentions = (await Event.Query(e => e.Kind == EventKind.Mention, Cancellation)).Count;
        using var ada = await SignedIn(tangentSpace, Ada);
        using var mo = await SignedIn(tangentSpace, Mo);

        // Her tombstone offers the undo, in its footer and in its JSON; a moderator's does not, since she removed it herself.
        var html = WebUtility.HtmlDecode((await Send(ada.Client, HttpMethod.Get, TopicPath(root))).Body);
        Assert.Contains("action=\"" + AddressOf(root, own) + "/" + EditPostController.UndoSegment + "\"", html);
        Assert.Contains(">Undo removal</button>", html);
        var tombstone = Shown((await Send(ada.Client, HttpMethod.Get, TopicPath(root), json: true)).Json, own.Id);
        Assert.False(Offers(Shown((await Send(mo.Client, HttpMethod.Get, TopicPath(root), json: true)).Json, own.Id), "undo"));
        var refused = await Json(mo, TopicPath(root), PostPath(root, own) + "/" + EditPostController.UndoSegment, new { });
        Assert.Equal((HttpStatusCode.Forbidden, Capabilities.Removed.Word), (refused.Status, refused.Word));

        var back = await Follow(ada, tombstone.GetProperty("undo"), new Dictionary<string, string>());

        Assert.Equal((HttpStatusCode.OK, AddressOf(root, own), false), (back.Status, back.Location, back.Json.GetProperty("removed").GetBoolean()));
        var restored = (await Post.Get(own.Id, Cancellation))!;
        Assert.Equal((false, (string?)null, own.Body, 1L), (restored.Removed, restored.RemovedBy, restored.Body, restored.Version));
        Assert.Equal([Bea], restored.MentionedIds);
        Assert.Equal([image.Id], restored.MediaIds);
        Assert.NotNull(await MediaUse.Get(MediaUse.Key(image.Id, own.Id), Cancellation));
        var undone = Assert.Single(await Event.Query(e => e.Kind == EventKind.RemovalUndone, Cancellation));
        Assert.Equal((own.Id, Ada, (string?)null, 1L), (undone.PostId, undone.ActorId, undone.TargetId, undone.Version));
        Assert.Equal(mentions, (await Event.Query(e => e.Kind == EventKind.Mention, Cancellation)).Count);
        // Sent again, it answers the Post as it stands.
        Assert.Equal(HttpStatusCode.OK, (await Json(ada, TopicPath(root), PostPath(root, own) + "/" + EditPostController.UndoSegment, new { })).Status);
        Assert.Single(await Event.Query(e => e.Kind == EventKind.RemovalUndone, Cancellation));
    }

    [Fact]
    public async Task A_moderators_removal_only_a_moderator_undoes_and_its_line_leaves_the_authors_door()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var root = await Started(Ada, "Lanterns", "How do you hang yours?");
        var reply = await Replied(root, Bea, "From a hook by the door.");
        Assert.IsType<Outcome<Post>.Done>(await new RemovePost(reply.Id, Mo).Send(Cancellation));
        // Removed, undone, and removed again: one line on her door, while it stays removed.
        Assert.IsType<Outcome<Post>.Done>(await new UndoRemoval(reply.Id, Mo).Send(Cancellation));
        Assert.IsType<Outcome<Post>.Done>(await new RemovePost(reply.Id, Mo).Send(Cancellation));
        var bea = (await Participant.Get(Bea, Cancellation))!;
        Assert.Single((await (await Unread.Of(bea, Cancellation)).ForYouNow(Cancellation)).Rows, row => row.PostId == reply.Id && row.Kind == EventKind.PostRemoved);
        using var asBea = await SignedIn(tangentSpace, Bea);
        using var mo = await SignedIn(tangentSpace, Mo);

        Assert.False(Offers(Shown((await Send(asBea.Client, HttpMethod.Get, TopicPath(root), json: true)).Json, reply.Id), "undo"));
        var hers = await Json(asBea, TopicPath(root), PostPath(root, reply) + "/" + EditPostController.UndoSegment, new { });
        Assert.Equal((HttpStatusCode.Forbidden, Capabilities.Removed.Word), (hers.Status, hers.Word));
        Assert.True((await Post.Get(reply.Id, Cancellation))?.Removed);

        var form = Shown((await Send(mo.Client, HttpMethod.Get, TopicPath(root), json: true)).Json, reply.Id).GetProperty("undo");
        var byForm = await Form(mo, TopicPath(root), form.GetProperty("action").GetString()![BaseUrl.Length..], new Dictionary<string, string>());

        Assert.Equal((HttpStatusCode.SeeOther, AddressOf(root, reply)), (byForm.Status, byForm.Location));
        Assert.Equal("From a hook by the door.", (await Post.Get(reply.Id, Cancellation))?.Body);
        Assert.DoesNotContain((await (await Unread.Of(bea, Cancellation)).ForYouNow(Cancellation)).Rows, row => row.PostId == reply.Id);
    }

    [Fact]
    public async Task A_timed_out_author_is_refused_with_their_timeout()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var root = await Started(Bea, "Lanterns", "How do you hang yours?");
        var own = await Replied(root, Ada, "From the porch beam.");
        Assert.IsType<Outcome<Post>.Done>(await new RemovePost(own.Id, Ada).Send(Cancellation));
        Assert.IsType<Outcome<Participant>.Done>(await new TimeOut(Mo, Ada, DateTimeOffset.UtcNow.AddHours(1)).Send(Cancellation));

        var refused = Assert.IsType<Outcome<Post>.Refused>(await new UndoRemoval(own.Id, Ada).Send(Cancellation));

        Assert.Equal(Capabilities.TimedOut.Word, refused.Reason.Word);
        Assert.True((await Post.Get(own.Id, Cancellation))?.Removed);
    }

    [Fact]
    public async Task An_undone_opening_takes_replies_again_and_an_edit_after_it_keeps_each_version_once()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var root = await Started(Ada, "Lanterns", "How do you hang yours?");
        Assert.IsType<Outcome<Post>.Done>(await new Edited(root.Id, 1, PostBody.Of("How do you hang yours, and where?"), null, Ada).Send(Cancellation));
        Assert.IsType<Outcome<Post>.Done>(await new RemovePost(root.Id, Mo).Send(Cancellation));
        Assert.Equal(Capabilities.RootRemoved, Assert.IsType<Outcome<Post>.Refused>(await new Replied(Tids.Mint(), root.Id, "Still open?", Bea).Send(Cancellation)).Reason);
        using var mo = await SignedIn(tangentSpace, Mo);
        var html = WebUtility.HtmlDecode((await Send(mo.Client, HttpMethod.Get, TopicPath(root))).Body);
        Assert.Contains("The topic takes replies again.", html);

        Assert.IsType<Outcome<Post>.Done>(await new UndoRemoval(root.Id, Mo).Send(Cancellation));

        Assert.Equal((2L, "How do you hang yours, and where?"), ((await Post.Get(root.Id, Cancellation))?.Version, (await Post.Get(root.Id, Cancellation))?.Body));
        await Replied(root, Bea, "From a hook by the door.");
        Assert.IsType<Outcome<Post>.Done>(await new Edited(root.Id, 2, PostBody.Of("How do you hang yours, where and why?"), null, Ada).Send(Cancellation));
        var history = (await Send(Agent(tangentSpace), HttpMethod.Get, TopicPath(root) + "/" + root.Id + "/" + PostHistoryController.Segment, json: true)).Json;
        Assert.Equal([2L, 1L], history.GetProperty("versions").EnumerateArray().Select(version => version.GetProperty("number").GetInt64()).ToList());
        Assert.Equal(["How do you hang yours, and where?", "How do you hang yours?"],
            history.GetProperty("versions").EnumerateArray().Select(version => version.GetProperty("body").GetString()).ToList());
    }

    /// <summary>The Post of a Topic's JSON, its root or one of its replies, by its id.</summary>
    private static JsonElement Shown(JsonElement topic, string id)
        => topic.GetProperty("root").GetProperty("id").GetString() == id
            ? topic.GetProperty("root")
            : topic.GetProperty("replies").EnumerateArray().Single(reply => reply.GetProperty("id").GetString() == id);

    private static bool Offers(JsonElement post, string form) => post.TryGetProperty(form, out var value) && value.ValueKind != JsonValueKind.Null;
}
