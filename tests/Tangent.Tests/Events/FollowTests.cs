using System.Net;
using System.Text.Json;
using Koan.Data.Core.Commands;
using Tangent.Access;
using Tangent.Capture;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Tools;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Events;

/// <summary>Following a Topic: writing in it follows it, and its new replies stand in the follower's news and count on their
/// door until they read it, each Post once; stopping holds through their later writes; anyone who may read it follows it
/// from its page or by the tool, a visitor is asked to sign in, and deleting the Tangent takes its follows with it.</summary>
public sealed class FollowTests
{
    private static readonly string Cy = Persona.Id("cy4follows");
    private static readonly string Dee = Persona.Id("dee4follows");
    private static readonly string Head = Persona.Id("head4follows");

    private static async Task<JsonElement> Door(Browser session) => (await Send(session.Client, HttpMethod.Get, "/", json: true)).Json;

    private static JsonElement[] Following(JsonElement door)
        => [.. door.GetProperty("news").GetProperty("following").GetProperty("topics").EnumerateArray()];

    private static async Task<Post> Answered(Post root, string author, string body, Post parent)
        => Assert.IsType<Outcome<Post>.Done>(await new Replied(Tids.Mint(), root.Id, body, author, parent.Id).Send(Cancellation)).Result;

    [Fact]
    public async Task Writing_follows_a_topic_its_new_replies_stand_in_the_news_once_each_reading_clears_them_and_stopping_holds()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Cy, [Roles.Member], ct: Cancellation);
        var root = await Started(Ada, "Lanterns", "Who lights theirs first?");
        var bea = await Replied(root, Bea, "I do, at six.");
        await Answered(root, Cy, "Six is late.", bea);
        using var ada = await SignedIn(tangentSpace, Ada);

        // Bea's reply is under replies; Cy's, which answers Bea, under the Topic Ada follows, and each counts once.
        var door = await Door(ada);
        var followed = Assert.Single(Following(door));
        Assert.Equal((root.Id, "Lanterns", 1L), (followed.GetProperty("topicId").GetString(), followed.GetProperty("title").GetString(), followed.GetProperty("new").GetInt64()));
        Assert.Equal((1, 2), (door.GetProperty("news").GetProperty("replies").GetProperty("count").GetInt32(), door.GetProperty("who").GetProperty("forYou").GetProperty("count").GetInt32()));
        var page = await Send(ada.Client, HttpMethod.Get, "/");
        Assert.Contains("Topics you follow · 1", page.Body);
        Assert.Contains("new in 1 topic you follow", page.Body);

        // Reading the Topic to its end clears it.
        var topic = (await Send(ada.Client, HttpMethod.Get, TopicPath(root), json: true)).Json;
        Assert.True(topic.GetProperty("following").GetBoolean());
        Assert.Equal("false", Valued(topic.GetProperty("follow"), "follow"));
        Assert.IsType<Outcome<Checkpoint>.Done>(await new Seen(Ada, topic.GetProperty("through").GetInt64(), root.Id).Send(Cancellation));
        Assert.Empty(Following(await Door(ada)));

        // She stops through the tool: Cy's next reply is not hers, and replying again does not follow it again.
        using (var tools = await ToolClient.Connect(tangentSpace, ada))
        {
            var stopped = await tools.Call("follow_topic", new { tangent = Workshop, topic = root.Slug, follow = false });
            Assert.False(stopped.Failed, stopped.Text);
            Assert.False(stopped.Structured!.Value.GetProperty("following").GetBoolean());
        }
        await Answered(root, Cy, "Earlier is better.", bea);
        Assert.Empty(Following(await Door(ada)));
        await Replied(root, Ada, "Then I light mine at five.");
        Assert.False((await Follow.Get(Follow.Key(Ada, root.Id), Cancellation))!.On);
        await Answered(root, Cy, "Five it is.", bea);
        Assert.Empty(Following(await Door(ada)));
    }

    [Fact]
    public async Task A_topic_followed_again_counts_what_came_after_though_a_reply_before_it_is_already_for_the_follower()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Cy, [Roles.Member], ct: Cancellation);
        var root = await Started(Ada, "Lanterns", "Who lights theirs first?");
        Assert.IsType<Outcome<Follow>.Done>(await new FollowTopic(Ada, root.Id, false).Send(Cancellation));
        var bea = await Replied(root, Bea, "I do, at six.");
        Assert.IsType<Outcome<Follow>.Done>(await new FollowTopic(Ada, root.Id).Send(Cancellation));
        await Answered(root, Cy, "Six is late.", bea);
        using var ada = await SignedIn(tangentSpace, Ada);

        // Bea's reply came before Ada followed again and is under replies; Cy's, after it, under the Topic, each once.
        var door = await Door(ada);
        var followed = Assert.Single(Following(door));
        Assert.Equal((root.Id, 1L), (followed.GetProperty("topicId").GetString(), followed.GetProperty("new").GetInt64()));
        Assert.Equal((1, 2), (door.GetProperty("news").GetProperty("replies").GetProperty("count").GetInt32(), door.GetProperty("who").GetProperty("forYou").GetProperty("count").GetInt32()));

        // Her own answer to Bea, written before she read the Topic, is nothing new to her: Cy's alone is.
        await Answered(root, Ada, "Six is fine by me.", bea);
        Assert.Equal(1L, Assert.Single(Following(await Door(ada))).GetProperty("new").GetInt64());
    }

    [Fact]
    public async Task Past_five_followed_topics_the_door_draws_five_and_opens_the_rest_in_place()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var roots = new List<Post>();
        for (var number = 1; number <= 6; number++)
        {
            var root = await Started(Bea, "Lantern " + number, "Who lights this one?");
            await Replied(root, Ada, "I do.");
            await Replied(root, Mo, "Me too.");
            roots.Add(root);
        }
        using var ada = await SignedIn(tangentSpace, Ada);

        var following = (await Door(ada)).GetProperty("news").GetProperty("following");
        Assert.Equal((6, false), (following.GetProperty("count").GetInt32(), following.GetProperty("atLeast").GetBoolean()));
        var newest = Enumerable.Reverse(roots).ToList();
        Assert.Equal(newest.Select(root => root.Id), Following(await Door(ada)).Select(topic => topic.GetProperty("topicId").GetString()));

        // The newest five are links under the heading; the oldest waits behind one line, which opens it in place.
        var html = (await Send(ada.Client, HttpMethod.Get, "/")).Body;
        Assert.Contains("new in 6 topics you follow", html);
        var heading = html.IndexOf("Topics you follow · 6", StringComparison.Ordinal);
        var line = html.IndexOf("<details class=\"door-news-more\"><summary>and 1 more</summary>", heading, StringComparison.Ordinal);
        Assert.True(heading > 0 && line > heading, "The Topics followed have no line of the rest.");
        Assert.All(newest.Take(Space.Door.Group.Drawn), root => Assert.InRange(html.IndexOf(TopicPath(root) + "\"", heading, StringComparison.Ordinal), heading, line));
        Assert.True(html.IndexOf(TopicPath(newest[^1]) + "\"", heading, StringComparison.Ordinal) > line);
    }

    [Fact]
    public async Task Someone_who_never_wrote_follows_from_the_topic_page_a_visitor_is_asked_to_sign_in_and_a_deleted_tangent_takes_its_follows()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Dee, [Roles.Member], ct: Cancellation);
        await Given.AParticipant(tangentSpace, Head, [Roles.Administrator], ct: Cancellation);
        var root = await Started(Ada, "Lanterns", "Who lights theirs first?");
        var bea = await Replied(root, Bea, "I do, at six.");
        using var dee = await SignedIn(tangentSpace, Dee);

        var html = (await Send(dee.Client, HttpMethod.Get, TopicPath(root))).Body;
        Assert.Contains("class=\"follow-topic\"", html);
        Assert.Contains("Follow it to have its new replies come to your door.", html);
        var followed = await Form(dee, TopicPath(root), TopicPath(root) + "/follow", new() { ["follow"] = "true" });
        Assert.Equal((HttpStatusCode.SeeOther, BaseUrl + TopicPath(root)), (followed.Status, followed.Location));
        Assert.Contains(">Unfollow</button>", (await Send(dee.Client, HttpMethod.Get, TopicPath(root))).Body);

        // Mo answers Bea: Dee never read the Topic, so it stands as new with no count.
        await Answered(root, Mo, "Six is fine.", bea);
        var shown = Assert.Single(Following(await Door(dee)));
        Assert.Equal(root.Id, shown.GetProperty("topicId").GetString());
        Assert.True(!shown.TryGetProperty("new", out var count) || count.ValueKind == JsonValueKind.Null);

        var visitor = await Send(Agent(tangentSpace), HttpMethod.Post, TopicPath(root) + "/follow", JsonBody(new { }), json: true, page: TopicPath(root));
        Assert.Equal("sign-in", visitor.Word);

        Assert.IsType<Outcome<Tangents.Tangent>.Done>(await new DeleteTangentRow(Workshop, Head, 1).Send(Cancellation));
        Assert.Empty(await Follow.All(Cancellation));
    }
}
