using System.Net;
using System.Text.Json;
using Tangent.Access;
using Tangent.Posts;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Events;

/// <summary>Reading moves a Topic's checkpoint and a participant watches a Topic or a Tangent, both through posts beside
/// the pages: the seen post moves the checkpoint to the sequence the page showed, never backwards, while a page's
/// <c>GET</c> moves nothing; the Tangent page and the door follow it; the watch flag sets and clears without moving the
/// sequence; and the door lists a watched Topic with news first, marked watched.</summary>
public sealed class SeenAndWatchTests
{
    private const string Leo = "did:plc:leo4checkpoints";

    [Fact]
    public async Task The_seen_post_moves_the_topic_checkpoint_to_the_sequence_shown_never_backwards_a_get_moves_nothing_a_tokenless_session_post_is_refused_and_a_delegates_bearer_moves_its_persons()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        var root = await Started(writer, Ada, "Lanterns", "How do you hang yours?");
        for (var reply = 0; reply < 3; reply++) await Replied(writer, root, Mo, "From a hook, number " + reply + ".");
        using var bea = await SignedIn(tangentSpace, Bea);
        var seenPath = TopicPath(root) + "/" + CheckpointController.SeenSegment;

        var page = await Send(bea.Client, HttpMethod.Get, TopicPath(root), json: true);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        var seen = page.Json.GetProperty("seen");
        Assert.Equal((BaseUrl + seenPath, 3L), (seen.GetProperty("action").GetString(), seen.GetProperty("through").GetInt64()));
        Assert.Contains("Mark seen", (await Send(bea.Client, HttpMethod.Get, TopicPath(root))).Body);
        Assert.Null(await SeenThrough(Bea, root));

        var tokenless = await Send(bea.Client, HttpMethod.Post, seenPath, FormBody(new Dictionary<string, string> { ["through"] = "3" }));
        Assert.Equal((HttpStatusCode.BadRequest, (long?)null), (tokenless.Status, await SeenThrough(Bea, root)));

        var two = await Form(bea, TopicPath(root), seenPath, new Dictionary<string, string> { ["through"] = "2" });
        Assert.Equal((HttpStatusCode.SeeOther, BaseUrl + TopicPath(root), (long?)2), (two.Status, two.Location, await SeenThrough(Bea, root)));
        var three = await Form(bea, TopicPath(root), seenPath, new Dictionary<string, string> { ["through"] = "3" });
        Assert.Equal((HttpStatusCode.SeeOther, (long?)3), (three.Status, await SeenThrough(Bea, root)));
        var back = await Json(bea, TopicPath(root), seenPath, new { through = 1 });
        Assert.Equal((HttpStatusCode.OK, 3L, (long?)3), (back.Status, back.Json.GetProperty("seq").GetInt64(), await SeenThrough(Bea, root)));
        var past = await Form(bea, TopicPath(root), seenPath, new Dictionary<string, string> { ["through"] = "4" });
        Assert.Equal((HttpStatusCode.BadRequest, (long?)3), (past.Status, await SeenThrough(Bea, root)));

        var read = await Send(bea.Client, HttpMethod.Get, TopicPath(root), json: true);
        Assert.False(read.Json.TryGetProperty("seen", out _));
        Assert.Equal((long?)3, await SeenThrough(Bea, root));
        Assert.Null(await SeenThrough(Ada, root));

