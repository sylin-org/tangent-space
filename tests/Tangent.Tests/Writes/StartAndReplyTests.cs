using System.Net;
using Tangent.Events;
using Tangent.Writer;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Posts;

/// <summary>Starting a Topic and replying, by form and by JSON, to the same addresses: a form is sent on to the Post's
/// address, JSON reads the Post at its <c>Location</c>; the TID is the idempotency key; and a Topic that names no kind
/// takes its Tangent's.</summary>
public sealed class StartAndReplyTests
{
    [Fact]
    public async Task A_topic_and_a_reply_by_form_are_sent_on_to_the_posts_address_and_by_json_answer_201_with_location_and_the_post()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);
        using var bea = await SignedIn(tangentSpace, Bea);
        const string form = "/t/" + Workshop + "/new";

        var page = await Send(ada.Client, HttpMethod.Get, form);
        var minted = MintedId(page.Body);
        var started = await Form(ada, form, form, new Dictionary<string, string> { ["id"] = minted, ["title"] = "Lanterns at dusk", ["body"] = "Who lights theirs first?" });

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.True(Tid.IsValid(minted));
        Assert.Equal((HttpStatusCode.SeeOther, BaseUrl + "/t/workshop/lanterns-at-dusk/" + minted), (started.Status, started.Location));
        var root = await Post.Get(minted, Cancellation);
        Assert.NotNull(root);
        Assert.Equal((Workshop, "lanterns-at-dusk", "Lanterns at dusk", "Who lights theirs first?", Ada, PostKind.Topic, 1L),
            (root.TangentId, root.Slug, root.Title, root.Body, root.AuthorId, root.Kind, root.Version));

        var replyId = Tids.Mint();
        var replied = await Form(bea, form, TopicPath(root), new Dictionary<string, string> { ["id"] = replyId, ["body"] = "I do, at six." });

        Assert.Equal((HttpStatusCode.SeeOther, BaseUrl + "/t/workshop/lanterns-at-dusk/" + replyId), (replied.Status, replied.Location));
        var reply = await Post.Get(replyId, Cancellation);
        Assert.Equal((root.Id, root.Id, 1L, Bea), (reply?.TopicId, reply?.ParentId, reply?.Sequence, reply?.AuthorId));
        // The address a form is sent on to is the Topic's page, opened at the Post and marking it.
        var landed = await Send(bea.Client, HttpMethod.Get, new Uri(replied.Location!).AbsolutePath);
        Assert.Equal(HttpStatusCode.OK, landed.Status);
        Assert.Contains("<article id=\"" + replyId + "\"", landed.Body);
        Assert.Contains("aria-current=\"true\"", landed.Body[landed.Body.IndexOf("<article id=\"" + replyId + "\"", StringComparison.Ordinal)..].Split('>')[0]);
        Assert.Contains("I do, at six.", landed.Body);

        var topicId = Tids.Mint();
        var asJson = await Json(ada, form, form, new { id = topicId, title = "Wicks", body = "Cotton or hemp?" });

        Assert.Equal((HttpStatusCode.Created, BaseUrl + "/t/workshop/wicks/" + topicId, "application/json"), (asJson.Status, asJson.Location, asJson.MediaType));
        Assert.Equal((topicId, BaseUrl + "/t/workshop/wicks/" + topicId, Workshop, topicId, "wicks", "topic", "Wicks", "Cotton or hemp?", 1L, Ada),
            (asJson.Json.GetProperty("id").GetString(), asJson.Json.GetProperty("address").GetString(), asJson.Json.GetProperty("tangent").GetString(),
                asJson.Json.GetProperty("topicId").GetString(), asJson.Json.GetProperty("slug").GetString(), asJson.Json.GetProperty("kind").GetString(),
                asJson.Json.GetProperty("title").GetString(), asJson.Json.GetProperty("body").GetString(), asJson.Json.GetProperty("version").GetInt64(),
                asJson.Json.GetProperty("authorId").GetString()));
        Assert.Contains("EditOwn", asJson.Json.GetProperty("can").EnumerateArray().Select(verb => verb.GetString()));

        var jsonReplyId = Tids.Mint();
        var jsonReply = await Json(bea, form, "/t/workshop/wicks", new { id = jsonReplyId, body = "Cotton, always.", parent = topicId });

        Assert.Equal((HttpStatusCode.Created, BaseUrl + "/t/workshop/wicks/" + jsonReplyId), (jsonReply.Status, jsonReply.Location));
        Assert.Equal((topicId, topicId, false, 1L),
            (jsonReply.Json.GetProperty("topicId").GetString(), jsonReply.Json.GetProperty("parentId").GetString(),
                jsonReply.Json.TryGetProperty("slug", out _), jsonReply.Json.GetProperty("sequence").GetInt64()));
        Assert.Equal(4, (await Event.Query(e => e.Kind == EventKind.PostCreated, Cancellation)).Count);
    }

    [Fact]
    public async Task The_same_tid_twice_is_one_post_and_the_same_tid_with_another_body_is_a_conflict_by_form_and_by_json()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);
        const string form = "/t/" + Workshop + "/new";
        var minted = MintedId((await Send(ada.Client, HttpMethod.Get, form)).Body);
        var fields = new Dictionary<string, string> { ["id"] = minted, ["title"] = "Twice", ["body"] = "Sent once, then again." };

        var first = await Form(ada, form, form, new Dictionary<string, string>(fields));
        var second = await Form(ada, form, form, new Dictionary<string, string>(fields));
        var afterRepeat = await Counted();
        var otherWords = await Form(ada, form, form, new Dictionary<string, string>(fields) { ["body"] = "Other words." });
        var otherJson = await Json(ada, form, form, new { id = minted, title = "Twice", body = "Other words." });
        var replyId = Tids.Mint();
        var reply = await Json(ada, form, "/t/workshop/twice", new { id = replyId, body = "And a reply." });
        var replyAgain = await Json(ada, form, "/t/workshop/twice", new { id = replyId, body = "And a reply." });
        var replyOther = await Json(ada, form, "/t/workshop/twice", new { id = replyId, body = "Another reply." });

        Assert.Equal((HttpStatusCode.SeeOther, HttpStatusCode.SeeOther), (first.Status, second.Status));
        Assert.Equal(first.Location, second.Location);
        Assert.Equal((1L, 1L), afterRepeat);
        Assert.Equal(HttpStatusCode.Conflict, otherWords.Status);
        Assert.Contains("This form was already sent with other words, so nothing was posted.", System.Net.WebUtility.HtmlDecode(otherWords.Body));
        Assert.DoesNotContain("value=\"" + minted + "\"", otherWords.Body);
        Assert.Equal((HttpStatusCode.Conflict, "application/problem+json"), (otherJson.Status, otherJson.MediaType));
        Assert.Equal((HttpStatusCode.Created, HttpStatusCode.Created), (reply.Status, replyAgain.Status));
        Assert.Equal(reply.Location, replyAgain.Location);
        Assert.Equal(HttpStatusCode.Conflict, replyOther.Status);
        Assert.Equal("Sent once, then again.", (await Post.Get(minted, Cancellation))?.Body);
        Assert.Equal("And a reply.", (await Post.Get(replyId, Cancellation))?.Body);
        Assert.Equal((2L, 2L), await Counted());
    }

    [Fact]
    public async Task An_id_a_post_holds_elsewhere_or_out_of_sight_answers_one_refusal_that_names_nothing_and_writes_nothing_while_a_replay_still_answers_the_post()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        var hidden = Post.Root(Tids.Mint(), Studio, "Plans", "plans", PostKind.Topic, "Not for everyone.", Mo, DateTimeOffset.UtcNow);
        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new Store<Post>(hidden), Cancellation));
        var root = await Started(writer, Ada, "Lanterns", "How do you hang yours?");
        var other = await Started(writer, Bea, "Wicks", "Cotton or hemp?");
        var elsewhere = await Replied(writer, other, Bea, "Cotton.");
        var mine = await Replied(writer, root, Ada, "From the porch beam.");
        using var ada = await SignedIn(tangentSpace, Ada);
        const string form = "/t/" + Workshop + "/new";
        var before = (await Counted(), await Snapshots());

        var answers = new[]
        {
            await Json(ada, form, TopicPath(root), new { id = hidden.Id, body = "A reply." }),
            await Json(ada, form, form, new { id = hidden.Id, title = "A topic", body = "Words." }),
            await Json(ada, form, TopicPath(root), new { id = elsewhere.Id, body = "A reply." }),
            await Json(ada, form, "/t/" + Articles + "/new", new { id = root.Id, title = "Lanterns", body = "How do you hang yours?" })
        };
        var onForm = await Form(ada, form, form, new Dictionary<string, string> { ["id"] = hidden.Id, ["title"] = "A topic", ["body"] = "Words." });
        var replay = await Json(ada, form, TopicPath(root), new { id = mine.Id, body = "From the porch beam." });
        var visibleConflict = await Json(ada, form, TopicPath(root), new { id = mine.Id, body = "Other words." });

        Assert.All(answers, answered => Assert.Equal((HttpStatusCode.BadRequest, Post.IdTaken), (answered.Status, answered.Json.GetProperty("detail").GetString())));
        Assert.All(answers, answered => Assert.Equal(answers[0].Body, answered.Body));
        Assert.Equal(HttpStatusCode.BadRequest, onForm.Status);
        Assert.Contains(Post.IdTaken, System.Net.WebUtility.HtmlDecode(onForm.Body));
        Assert.DoesNotContain("value=\"" + hidden.Id + "\"", onForm.Body);
        Assert.DoesNotContain("Not for everyone.", onForm.Body + string.Concat(answers.Select(answered => answered.Body)));
        Assert.Equal((HttpStatusCode.Created, AddressOf(root, mine)), (replay.Status, replay.Location));
        Assert.Equal(HttpStatusCode.Conflict, visibleConflict.Status);
        Assert.Equal(before, (await Counted(), await Snapshots()));
    }

    [Fact]
    public async Task A_topic_that_names_no_kind_takes_its_tangents_so_one_in_an_articles_tangent_is_an_article_and_one_that_names_its_kind_keeps_it()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);
        const string articles = "/t/" + Articles + "/new";

        var page = await Send(ada.Client, HttpMethod.Get, articles, json: true);
        var article = await Json(ada, articles, articles, new { id = Tids.Mint(), title = "On wicks", body = "A short piece." });
        var topic = await Json(ada, articles, articles, new { id = Tids.Mint(), title = "A question", body = "Not a piece.", kind = "topic" });
        var workshop = await Json(ada, articles, "/t/" + Workshop + "/new", new { id = Tids.Mint(), title = "In the workshop", body = "Plain." });
        var unknown = await Json(ada, articles, articles, new { id = Tids.Mint(), title = "Odd", body = "Odd kind.", kind = "essay" });

        Assert.Equal(("article", true), (page.Json.GetProperty("kind").GetString(), Tid.IsValid(Valued(page.Json.GetProperty("start"), PostWrites.IdField))));
        Assert.Equal("article", article.Json.GetProperty("kind").GetString());
        Assert.Equal(PostKind.Article, (await Post.Get(article.Json.GetProperty("id").GetString()!, Cancellation))?.Kind);
        Assert.Equal("topic", topic.Json.GetProperty("kind").GetString());
        Assert.Equal("topic", workshop.Json.GetProperty("kind").GetString());
        Assert.Equal((HttpStatusCode.BadRequest, PostWrites.KindRule), (unknown.Status, unknown.Json.GetProperty("detail").GetString()));
    }
}
