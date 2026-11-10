using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Koan.Data.Core.Commands;
using Koan.Identity.Roles;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Capture;
using Tangent.Events;
using Tangent.Space;
using Tangent.Tangents;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Posts;

/// <summary>Reports: any participant who can see a Post reports it in a sentence, and the report reaches the moderators
/// as a Topic in their own Tangent, taken on a tangent from what was reported, without the reported author or anyone
/// else who may not view that Tangent learning of it.</summary>
public sealed partial class ReportTests
{
    private static readonly string Leo = Persona.Id("leo4reports");
    private static readonly string Keeper = Persona.Id("keeper4reports");
    private const string Moderators = ModeratorsTangent.Slug;

    [Fact]
    public async Task First_run_creates_the_moderators_tangent_private_and_recorded_and_a_start_creates_it_once_for_a_place_without_one()
    {
        await using (var established = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation))
        {
            Assert.IsType<Outcome<TangentSpace>.Done>(await new Establish(Leo, Persona.Did(Leo), "leo.example", "Field notes").Send(Cancellation));
            await Given.AParticipant(established, Ada, [Roles.Member], ct: Cancellation);
            await Given.AParticipant(established, Mo, [Roles.Moderator], ct: Cancellation);

            var space = await TangentSpace.Get(TangentSpace.SingleRow, Cancellation);
            Assert.Equal((Moderators, 1L), (space?.ModeratorsTangentId, space?.Version));
            await AssertModerators(established);
            var created = Assert.Single(await Event.Query(e => e.Kind == EventKind.TangentChanged && e.TangentId == Moderators, Cancellation));
            Assert.Equal((Moderators, Leo, Audience.Private), (created.TangentId, created.ActorId, created.Audience));
            // Members find nothing there; moderators and the operator, an administrator, read, reply and start Topics.
            Assert.False((await Capabilities.Load(Ada, Moderators, null, Cancellation)).Exists);
            Assert.True((await Capabilities.Load(Mo, Moderators, null, Cancellation)).Holds(Bit.View | Bit.Post | Bit.Topic));
            await established.Services.GetRequiredService<RoleCollection>().Add(Roles.Administrator, Leo, Cancellation);
            Assert.True((await Capabilities.Load(Leo, Moderators, null, Cancellation)).Holds(Bit.View | Bit.Post | Bit.Topic));
        }

        // A place established without one: the next start creates it once, and the start after that changes nothing.
        await using var first = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var operatorRow = new TangentSpace { Name = "Field notes", OperatorId = Leo, ProprietorId = Leo };
        Assert.IsType<Outcome<TangentSpace>.Done>(await new Store<TangentSpace>(operatorRow).Send(Cancellation));
        await first.StopAsync(Cancellation);

        await using var second = await TangentSpaceFixture.StartAsync(Over(first), ct: Cancellation);
        await Drained(second);
        var repaired = await TangentSpace.Get(TangentSpace.SingleRow, Cancellation);
        var counted = await Counted();
        Assert.Equal((Moderators, 2L), (repaired?.ModeratorsTangentId, repaired?.Version));
        await AssertModerators(second);
        Assert.Equal([(EventKind.TangentChanged, Leo), (EventKind.SpaceChanged, Leo)],
            (await Event.All(Cancellation)).OrderBy(e => e.Seq).Select(e => (e.Kind, e.ActorId)).ToList());
        await second.StopAsync(Cancellation);

