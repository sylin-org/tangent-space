using System.Net;
using System.Text.Json;
using Koan.Data.Core.Commands;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Space;
using Tangent.Tools;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Events;

/// <summary>What is new since you were here, told once and truthfully: a reply reaches its author's door once, with the
/// count every page's masthead shows; reading its Topic clears it there, on the Tangent page and on the Topic page; one
/// <c>seen</c> from the door clears everything; and an agent at <c>/mcp</c> meets the same through <c>arrive</c>,
/// <c>read_topic</c> and <c>seen</c>. The count holds only what is for the reader, that someone else did and that
/// they may view, and past its bound it reads "at least".</summary>
public sealed class UnreadTests
{
    private static readonly string Leo = Persona.Id("leo4unread");

    [Fact]
    public async Task A_reply_reaches_its_authors_door_once_reading_the_topic_clears_it_and_seen_from_the_door_clears_everything()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var topic = await Started(Ada, "Lanterns", "Who lights theirs first?");
        using var ada = await SignedIn(tangentSpace, Ada);
        Assert.True((await Door(ada)).GetProperty("news").GetProperty("quiet").GetBoolean());

        // Bea replies in Ada's Topic: Ada's door tells it once, with the count, and her Tangent page counts the Topic new.
        var reply = await Replied(topic, Bea, "I do, at six.");
        var door = await Door(ada);
        var news = door.GetProperty("news");
        Assert.Equal((1, reply.Id), (news.GetProperty("replies").GetProperty("count").GetInt32(), news.GetProperty("replies").GetProperty("posts")[0].GetProperty("postId").GetString()));
        Assert.Equal(1, door.GetProperty("who").GetProperty("forYou").GetProperty("count").GetInt32());
        var html = (await Send(ada.Client, HttpMethod.Get, "/")).Body;
        Assert.Contains("Replies to you · 1", html);
        Assert.Contains("<span class=\"masthead-unseen-count\">1</span>", html);
        Assert.Contains($"<form class=\"seen\" method=\"post\" action=\"{BaseUrl}{SeenController.Path}\">", html);
        Assert.Equal((true, (long?)1), await OnTangentPage(ada, topic));

        // Opening the Topic writes nothing; the page marks the reply new and carries the form its island posts.
        var page = (await Send(ada.Client, HttpMethod.Get, TopicPath(topic), json: true)).Json;
        Assert.True(Assert.Single(page.GetProperty("replies").EnumerateArray()).GetProperty("new").GetBoolean());
        Assert.Equal(1, (await Door(ada)).GetProperty("news").GetProperty("replies").GetProperty("count").GetInt32());
        var seen = page.GetProperty("seen");
        Assert.Equal(HttpStatusCode.SeeOther, (await Form(ada, TopicPath(topic), SeenController.Path,
            new Dictionary<string, string> { [SeenController.TopicField] = Valued(seen, SeenController.TopicField)!, [SeenController.ThroughField] = Valued(seen, SeenController.ThroughField)! })).Status);

        // Read, it clears everywhere it was told.
        door = await Door(ada);
        Assert.True(door.GetProperty("news").GetProperty("quiet").GetBoolean());
        Assert.Equal(0, door.GetProperty("who").GetProperty("forYou").GetProperty("count").GetInt32());
        Assert.Equal((false, (long?)0), await OnTangentPage(ada, topic));
        Assert.Contains("Nothing new since you were here.", (await Send(ada.Client, HttpMethod.Get, "/")).Body);

