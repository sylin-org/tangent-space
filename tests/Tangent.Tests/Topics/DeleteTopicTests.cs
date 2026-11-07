using System.Net;
using System.Text.Json;
using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Tangent.Access;
using Tangent.Events;
using Tangent.Space;
using Tangent.Tangents;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Posts;

/// <summary>Deleting a Topic whole: whoever moderates its Tangent deletes it from its root's footer, confirmed in the page with
/// how many replies go, a removed root's among them, and nothing of it stays, while its Tangent counts it no more; a reply that
/// landed since the page was read refuses the delete with the page again, and a member is refused.</summary>
public sealed class DeleteTopicTests
{
    [Fact]
    public async Task A_moderator_deletes_a_removed_topic_whole_and_nothing_of_it_stays()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var leo = Persona.Id("leo4deletes");
        await Given.AParticipant(tangentSpace, leo, [Roles.Administrator], ct: Cancellation);
        var image = await Media.Keep(ArticleMediaTests.Jpeg(), Cancellation);
        Assert.IsType<Outcome<MediaUse>.Done>(await new BringMedia(image.Id, Bea).Send(Cancellation));
        var stays = await Started(Ada, "Kilns", "Gas or wood?");
        var root = await Started(Ada, "Cheap watches", "Buy one now at the link.");
        var reply = await Replied(root, Bea, "Here is mine. ![Watch](/media/" + image.Id + ")");
        Assert.IsType<Outcome<Post>.Done>(await new EditPost(reply.Id, 1, PostBody.Of("Mine, edited. ![Watch](/media/" + image.Id + ")"), null, Bea).Send(Cancellation));
        Assert.IsType<Outcome<Post>.Done>(await new RemovePost(root.Id, Mo).Send(Cancellation));
        var before = (await Tangents.Tangent.Get(Workshop, Cancellation))!;
        var snapshots = await Snapshots();
        using var mo = await SignedIn(tangentSpace, Mo);

        // Mo's page offers the delete on the removed root, confirming in the page what goes.
        var html = WebUtility.HtmlDecode((await Send(mo.Client, HttpMethod.Get, TopicPath(root))).Body);
        Assert.Contains("<summary class=\"post-remove-toggle\">Delete topic</summary>", html);
        Assert.Contains("Its one reply goes with it, and nothing brings them back.", html);
        var deleting = (await Send(mo.Client, HttpMethod.Get, TopicPath(root), json: true)).Json.GetProperty("deleting");
        Assert.Equal("1", Valued(deleting, TopicPageController.RepliesField));
        var deleted = await Form(mo, TopicPath(root), deleting.GetProperty("action").GetString()![BaseUrl.Length..],
            new Dictionary<string, string> { [TopicPageController.RepliesField] = "1" });

