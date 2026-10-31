using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Koan.Identity.Roles;
using Koan.Web.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Events;
using Tangent.Posts;
using Tangent.Space;
using Tangent.Writer;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Participants;

/// <summary>An agent with an atproto identity of its own passes a service-auth token, signed with a real key and read against
/// the document a loopback directory serves, and is signed in exactly as a browser's sign-in ends: a session cookie, under
/// the same admission and roles as anyone, with no browser and no consent page.</summary>
public sealed class ProofDoorTests
{
    private const string Addressed = "did:web:tangent.test#tangent";
    private const string Exchange = ProofDoor.ExchangePath;

    private static readonly AgentAccount Scout = new("did:plc:scoutscoutscoutscoutscou", "scout.agent.example");

    private static Task<TangentSpaceFixture> Start(AgentDirectory directory)
        => TangentSpaceFixture.StartAsync(directory.Settings(Settings()), ct: Cancellation, environment: "Development");

    private static string Proof(AgentAccount account) => account.Token(Addressed, ProofDoor.Method);

    /// <summary>A client that keeps cookies, signed in by passing a new token.</summary>
    private static async Task<Browser> SignedInByProof(TangentSpaceFixture tangentSpace, AgentAccount account)
    {
        var session = new Browser(tangentSpace);
        var answer = await Send(session.Client, HttpMethod.Post, Exchange, json: true, bearer: Proof(account));
        Assert.True(answer.Status == HttpStatusCode.NoContent, answer.Body);
        return session;
    }

    private static async Task<JsonElement> Read(Browser session, string path)
    {
        var answer = await Send(session.Client, HttpMethod.Get, path, json: true);
        Assert.True(answer.Status == HttpStatusCode.OK, $"{path} answered {(int)answer.Status}: {answer.Body}");
        return answer.Json;
    }

    [Fact]
    public async Task A_valid_token_signs_the_agent_in_and_later_pages_read_and_write_as_it_until_it_signs_out()
    {
        await using var directory = await AgentDirectory.Start(Scout);
        await using var tangentSpace = await Start(directory);
        var writer = await Place(tangentSpace);
        var root = await Started(writer, Ada, "Lanterns at dusk", "Who lights theirs first?");

        // The token alone signs in; a body says nothing and is not read, however large.
        using var session = new Browser(tangentSpace);
        var proved = await Send(session.Client, HttpMethod.Post, Exchange, new StringContent(new string(' ', 9000), Encoding.UTF8, "application/json"), json: true, bearer: Proof(Scout));
        Assert.True(proved.Status == HttpStatusCode.NoContent, proved.Body);

        // It arrived as anyone does: a participant on its verified handle, in the member role, with a session of its own.
        var participant = await Participant.Get(Scout.Did, Cancellation);
        Assert.Equal((Scout.Handle, Standing.Admitted), (participant?.Handle, participant?.Standing));
        Assert.Contains(Roles.Member, (await tangentSpace.Services.GetRequiredService<RoleCollection>().ForPerson(Scout.Did, Cancellation)).Select(role => role.Key));
        Assert.Single(await Koan.Identity.Session.Query(row => row.IdentityId == Scout.Did, Cancellation));

        // A later page, by the cookie alone, reads as it; opening its door is its own opening.
        var door = await Read(session, "/");
        var who = door.GetProperty("who");
        Assert.Equal((Scout.Did, Scout.Handle), (who.GetProperty("id").GetString(), who.GetProperty("handle").GetString()));
        await Eventually(async () => await Checkpoint.Get(Checkpoint.At(Scout.Did, Scope.Place, 0).Id, Cancellation) is not null);

        // It writes by the form a page gives it, with the form's token, as any session does.
        var page = await Read(session, TopicPath(root));
        var replied = await Follow(session, page.GetProperty("reply"), new Dictionary<string, string> { [PostWrites.BodyField] = "Whoever is first home." });
        Assert.True(replied.Status == HttpStatusCode.Created, replied.Body);
        Assert.Equal("Whoever is first home.", Assert.Single(await Post.Query(post => post.AuthorId == Scout.Did, Cancellation)).Body);

        // Signing out ends it, the way a browser's ends.
        (await session.Follow(HttpMethod.Post, who.GetProperty("signOut").GetString()![BaseUrl.Length..], Cancellation)).Dispose();
        Assert.False((await Send(session.Client, HttpMethod.Get, "/", json: true)).Json.TryGetProperty("who", out var after) && after.ValueKind == JsonValueKind.Object);
    }

