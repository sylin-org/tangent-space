using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Koan.Data.Core.Commands;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Space;
using Tangent.Tools;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Access;

/// <summary>Whatever door someone tries, a refusal tells them the one thing to do next: the Doorway judges who the reader is and what the
/// address names once, before anything they sent is read, and a person reads the sentence, an agent the word, and someone not signed in
/// the challenge that names the proof door.</summary>
public sealed class DoorwayTests
{
    private static readonly string Ivy = Persona.Id("ivy4doorway");

    private static string Challenge => ProofChallenge.Scheme + " proof=\"" + BaseUrl + ProofDoor.StatementPath + "\"";

    [Fact]
    public async Task A_visitor_of_a_private_place_is_asked_to_sign_in_to_read_and_never_to_write()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Given.AnEstablishedTangentSpace(tangentSpace, "Field notes", Audience.Private, ct: Cancellation);
        await Given.ATangent(tangentSpace, Workshop, ct: Cancellation);
        using var visitor = Agent(tangentSpace);

        var page = await Send(visitor, HttpMethod.Get, "/t/" + Workshop);
        var json = await Send(visitor, HttpMethod.Get, "/t/" + Workshop, json: true);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/t/" + Workshop);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        using var signIn = await visitor.SendAsync(request, Cancellation);