        await using var third = await TangentSpaceFixture.StartAsync(Over(first), ct: Cancellation);
        await Drained(third);
        var unchanged = await TangentSpace.Get(TangentSpace.SingleRow, Cancellation);
        Assert.Equal((Moderators, 2L), (unchanged?.ModeratorsTangentId, unchanged?.Version));
        Assert.Equal(counted, await Counted());
        Assert.Single(await Tangents.Tangent.All(Cancellation));
    }

    [Fact]
    public async Task A_member_reports_a_reply_and_a_topic_starts_in_the_moderators_tangent_with_the_origin_and_the_reason_which_the_member_never_sees()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Reporting(tangentSpace);
        var topic = await Started(Bea, "Lanterns at dusk", "Who lights theirs first?");
        var reply = await Replied(topic, Bea, "Whoever is slowest, obviously.");
        using var ada = await SignedIn(tangentSpace, Ada);

        var page = await Send(ada.Client, HttpMethod.Get, TopicPath(topic));
        var (action, minted) = ReportForm(page.Body, topic, reply);
        var sent = await Form(ada, TopicPath(topic), action, new Dictionary<string, string> { ["id"] = minted, ["body"] = "This mocks someone in the workshop." });

        Assert.False((await Capabilities.Load(Ada, Moderators, null, Cancellation)).Holds(Bit.Topic));
        Assert.Equal((HttpStatusCode.SeeOther, AddressOf(topic, reply) + "/report?received=" + minted), (sent.Status, sent.Location));
        var report = await Post.Get(minted, Cancellation);
        Assert.NotNull(report);
        Assert.Equal((Moderators, (string?)null, reply.Id, Ada, "Report: Lanterns at dusk"),
            (report.TangentId, report.ParentId, report.OriginId, report.AuthorId, report.Title));
        // It opens with the reason, then the reported words, quoted and linked to them.
        Assert.StartsWith("This mocks someone in the workshop.\n\n> [", report.Body);
        Assert.EndsWith("](" + AddressOf(topic, reply) + ")", report.Body);
        Assert.Equal(1, (await Tangents.Tangent.Get(Moderators, Cancellation))?.TopicCount);

        var received = await Send(ada.Client, HttpMethod.Get, new Uri(sent.Location!).PathAndQuery);
        var receivedJson = await Send(ada.Client, HttpMethod.Get, new Uri(sent.Location!).PathAndQuery, json: true);
        var moderatorsTopic = await Send(ada.Client, HttpMethod.Get, "/t/" + Moderators + "/" + report.Slug);

        Assert.Equal(HttpStatusCode.OK, received.Status);
        Assert.Contains("Report received", received.Body);
        Assert.True(receivedJson.Json.GetProperty("received").GetBoolean());
        foreach (var answer in new[] { received.Body, receivedJson.Body })
        {
            Assert.DoesNotContain("/t/" + Moderators, answer);
            Assert.DoesNotContain(report.Slug!, answer);
        }

        Assert.Equal(HttpStatusCode.NotFound, moderatorsTopic.Status);
        // Another reader naming Ada's report reads the form, and nothing of it.
        using var bea = await SignedIn(tangentSpace, Bea);
        var notTheirs = await Send(bea.Client, HttpMethod.Get, new Uri(sent.Location!).PathAndQuery, json: true);
        Assert.False(notTheirs.Json.GetProperty("received").GetBoolean());
    }

    [Fact]
    public async Task The_reported_author_and_the_reporter_read_nothing_of_the_report_and_a_moderator_reads_it_as_news_linked_to_its_origin()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Reporting(tangentSpace);
        var topic = await Started(Bea, "Lanterns at dusk", "Who lights theirs first?");
        var reply = await Replied(topic, Bea, "Whoever is slowest, obviously.");
        var since = (await Event.All(Cancellation)).Max(e => e.Seq);
        var report = await Reported(Ada, reply, "This mocks someone in the workshop.");
        using var bea = await SignedIn(tangentSpace, Bea);
        using var ada = await SignedIn(tangentSpace, Ada);
        using var mo = await SignedIn(tangentSpace, Mo);

        var created = Assert.Single(await Event.Query(e => e.Kind == EventKind.PostCreated && e.PostId == report.Id, Cancellation));
        Assert.Null(created.TargetId);
        foreach (var reader in new[] { bea, ada })
        {
            var door = await Send(reader.Client, HttpMethod.Get, "/", json: true);
            var doorPage = await Send(reader.Client, HttpMethod.Get, "/");
            var origin = await Send(reader.Client, HttpMethod.Get, PostPath(topic, reply), json: true);
            var originPage = await Send(reader.Client, HttpMethod.Get, PostPath(topic, reply));
            var poll = await Send(reader.Client, HttpMethod.Get, EventsController.Path + "?since=" + since, json: true);
            foreach (var answer in new[] { door.Body, doorPage.Body, origin.Body, originPage.Body, poll.Body })
            {
                Assert.DoesNotContain(report.Id, answer);
                Assert.DoesNotContain("/t/" + Moderators, answer);
                Assert.DoesNotContain("mocks someone", answer);
                Assert.DoesNotContain("Report: ", answer);
            }

            var news = door.Json.GetProperty("news");
            Assert.Equal(0, news.TryGetProperty("taken", out var taken) && taken.ValueKind != JsonValueKind.Null ? taken.GetProperty("count").GetInt32() : 0);
            Assert.DoesNotContain(news.GetProperty("perTangent").EnumerateArray(), tangent => tangent.GetProperty("slug").GetString() == Moderators);
            Assert.DoesNotContain(door.Json.GetProperty("tangents").EnumerateArray(), tangent => tangent.GetProperty("slug").GetString() == Moderators);
            Assert.DoesNotContain("post-taken", originPage.Body);
            Assert.Equal(1, origin.Json.GetProperty("replyCount").GetInt32());
            Assert.Equal([], poll.Json.GetProperty("events").EnumerateArray().Select(row => row.GetProperty("post").GetString()));
        }

        var moDoor = await Send(mo.Client, HttpMethod.Get, "/", json: true);
        var moTopic = await Send(mo.Client, HttpMethod.Get, TopicPath(report));
        var moOrigin = await Send(mo.Client, HttpMethod.Get, PostPath(topic, reply), json: true);
        var moPoll = await Send(mo.Client, HttpMethod.Get, EventsController.Path + "?since=" + since, json: true);

        var moderators = Assert.Single(moDoor.Json.GetProperty("news").GetProperty("perTangent").EnumerateArray(),
            tangent => tangent.GetProperty("slug").GetString() == Moderators);
        Assert.Equal(1, moderators.GetProperty("topics").GetInt32());
        Assert.Equal(HttpStatusCode.OK, moTopic.Status);
        Assert.Contains("mocks someone", moTopic.Body);
        Assert.Contains("href=\"" + AddressOf(topic, reply) + "\"", Between(moTopic.Body, "topic-taken-from"));
        Assert.Equal(["Report: Lanterns at dusk"], Taken(moOrigin.Json, reply.Id));
        Assert.Contains(report.Id, moPoll.Json.GetProperty("events").EnumerateArray().Select(row => row.GetProperty("post").GetString()));
    }

    [Fact]
    public async Task Reports_in_the_moderators_tangent_never_crowd_out_the_topics_a_reader_may_view_or_make_a_page_say_at_least()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Reporting(tangentSpace);
        var topic = await Started(Bea, "Lanterns at dusk", "Who lights theirs first?");
        var reply = await Replied(topic, Bea, "Whoever is slowest, obviously.");
        Assert.IsType<Outcome<Post>.Done>(await new Started(Tids.Mint(), Workshop, "Slow lanterns", "", Ada, OriginId: reply.Id).Send(Cancellation));
        for (var reporter = 0; reporter < Origins.TakenRead; reporter++)
        {
            var id = Persona.Id($"reporter{reporter}4reports");
            await Given.AParticipant(tangentSpace, id, [Roles.Member], ct: Cancellation);
            await Reported(id, reply, "Unkind.");
        }

        using var bea = await SignedIn(tangentSpace, Bea);
        using var mo = await SignedIn(tangentSpace, Mo);
        var asAuthor = await Send(bea.Client, HttpMethod.Get, PostPath(topic, reply), json: true);
        var asAuthorPage = await Send(bea.Client, HttpMethod.Get, PostPath(topic, reply));
        var asModerator = await Send(mo.Client, HttpMethod.Get, PostPath(topic, reply), json: true);

        var listed = TakenOf(asAuthor.Json, reply.Id);
        Assert.Equal((1, false), (listed.GetProperty("count").GetInt32(), listed.GetProperty("atLeast").GetBoolean()));
        Assert.Contains("Taken on a tangent in 1 topic:", asAuthorPage.Body);
        Assert.True(TakenOf(asModerator.Json, reply.Id).GetProperty("atLeast").GetBoolean());
    }

    [Fact]
    public async Task A_visitor_is_refused_with_sign_in_a_post_the_reporter_may_not_view_is_absent_and_the_same_tid_twice_is_one_report()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Reporting(tangentSpace);
        var topic = await Started(Bea, "Lanterns at dusk", "Who lights theirs first?");
        var reply = await Replied(topic, Bea, "Whoever is slowest, obviously.");
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        var studio = await Started(Leo, "Quiet planning", "The studio’s private words.", Studio);
        using var visitor = Agent(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);
        var action = PostPath(topic, reply) + "/report";
        var counted = await Counted();

        var refused = await Send(visitor, HttpMethod.Post, action, JsonBody(new { id = Tids.Mint(), body = "Unkind." }), json: true);
        var absent = await Json(ada, TopicPath(topic), PostPath(studio, studio) + "/report", new { id = Tids.Mint(), body = "Unkind." });
        var visitorForm = await Send(visitor, HttpMethod.Get, action);

        Assert.Equal((HttpStatusCode.Unauthorized, Capabilities.SignIn.Word), (refused.Status, refused.Word));
        Assert.Equal(HttpStatusCode.NotFound, absent.Status);
        Assert.Contains("data-word=\"sign-in\"", visitorForm.Body);
        Assert.DoesNotContain("name=\"id\"", visitorForm.Body);
        Assert.Equal(counted, await Counted());

        var minted = Tids.Mint();
        var fields = new Dictionary<string, string> { ["id"] = minted, ["body"] = "This mocks someone in the workshop." };
        var first = await Form(ada, action, action, new Dictionary<string, string>(fields));
        var afterFirst = await Counted();
        var again = await Form(ada, action, action, new Dictionary<string, string>(fields));
        var otherWords = await Json(ada, action, action, new { id = minted, body = "Something else." });
        var afterOtherWords = await Counted();

        Assert.Equal((HttpStatusCode.SeeOther, HttpStatusCode.SeeOther), (first.Status, again.Status));
        Assert.Equal(first.Location, again.Location);
        Assert.Equal(afterFirst, await Counted());
        Assert.Single(await Post.Query(post => post.OriginId == reply.Id, Cancellation));
        // The same id with other words is the same report: received, and nothing more is written.
        Assert.Equal(HttpStatusCode.OK, otherWords.Status);
        Assert.True(otherWords.Json.GetProperty("received").GetBoolean());
        Assert.Equal(afterFirst, afterOtherWords);

        // A report needs no words: one of the Topic's opening Post with none starts its own report, titled by the Topic.
        var rootAction = PostPath(topic, topic) + "/report";
        var wordless = Tids.Mint();
        var empty = await Form(ada, rootAction, rootAction, new Dictionary<string, string> { ["id"] = wordless, ["body"] = "   " });
        Assert.Equal(HttpStatusCode.SeeOther, empty.Status);
        var unsaid = await Post.Get(wordless, Cancellation);
        Assert.Equal((Moderators, topic.Id, "Report: Lanterns at dusk"), (unsaid!.TangentId, unsaid.OriginId, unsaid.Title));
        Assert.Equal("> [Who lights theirs first\\?](" + AddressOf(topic, topic) + ")", unsaid.Body);
        afterFirst = await Counted();
        var tooLong = await Form(ada, action, action, new Dictionary<string, string> { ["id"] = Tids.Mint(), ["body"] = new string('a', Post.BodyLimit + 1) });
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.Status);
        Assert.Contains("id=\"report-body-failure\">" + Post.BodyTooLong + "</p>", WebUtility.HtmlDecode(tooLong.Body));
        Assert.Equal(afterFirst, await Counted());
    }

    [Fact]
    public async Task Reporting_the_same_post_again_while_the_report_stands_replies_in_its_topic_and_after_its_removal_starts_another()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Reporting(tangentSpace);
        var topic = await Started(Bea, "Lanterns at dusk", "Who lights theirs first?");
        var reply = await Replied(topic, Bea, "Whoever is slowest, obviously.");

        var first = await Reported(Ada, reply, "This mocks someone in the workshop.");
        var second = await Reported(Ada, reply, "They edited it, and it is worse now.");
        var elsewhere = await Reported(Mo, reply, "Agreed, unkind.");

        Assert.Equal((first.Id, first.Id, Ada), (second.TopicId, second.ParentId, second.AuthorId));
        Assert.Equal(1, (await Post.Get(first.Id, Cancellation))?.ReplyCount);
        Assert.Null(elsewhere.ParentId);
        var created = Assert.Single(await Event.Query(e => e.Kind == EventKind.PostCreated && e.PostId == second.Id, Cancellation));
        Assert.Null(created.TargetId);

        Assert.IsType<Outcome<Post>.Done>(await new RemovePost(first.Id, Mo).Send(Cancellation));
        var third = await Reported(Ada, reply, "Still there.");

        Assert.Null(third.ParentId);
        Assert.NotEqual(first.Id, third.TopicId);
        Assert.Equal(reply.Id, third.OriginId);
    }

    [Fact]
    public async Task A_report_the_moderators_close_is_settled_and_the_next_report_of_that_post_starts_a_topic_of_its_own()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Reporting(tangentSpace);
        var topic = await Started(Bea, "Lanterns at dusk", "Who lights theirs first?");
        var reply = await Replied(topic, Bea, "Whoever is slowest, obviously.");
        var first = await Reported(Ada, reply, "This mocks someone in the workshop.");

        Assert.IsType<Outcome<Post>.Done>(await new CloseTopic(first.Id, Mo).Send(Cancellation));
        var next = await Reported(Ada, reply, "They did it again.");

        Assert.Equal(((string?)null, reply.Id), (next.ParentId, next.OriginId));
        Assert.StartsWith("They did it again.", next.Body);
        Assert.NotEqual(first.Id, next.TopicId);
        var settled = await Post.Get(first.Id, Cancellation);
        Assert.Equal((true, 0), (settled?.Closed, settled?.ReplyCount));
    }

    [Fact]
    public async Task A_report_of_a_post_the_moderators_may_not_view_carries_nothing_of_its_topic_and_shows_its_origin_as_unseen()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Reporting(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        await Given.AParticipant(tangentSpace, Keeper, [Roles.Administrator], ct: Cancellation);
        var secret = await Started(Leo, "Secret acquisition plans", "We buy the kiln shop next spring.", Studio);
        var report = await Reported(Keeper, secret, "This names a seller who asked to stay out of it.");
        using var mo = await SignedIn(tangentSpace, Mo);

        var moderators = await Send(mo.Client, HttpMethod.Get, "/t/" + Moderators);
        var door = await Send(mo.Client, HttpMethod.Get, "/");
        var doorJson = await Send(mo.Client, HttpMethod.Get, "/", json: true);
        var topic = await Send(mo.Client, HttpMethod.Get, TopicPath(report));

        Assert.Equal("Report: This names a seller who asked to stay out of it.", report.Title);
        Assert.Equal(HttpStatusCode.OK, topic.Status);
        foreach (var answer in new[] { moderators.Body, door.Body, doorJson.Body, topic.Body })
        {
            Assert.DoesNotContain("Secret acquisition", answer);
            Assert.DoesNotContain("kiln shop", answer);
            Assert.DoesNotContain("/t/" + Studio, answer);
        }

        Assert.Contains("This names a seller", topic.Body);
        Assert.Contains("Taken on a tangent from a post you can’t see", WebUtility.HtmlDecode(Between(topic.Body, "topic-taken-from")));
        // Its opening says who wrote it and where, and none of its words.
        var opening = await ReportPost.OpeningOf("Unkind.", secret, secret, (await Tangents.Tangent.Get(Studio, Cancellation))!,
            (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!, BaseUrl, Cancellation);
        Assert.Contains("which moderators can’t read", opening!.Text);
        Assert.DoesNotContain("kiln shop", opening.Text);
        Assert.DoesNotContain("/t/" + Studio, opening.Text);
        Assert.Contains("/p/", opening.Text);
        // Its author once they left reads as someone who left, with no profile to link to.
        var theirs = await Started(Keeper, "Kiln inventory", "Three shelves of bisque.", Studio);
        Assert.IsType<Outcome<Participants.LeavePlace.Gone>.Done>(await new Participants.LeavePlace(Keeper).Send(Cancellation));
        var left = await ReportPost.OpeningOf(null, theirs, theirs, (await Tangents.Tangent.Get(Studio, Cancellation))!,
            (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!, BaseUrl, Cancellation);
        Assert.Contains("A post by " + Render.Person.LeftName + " in ", left!.Text);
        Assert.DoesNotContain("/p/", left.Text);
        Assert.DoesNotContain(Keeper, left.Text);
        // Where the moderators may view the reported Tangent, the title names the reported Topic.
        var open = await Started(Bea, "Lanterns at dusk", "Who lights theirs first?");
        Assert.Equal("Report: Lanterns at dusk", (await Reported(Ada, open, "Unkind.")).Title);
    }

    [Fact]
    public async Task A_resend_after_the_moderators_removed_the_report_answers_received_and_says_nothing_of_what_they_did()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Reporting(tangentSpace);
        var topic = await Started(Bea, "Lanterns at dusk", "Who lights theirs first?");
        var reply = await Replied(topic, Bea, "Whoever is slowest, obviously.");
        using var ada = await SignedIn(tangentSpace, Ada);
        var action = PostPath(topic, reply) + "/report";
        var minted = Tids.Mint();
        var fields = new Dictionary<string, string> { ["id"] = minted, ["body"] = "This mocks someone in the workshop." };
        var first = await Form(ada, action, action, new Dictionary<string, string>(fields));
        Assert.IsType<Outcome<Post>.Done>(await new RemovePost(minted, Mo).Send(Cancellation));
        var counted = await Counted();

        var again = await Form(ada, action, action, new Dictionary<string, string>(fields));
        var json = await Json(ada, action, action, new { id = minted, body = "This mocks someone in the workshop." });
        var received = await Send(ada.Client, HttpMethod.Get, new Uri(again.Location!).PathAndQuery);

        Assert.Equal((HttpStatusCode.SeeOther, first.Location), (again.Status, again.Location));
        Assert.Equal(HttpStatusCode.OK, json.Status);
        Assert.True(json.Json.GetProperty("received").GetBoolean());
        Assert.Contains("Report received", received.Body);
        foreach (var answer in new[] { json.Body, received.Body })
        {
            Assert.DoesNotContain("emoved by", answer);
            Assert.DoesNotContain("This mocks someone", answer);
        }

        Assert.Equal(counted, await Counted());
    }

    [Fact]
    public async Task Report_is_offered_on_someone_elses_post_and_never_on_ones_own_or_inside_the_moderators_tangent()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Reporting(tangentSpace);
        var topic = await Started(Bea, "Lanterns at dusk", "Who lights theirs first?");
        var own = await Replied(topic, Ada, "At six, by the gate.");
        var report = await Reported(Ada, topic, "Unkind.");
        using var ada = await SignedIn(tangentSpace, Ada);
        using var mo = await SignedIn(tangentSpace, Mo);

        var page = await Send(ada.Client, HttpMethod.Get, TopicPath(topic));
        var json = await Send(ada.Client, HttpMethod.Get, TopicPath(topic), json: true);
        var moderators = await Send(mo.Client, HttpMethod.Get, TopicPath(report), json: true);
        var nested = await new ReportPost(Tids.Mint(), report.Id, PostBody.Of("Report the report."), Mo).Send(Cancellation);

        Assert.Contains("action=\"" + AddressOf(topic, topic) + "/report\"", page.Body);
        Assert.DoesNotContain("action=\"" + AddressOf(topic, own) + "/report\"", page.Body);
        Assert.False(Present(Shown(json.Json, own.Id), "report"));
        Assert.True(Present(Shown(json.Json, topic.Id), "report"));
        Assert.False(Present(moderators.Json.GetProperty("root"), "report"));
        Assert.Equal(ReportPost.InModerators, Assert.IsType<Outcome<Post>.Refused>(nested).Reason);
        var mine = await new ReportPost(Tids.Mint(), own.Id, PostBody.Of("Mine."), Ada).Send(Cancellation);
        Assert.Equal(ReportPost.Own, Assert.IsType<Outcome<Post>.Refused>(mine).Reason);

        // Its report address answers the refusal the post would get: no form, no Id.
        var form = await Send(mo.Client, HttpMethod.Get, PostPath(report, report) + "/report");
        var formJson = await Send(mo.Client, HttpMethod.Get, PostPath(report, report) + "/report", json: true);
        Assert.Equal((HttpStatusCode.BadRequest, HttpStatusCode.BadRequest), (form.Status, formJson.Status));
        Assert.Contains(ReportPost.InModerators.Text, WebUtility.HtmlDecode(form.Body));
        Assert.DoesNotContain("<form class=\"sign-in-form\"", form.Body);
        Assert.DoesNotContain("name=\"id\"", form.Body);
        Assert.False(Present(formJson.Json, "report"));
        Assert.Equal(ReportPost.InModerators.Text, formJson.Json.GetProperty("failure").GetString());
    }

    /// <summary>The place every proof of the write verbs starts from, with the moderators' Tangent a start creates for it.</summary>
    private static async Task Reporting(TangentSpaceFixture tangentSpace)
    {
        await Place(tangentSpace);
        Assert.IsType<Outcome<Tangents.Tangent>.Done>(await new CreateModeratorsTangent().Send(Cancellation));
    }

    private static async Task<Post> Reported(string reporter, Post post, string reason)
        => Assert.IsType<Outcome<Post>.Done>(await new ReportPost(Tids.Mint(), post.Id, PostBody.Of(reason), reporter).Send(Cancellation)).Result;

    /// <summary>The settings of a start over the data folder <paramref name="first"/> holds.</summary>
    private static Dictionary<string, string?> Over(TangentSpaceFixture first)
    {
        var settings = Settings();
        settings["Tangent:DataFolder"] = first.DataFolder;
        return settings;
    }

    /// <summary>Answers once every command sent so far has had its turn, since turns go in arrival order.</summary>
    private static async Task Drained(TangentSpaceFixture tangentSpace)
        => Assert.IsType<Outcome<bool>.Done>(await new Nothing().Send(Cancellation));

    /// <summary>The moderators' Tangent as first run or a start leaves it: its row, private, and the moderator role's
    /// overwrite allowing what moderators do there.</summary>
    private static async Task AssertModerators(TangentSpaceFixture tangentSpace)
    {
        var roles = tangentSpace.Services.GetRequiredService<RoleCollection>();
        var tangent = await Tangents.Tangent.Get(Moderators, Cancellation);
        Assert.Equal((ModeratorsTangent.Name, ModeratorsTangent.Order), (tangent?.Name, tangent?.Order));
        Assert.Equal(Audience.Private, await Overwrites.AudienceOf(Moderators, Cancellation));
        var moderator = Assert.Single((await roles.Overwrites(Overwrites.Resource(Moderators), ct: Cancellation)).Items,
            row => row.SubjectKind == OverwriteSubjectKind.Role && row.SubjectId == Roles.Moderator);
        Assert.Equal(Bits.Tokens(Bit.View | Bit.Post | Bit.Topic).Order(), moderator.Allow.Order());
    }

    /// <summary>The inline report form a page holds for <paramref name="post"/>: where it posts, under the Post's
    /// address, and the TID minted into it.</summary>
    private static (string Action, string Id) ReportForm(string html, Post root, Post post)
    {
        var action = PostPath(root, post) + "/report";
        var at = html.IndexOf("action=\"" + BaseUrl + action + "\"", StringComparison.Ordinal);
        Assert.True(at >= 0, "The page holds no report form for the post.");
        var id = ReportId().Match(html, at);
        Assert.True(id.Success);
        return (action, id.Groups["id"].Value);
    }

    private static string Between(string html, string marker)
    {
        var start = html.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"The page holds no {marker}.");
        return html[start..html.IndexOf("</p>", start, StringComparison.Ordinal)];
    }

    /// <summary>The titles of the Topics taken on a tangent from <paramref name="postId"/> a Topic page's JSON lists.</summary>
    private static IReadOnlyList<string> Taken(JsonElement page, string postId)
        => TakenOf(page, postId).GetProperty("topics").EnumerateArray().Select(topic => topic.GetProperty("title").GetString()!).ToList();

    private static JsonElement TakenOf(JsonElement page, string postId) => Shown(page, postId).GetProperty("taken");

    /// <summary>One Post as a Topic page's JSON shows it.</summary>
    private static JsonElement Shown(JsonElement page, string postId)
        => page.GetProperty("replies").EnumerateArray().Prepend(page.GetProperty("root")).Single(post => post.GetProperty("id").GetString() == postId);

    /// <summary>Whether a JSON object carries a value under <paramref name="name"/>.</summary>
    private static bool Present(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null;

    [GeneratedRegex("name=\"id\" value=\"(?<id>[^\"]+)\"")]
    private static partial Regex ReportId();

    /// <summary>A command that writes nothing, so its outcome says every command queued before it has run.</summary>
    private sealed record Nothing : ICommand<bool>
    {
        public Task<Outcome<bool>> Run(CancellationToken ct) => Task.FromResult<Outcome<bool>>(new Outcome<bool>.Done(true));
    }
}