    [Fact]
    public async Task An_applicant_who_proves_itself_reads_what_a_visitor_reads_and_is_offered_no_write()
    {
        await using var directory = await AgentDirectory.Start(Scout);
        await using var tangentSpace = await Start(directory);
        var writer = await Place(tangentSpace);
        var root = await Started(writer, Ada, "Lanterns at dusk", "Who lights theirs first?");
        var space = (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!;
        space.Admission = Admission.Apply;
        Assert.IsType<Outcome<TangentSpace>.Done>(await writer.Send(new Store<TangentSpace>(space), Cancellation));

        using var session = await SignedInByProof(tangentSpace, Scout);

        Assert.Equal(Standing.Applied, (await Participant.Get(Scout.Did, Cancellation))?.Standing);
        Assert.DoesNotContain(Roles.Member, (await tangentSpace.Services.GetRequiredService<RoleCollection>().ForPerson(Scout.Did, Cancellation)).Select(role => role.Key));
        Assert.Equal(Capabilities.Apply, (await Read(session, "/")).GetProperty("gate").GetString());
        var page = await Read(session, TopicPath(root));
        Assert.False(page.TryGetProperty("reply", out var reply) && reply.ValueKind == JsonValueKind.Object);
    }

    [Fact]
    public async Task A_banned_agent_finds_the_place_absent_in_the_session_it_holds_and_in_a_new_one_as_a_browser_would()
    {
        await using var directory = await AgentDirectory.Start(Scout);
        await using var tangentSpace = await Start(directory);
        var writer = await Place(tangentSpace);
        using var held = await SignedInByProof(tangentSpace, Scout);
        Assert.IsType<Outcome<Participant>.Done>(await writer.Send(new Ban(Mo, Scout.Did), Cancellation));

        Assert.Equal(Capabilities.Absent, (await Send(held.Client, HttpMethod.Get, "/", json: true)).Gate);

        using var again = await SignedInByProof(tangentSpace, Scout);
        Assert.Equal(Capabilities.Absent, (await Send(again.Client, HttpMethod.Get, "/", json: true)).Gate);
        Assert.Equal(Standing.Banned, (await Participant.Get(Scout.Did, Cancellation))?.Standing);
    }

    [Theory]
    [InlineData("a bad signature")]
    [InlineData("another place")]
    [InlineData("the place without its door")]
    [InlineData("another method")]
    [InlineData("expired")]
    [InlineData("a malformed issuer")]
    [InlineData("not a token")]
    public async Task A_forged_misaddressed_or_expired_token_signs_no_one_in(string flaw)
    {
        await using var directory = await AgentDirectory.Start(Scout);
        await using var tangentSpace = await Start(directory);
        await Place(tangentSpace);
        var token = flaw switch
        {
            "a bad signature" => new AgentAccount(Scout.Did, Scout.Handle).Token(Addressed, ProofDoor.Method),
            "another place" => Scout.Token("did:web:elsewhere.example#tangent", ProofDoor.Method),
            "the place without its door" => Scout.Token("did:web:tangent.test", ProofDoor.Method),
            "another method" => Scout.Token(Addressed, "com.atproto.server.getSession"),
            "expired" => Scout.Token(Addressed, ProofDoor.Method, TimeSpan.FromMinutes(2)),
            "a malformed issuer" => new AgentAccount("did:web:evil.example%2Fpath", Scout.Handle).Token(Addressed, ProofDoor.Method),
            _ => "not-a-token",
        };
        using var session = new Browser(tangentSpace);

        var answer = await Send(session.Client, HttpMethod.Post, Exchange, json: true, bearer: token);

        Assert.Equal((HttpStatusCode.Unauthorized, "invalid_service_proof"), (answer.Status, answer.Json.GetProperty("error").GetString()));
        Assert.Null(await Participant.Get(Scout.Did, Cancellation));
        Assert.False((await Send(session.Client, HttpMethod.Get, "/", json: true)).Json.TryGetProperty("who", out var who) && who.ValueKind == JsonValueKind.Object);
    }

    [Fact]
    public async Task A_request_with_no_token_is_told_so()
    {
        await using var directory = await AgentDirectory.Start(Scout);
        await using var tangentSpace = await Start(directory);
        await Place(tangentSpace);
        using var agent = Agent(tangentSpace);

        var none = await Send(agent, HttpMethod.Post, Exchange, json: true);

        Assert.Equal((HttpStatusCode.Unauthorized, "service_proof_required"), (none.Status, none.Json.GetProperty("error").GetString()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_audience_is_the_address_the_place_holds_however_a_request_names_the_host(bool configured)
    {
        await using var directory = await AgentDirectory.Start(Scout);
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(directory.Settings(configured ? Settings() : Unaddressed()), ct: Cancellation, environment: "Development");
        await Place(tangentSpace);
        if (!configured) await tangentSpace.Services.GetRequiredService<KoanWebBaseUrl>().Accept(BaseUrl, Cancellation);
        using var agent = Agent(tangentSpace);

        var misaddressed = await At(agent, Exchange, "evil.example", Scout.Token("did:web:evil.example#tangent", ProofDoor.Method));
        Assert.Equal((HttpStatusCode.Unauthorized, "invalid_service_proof"), (misaddressed.Status, misaddressed.Json.GetProperty("error").GetString()));
        Assert.Null(await Participant.Get(Scout.Did, Cancellation));

        var proved = await At(agent, Exchange, "evil.example", Proof(Scout));
        Assert.True(proved.Status == HttpStatusCode.NoContent, proved.Body);
        var statement = await At(agent, ProofDoor.StatementPath, "evil.example");
        Assert.Equal(Addressed, statement.Json.GetProperty("serviceProof").GetProperty("audience").GetString());
    }

    [Fact]
    public async Task A_place_with_no_address_of_its_own_takes_no_token_whatever_the_host_says_and_says_why_and_a_sign_in_gives_it_one()
    {
        await using var directory = await AgentDirectory.Start(Scout);
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(directory.Settings(Unaddressed()), ct: Cancellation, environment: "Development");
        using var agent = Agent(tangentSpace);
        var baseUrl = tangentSpace.Services.GetRequiredService<KoanWebBaseUrl>();
        Assert.Null(baseUrl.Current);

        var refused = await At(agent, Exchange, "evil.example", Scout.Token("did:web:evil.example#tangent", ProofDoor.Method));
        Assert.Equal((HttpStatusCode.ServiceUnavailable, "exchange_unconfigured", ProofController.NoAddress),
            (refused.Status, refused.Json.GetProperty("error").GetString(), refused.Json.GetProperty("message").GetString()));
        var statement = await Send(agent, HttpMethod.Get, ProofDoor.StatementPath, json: true);
        Assert.Equal((HttpStatusCode.ServiceUnavailable, "exchange_unconfigured"), (statement.Status, statement.Json.GetProperty("error").GetString()));
        Assert.Null(await Participant.Get(Scout.Did, Cancellation));
        Assert.Null(await TangentSpace.Get(TangentSpace.SingleRow, Cancellation));

        // Signing in through atproto sets the place up and gives it its address, and a token addressed there is taken.
        using (var setter = new Browser(tangentSpace))
            (await setter.SignIn("leo", "did:plc:leoleoleoleoleoleoleoleol", Cancellation, handle: "leo.example")).Dispose();
        Assert.NotNull(await TangentSpace.Get(TangentSpace.SingleRow, Cancellation));
        var place = Assert.IsType<string>(baseUrl.Current);
        var proved = await Send(agent, HttpMethod.Post, Exchange, json: true, bearer: Scout.Token(ProofDoor.Audience(place), ProofDoor.Method));
        Assert.True(proved.Status == HttpStatusCode.NoContent, proved.Body);
    }

    [Fact]
    public async Task An_established_place_with_no_address_states_no_proof_door_and_llms_txt_says_why()
    {
        await using var directory = await AgentDirectory.Start(Scout);
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(directory.Settings(Unaddressed()), ct: Cancellation, environment: "Development");
        await Given.AnEstablishedTangentSpace(tangentSpace, "Field notes", ct: Cancellation);
        using var agent = Agent(tangentSpace);

        Assert.False((await Send(agent, HttpMethod.Get, "/tangent.json", json: true)).Json.TryGetProperty("proof", out _));
        var llms = await agent.GetStringAsync("/llms.txt", Cancellation);
        Assert.Contains("This place has none yet", llms);
        Assert.Contains("signing in through atproto gives it one", llms);
        Assert.DoesNotContain(ProofDoor.Method, llms);
    }

    [Fact]
    public async Task An_agent_proving_itself_first_at_a_configured_address_sets_a_fresh_place_up_as_its_proprietor_and_operator()
    {
        await using var directory = await AgentDirectory.Start(Scout);
        await using var tangentSpace = await Start(directory);
        using var agent = Agent(tangentSpace);
        Assert.Null(await TangentSpace.Get(TangentSpace.SingleRow, Cancellation));
        Assert.Equal(HttpStatusCode.OK, (await Send(agent, HttpMethod.Get, ProofDoor.StatementPath, json: true)).Status);

        using var session = await SignedInByProof(tangentSpace, Scout);

        var space = await TangentSpace.Get(TangentSpace.SingleRow, Cancellation);
        Assert.Equal(("Scout's Tangent Space", Scout.Did, Scout.Did), (space?.Name, space?.ProprietorId, space?.OperatorId));
        Assert.Equal(Scout.Did, (await Read(session, "/")).GetProperty("who").GetProperty("id").GetString());
    }

    [Fact]
    public async Task A_private_place_answers_the_door_to_a_visitor_and_its_session_reads_what_the_agent_may()
    {
        await using var directory = await AgentDirectory.Start(Scout);
        await using var tangentSpace = await Start(directory);
        var writer = await Place(tangentSpace);
        var space = (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!;
        space.Audience = Audience.Private;
        Assert.IsType<Outcome<TangentSpace>.Done>(await writer.Send(new Store<TangentSpace>(space), Cancellation));
        using var agent = Agent(tangentSpace);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(agent, HttpMethod.Get, "/", json: true)).Status);
        Assert.Equal(HttpStatusCode.OK, (await Send(agent, HttpMethod.Get, ProofDoor.StatementPath, json: true)).Status);

        using var session = await SignedInByProof(tangentSpace, Scout);

        Assert.Equal(Scout.Did, (await Read(session, "/")).GetProperty("who").GetProperty("id").GetString());
    }

    [Fact]
    public async Task The_statement_the_descriptor_and_llms_txt_say_how_to_prove_oneself_here()
    {
        await using var directory = await AgentDirectory.Start(Scout);
        await using var tangentSpace = await Start(directory);
        await Given.AnEstablishedTangentSpace(tangentSpace, "Field notes", ct: Cancellation);
        using var agent = Agent(tangentSpace);

        var statement = (await Send(agent, HttpMethod.Get, ProofDoor.StatementPath, json: true)).Json;
        Assert.Equal(BaseUrl, statement.GetProperty("canonicalOrigin").GetString());
        var proof = statement.GetProperty("serviceProof");
        Assert.Equal((ProofDoor.Method, ProofDoor.Method, Addressed, "authorization_header", BaseUrl + Exchange),
            (proof.GetProperty("method").GetString(), proof.GetProperty("purpose").GetString(), proof.GetProperty("audience").GetString(),
                proof.GetProperty("transport").GetString(), proof.GetProperty("endpoint").GetString()));
        Assert.Equal(["ES256", "ES256K"], proof.GetProperty("algorithms").EnumerateArray().Select(algorithm => algorithm.GetString()));

        var descriptor = (await Send(agent, HttpMethod.Get, "/tangent.json", json: true)).Json.GetProperty("proof");
        Assert.Equal((BaseUrl + ProofDoor.StatementPath, BaseUrl + Exchange, Addressed, ProofDoor.Method),
            (descriptor.GetProperty("statement").GetString(), descriptor.GetProperty("exchange").GetString(), descriptor.GetProperty("audience").GetString(), descriptor.GetProperty("method").GetString()));

        var llms = await agent.GetStringAsync("/llms.txt", Cancellation);
        Assert.Contains("com.atproto.server.getServiceAuth", llms);
        Assert.Contains("aud=" + Addressed, llms);
        Assert.Contains("lxm=" + ProofDoor.Method, llms);
        Assert.Contains(BaseUrl + Exchange, llms);
        Assert.Contains("keep the cookie", llms);
        Assert.Contains("`" + TokenHeader + "`", llms);
    }

    [Theory]
    [InlineData("http://127.0.0.1:5220", "did:web:localhost%3A5220#tangent")]
    [InlineData("http://localhost", "did:web:localhost#tangent")]
    [InlineData("https://tangent.sylin.org", "did:web:tangent.sylin.org#tangent")]
    public void The_audience_is_the_one_a_client_derives_from_the_address(string baseUrl, string audience)
        => Assert.Equal(audience, ProofDoor.Audience(baseUrl));

    /// <summary>The settings of a place where no address is configured, so each request's own stands in for one.</summary>
    private static Dictionary<string, string?> Unaddressed()
    {
        var settings = Settings();
        settings[TangentModule.BaseUrlKey] = "";
        return settings;
    }

    /// <summary>A request whose <c>Host</c> header the caller chose, with a token when there is one.</summary>
    private static async Task<Answered> At(HttpClient agent, string path, string host, string? bearer = null)
    {
        using var request = new HttpRequestMessage(bearer is null ? HttpMethod.Get : HttpMethod.Post, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Host = host;
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        using var response = await agent.SendAsync(request, Cancellation);
        return new Answered(response.StatusCode, response.Headers.Location?.ToString(), response.Content.Headers.ContentType?.MediaType,
            await response.Content.ReadAsStringAsync(Cancellation));
    }

    private static async Task Eventually(Func<Task<bool>> holds)
    {
        for (var tries = 0; tries < 50; tries++)
        {
            if (await holds()) return;
            await Task.Delay(100, Cancellation);
        }

        Assert.Fail("It did not hold within five seconds.");
    }
}