        Assert.Equal([Challenge], signIn.Headers.WwwAuthenticate.Select(challenge => challenge.ToString()));
        Assert.Equal(HttpStatusCode.Unauthorized, page.Status);
        Assert.Contains("Sign in to read", page.Body);
        Assert.Contains("/sign-in", page.Body);
        Assert.DoesNotContain("to write", page.Body);
        Assert.Equal((HttpStatusCode.Unauthorized, Capabilities.SignIn.Word, Capabilities.SignInToRead.Text),
            (json.Status, json.Word, json.Json.GetProperty("detail").GetString()));
    }

    [Fact]
    public async Task A_visitor_who_sends_a_malformed_reply_is_told_to_sign_in_before_the_body_is_read_with_the_challenge_naming_the_proof_door()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var root = await Started(Ada, "Lanterns", "How do you hang yours?");
        using var visitor = Agent(tangentSpace);
        var before = await Counted();

        foreach (var malformed in new[] { "[]", "{\"body\":", "{\"body\":\"\",\"parent\":{\"x\":1}}" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, TopicPath(root)) { Content = new StringContent(malformed, Encoding.UTF8, "application/json") };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await visitor.SendAsync(request, Cancellation);
            using var problem = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));

            Assert.Equal((HttpStatusCode.Unauthorized, Capabilities.SignIn.Word, Capabilities.SignIn.Text),
                (response.StatusCode, problem.RootElement.GetProperty("word").GetString(), problem.RootElement.GetProperty("detail").GetString()));
            Assert.Equal([Challenge], response.Headers.WwwAuthenticate.Select(challenge => challenge.ToString()));
        }

        var form = await Send(visitor, HttpMethod.Post, TopicPath(root), FormBody(new Dictionary<string, string> { ["body"] = "" }));
        Assert.Equal(HttpStatusCode.Unauthorized, form.Status);
        Assert.Contains(Capabilities.SignIn.Text, form.Body);
        Assert.DoesNotContain(Post.NeedsBody.Text, form.Body);
        Assert.Equal(before, await Counted());
    }

    [Fact]
    public async Task A_timed_out_participant_who_replies_is_told_until_when()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var root = await Started(Ada, "Lanterns", "How do you hang yours?");
        var until = DateTimeOffset.UtcNow.AddDays(1);
        var row = (await Participant.Get(Bea, Cancellation))!;
        row.Standing = Standing.TimedOut;
        row.StandingUntil = until;
        Assert.IsType<Outcome<Participant>.Done>(await new Store<Participant>(row).Send(Cancellation));
        using var bea = await SignedIn(tangentSpace, Bea);

        var refused = await Json(bea, TopicPath(root), TopicPath(root), new { id = Tids.Mint(), body = "Still here?" });
        var page = await Form(bea, TopicPath(root), TopicPath(root), new Dictionary<string, string> { ["id"] = Tids.Mint(), ["body"] = "Still here?" });

        Assert.Equal((HttpStatusCode.Forbidden, Capabilities.TimedOut.Word, Capabilities.TimedOutUntil(until).Text),
            (refused.Status, refused.Word, refused.Json.GetProperty("detail").GetString()));
        Assert.Contains("until " + Tangent.Render.Wording.Until(until), System.Net.WebUtility.HtmlDecode(page.Body));
    }

    [Fact]
    public async Task Removing_ones_own_post_again_answers_the_post_and_changes_nothing()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var root = await Started(Ada, "Lanterns", "How do you hang yours?");
        using var ada = await SignedIn(tangentSpace, Ada);
        using var bea = await SignedIn(tangentSpace, Bea);
        var remove = PostPath(root, root) + "/remove";

        var first = await Json(ada, TopicPath(root), remove, new { });
        var counted = await Counted();
        var again = await Json(ada, TopicPath(root), remove, new { });
        var anothers = await Json(bea, TopicPath(root), remove, new { });

        Assert.Equal(HttpStatusCode.OK, first.Status);
        Assert.Equal((HttpStatusCode.OK, first.Body), (again.Status, again.Body));
        Assert.Equal((HttpStatusCode.Forbidden, Capabilities.Removed.Word), (anothers.Status, anothers.Word));
        Assert.Equal(counted, await Counted());
    }

    [Fact]
    public async Task An_applicant_of_a_private_place_who_opens_a_topic_is_told_they_wait_on_a_page_naming_them_that_no_cache_keeps()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Given.AnEstablishedTangentSpace(tangentSpace, "Field notes", Audience.Private, Admission.Apply, Cancellation);
        await Given.ATangent(tangentSpace, Workshop, ct: Cancellation);
        await Given.AParticipant(tangentSpace, Ada, [Roles.Member], ct: Cancellation);
        await Given.AParticipant(tangentSpace, Ivy, [], Standing.Applied, Cancellation);
        var root = await Started(Ada, "Lanterns", "How do you hang yours?");
        using var ivy = await SignedIn(tangentSpace, Ivy);

        var page = await Send(ivy.Client, HttpMethod.Get, TopicPath(root));
        var json = await Send(ivy.Client, HttpMethod.Get, TopicPath(root), json: true);
        using var request = new HttpRequestMessage(HttpMethod.Get, TopicPath(root));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        using var cached = await ivy.Client.SendAsync(request, Cancellation);

        Assert.Equal(HttpStatusCode.Forbidden, page.Status);
        Assert.Contains(">Waiting to be admitted</h1>", page.Body);
        Assert.Contains(Capabilities.ApplyToRead.Text, page.Body);
        Assert.Contains("Signed in as", page.Body);
        Assert.Equal((true, true), (cached.Headers.CacheControl?.NoCache == true, cached.Headers.CacheControl?.NoStore == true));
        Assert.Equal((HttpStatusCode.Forbidden, Capabilities.Apply.Word, Capabilities.ApplyToRead.Text),
            (json.Status, json.Word, json.Json.GetProperty("detail").GetString()));
        Assert.DoesNotContain("How do you hang yours?", page.Body);
    }

    [Fact]
    public async Task An_agent_at_the_tool_door_is_told_to_sign_in_as_a_visitor_and_told_the_deciding_word_for_a_moderators_act()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var root = await Started(Bea, "Lanterns", "How do you hang yours?");
        using var visitor = await ToolClient.Connect(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);
        using var adaTools = await ToolClient.Connect(tangentSpace, ada);

        var reply = await visitor.Call("reply", new { tangent = Workshop, topic = root.Slug, body = "Hello" });
        var edit = await adaTools.Call("edit", new { tangent = Workshop, topic = root.Slug, post = root.Id, body = "Mine now.", version = 1 });
        var settings = await adaTools.Call("change_settings", new { name = "Ada's place" });

        Assert.Equal((401, true, Capabilities.SignIn.Word), (reply.Status, reply.Failed, reply.Structured!.Value.GetProperty("word").GetString()));
        Assert.Equal((403, true, Capabilities.NotYours.Word), (edit.Status, edit.Failed, edit.Structured!.Value.GetProperty("word").GetString()));
        Assert.Equal((403, Capabilities.NoRoleGrants(Bit.ManageSpace).Word), (settings.Status, settings.Structured!.Value.GetProperty("word").GetString()));
        Assert.Equal("Field notes", (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!.Name);
    }

    [Fact]
    public async Task Search_answers_every_query_it_is_given_and_a_topic_read_by_its_roots_tid_moves_to_its_slugs_address()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var root = await Started(Ada, "Lanterns", "How do you hang yours?");
        using var visitor = await ToolClient.Connect(tangentSpace);

        foreach (var asked in new object[] { new { q = "lanterns " + new string('a', 500) }, new { q = "" }, new { q = "lanterns", page = "1000" }, new { q = "lanterns", page = "2" } })
        {
            var search = await visitor.Call("search", asked);
            Assert.Equal((200, false), (search.Status, search.Failed));
        }

        var moved = await visitor.Call("read_topic", new { tangent = Workshop, topic = root.Id });
        Assert.Equal(BaseUrl + TopicPath(root), moved.Location);
        Assert.InRange(moved.Status, 300, 399);
    }
}