        using var ada = await SignedIn(tangentSpace, Ada);
        var token = await Delegate(tangentSpace, ada);
        using var agent = Agent(tangentSpace);
        var delegated = await Send(agent, HttpMethod.Post, seenPath, JsonBody(new { through = 2 }), json: true, bearer: token);
        Assert.Equal((HttpStatusCode.OK, "topic:" + root.Id, 2L), (delegated.Status, delegated.Json.GetProperty("scope").GetString(),
            delegated.Json.GetProperty("seq").GetInt64()));
        Assert.Equal(((long?)2, (long?)3), (await SeenThrough(Ada, root), await SeenThrough(Bea, root)));
    }

    [Fact]
    public async Task After_the_seen_post_the_tangent_page_no_longer_marks_the_topic_new_and_the_door_no_longer_lists_it_until_a_later_reply()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        var root = await Started(writer, Ada, "Lanterns", "How do you hang yours?");
        await Replied(writer, root, Mo, "From a hook.");
        using var bea = await SignedIn(tangentSpace, Bea);

        Assert.True(await NewOnTangentPage(bea, root));
        Assert.Equal(2, await NewOnDoor(bea));

        var seen = await Form(bea, TopicPath(root), TopicPath(root) + "/" + CheckpointController.SeenSegment,
            new Dictionary<string, string> { ["through"] = "1" });
        Assert.Equal(HttpStatusCode.SeeOther, seen.Status);

        Assert.False(await NewOnTangentPage(bea, root));
        Assert.Equal(0, await NewOnDoor(bea));
        Assert.True((await Send(bea.Client, HttpMethod.Get, "/", json: true)).Json.GetProperty("news").GetProperty("quiet").GetBoolean());

        await Replied(writer, root, Mo, "Or a nail, at a pinch.");

        Assert.True(await NewOnTangentPage(bea, root));
        Assert.Equal(1, await NewOnDoor(bea));
    }

    [Fact]
    public async Task A_window_opened_past_a_gap_offers_no_seen_form_and_reading_from_the_first_unread_to_the_end_moves_the_checkpoint_to_the_last_reply()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        var root = await Started(writer, Ada, "Lanterns", "How do you hang yours?");
        var replies = new List<Post>();
        for (var reply = 1; reply <= 60; reply++) replies.Add(await Replied(writer, root, Mo, "Reply number " + reply + "."));
        using var bea = await SignedIn(tangentSpace, Bea);
        var seenPath = TopicPath(root) + "/" + CheckpointController.SeenSegment;
        await Form(bea, TopicPath(root), seenPath, new Dictionary<string, string> { ["through"] = "10" });
        Assert.Equal((long?)10, await SeenThrough(Bea, root));

        var anchored = await Send(bea.Client, HttpMethod.Get, PostPath(root, replies[49]), json: true);
        Assert.Equal(HttpStatusCode.OK, anchored.Status);
        Assert.True(anchored.Json.GetProperty("from").GetInt64() > 11, "The window around the fiftieth reply leaves a gap after the checkpoint.");
        Assert.False(anchored.Json.TryGetProperty("seen", out _));
        Assert.DoesNotContain("Mark seen", (await Send(bea.Client, HttpMethod.Get, PostPath(root, replies[49]))).Body);
        Assert.Equal((long?)10, await SeenThrough(Bea, root));

        if (anchored.Json.TryGetProperty("newer", out var newer))
            Assert.False((await Send(bea.Client, HttpMethod.Get, newer.GetString()![BaseUrl.Length..], json: true)).Json.TryGetProperty("seen", out _));

        using var ada = await SignedIn(tangentSpace, Ada);
        Assert.False((await Send(ada.Client, HttpMethod.Get, PostPath(root, replies[49]), json: true)).Json.TryGetProperty("seen", out _));
        Assert.Null(await SeenThrough(Ada, root));

        for (var turn = 0; turn < 10 && await SeenThrough(Bea, root) < 60; turn++)
        {
            var page = (await Send(bea.Client, HttpMethod.Get, TopicPath(root), json: true)).Json;
            var seen = page.GetProperty("seen");
            Assert.Equal((await SeenThrough(Bea, root))!.Value + 1, page.GetProperty("from").GetInt64());
            var posted = await Form(bea, TopicPath(root), seenPath, new Dictionary<string, string> { ["through"] = seen.GetProperty("through").GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture) });
            Assert.Equal(HttpStatusCode.SeeOther, posted.Status);
        }

        Assert.Equal((long?)60, await SeenThrough(Bea, root));
    }

    [Fact]
    public async Task A_mention_added_by_editing_a_post_the_reader_already_read_is_listed_on_the_door()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        var root = await Started(writer, Ada, "Lanterns", "How do you hang yours?");
        var old = await Replied(writer, root, Mo, "From a hook.");
        using var bea = await SignedIn(tangentSpace, Bea);
        await Form(bea, TopicPath(root), TopicPath(root) + "/" + CheckpointController.SeenSegment, new Dictionary<string, string> { ["through"] = "1" });
        Assert.True((await Send(bea.Client, HttpMethod.Get, "/", json: true)).Json.GetProperty("news").GetProperty("quiet").GetBoolean());

        Assert.IsType<Writer.Outcome<Event>.Done>(await writer.Send(new Space.Mentioned(Workshop, root.Id, old.Id, Mo, Bea), Cancellation));

        var mentions = (await Send(bea.Client, HttpMethod.Get, "/", json: true)).Json.GetProperty("news").GetProperty("mentions");
        Assert.Equal((1, old.Id), (mentions.GetProperty("count").GetInt32(), mentions.GetProperty("posts")[0].GetProperty("postId").GetString()));
        Assert.Equal((long?)1, await SeenThrough(Bea, root));
    }

    [Fact]
    public async Task Watch_and_unwatch_on_a_topic_and_a_tangent_set_and_clear_the_flag_without_moving_the_sequence_and_a_reader_who_may_not_view_gets_the_absent_answer()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        var root = await Started(writer, Ada, "Lanterns", "How do you hang yours?");
        await Replied(writer, root, Mo, "From a hook.");
        var hidden = await Started(writer, Leo, "Among administrators", "Only we read this.", Studio);
        using var bea = await SignedIn(tangentSpace, Bea);
        var topicWatch = TopicPath(root) + "/" + CheckpointController.WatchSegment;
        var tangentWatch = "/t/" + Workshop + "/" + CheckpointController.WatchSegment;
        await Form(bea, TopicPath(root), TopicPath(root) + "/" + CheckpointController.SeenSegment, new Dictionary<string, string> { ["through"] = "1" });

        var topicPage = (await Send(bea.Client, HttpMethod.Get, TopicPath(root), json: true)).Json.GetProperty("watching");
        Assert.Equal((BaseUrl + topicWatch, false), (topicPage.GetProperty("action").GetString(), topicPage.GetProperty("watch").GetBoolean()));
        var watched = await Form(bea, TopicPath(root), topicWatch, new Dictionary<string, string> { ["watch"] = "true" });
        Assert.Equal((HttpStatusCode.SeeOther, BaseUrl + TopicPath(root)), (watched.Status, watched.Location));
        Assert.Equal((1L, true), await Flag(Bea, Scope.Topic(root.Id)));
        Assert.True((await Send(bea.Client, HttpMethod.Get, TopicPath(root), json: true)).Json.GetProperty("watching").GetProperty("watch").GetBoolean());
        Assert.Contains("Watching", (await Send(bea.Client, HttpMethod.Get, TopicPath(root))).Body);
        await Form(bea, TopicPath(root), topicWatch, new Dictionary<string, string> { ["watch"] = "false" });
        Assert.Equal((1L, false), await Flag(Bea, Scope.Topic(root.Id)));

        var tangent = await Form(bea, "/t/" + Workshop, tangentWatch, new Dictionary<string, string> { ["watch"] = "true" });
        Assert.Equal((HttpStatusCode.SeeOther, BaseUrl + "/t/" + Workshop), (tangent.Status, tangent.Location));
        Assert.Equal((Checkpoint.Nothing, true), await Flag(Bea, Scope.Tangent(Workshop)));
        Assert.True((await Send(bea.Client, HttpMethod.Get, "/t/" + Workshop, json: true)).Json.GetProperty("watching").GetProperty("watch").GetBoolean());
        var unwatched = await Json(bea, "/t/" + Workshop, tangentWatch, new { watch = false });
        Assert.Equal((HttpStatusCode.OK, false, false), (unwatched.Status, unwatched.Json.GetProperty("watch").GetBoolean(),
            unwatched.Json.TryGetProperty("seq", out _)));
        Assert.Equal((Checkpoint.Nothing, false), await Flag(Bea, Scope.Tangent(Workshop)));
        Assert.False((await Send(bea.Client, HttpMethod.Get, "/t/" + Workshop, json: true)).Json.GetProperty("topics")[0].GetProperty("new").GetBoolean());

        using var ada = await SignedIn(tangentSpace, Ada);
        var token = await Delegate(tangentSpace, ada);
        using var agent = Agent(tangentSpace);
        var delegated = await Send(agent, HttpMethod.Post, topicWatch, JsonBody(new { watch = true }), json: true, bearer: token);
        Assert.Equal((HttpStatusCode.OK, true), (delegated.Status, delegated.Json.GetProperty("watch").GetBoolean()));
        Assert.Equal((Checkpoint.Nothing, true), await Flag(Ada, Scope.Topic(root.Id)));

        foreach (var path in new[] { TopicPath(hidden) + "/" + CheckpointController.WatchSegment, "/t/" + Studio + "/" + CheckpointController.WatchSegment })
        {
            var absent = await Form(bea, TopicPath(root), path, new Dictionary<string, string> { ["watch"] = "true" });
            Assert.Equal(HttpStatusCode.NotFound, absent.Status);
            var absentJson = await Json(bea, TopicPath(root), path, new { watch = true });
            Assert.Equal((HttpStatusCode.NotFound, Capabilities.Absent), (absentJson.Status, absentJson.Gate));
        }

        var hiddenSeen = await Form(bea, TopicPath(root), TopicPath(hidden) + "/" + CheckpointController.SeenSegment, new Dictionary<string, string> { ["through"] = "0" });
        Assert.Equal(HttpStatusCode.NotFound, hiddenSeen.Status);
        Assert.Null(await Checkpoint.Get(Checkpoint.Key(Bea, Scope.Topic(hidden.Id)), Cancellation));
        Assert.Null(await Checkpoint.Get(Checkpoint.Key(Bea, Scope.Tangent(Studio)), Cancellation));
    }

    [Fact]
    public async Task On_the_door_a_watched_topic_with_news_is_listed_first_and_marked_watched()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        var watchedTopic = await Started(writer, Ada, "Lanterns", "How do you hang yours?");
        var other = await Started(writer, Ada, "Kites", "Who flies one?");
        using var bea = await SignedIn(tangentSpace, Bea);
        await Form(bea, TopicPath(watchedTopic), TopicPath(watchedTopic) + "/" + CheckpointController.WatchSegment,
            new Dictionary<string, string> { ["watch"] = "true" });
        await Replied(writer, watchedTopic, Mo, "From a hook.");
        await Replied(writer, other, Mo, "On windy days.");

        var news = (await Send(bea.Client, HttpMethod.Get, "/", json: true)).Json.GetProperty("news");
        var first = news.EnumerateObject().Select(property => property.Name).SkipWhile(name => name is "from" or "through").First();
        Assert.Equal("watched", first);
        var watched = news.GetProperty("watched");
        Assert.Equal(1, watched.GetArrayLength());
        Assert.Equal(("topic", "Lanterns", BaseUrl + TopicPath(watchedTopic), 2),
            (watched[0].GetProperty("scope").GetString(), watched[0].GetProperty("name").GetString(), watched[0].GetProperty("address").GetString(),
                watched[0].GetProperty("new").GetInt32()));

        var html = (await Send(bea.Client, HttpMethod.Get, "/")).Body;
        var marked = html.IndexOf("Watched · 2 new posts", StringComparison.Ordinal);
        Assert.True(marked > 0, "The door marks the watched topic.");
        Assert.True(marked < html.IndexOf("New in each Tangent", StringComparison.Ordinal), "The watched topic is listed before the rest of the news.");
        Assert.Contains($"<a href=\"{BaseUrl + TopicPath(watchedTopic)}\">Lanterns</a> in {Workshop}", html);
    }

    /// <summary>The sequence of a participant's checkpoint on a Topic; null where they have none.</summary>
    private static async Task<long?> SeenThrough(string participantId, Post root)
        => (await Checkpoint.Get(Checkpoint.Key(participantId, Scope.Topic(root.Id)), Cancellation))?.Seq;

    /// <summary>A participant's checkpoint in a scope: its sequence and its watch flag.</summary>
    private static async Task<(long Seq, bool Watch)> Flag(string participantId, Scope scope)
    {
        var checkpoint = await Checkpoint.Get(Checkpoint.Key(participantId, scope), Cancellation);
        Assert.NotNull(checkpoint);
        return (checkpoint.Seq, checkpoint.Watch);
    }

    /// <summary>Whether the Tangent page marks the Topic new to the reader.</summary>
    private static async Task<bool> NewOnTangentPage(Participants.Browser browser, Post root)
    {
        var topics = (await Send(browser.Client, HttpMethod.Get, "/t/" + root.TangentId, json: true)).Json.GetProperty("topics");
        return topics.EnumerateArray().Single(topic => topic.GetProperty("id").GetString() == root.Id).GetProperty("new").GetBoolean();
    }

    /// <summary>How many Posts the door lists as new in the workshop.</summary>
    private static async Task<int> NewOnDoor(Participants.Browser browser)
    {
        var perTangent = (await Send(browser.Client, HttpMethod.Get, "/", json: true)).Json.GetProperty("news").GetProperty("perTangent");
        return perTangent.EnumerateArray().Where(tangent => tangent.GetProperty("slug").GetString() == Workshop)
            .Sum(tangent => tangent.GetProperty("topics").GetInt32() + tangent.GetProperty("replies").GetInt32());
    }
}
