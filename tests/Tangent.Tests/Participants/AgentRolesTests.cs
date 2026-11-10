using System.Net;
using System.Text.Json;
using Koan.Data.Core.Commands;
using Koan.Identity.Roles;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Posts;
using Tangent.Space;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Participants;

/// <summary>An agent as itself is an ordinary participant: signed in through atproto with its own DID, labelled a bot on
/// its own profile, it holds the moderator or administrator role, or is the place's proprietor and operator, and does
/// what those allow through the addresses and forms the pages' JSON give it, under the same rules as anyone.</summary>
public sealed class AgentRolesTests
{
    private static readonly string Scout = Persona.Id("scout4agent");
    private const string ScoutHandle = "scout.agent.example";
    private static readonly string Rook = Persona.Id("rook4agent");
    private static readonly string Leo = Persona.Id("leo4agentroles");

    [Fact]
    public async Task An_agent_given_the_moderator_role_reads_a_report_moderates_the_post_and_times_out_a_member_by_the_forms_its_pages_give()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        Assert.IsType<Outcome<Tangents.Tangent>.Done>(await new CreateModeratorsTangent().Send(Cancellation));
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        var topic = await Started(Bea, "Lanterns at dusk", "Who lights theirs first?");
        var reply = await Replied(topic, Bea, "Whoever is slowest, obviously.");
        using var leo = await SignedIn(tangentSpace, Leo);
        using var ada = await SignedIn(tangentSpace, Ada);
        using var scout = await AgentArrives(tangentSpace, Scout, ScoutHandle);

        // Arriving, it reads who it is here: an agent, with its own profile's address, which answers as itself, and the
        // account menu's mark is an agent's.
        var self = Who(await Page(scout, "/"));
        Assert.Equal(("agent", BaseUrl + "/p/" + ScoutHandle), (self.GetProperty("kind").GetString(), self.GetProperty("profile").GetString()));
        Assert.Equal(Scout, (await Page(scout, Under(self.GetProperty("profile").GetString()!))).GetProperty("id").GetString());
        var html = (await Send(scout.Client, HttpMethod.Get, "/")).Body;
        var account = html[html.IndexOf("class=\"account-button\"", StringComparison.Ordinal)..html.IndexOf("class=\"account-panel", StringComparison.Ordinal)];
        Assert.Contains("participant is-agent is-you", account);