        // Something new elsewhere and here again; seen from the door clears all of it, on every page.
        var other = await Started(Bea, "Wicks", "Cotton or hemp?");
        var again = await Replied(topic, Bea, "Six sharp.");
        news = (await Door(ada)).GetProperty("news");
        Assert.Equal(1, news.GetProperty("replies").GetProperty("count").GetInt32());
        Assert.Equal(2, news.GetProperty("perTangent").EnumerateArray().Sum(tangent => tangent.GetProperty("topics").GetInt32() + tangent.GetProperty("replies").GetInt32()));
        Assert.Equal((true, (long?)null), await OnTangentPage(ada, other));
        var through = news.GetProperty("through").GetInt64();
        Assert.Equal(through, long.Parse(Valued(news.GetProperty("seen"), SeenController.ThroughField)!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(HttpStatusCode.SeeOther, (await Form(ada, "/", SeenController.Path,
            new Dictionary<string, string> { [SeenController.ThroughField] = through.ToString(System.Globalization.CultureInfo.InvariantCulture) })).Status);

        door = await Door(ada);
        Assert.True(door.GetProperty("news").GetProperty("quiet").GetBoolean());
        Assert.Equal(0, door.GetProperty("who").GetProperty("forYou").GetProperty("count").GetInt32());
        Assert.Equal((false, (long?)0), await OnTangentPage(ada, topic));
        Assert.Equal((false, (long?)0), await OnTangentPage(ada, other));
        var reread = (await Send(ada.Client, HttpMethod.Get, PostPath(topic, again), json: true)).Json;
        Assert.All(reread.GetProperty("replies").EnumerateArray(), post => Assert.False(post.GetProperty("new").GetBoolean()));
    }

    [Fact]
    public async Task An_agent_at_mcp_meets_the_same_through_arrive_read_topic_and_seen()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var topic = await Started(Ada, "Lanterns", "Who lights theirs first?");
        using var ada = await SignedIn(tangentSpace, Ada);
        using var tools = await ToolClient.Connect(tangentSpace, ada);

        async Task<JsonElement> Worked(string name, object arguments)
        {
            var called = await tools.Call(name, arguments);
            Assert.False(called.Failed, name + ": " + called.Text);
            return called.Structured ?? default;
        }

        var reply = await Replied(topic, Bea, "I do, at six.");
        var arrived = await Worked("arrive", new { });
        Assert.Equal(reply.Id, Assert.Single(arrived.GetProperty("news").GetProperty("replies").GetProperty("posts").EnumerateArray()).GetProperty("postId").GetString());
        Assert.Equal(1, arrived.GetProperty("who").GetProperty("forYou").GetProperty("count").GetInt32());

        var read = await Worked("read_topic", new { tangent = Workshop, topic = topic.Slug });
        Assert.True(Assert.Single(read.GetProperty("replies").EnumerateArray()).GetProperty("new").GetBoolean());
        await Worked("seen", new { topic = read.GetProperty("id").GetString(), through = read.GetProperty("through").GetInt64() });
        arrived = await Worked("arrive", new { });
        Assert.True(arrived.GetProperty("news").GetProperty("quiet").GetBoolean());

        await Replied(topic, Bea, "Six sharp.");
        await Started(Bea, "Wicks", "Cotton or hemp?");
        arrived = await Worked("arrive", new { });
        Assert.False(arrived.GetProperty("news").GetProperty("quiet").GetBoolean());
        await Worked("seen", new { through = arrived.GetProperty("news").GetProperty("through").GetInt64() });
        arrived = await Worked("arrive", new { });
        Assert.True(arrived.GetProperty("news").GetProperty("quiet").GetBoolean());
        Assert.Equal(0, arrived.GetProperty("who").GetProperty("forYou").GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task The_count_holds_only_what_is_for_the_reader_that_someone_else_did_and_that_they_may_view_and_a_hidden_row_moves_nothing()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        var mine = await Started(Ada, "Lanterns", "Who lights theirs first?");
        await Replied(mine, Bea, "I do, at six.");
        await Started(Bea, "A question for Ada", $"Over to [@ada](/p/{Ada}).");
        await Replied(mine, Ada, "And mine at seven.");
        Assert.IsType<Outcome<Event>.Done>(await new Logged(EventKind.StandingChanged, Ada, Ada).Send(Cancellation));
        await Started(Ada, "Over to Bea", $"Your turn, [@bea](/p/{Bea}).");
        var newest = await Event.Log.Highest(Cancellation);
        var secret = await Started(Leo, "Studio notes", "Only for us.", Studio);
        Assert.IsType<Outcome<Event>.Done>(await new Logged(EventKind.Mention, Leo, Ada, secret).Send(Cancellation));

        using var ada = await SignedIn(tangentSpace, Ada);
        var forYou = (await Door(ada)).GetProperty("who").GetProperty("forYou");
        Assert.Equal((2, false, BaseUrl + "/"), (forYou.GetProperty("count").GetInt32(), forYou.GetProperty("atLeast").GetBoolean(), forYou.GetProperty("door").GetString()));
        Assert.True(forYou.GetProperty("through").GetInt64() < newest + 1, "A row the reader may not view moves nothing they can see.");

        // Every page's masthead shows it, as a link to the door.
        foreach (var path in new[] { "/", TopicPath(mine), "/t/" + Workshop, "/t", "/p/ada4writes.example" })
        {
            var page = await Send(ada.Client, HttpMethod.Get, path);
            Assert.Equal(HttpStatusCode.OK, page.Status);
            Assert.Contains($"<a class=\"masthead-unseen\" href=\"{BaseUrl}/\" aria-label=\"2 new for you\"", Collapsed(page.Body));
            Assert.Contains("<span class=\"masthead-unseen-count\">2</span>", page.Body);
        }

        // Reading the Topic the reply is in clears the reply, and the mention in another Topic stays.
        var read = (await Send(ada.Client, HttpMethod.Get, TopicPath(mine), json: true)).Json.GetProperty("seen");
        await Form(ada, TopicPath(mine), SeenController.Path,
            new Dictionary<string, string> { [SeenController.TopicField] = mine.Id, [SeenController.ThroughField] = Valued(read, SeenController.ThroughField)! });
        Assert.Equal(1, (await Door(ada)).GetProperty("who").GetProperty("forYou").GetProperty("count").GetInt32());

        using var mo = await SignedIn(tangentSpace, Mo);
        Assert.Contains("<a class=\"masthead-unseen\" href=\"" + BaseUrl + "/\" hidden aria-label=\"0 new for you\"", Collapsed((await Send(mo.Client, HttpMethod.Get, "/")).Body));
        using var bea = await SignedIn(tangentSpace, Bea);
        Assert.Equal(1, (await Door(bea)).GetProperty("who").GetProperty("forYou").GetProperty("count").GetInt32());
        Assert.DoesNotContain("masthead-unseen", (await Send(Agent(tangentSpace), HttpMethod.Get, "/")).Body);
    }

    [Fact]
    public async Task Past_its_bound_the_count_reads_at_least()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var theirs = await Started(Bea, "Lanterns", "Who lights theirs first?");
        Assert.IsType<Outcome<int>.Done>(await new Many(EventKind.Mention, Bea, Ada, theirs, Unread.CountLimit + 5).Send(Cancellation));
        using var ada = await SignedIn(tangentSpace, Ada);

        var door = await Door(ada);
        var forYou = door.GetProperty("who").GetProperty("forYou");
        Assert.True(forYou.GetProperty("atLeast").GetBoolean());
        Assert.Equal(Unread.CountLimit + 1, forYou.GetProperty("count").GetInt32());
        Assert.Equal(await Event.Log.Highest(Cancellation), forYou.GetProperty("through").GetInt64());
        Assert.True(door.GetProperty("news").GetProperty("mentions").GetProperty("atLeast").GetBoolean());
        var html = WebUtility.HtmlDecode((await Send(ada.Client, HttpMethod.Get, "/")).Body);
        Assert.Contains("<span class=\"masthead-unseen-count\">99+</span>", html);
        Assert.Contains($"aria-label=\"At least {Unread.CountLimit + 1} new for you\"", html);
        Assert.Contains("Mentions of you · at least", html);
    }