        Assert.Equal((HttpStatusCode.SeeOther, BaseUrl + "/t/" + Workshop), (deleted.Status, deleted.Location));
        Assert.Null(await Post.Get(root.Id, Cancellation));
        Assert.Null(await Post.Get(reply.Id, Cancellation));
        Assert.Equal(snapshots - 2, await Snapshots());
        Assert.Null(await MediaUse.Get(MediaUse.Key(image.Id, reply.Id), Cancellation));
        var after = (await Tangents.Tangent.Get(Workshop, Cancellation))!;
        Assert.Equal(before.TopicCount - 1, after.TopicCount);
        Assert.True(after.Tallied());
        var row = Assert.Single(await Event.Query(e => e.Kind == EventKind.TopicDeleted, Cancellation));
        Assert.Equal((Workshop, root.Id, Mo, (string?)null), (row.TangentId, row.TopicId, row.ActorId, row.TargetId));
        // Nothing of it shows, on its Tangent, at its address or in search; sent again, nothing is there to delete.
        var visitor = Agent(tangentSpace);
        var listed = (await Send(visitor, HttpMethod.Get, "/t/" + Workshop, json: true)).Json.GetProperty("topics").EnumerateArray()
            .Select(topic => topic.GetProperty("id").GetString()).ToList();
        Assert.Equal([stays.Id], listed);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(visitor, HttpMethod.Get, TopicPath(root))).Status);
        Assert.DoesNotContain(reply.Id, (await Send(visitor, HttpMethod.Get, "/search?q=edited", json: true)).Body);
        Assert.Equal(HttpStatusCode.NotFound, (await Json(mo, TopicPath(root), TopicPath(root) + "/" + TopicPageController.DeleteSegment, new { replies = 1 })).Status);
        // The workshop counts what it holds, so deleting it with that count lands.
        Assert.IsType<Outcome<Tangents.Tangent>.Done>(await new DeleteTangent(Workshop, leo, Topics: after.TopicCount).Send(Cancellation));
    }

    [Fact]
    public async Task A_delete_that_read_fewer_replies_than_landed_since_is_refused_and_the_page_says_what_goes_now()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var root = await Started(Ada, "Lanterns", "Mine hangs by the door.");
        await Replied(root, Bea, "Mine by the window.");
        using var mo = await SignedIn(tangentSpace, Mo);
        var deleting = (await Send(mo.Client, HttpMethod.Get, TopicPath(root), json: true)).Json.GetProperty("deleting");
        await Replied(root, Ada, "And mine on a hook.");
        var events = (await Counted()).Events;

        var byJson = await Follow(mo, deleting, new Dictionary<string, string>());
        var byForm = await Form(mo, TopicPath(root), deleting.GetProperty("action").GetString()![BaseUrl.Length..],
            new Dictionary<string, string> { [TopicPageController.RepliesField] = "1" });

        Assert.Equal((HttpStatusCode.Conflict, DeleteTopic.HoldsRepliesWord, DeleteTopic.HoldsReplies(2).Text),
            (byJson.Status, byJson.Word, byJson.Json.GetProperty("detail").GetString()));
        Assert.Equal(HttpStatusCode.Conflict, byForm.Status);
        var page = WebUtility.HtmlDecode(byForm.Body);
        Assert.Contains(DeleteTopic.HoldsReplies(2).Text, page);
        Assert.Contains("Its 2 replies go with it, and nothing brings them back.", page);
        Assert.Contains("<input type=\"hidden\" name=\"replies\" value=\"2\"", page);
        Assert.Contains("<details class=\"post-remove delete-topic\" open", page);
        Assert.Equal(2, (await Post.Get(root.Id, Cancellation))?.ReplyCount);
        Assert.Equal(events, (await Counted()).Events);
    }

    [Fact]
    public async Task A_member_is_refused_and_offered_no_delete_and_a_report_stays()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        Assert.IsType<Outcome<Tangents.Tangent>.Done>(await new CreateModeratorsTangent().Send(Cancellation));
        var root = await Started(Ada, "Lanterns", "Mine hangs by the door.");
        var reply = await Replied(root, Bea, "Mine by the window.");
        var report = Assert.IsType<Outcome<Post>.Done>(await new ReportPost(Tids.Mint(), reply.Id, PostBody.Of("Unkind."), Ada).Send(Cancellation)).Result;
        using var ada = await SignedIn(tangentSpace, Ada);
        using var mo = await SignedIn(tangentSpace, Mo);

        var refused = await Json(ada, TopicPath(root), TopicPath(root) + "/" + TopicPageController.DeleteSegment, new { replies = 1 });
        var kept = await Json(mo, TopicPath(report), TopicPath(report) + "/" + TopicPageController.DeleteSegment, new { replies = 0 });

        Assert.Equal((HttpStatusCode.Forbidden, "gated"), (refused.Status, refused.Kind));
        Assert.Equal((HttpStatusCode.BadRequest, DeleteTopic.ReportStays.Text), (kept.Status, kept.Json.GetProperty("detail").GetString()));
        Assert.NotNull(await Post.Get(root.Id, Cancellation));
        Assert.NotNull(await Post.Get(report.Id, Cancellation));
        var page = (await Send(ada.Client, HttpMethod.Get, TopicPath(root), json: true)).Json;
        Assert.True(!page.TryGetProperty("deleting", out var offered) || offered.ValueKind == JsonValueKind.Null);
    }
}
