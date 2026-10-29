using System.Net;
using System.Text.Json;
using Koan.Identity.Roles;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Events;
using Tangent.Tangents;
using Tangent.Writer;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Posts;

/// <summary>Take this on a tangent: any Post a reader can see becomes the root of a new Topic in a Tangent they may
/// start one in, through the form at the Post's address under <c>/tangent</c>; the new Topic links to its origin and the
/// origin lists the Topics taken from it, each only as far as the reader may view it.</summary>
public sealed class TakeOnATangentTests
{
    private const string Garden = "garden";
    private const string Notice = "notice";
    private const string Root = "did:plc:root4tangents";
    private const string Keeper = "did:plc:keeper4tangents";

    [Fact]
    public async Task Taking_a_reply_into_another_tangent_starts_a_topic_there_with_the_origin_and_the_same_tid_twice_is_one_topic()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        await Given.ATangent(tangentSpace, Garden, ct: Cancellation);
        var topic = await Started(writer, Ada, "Lanterns at dusk", "Who lights theirs first?");
        var reply = await Replied(writer, topic, Bea, "I do, at six.");
        using var ada = await SignedIn(tangentSpace, Ada);
        var form = PostPath(topic, reply) + "/tangent";

        var page = await Send(ada.Client, HttpMethod.Get, form);
        var minted = MintedId(page.Body);
        var fields = new Dictionary<string, string> { ["id"] = minted, ["tangent"] = Garden, ["title"] = "Six o’clock lanterns", ["body"] = "" };
        var taken = await Form(ada, form, form, new Dictionary<string, string>(fields));
        var counted = await Counted();
        var again = await Form(ada, form, form, new Dictionary<string, string>(fields));

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains("I do, at six.", page.Body);
        var started = await Post.Get(minted, Cancellation);
        Assert.NotNull(started);
        Assert.Null(started.ParentId);
        Assert.Equal((Garden, reply.Id, "", Ada, minted), (started.TangentId, started.OriginId, started.Body, started.AuthorId, started.TopicId));
        Assert.Equal((HttpStatusCode.SeeOther, BaseUrl + "/t/garden/" + started.Slug + "/" + minted), (taken.Status, taken.Location));
        Assert.Equal((HttpStatusCode.SeeOther, taken.Location), (again.Status, again.Location));
        Assert.Equal(counted, await Counted());
        Assert.Single(await Post.Query(post => post.OriginId == reply.Id, Cancellation));
        // Its post.created is the new Topic's, and tells the author of the reply it was taken from.
        var created = Assert.Single(await Event.Query(e => e.Kind == EventKind.PostCreated && e.PostId == minted, Cancellation));
        Assert.Equal((Garden, minted, Ada, Bea), (created.TangentId, created.TopicId, created.ActorId, created.TargetId));
    }

    [Fact]
    public async Task The_new_topic_links_to_its_origin_and_the_origin_lists_the_topics_taken_from_it_newest_first_with_at_least_past_the_bound()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        await Given.ATangent(tangentSpace, Garden, ct: Cancellation);
        var topic = await Started(writer, Ada, "Lanterns at dusk", "Who lights theirs first?");
        var reply = await Replied(writer, topic, Bea, "I do, at six.");
        var first = await Taken(writer, Ada, reply, Garden, "Six o’clock lanterns", at: Start);
        using var bea = await SignedIn(tangentSpace, Bea);

        var newTopic = await Send(bea.Client, HttpMethod.Get, TopicPath(first));
        var origin = await Send(bea.Client, HttpMethod.Get, PostPath(topic, reply));
        var originJson = await Send(bea.Client, HttpMethod.Get, PostPath(topic, reply), json: true);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (newTopic.Status, origin.Status));
        var takenFrom = Between(newTopic.Body, "topic-taken-from");
        Assert.Contains("href=\"" + AddressOf(topic, reply) + "\"", takenFrom);
        Assert.Contains("Lanterns at dusk", WebUtility.HtmlDecode(takenFrom));
        Assert.Contains("href=\"" + BaseUrl + TopicPath(first) + "\"", Between(origin.Body, "post-taken"));
        var listed = Taken(originJson.Json, reply.Id);
        Assert.False(listed.GetProperty("atLeast").GetBoolean());
        Assert.Equal(["Six o’clock lanterns"], Titles(listed));
        // The root offers the verb, as every Post does to a reader who may start a Topic somewhere.
        Assert.Contains("href=\"" + AddressOf(topic, topic) + "/tangent\"", origin.Body);

        for (var more = 1; more <= Origins.TakenLimit; more++) await Taken(writer, Bea, reply, Garden, "Lanterns " + more, at: Start.AddMinutes(more));
        var bounded = Taken((await Send(bea.Client, HttpMethod.Get, PostPath(topic, reply), json: true)).Json, reply.Id);
        var boundedPage = await Send(bea.Client, HttpMethod.Get, PostPath(topic, reply));

        Assert.True(bounded.GetProperty("atLeast").GetBoolean());
        Assert.Equal(["Lanterns 5", "Lanterns 4", "Lanterns 3", "Lanterns 2", "Lanterns 1"], Titles(bounded));
        Assert.Contains("Taken on a tangent in at least 6 topics:", boundedPage.Body);
    }

    [Fact]
    public async Task A_reader_who_may_not_view_the_origin_sees_a_post_they_cannot_see_and_one_who_may_not_view_the_new_topic_does_not_see_it()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Root, [Roles.Administrator], ct: Cancellation);
        await Given.AParticipant(tangentSpace, Keeper, [Roles.Administrator], ct: Cancellation);
        var hidden = await Started(writer, Keeper, "Quiet planning", "The studio’s private words.", Studio);
        var fromHidden = await Taken(writer, Root, hidden, Workshop, "Out in the open");
        var open = await Started(writer, Ada, "Lanterns at dusk", "Who lights theirs first?");
        var intoHidden = await Taken(writer, Root, open, Studio, "Behind the door");
        using var ada = await SignedIn(tangentSpace, Ada);

        var page = await Send(ada.Client, HttpMethod.Get, TopicPath(fromHidden));
        var json = await Send(ada.Client, HttpMethod.Get, TopicPath(fromHidden), json: true);
        var origin = await Send(ada.Client, HttpMethod.Get, TopicPath(open));
        var originJson = await Send(ada.Client, HttpMethod.Get, TopicPath(open), json: true);
        using var root = await SignedIn(tangentSpace, Root);
        var asRoot = await Send(root.Client, HttpMethod.Get, TopicPath(open), json: true);

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains("Taken on a tangent from a post you can’t see", WebUtility.HtmlDecode(Between(page.Body, "topic-taken-from")));
        foreach (var answer in new[] { page.Body, json.Body })
        {
            Assert.DoesNotContain("Quiet planning", answer);
            Assert.DoesNotContain("private words", answer);
            Assert.DoesNotContain(hidden.Id, answer);
            Assert.DoesNotContain("/t/studio", answer);
            Assert.DoesNotContain(Keeper, answer);
        }

        var takenFrom = json.Json.GetProperty("takenFrom");
        Assert.True(takenFrom.GetProperty("absent").GetBoolean());
        Assert.Equal([], new[] { "address", "topic", "tangent", "author" }.Where(name => Present(takenFrom, name)));
        foreach (var answer in new[] { origin.Body, originJson.Body })
        {
            Assert.DoesNotContain("Behind the door", answer);
            Assert.DoesNotContain(intoHidden.Id, answer);
        }

        Assert.False(Present(originJson.Json.GetProperty("root"), "taken"));
        Assert.Equal(["Behind the door"], Titles(Taken(asRoot.Json, open.Id)));
    }

    [Fact]
    public async Task The_form_is_absent_for_an_origin_the_reader_may_not_view_lists_only_tangents_where_they_start_topics_and_refuses_another_with_the_gate()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        await Given.ATangent(tangentSpace, Notice, ct: Cancellation);
        await tangentSpace.Services.GetRequiredService<RoleCollection>().Overwrite(Overwrites.Resource(Notice), OverwriteSubjectKind.Role,
            RoleTokens.Everyone, [Bits.Token(Bit.View)], [Bits.Token(Bit.Topic)], ct: Cancellation);
        await Given.AParticipant(tangentSpace, Root, [Roles.Administrator], ct: Cancellation);
        var hidden = await Started(writer, Root, "Quiet planning", "The studio’s private words.", Studio);
        var topic = await Started(writer, Bea, "Lanterns at dusk", "Who lights theirs first?");
        using var ada = await SignedIn(tangentSpace, Ada);
        var hiddenForm = PostPath(hidden, hidden) + "/tangent";
        var nowhere = "/t/studio/no-such-topic/" + Tids.Mint() + "/tangent";
        var form = PostPath(topic, topic) + "/tangent";

        var absent = await Send(ada.Client, HttpMethod.Get, hiddenForm);
        var missing = await Send(ada.Client, HttpMethod.Get, nowhere);
        var absentPost = await Json(ada, form, hiddenForm, new { id = Tids.Mint(), tangent = Workshop, title = "Out", body = "" });
        var listed = await Send(ada.Client, HttpMethod.Get, form, json: true);
        var counted = await Counted();
        var refused = await Json(ada, form, form, new { id = Tids.Mint(), tangent = Notice, title = "Not here", body = "" });
        var refusedForm = await Form(ada, form, form, new Dictionary<string, string> { ["id"] = Tids.Mint(), ["tangent"] = Notice, ["title"] = "Not here" });
        var intoAbsent = await Json(ada, form, form, new { id = Tids.Mint(), tangent = Studio, title = "In there", body = "" });
        var intoNothing = await Json(ada, form, form, new { id = Tids.Mint(), tangent = "nowhere", title = "In there", body = "" });

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound), (absent.Status, missing.Status, absentPost.Status));
        Assert.Equal(missing.Body, absent.Body);
        Assert.DoesNotContain("Quiet planning", absent.Body + absentPost.Body);
        Assert.Equal(HttpStatusCode.OK, listed.Status);
        Assert.Equal([Articles, Workshop], listed.Json.GetProperty("tangents").EnumerateArray().Select(choice => choice.GetProperty("slug").GetString()!).Order());
        Assert.Equal((HttpStatusCode.Forbidden, Capabilities.Denied(RoleTokens.Everyone, Bit.Topic)), (refused.Status, refused.Gate));
        Assert.Equal(HttpStatusCode.Forbidden, refusedForm.Status);
        Assert.Contains(PostWrites.Sentence(Capabilities.Denied(RoleTokens.Everyone, Bit.Topic)), WebUtility.HtmlDecode(refusedForm.Body));
        Assert.Equal((HttpStatusCode.BadRequest, HttpStatusCode.BadRequest), (intoAbsent.Status, intoNothing.Status));
        Assert.Equal(intoNothing.Body, intoAbsent.Body);
        Assert.Equal(counted, await Counted());
    }

    [Fact]
    public async Task A_removed_origin_shows_as_a_tombstone_link_and_a_removed_new_root_keeps_its_origin()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        await Given.ATangent(tangentSpace, Garden, ct: Cancellation);
        var topic = await Started(writer, Ada, "Lanterns at dusk", "Who lights theirs first?");
        var reply = await Replied(writer, topic, Bea, "I do, at six.");
        var taken = await Taken(writer, Ada, reply, Garden, "Six o’clock lanterns", "Bea starts early.");
        using var ada = await SignedIn(tangentSpace, Ada);
        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new RemovePost(reply.Id, Bea), Cancellation));

        var tombstone = await Send(ada.Client, HttpMethod.Get, TopicPath(taken));
        var tombstoneJson = await Send(ada.Client, HttpMethod.Get, TopicPath(taken), json: true);

        var link = Between(tombstone.Body, "topic-taken-from");
        Assert.Contains("href=\"" + AddressOf(topic, reply) + "\">a removed post</a>", link);
        Assert.DoesNotContain("I do, at six.", tombstone.Body);
        var origin = tombstoneJson.Json.GetProperty("takenFrom");
        Assert.Equal((false, true, AddressOf(topic, reply)),
            (origin.GetProperty("absent").GetBoolean(), origin.GetProperty("removed").GetBoolean(), origin.GetProperty("address").GetString()));

        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new RemovePost(taken.Id, Ada), Cancellation));
        var removedRoot = await Send(ada.Client, HttpMethod.Get, TopicPath(taken));

        Assert.Equal(reply.Id, (await Post.Get(taken.Id, Cancellation))?.OriginId);
        Assert.Contains("href=\"" + AddressOf(topic, reply) + "\"", Between(removedRoot.Body, "topic-taken-from"));
        Assert.DoesNotContain("Bea starts early.", removedRoot.Body);
    }

    [Fact]
    public async Task Only_a_root_taken_on_a_tangent_starts_or_is_edited_without_a_body_of_its_own()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        var topic = await Started(writer, Ada, "Lanterns at dusk", "Who lights theirs first?");
        using var ada = await SignedIn(tangentSpace, Ada);
        const string form = "/t/" + Workshop + "/new";

        var bare = await writer.Send(new StartTopic(Tids.Mint(), Workshop, "Nothing to say", "", Ada), Cancellation);
        var bareJson = await Json(ada, form, form, new { id = Tids.Mint(), title = "Nothing to say", body = "" });
        var bareReply = await writer.Send(new Reply(Tids.Mint(), topic.Id, "", Ada), Cancellation);
        var taken = await Taken(writer, Ada, topic, Workshop, "Lanterns elsewhere");
        var retitled = await writer.Send(new EditPost(taken.Id, taken.Version, "", "Lanterns, elsewhere", Ada), Cancellation);
        var emptied = await writer.Send(new EditPost(topic.Id, topic.Version, "", "Lanterns at dawn", Ada), Cancellation);

        Assert.Equal(Post.NeedsBody, Assert.IsType<Outcome<Post>.Refused>(bare).Reason);
        Assert.Equal(HttpStatusCode.BadRequest, bareJson.Status);
        Assert.Equal(Post.NeedsBody, bareJson.Json.GetProperty("detail").GetString());
        Assert.Equal(Post.NeedsBody, Assert.IsType<Outcome<Post>.Refused>(bareReply).Reason);
        Assert.Equal("", taken.Body);
        var edited = Assert.IsType<Outcome<Post>.Done>(retitled).Result;
        Assert.Equal(("Lanterns, elsewhere", "", 2L), (edited.Title, edited.Body, edited.Version));
        Assert.Equal(Post.NeedsBody, Assert.IsType<Outcome<Post>.Refused>(emptied).Reason);
        Assert.Equal(("Lanterns at dusk", "Who lights theirs first?"), ((await Post.Get(topic.Id, Cancellation))?.Title, (await Post.Get(topic.Id, Cancellation))?.Body));
    }

    [Fact]
    public async Task A_topic_taken_from_a_readers_post_is_a_group_of_its_own_on_their_door_and_never_a_reply()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        await Given.ATangent(tangentSpace, Garden, ct: Cancellation);
        var topic = await Started(writer, Ada, "Lanterns at dusk", "Who lights theirs first?");
        var reply = await Replied(writer, topic, Bea, "I do, at six.");
        var answer = Assert.IsType<Outcome<Post>.Done>(await writer.Send(new Reply(Tids.Mint(), topic.Id, "Six is early.", Ada, reply.Id), Cancellation)).Result;
        using var bea = await SignedIn(tangentSpace, Bea);
        var before = (await Send(bea.Client, HttpMethod.Get, "/", json: true)).Json.GetProperty("news");

        var taken = await Taken(writer, Ada, reply, Garden, "Six o’clock lanterns");
        var door = await Send(bea.Client, HttpMethod.Get, "/", json: true);
        var page = await Send(bea.Client, HttpMethod.Get, "/");

        var news = door.Json.GetProperty("news");
        Assert.Equal((1, answer.Id), (before.GetProperty("replies").GetProperty("count").GetInt32(),
            before.GetProperty("replies").GetProperty("posts")[0].GetProperty("postId").GetString()));
        Assert.Equal((1, answer.Id), (news.GetProperty("replies").GetProperty("count").GetInt32(),
            news.GetProperty("replies").GetProperty("posts")[0].GetProperty("postId").GetString()));
        Assert.Equal((1, taken.Id), (news.GetProperty("taken").GetProperty("count").GetInt32(),
            news.GetProperty("taken").GetProperty("posts")[0].GetProperty("postId").GetString()));
        var html = WebUtility.HtmlDecode(page.Body);
        Assert.Contains("1 reply to you, 1 topic taken on a tangent from your posts", html);
        Assert.Contains("Taken on a tangent from your posts · 1", html);
    }

    [Fact]
    public async Task At_least_is_said_when_topics_the_reader_may_not_view_fill_the_read()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Root, [Roles.Administrator], ct: Cancellation);
        var topic = await Started(writer, Ada, "Lanterns at dusk", "Who lights theirs first?");
        for (var open = 1; open <= 6; open++) await Taken(writer, Bea, topic, Workshop, "Open " + open, at: Start.AddMinutes(open));
        for (var hidden = 1; hidden <= 7; hidden++) await Taken(writer, Root, topic, Studio, "Hidden " + hidden, at: Start.AddHours(1).AddMinutes(hidden));
        using var ada = await SignedIn(tangentSpace, Ada);

        var json = await Send(ada.Client, HttpMethod.Get, TopicPath(topic), json: true);
        var page = await Send(ada.Client, HttpMethod.Get, TopicPath(topic));

        var taken = Taken(json.Json, topic.Id);
        Assert.Equal((true, 5), (taken.GetProperty("atLeast").GetBoolean(), taken.GetProperty("count").GetInt32()));
        Assert.Equal(["Open 6", "Open 5", "Open 4", "Open 3", "Open 2"], Titles(taken));
        Assert.Contains("Taken on a tangent in at least 5 topics:", page.Body);
        Assert.DoesNotContain("Hidden", json.Body + page.Body);
    }

    private static readonly DateTimeOffset Start = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A Topic taken on a tangent from <paramref name="origin"/> into <paramref name="tangent"/>, through the
    /// writer.</summary>
    private static async Task<Post> Taken(WriteLoop writer, string author, Post origin, string tangent, string title, string body = "",
        DateTimeOffset? at = null)
        => Assert.IsType<Outcome<Post>.Done>(await writer.Send(
            new StartTopic(Tids.Mint(), tangent, title, body, author, At: at, OriginId: origin.Id), Cancellation)).Result;

    /// <summary>The Topics taken from the Post with this Id, as a Topic page's JSON holds them.</summary>
    private static JsonElement Taken(JsonElement page, string postId)
    {
        var posts = page.GetProperty("replies").EnumerateArray().Prepend(page.GetProperty("root"));
        return posts.Single(post => post.GetProperty("id").GetString() == postId).GetProperty("taken");
    }

    /// <summary>Whether a JSON object holds a member of this name with a value other than null.</summary>
    private static bool Present(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null;

    private static string[] Titles(JsonElement taken)
        => taken.GetProperty("topics").EnumerateArray().Select(topic => topic.GetProperty("title").GetString()!).ToArray();

    /// <summary>The element whose class names <paramref name="marker"/>, from its opening tag to the end of its first
    /// paragraph.</summary>
    private static string Between(string html, string marker)
    {
        var start = html.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"The page holds no {marker}.");
        var end = html.IndexOf("</p>", start, StringComparison.Ordinal);
        return html[start..end];
    }
}