    [Fact]
    public async Task A_timeout_is_told_on_the_door_the_count_counts_with_until_when()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var until = DateTimeOffset.UtcNow.AddHours(1);
        Assert.IsType<Outcome<Participant>.Done>(await new TimeOut(Mo, Ada, until).Send(Cancellation));
        using var ada = await SignedIn(tangentSpace, Ada);

        var door = await Door(ada);
        var news = door.GetProperty("news");
        Assert.Equal((1, false), (door.GetProperty("who").GetProperty("forYou").GetProperty("count").GetInt32(), news.GetProperty("quiet").GetBoolean()));
        Assert.Equal(Mo, news.GetProperty("timedOut").GetProperty("who").GetProperty("id").GetString());
        Assert.Equal(until.ToUnixTimeSeconds(), news.GetProperty("timedOut").GetProperty("until").GetDateTimeOffset().ToUnixTimeSeconds());
        Assert.True(news.TryGetProperty("seen", out var seen) && seen.ValueKind == JsonValueKind.Object);
        var html = WebUtility.HtmlDecode((await Send(ada.Client, HttpMethod.Get, "/")).Body);
        Assert.Contains("Since you were here: you were timed out.", html);
        Assert.Contains("timed you out until", html);
        Assert.DoesNotContain("Nothing new since you were here.", html);
    }

    [Fact]
    public async Task A_timeout_that_has_ended_is_told_as_ended()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var then = DateTimeOffset.UtcNow.AddHours(-2);
        Assert.IsType<Outcome<Participant>.Done>(await new TimeOut(Mo, Ada, then.AddHours(1), then).Send(Cancellation));
        using var ada = await SignedIn(tangentSpace, Ada);

        var door = await Door(ada);
        Assert.Equal(1, door.GetProperty("who").GetProperty("forYou").GetProperty("count").GetInt32());
        Assert.False(door.GetProperty("news").GetProperty("quiet").GetBoolean());
        Assert.False(door.TryGetProperty("gate", out var gate) && gate.ValueKind == JsonValueKind.String);
        Assert.Contains("It has ended, and you can start topics and reply here again.", WebUtility.HtmlDecode((await Send(ada.Client, HttpMethod.Get, "/")).Body));
    }

    [Fact]
    public async Task A_declined_application_is_told_on_the_applicants_door_the_count_counts()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Applying(tangentSpace);
        Assert.IsType<Outcome<Participant>.Done>(await new FirstArrival(Grace, Persona.Did(Grace), "grace4unread.example").Send(Cancellation));
        Assert.IsType<Outcome<Participant>.Done>(await new Decline(Mo, Grace).Send(Cancellation));
        using var grace = await SignedIn(tangentSpace, Grace);

        var door = await Door(grace);
        var news = door.GetProperty("news");
        Assert.Equal((1, false), (door.GetProperty("who").GetProperty("forYou").GetProperty("count").GetInt32(), news.GetProperty("quiet").GetBoolean()));
        Assert.Equal(Mo, news.GetProperty("declined").GetProperty("who").GetProperty("id").GetString());
        var html = WebUtility.HtmlDecode((await Send(grace.Client, HttpMethod.Get, "/")).Body);
        Assert.Contains("Since you were here: your application was declined.", html);
        Assert.Contains("declined your application", html);
    }

    [Fact]
    public async Task A_deciders_door_whose_only_news_is_a_waiting_application_is_not_quiet_and_seen_clears_it()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Applying(tangentSpace);
        Assert.IsType<Outcome<Participant>.Done>(await new FirstArrival(Grace, Persona.Did(Grace), "grace4unread.example").Send(Cancellation));
        using var mo = await SignedIn(tangentSpace, Mo);

        var door = await Door(mo);
        var news = door.GetProperty("news");
        Assert.Equal((1, false), (door.GetProperty("who").GetProperty("forYou").GetProperty("count").GetInt32(), news.GetProperty("quiet").GetBoolean()));
        Assert.Equal(1, news.GetProperty("applications").GetProperty("count").GetInt32());
        Assert.Contains("Since you were here: 1 new application.", WebUtility.HtmlDecode((await Send(mo.Client, HttpMethod.Get, "/")).Body));

        await Form(mo, "/", SeenController.Path, new Dictionary<string, string> { [SeenController.ThroughField] = Valued(news.GetProperty("seen"), SeenController.ThroughField)! });
        door = await Door(mo);
        Assert.Equal((0, true, 1), (door.GetProperty("who").GetProperty("forYou").GetProperty("count").GetInt32(),
            door.GetProperty("news").GetProperty("quiet").GetBoolean(), door.GetProperty("applications").GetProperty("count").GetInt32()));
    }

    /// <summary>The writing place, whose admission says apply.</summary>
    private static async Task Applying(TangentSpaceFixture tangentSpace)
    {
        await Place(tangentSpace);
        var space = (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!;
        space.Admission = Admission.Apply;
        Assert.IsType<Outcome<TangentSpace>.Done>(await new Store<TangentSpace>(space).Send(Cancellation));
    }

    private static readonly string Grace = Persona.Id("grace4unread");

    private static async Task<JsonElement> Door(Participants.Browser browser) => (await Send(browser.Client, HttpMethod.Get, "/", json: true)).Json;

    /// <summary>What the Tangent page says is new in the Topic: whether it is, and how many of its Posts, null where it is
    /// new with no count.</summary>
    private static async Task<(bool New, long? Count)> OnTangentPage(Participants.Browser browser, Post root)
    {
        var topics = (await Send(browser.Client, HttpMethod.Get, "/t/" + root.TangentId, json: true)).Json.GetProperty("topics");
        var row = topics.EnumerateArray().Single(topic => topic.GetProperty("id").GetString() == root.Id);
        return (row.GetProperty("new").GetBoolean(),
            row.TryGetProperty("newPosts", out var count) && count.ValueKind == JsonValueKind.Number ? count.GetInt64() : null);
    }

    /// <summary>The page's markup with each run of whitespace between tags taken out, so a tag spread over lines reads as
    /// one.</summary>
    private static string Collapsed(string html) => System.Text.RegularExpressions.Regex.Replace(html, @">\s+<", "><");

    /// <summary>Appends one event of <paramref name="Kind"/> by <paramref name="ActorId"/> targeting
    /// <paramref name="TargetId"/>, about <paramref name="Post"/> where one is named: for a proof that needs one no
    /// command writes, such as a mention in a Tangent its target may not view.</summary>
    private sealed record Logged(string Kind, string ActorId, string TargetId, Post? Post = null) : ICommand<Event>
    {
        public async Task<Outcome<Event>> Run(CancellationToken ct)
        {
            return new Outcome<Event>.Done(await Event.Append(Kind, DateTimeOffset.UtcNow, ct, tangentId: Post?.TangentId, topicId: Post?.TopicId, postId: Post?.Id,
                actorId: ActorId, targetId: TargetId));
        }
    }

    /// <summary>Appends <paramref name="Times"/> events of <paramref name="Kind"/> about <paramref name="Root"/> by
    /// <paramref name="ActorId"/> targeting <paramref name="TargetId"/>, in one transaction.</summary>
    private sealed record Many(string Kind, string ActorId, string TargetId, Post Root, int Times) : ICommand<int>
    {
        public async Task<Outcome<int>> Run(CancellationToken ct)
        {
            for (var index = 0; index < Times; index++)
                await Event.Append(Kind, DateTimeOffset.UtcNow, ct, tangentId: Root.TangentId, topicId: Root.Id, postId: Root.Id,
                    actorId: ActorId, targetId: TargetId);
            return new Outcome<int>.Done(Times);
        }
    }
}