        // A member like any other: no settings, and the people are not theirs to read.
        Assert.False(self.TryGetProperty("settings", out var none) && none.ValueKind == JsonValueKind.String);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(scout.Client, HttpMethod.Get, PeopleController.Path, json: true)).Status);

        // Someone who holds ManageRoles finds it among the people, as an agent, and gives it the role with the one form.
        var people = await Page(leo, PeopleController.Path);
        var listed = Row(people, Scout);
        Assert.Equal(("agent", "admitted"), (listed.GetProperty("person").GetProperty("kind").GetString(), listed.GetProperty("standing").GetString()));
        Assert.Contains("· agent ·", WebUtility.HtmlDecode((await Send(leo.Client, HttpMethod.Get, PeopleController.Path)).Body));
        var given = await Follow(leo, people.GetProperty("forms").GetProperty("give"),
            new Dictionary<string, string> { [PeopleController.ParticipantField] = Scout, [PeopleController.RoleField] = "moderator" });
        Assert.True(given.Status == HttpStatusCode.OK, given.Body);

        // A report reaches it on its door, and the addresses lead from there to the reported Post.
        var report = Assert.IsType<Outcome<Post>.Done>(await new ReportPost(Tids.Mint(), reply.Id, PostBody.Of("This mocks someone."), Ada).Send(Cancellation)).Result;
        var door = await Page(scout, "/");
        Assert.Contains("moderator", door.GetProperty("news").GetProperty("roles").EnumerateArray().Select(role => role.GetProperty("role").GetString()));
        var entry = Assert.Single(door.GetProperty("news").GetProperty("reports").GetProperty("posts").EnumerateArray());
        Assert.Equal(BaseUrl + TopicPath(report), entry.GetProperty("address").GetString());
        var reportTopic = await Page(scout, Under(entry.GetProperty("address").GetString()!));
        Assert.Equal("This mocks someone.", reportTopic.GetProperty("root").GetProperty("body").GetString());
        var reported = await Page(scout, Under(reportTopic.GetProperty("takenFrom").GetProperty("address").GetString()!));
        var post = Assert.Single(reported.GetProperty("replies").EnumerateArray(), candidate => candidate.GetProperty("id").GetString() == reply.Id);

        // Its edit page holds both forms; an edit is marked with who made it, and the removal leaves a tombstone that says so.
        var edit = await Page(scout, Under(post.GetProperty("edit").GetString()!));
        var edited = await Follow(scout, edit.GetProperty("edit"), new Dictionary<string, string> { [PostWrites.BodyField] = "Edited by a moderator." });
        Assert.True(edited.Status == HttpStatusCode.OK, edited.Body);
        Assert.Equal(Scout, (await Post.Get(reply.Id, Cancellation))?.EditedBy);
        var removed = await Follow(scout, (await Page(scout, Under(post.GetProperty("edit").GetString()!))).GetProperty("remove"), new Dictionary<string, string>());
        Assert.True(removed.Status == HttpStatusCode.OK, removed.Body);
        Assert.Equal((true, Scout), ((await Post.Get(reply.Id, Cancellation))?.Removed, (await Post.Get(reply.Id, Cancellation))?.RemovedBy));
        using var bea = await SignedIn(tangentSpace, Bea);
        Assert.Contains(
            (await Page(bea, "/")).GetProperty("news").GetProperty("removed").GetProperty("posts").EnumerateArray(),
            told => told.GetProperty("who").GetProperty("id").GetString() == Scout);

        // The settings it may use are a link from its door: the people, whom it changes as a moderator may.
        var settings = await Page(scout, Under(Who(door).GetProperty("settings").GetString()!));
        var section = Assert.Single(settings.GetProperty("sections").EnumerateArray(), section => section.GetProperty("name").GetString() == TangentSpaceSettingsController.PeopleSection);
        var managed = await Page(scout, Under(section.GetProperty("address").GetString()!));
        Assert.Contains("timed-out", Strings(Row(managed, Ada), "can"));
        var forms = managed.GetProperty("forms");
        Assert.False(forms.TryGetProperty("give", out var give) && give.ValueKind != JsonValueKind.Null);
        // Sent as a form, with the token in the field it names, as well as one JSON object with the token in its header.
        var timedOut = await Follow(scout, forms.GetProperty("standing"),
            new Dictionary<string, string> { [PeopleController.ParticipantField] = Ada, [PeopleController.ToField] = "timed-out", [PeopleController.ForField] = "hour" }, json: false);
        Assert.True(timedOut.Status == HttpStatusCode.SeeOther, timedOut.Body);
        Assert.Equal(Capabilities.TimedOut.Word, (await Page(ada, "/")).GetProperty("gate").GetString());
        Assert.Equal((Standing.TimedOut, Scout), await StoodBy(Ada));

        // The same rules hold it: nothing against anyone not below it, and whoever stands above it times it out.
        Assert.Empty(Strings(Row(await Page(scout, PeopleController.Path), Leo), "can"));
        var refused = await Follow(scout, (await Page(scout, PeopleController.Path)).GetProperty("forms").GetProperty("standing"),
            new Dictionary<string, string> { [PeopleController.ParticipantField] = Leo, [PeopleController.ToField] = "banned" });
        Assert.Equal((HttpStatusCode.Forbidden, People.NotBelow.Text), (refused.Status, refused.Json.GetProperty("detail").GetString()));
        var timedOutAgent = await Follow(leo, (await Page(leo, PeopleController.Path)).GetProperty("forms").GetProperty("standing"),
            new Dictionary<string, string> { [PeopleController.ParticipantField] = Scout, [PeopleController.ToField] = "timed-out", [PeopleController.ForField] = "hour" });
        Assert.True(timedOutAgent.Status == HttpStatusCode.OK, timedOutAgent.Body);
        Assert.Equal(Capabilities.TimedOut.Word, (await Page(scout, "/")).GetProperty("gate").GetString());
    }

    [Fact]
    public async Task An_agent_that_signs_in_first_sets_the_place_up_as_its_proprietor_and_operator_and_an_agent_it_makes_administrator_manages_the_place()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);

        // Every address answers first run, and its challenge comes back to it.
        var first = await Visitor(tangentSpace, "/");
        var comeBack = Uri.UnescapeDataString(first.GetProperty("challenge").GetString()!.Split("return=")[1]);
        Assert.Equal(FirstRunController.Path, comeBack);

        using var scout = new Browser(tangentSpace);
        using (var landed = await scout.SignIn("scout", Scout, Cancellation, handle: ScoutHandle, comeBack: comeBack))
            Assert.Equal((HttpStatusCode.OK, FirstRunController.Path), (landed.StatusCode, landed.RequestMessage!.RequestUri!.AbsolutePath));
        await LabelledAnAgent(Scout);

        var space = await TangentSpace.Get(TangentSpace.SingleRow, Cancellation);
        Assert.Equal(("Scout's Tangent Space", Scout, Scout), (space?.Name, space?.ProprietorId, space?.OperatorId));
        Assert.Equal(ParticipantKind.Agent, (await Participant.Get(Scout, Cancellation))?.Kind);
        Assert.Contains(Roles.Administrator, (await tangentSpace.Services.GetRequiredService<RoleCollection>().Bag(Scout, authenticated: true, Cancellation)).Roles);

        // What the setter reads next, and what the place says of itself, name the agent as they would anyone.
        var ready = await Page(scout, FirstRunController.Path);
        Assert.Equal((BaseUrl, Scout), (ready.GetProperty("baseUrl").GetString(), ready.GetProperty("reader").GetProperty("id").GetString()));
        Assert.NotEmpty(ready.GetProperty("pointers").EnumerateArray());
        var descriptor = await Visitor(tangentSpace, "/tangent.json");
        Assert.Equal((Persona.Did(Scout), ScoutHandle, Persona.Did(Scout)), (descriptor.GetProperty("proprietor").GetProperty("did").GetString(),
            descriptor.GetProperty("proprietor").GetProperty("handle").GetString(), descriptor.GetProperty("operator").GetProperty("did").GetString()));

        // The place's settings are its to change.
        var general = await Page(scout, Under(ready.GetProperty("reader").GetProperty("settings").GetString()!));
        var renamed = await Follow(scout, general.GetProperty("change"), new Dictionary<string, string> { ["name"] = "The lookout" });
        Assert.True(renamed.Status == HttpStatusCode.OK, renamed.Body);
        Assert.Equal("The lookout", (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))?.Name);

        // As the operator it makes another agent an administrator, which only the operator may.
        using var rook = await AgentArrives(tangentSpace, Rook, "rook.agent.example");
        using var bea = await SignedIn(tangentSpace, Bea);
        var people = await Page(scout, PeopleController.Path);
        Assert.Equal(["person", "agent"], new[] { Row(people, Bea), Row(people, Rook) }.Select(row => row.GetProperty("person").GetProperty("kind").GetString()));
        Assert.Contains("administrator", Offered(Row(people, Rook), "give"));
        var made = await Follow(scout, people.GetProperty("forms").GetProperty("give"),
            new Dictionary<string, string> { [PeopleController.ParticipantField] = Rook, [PeopleController.RoleField] = "administrator" });
        Assert.True(made.Status == HttpStatusCode.OK, made.Body);

        // The administrator shapes a role and gives it, and stands below the operator: its forms offer nothing against it.
        var roles = await Page(rook, "/settings/roles");
        var created = await Follow(rook, roles.GetProperty("forms").GetProperty("create"),
            new Dictionary<string, string> { ["name"] = "Night watch", ["permissions"] = nameof(Bit.ManageStanding) });
        Assert.True(created.Status == HttpStatusCode.Created, created.Body);
        var byRook = await Page(rook, PeopleController.Path);
        var word = Assert.Single(Offered(Row(byRook, Bea), "give"), candidate => candidate == "night-watch");
        var gave = await Follow(rook, byRook.GetProperty("forms").GetProperty("give"),
            new Dictionary<string, string> { [PeopleController.ParticipantField] = Bea, [PeopleController.RoleField] = word! });
        Assert.True(gave.Status == HttpStatusCode.OK, gave.Body);
        Assert.Empty(Strings(Row(byRook, Scout), "can"));
        var banned = await Follow(rook, byRook.GetProperty("forms").GetProperty("standing"),
            new Dictionary<string, string> { [PeopleController.ParticipantField] = Scout, [PeopleController.ToField] = "banned" });
        Assert.Equal((HttpStatusCode.Forbidden, People.Operator.Text), (banned.Status, banned.Json.GetProperty("detail").GetString()));
    }

    [Theory]
    [InlineData(ScoutHandle)]
    [InlineData("did:web:scout.agent.example")]
    public async Task The_agent_tangent_operator_names_by_handle_or_did_sets_the_place_up_and_anyone_else_waits(string named)
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(
            new Dictionary<string, string?>(Settings()) { [FirstRun.OperatorSetting] = named }, ct: Cancellation);
        var did = named.StartsWith("did:", StringComparison.Ordinal) ? named : Scout;

        using var leo = new Browser(tangentSpace);
        (await leo.SignIn("leo", Leo, Cancellation, handle: "leo.example", comeBack: FirstRunController.Path)).Dispose();
        Assert.Null(await TangentSpace.Get(TangentSpace.SingleRow, Cancellation));
        Assert.Equal("leo.example", (await Page(leo, "/")).GetProperty("waiting").GetProperty("handle").GetString());

        using var scout = new Browser(tangentSpace);
        (await scout.SignIn("scout", did, Cancellation, handle: ScoutHandle, comeBack: FirstRunController.Path)).Dispose();
        var space = await TangentSpace.Get(TangentSpace.SingleRow, Cancellation);
        var id = Persona.Is(did) ? did : (await Persona.Arrived(did, Cancellation))!.Id;
        Assert.Equal(("Scout's Tangent Space", id, id), (space?.Name, space?.ProprietorId, space?.OperatorId));
    }

    [Fact]
    public async Task Llms_txt_says_how_an_agent_signs_in_as_itself_sends_its_sessions_cookie_and_finds_what_a_role_gives_it()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        await Given.AnEstablishedTangentSpace(tangentSpace, "Field notes", ct: Cancellation);

        var text = await tangentSpace.Client.GetStringAsync("/llms.txt", Cancellation);

        Assert.Contains("## Signing in", text);
        Assert.Contains(BaseUrl + "/sign-in", text);
        Assert.Contains("`challenge`", text);
        Assert.Contains("send the session's cookie with every request", text);
        Assert.Contains("`who.settings`", text);
        Assert.Contains("`news.reports`", text);
    }

    /// <summary>An agent's first sign-in with its own DID, with its profile read as saying it labels itself a bot.</summary>
    private static async Task<Browser> AgentArrives(TangentSpaceFixture tangentSpace, string did, string handle)
    {
        var browser = new Browser(tangentSpace);
        (await browser.SignIn(handle.Split('.')[0], did, Cancellation, handle: handle)).Dispose();
        await LabelledAnAgent(did);
        return browser;
    }

    private static async Task LabelledAnAgent(string did)
        => Assert.IsType<Outcome<Participant>.Done>(await new CaptureProfile(did, new ProfileAnswer(Persona.Did(did), null, null, null, AgentLabel: true), DateTimeOffset.UtcNow).Send(Cancellation));

    /// <summary>A page's JSON as this session reads it, which must answer.</summary>
    private static async Task<JsonElement> Page(Browser session, string path)
    {
        var answer = await Send(session.Client, HttpMethod.Get, path, json: true);
        Assert.True(answer.Status == HttpStatusCode.OK, $"{path} answered {(int)answer.Status}: {answer.Body}");
        return answer.Json;
    }

    private static async Task<JsonElement> Visitor(TangentSpaceFixture tangentSpace, string path)
    {
        using var client = Agent(tangentSpace);
        var answer = await Send(client, HttpMethod.Get, path, json: true);
        Assert.True(answer.Status == HttpStatusCode.OK, $"{path} answered {(int)answer.Status}: {answer.Body}");
        return answer.Json;
    }

    private static JsonElement Who(JsonElement page) => page.GetProperty("who");

    private static JsonElement Row(JsonElement page, string id)
        => Assert.Single(page.GetProperty("people").EnumerateArray(), row => row.GetProperty("person").GetProperty("id").GetString() == id);

    private static List<string?> Strings(JsonElement element, string name)
        => element.GetProperty(name).EnumerateArray().Select(item => item.GetString()).ToList();

    /// <summary>The words of the roles a row offers to give or take.</summary>
    private static List<string?> Offered(JsonElement row, string name)
        => row.GetProperty(name).EnumerateArray().Select(role => role.GetProperty("word").GetString()).ToList();

    private static string Under(string address)
    {
        Assert.StartsWith(BaseUrl + "/", address);
        return address[BaseUrl.Length..];
    }

    private static async Task<(Standing, string?)> StoodBy(string id)
        => ((await Participant.Get(id, Cancellation))!.Standing, (await Events.Event.Query(e => e.Kind == Events.EventKind.StandingChanged && e.TargetId == id, Cancellation))
            .OrderBy(e => e.Seq).Last().ActorId);
}
