using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Koan.Identity.Roles;
using Koan.Web.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tangent.Access;
using Tangent.Events;
using Tangent.Participants;
using Tangent.Writer;
using Xunit;

namespace Tangent.Space;

/// <summary>First run: every page answers it until the place is established; only the configured operator, or whoever
/// has the logged setup code, names the place, sees the configured base URL, declares the proprietor and becomes
/// operator and administrator; the page then prints the site's pointers.</summary>
public sealed partial class FirstRunTests
{
    private const string BaseUrl = TangentSpaceFixture.DefaultBaseUrl;
    private const string Leo = "did:plc:leo4example";
    private const string Ada = "did:plc:ada4example";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>A Tangent Space where Koan's test connector signs people in, whose profile job reads from a loopback
    /// address nothing answers, with <paramref name="settings"/> besides.</summary>
    private static Dictionary<string, string?> TestConnector(params (string Key, string Value)[] settings)
    {
        var all = new Dictionary<string, string?>
        {
            ["Koan:Web:Auth:TestProvider:Enabled"] = "true",
            ["Tangent:Profile:BaseAddress"] = "http://127.0.0.1:1/",
            ["Tangent:Profile:Timeout"] = "00:00:01"
        };
        foreach (var (key, value) in settings) all[key] = value;
        return all;
    }

    private static readonly (string, string) PinnedToLeo = (FirstRun.OperatorSetting, Leo);

    [Fact]
    public async Task Before_first_run_the_door_and_a_tangents_page_answer_first_run_both_ways_and_health_sign_in_and_a_static_file_answer_as_themselves()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        await Given.ATangent(tangentSpace, "workshop", ct: Cancellation);
        var challenge = $"{BaseUrl}/auth/atproto/challenge?return={Uri.EscapeDataString("/first-run")}";

        foreach (var path in new[] { "/", "/t/workshop" })
        {
            var (htmlStatus, html) = await Read(tangentSpace, path, "text/html");
            var (jsonStatus, body) = await Read(tangentSpace, path, "application/json");
            using var json = JsonDocument.Parse(body);

            Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (htmlStatus, jsonStatus));
            Assert.Contains("<h1 id=\"first-run-title\" class=\"sign-in-title\">Set up this Tangent Space</h1>", html);
            Assert.Contains($"<form class=\"sign-in-form\" method=\"get\" action=\"{BaseUrl}/auth/atproto/challenge\" data-island=\"submitting\">", html);
            Assert.Contains("<span class=\"panel-head-name\">New Tangent Space</span>", html);
            Assert.Contains("Setting it up takes the setup code this Tangent Space wrote to its log when it started, or <code>Tangent:Operator</code> set to your DID and a restart.", html);
            Assert.Equal(TangentSpace.Unnamed, json.RootElement.GetProperty("name").GetString());
            Assert.Equal(BaseUrl, json.RootElement.GetProperty("baseUrl").GetString());
            Assert.Equal(TangentModule.BaseUrlKey, json.RootElement.GetProperty("setting").GetString());
            Assert.Equal(challenge, json.RootElement.GetProperty("challenge").GetString());
            Assert.False(json.RootElement.TryGetProperty("form", out _));
        }

        var (liveStatus, live) = await Read(tangentSpace, "/health/live", "application/json");
        var (signInStatus, signIn) = await Read(tangentSpace, "/sign-in", "text/html");
        using var stylesheet = await tangentSpace.Client.GetAsync("/tangent.css", Cancellation);

        Assert.Equal(HttpStatusCode.OK, liveStatus);
        Assert.DoesNotContain("challenge", live);
        Assert.Equal(HttpStatusCode.OK, signInStatus);
        Assert.Contains("Sign in to reply and watch", signIn);
        Assert.Equal(HttpStatusCode.OK, stylesheet.StatusCode);
        Assert.Equal("text/css", stylesheet.Content.Headers.ContentType?.MediaType);
        Assert.Equal(0, await TangentSpace.Count.Exact(Cancellation));
    }

    [Fact]
    public async Task First_run_by_the_configured_operator_establishes_the_place_and_makes_them_its_operator_and_an_administrator()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector(PinnedToLeo), ct: Cancellation);
        using var browser = new Browser(tangentSpace);
        using var signedIn = await browser.SignIn("leo", Leo, Cancellation, handle: "leo.example");
        using var form = await browser.Follow(HttpMethod.Get, "/first-run", Cancellation);
        var page = await form.Content.ReadAsStringAsync(Cancellation);

        Assert.Contains("You’re signed in as <strong>@leo.example</strong>", page);
        Assert.Contains($"<span class=\"first-run-address\" aria-labelledby=\"first-run-address-label\">{BaseUrl}</span>", page);
        Assert.Contains("<code>Koan:Web:ApplicationUrl</code>", page);
        Assert.DoesNotContain("name=\"code\"", page);

        using var established = await browser.SetUp("Field notes", Cancellation);

        Assert.Equal(HttpStatusCode.SeeOther, established.StatusCode);
        Assert.Equal("/first-run", established.Headers.Location?.OriginalString);
        var space = await TangentSpace.Get(TangentSpace.SingleRow, Cancellation);
        Assert.NotNull(space);
        Assert.Equal(("Field notes", Leo, Leo), (space.Name, space.ProprietorId, space.OperatorId));
        Assert.Equal((Audience.Public, Admission.Join), (space.Audience, space.Admission));
        Assert.Equal(1, await TangentSpace.Count.Exact(Cancellation));
        var log = (await Event.All(Cancellation)).OrderBy(e => e.Seq).Select(e => (e.Kind, e.ActorId)).ToList();
        Assert.Equal([(EventKind.StandingChanged, Leo), (EventKind.SpaceChanged, Leo)], log);
        var participant = await Participant.Get(Leo, Cancellation);
        Assert.Equal((ParticipantKind.Person, KindSource.OwnChoice, Standing.Admitted, "leo.example"),
            (participant?.Kind, participant?.KindSource, participant?.Standing, participant?.Handle));
        var administrator = await tangentSpace.Services.GetRequiredService<RoleCollection>().Get(Roles.Administrator, Cancellation);
        Assert.Contains(Leo, administrator!.Members);

        using var ready = await browser.Follow(HttpMethod.Get, "/first-run", Cancellation);
        var pointers = await ready.Content.ReadAsStringAsync(Cancellation);
        var (descriptorStatus, descriptor) = await Read(tangentSpace, "/tangent.json", "application/json");
        var (visitorStatus, _) = await Read(tangentSpace, "/first-run", "text/html");

        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Contains("Field notes is ready", pointers);
        Assert.Contains(WebUtility.HtmlEncode($"<link rel=\"tangent\" href=\"{BaseUrl}\">"), pointers);
        Assert.Equal(HttpStatusCode.OK, descriptorStatus);
        Assert.Equal("Field notes", JsonDocument.Parse(descriptor).RootElement.GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.NotFound, visitorStatus);
    }

    [Fact]
    public async Task A_pinned_install_shows_anyone_else_who_may_set_it_up_and_no_form()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector(PinnedToLeo), ct: Cancellation);
        using var ada = new Browser(tangentSpace);
        using var signedIn = await ada.SignIn("ada", Ada, Cancellation, handle: "ada.example");

        using var page = await ada.Follow(HttpMethod.Get, "/", Cancellation);
        var html = await page.Content.ReadAsStringAsync(Cancellation);
        using var asJson = new HttpRequestMessage(HttpMethod.Get, "/first-run");
        asJson.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var answered = await ada.Client.SendAsync(asJson, Cancellation);
        using var json = JsonDocument.Parse(await answered.Content.ReadAsStringAsync(Cancellation));

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Waiting for its operator", html);
        Assert.Contains($"Only the person whose DID is <code>{Leo}</code> can set it up", html);
        Assert.DoesNotContain("<form", html);
        Assert.DoesNotContain("__RequestVerificationToken", html);
        Assert.Equal(Leo, json.RootElement.GetProperty("operator").GetString());
        Assert.Equal("ada.example", json.RootElement.GetProperty("waiting").GetProperty("handle").GetString());
        // The way out is the frame's account: its menu signs this person out and comes back to first run.
        Assert.Contains("class=\"account-button\"", html);
        var signOut = json.RootElement.GetProperty("reader").GetProperty("signOut").GetString();
        Assert.EndsWith("?return=" + Uri.EscapeDataString("/first-run"), signOut);
        Assert.Contains($"<a class=\"account-item\" href=\"{WebUtility.HtmlEncode(signOut)}\">", html);
        Assert.Contains("</svg>Sign out</a>", html);
        Assert.False(json.RootElement.TryGetProperty("form", out _));
        Assert.Equal(0, await TangentSpace.Count.Exact(Cancellation));
    }

    [Fact]
    public async Task An_unpinned_install_refuses_a_missing_or_wrong_setup_code_and_accepts_the_one_it_logged()
    {
        var lines = new ConcurrentQueue<string>();
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector(),
            services => services.AddSingleton<ILoggerProvider>(new Lines(lines)), Cancellation);
        var logged = Assert.Single(lines, line => line.StartsWith("Setup code ", StringComparison.Ordinal));
        var code = SetupCodeLine().Match(logged).Groups["code"].Value;
        using var browser = new Browser(tangentSpace);
        using var signedIn = await browser.SignIn("leo", Leo, Cancellation, handle: "leo.example");
        using var form = await browser.Follow(HttpMethod.Get, "/first-run", Cancellation);
        var page = await form.Content.ReadAsStringAsync(Cancellation);

        Assert.Matches("^[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4}$", code);
        Assert.Contains("name=\"code\"", page);
        Assert.DoesNotContain(code, page);

        using var missing = await browser.SetUp("Field notes", Cancellation);
        using var wrong = await browser.SetUp("Field notes", Cancellation, code: "AAAA-BBBB-CCCC-DDDD");
        var wrongPage = await wrong.Content.ReadAsStringAsync(Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Contains("id=\"first-run-code-failure\">Write the setup code from this Tangent Space’s log.</p>", WebUtility.HtmlDecode(await missing.Content.ReadAsStringAsync(Cancellation)));
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Contains("id=\"first-run-code-failure\">That is not the setup code", wrongPage);
        Assert.DoesNotContain("AAAA-BBBB-CCCC-DDDD", wrongPage);
        Assert.Equal(0, await TangentSpace.Count.Exact(Cancellation));

        using var right = await browser.SetUp("Field notes", Cancellation, code: code.ToLowerInvariant());

        Assert.Equal(HttpStatusCode.SeeOther, right.StatusCode);
        Assert.Equal(Leo, (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))?.OperatorId);
    }

    [Fact]
    public async Task The_setup_code_reaches_a_log_that_keeps_only_warnings()
    {
        var lines = new ConcurrentQueue<string>();
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(
            new Dictionary<string, string?> { ["Logging:LogLevel:Default"] = "Warning" },
            services => services.AddSingleton<ILoggerProvider>(new Lines(lines)), Cancellation);

        var logged = Assert.Single(lines, line => line.StartsWith("Setup code ", StringComparison.Ordinal));

        Assert.Matches("^[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4}-[0-9A-Z]{4}$", SetupCodeLine().Match(logged).Groups["code"].Value);
        Assert.DoesNotContain(lines, line => line.StartsWith("Tangent Space at ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_form_kept_open_past_first_run_is_refused_as_already_set_up_for_the_operator_and_answers_missing_to_anyone_else()
    {
        var lines = new ConcurrentQueue<string>();
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector(),
            services => services.AddSingleton<ILoggerProvider>(new Lines(lines)), Cancellation);
        var code = SetupCodeLine().Match(Assert.Single(lines, line => line.StartsWith("Setup code ", StringComparison.Ordinal))).Groups["code"].Value;
        using var leo = new Browser(tangentSpace);
        using var ada = new Browser(tangentSpace);
        using var leoIn = await leo.SignIn("leo", Leo, Cancellation, handle: "leo.example");
        using var adaIn = await ada.SignIn("ada", Ada, Cancellation, handle: "ada.example");
        var leoForm = await leo.Token("/first-run", Cancellation);
        var adaForm = await ada.Token("/first-run", Cancellation);
        using var established = await leo.SetUp("Field notes", Cancellation, code: code);
        Assert.Equal(HttpStatusCode.SeeOther, established.StatusCode);
        var events = await Event.Count.Exact(Cancellation);

        using var operatorAgain = await Stale(leo, leoForm, "Leo's other place", code, "application/json");
        using var operatorPage = await Stale(leo, leoForm, "Leo's other place", code, "text/html");
        using var participant = await Stale(ada, adaForm, "Ada's place", code, "application/json");
        using var visitor = await tangentSpace.Client.PostAsync("/first-run",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["name"] = "Anyone's place" }), Cancellation);
        using var problem = JsonDocument.Parse(await operatorAgain.Content.ReadAsStringAsync(Cancellation));

        Assert.Equal(HttpStatusCode.Conflict, operatorAgain.StatusCode);
        Assert.Equal("application/problem+json", operatorAgain.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Already set up", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal($"{Establish.AlreadyEstablished} Its pointers are at {BaseUrl}/first-run.", problem.RootElement.GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.Conflict, operatorPage.StatusCode);
        Assert.Contains($"{BaseUrl}/first-run", await operatorPage.Content.ReadAsStringAsync(Cancellation));
        Assert.Equal(HttpStatusCode.NotFound, participant.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, visitor.StatusCode);
        Assert.Equal(("Field notes", Leo), ((await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))?.Name, (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))?.OperatorId));
        Assert.Equal(events, await Event.Count.Exact(Cancellation));
    }

    /// <summary>Posts a first-run form the browser kept open, with the token it carried then.</summary>
    private static async Task<HttpResponseMessage> Stale(Browser browser, Dictionary<string, string> token, string name, string code, string mediaType)
    {
        var fields = new Dictionary<string, string>(token) { ["name"] = name, ["proprietor"] = "me", ["accountable"] = "yes", ["code"] = code };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/first-run") { Content = new FormUrlEncodedContent(fields) };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(mediaType));
        return await browser.Client.SendAsync(request, Cancellation);
    }

    [Fact]
    public async Task A_form_without_the_operators_statement_is_refused_inline_and_writes_nothing()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector(PinnedToLeo), ct: Cancellation);
        using var browser = new Browser(tangentSpace);
        using var signedIn = await browser.SignIn("leo", Leo, Cancellation, handle: "leo.example");

        using var unstated = await browser.SetUp("Field notes", Cancellation, accountable: false);

        Assert.Equal(HttpStatusCode.BadRequest, unstated.StatusCode);
        Assert.Contains("id=\"first-run-accountable-failure\">Confirm that you are the person who answers for this place.</p>",
            await unstated.Content.ReadAsStringAsync(Cancellation));
        Assert.Equal(0, await TangentSpace.Count.Exact(Cancellation));
    }

    [Fact]
    public async Task Once_established_a_start_under_another_configured_operator_refuses_naming_both()
    {
        var dataFolder = Path.Combine(Path.GetTempPath(), "tangent-data-" + Guid.CreateVersion7().ToString("n"));
        try
        {
            await using (var first = await TangentSpaceFixture.StartAsync(Settings(dataFolder, BaseUrl, (FirstRun.OperatorSetting, Leo)), ct: Cancellation))
                Assert.IsType<Outcome<TangentSpace>.Done>(await first.Services.GetRequiredService<WriteLoop>()
                    .Send(new Establish(Leo, "leo.example", "Field notes"), Cancellation));

            var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => TangentSpaceFixture.StartAsync(
                Settings(dataFolder, BaseUrl, (FirstRun.OperatorSetting, Ada)), ct: Cancellation));

            Assert.Contains($"{FirstRun.OperatorSetting} names {Ada}", refused.Message);
            Assert.Contains($"operator is {Leo}", refused.Message);
            Assert.Contains("never transfers the place", refused.Message);
        }
        finally
        {
            Forget(dataFolder);
        }
    }

    [Fact]
    public async Task A_start_makes_an_operator_who_lacks_the_administrator_role_a_member()
    {
        var dataFolder = Path.Combine(Path.GetTempPath(), "tangent-data-" + Guid.CreateVersion7().ToString("n"));
        try
        {
            await using (var first = await TangentSpaceFixture.StartAsync(Settings(dataFolder, BaseUrl), ct: Cancellation))
            {
                Assert.IsType<Outcome<TangentSpace>.Done>(await first.Services.GetRequiredService<WriteLoop>()
                    .Send(new Establish(Leo, "leo.example", "Field notes"), Cancellation));
                var before = await first.Services.GetRequiredService<RoleCollection>().Get(Roles.Administrator, Cancellation);
                Assert.DoesNotContain(Leo, before!.Members);
            }

            await using var second = await TangentSpaceFixture.StartAsync(Settings(dataFolder, BaseUrl), ct: Cancellation);
            var after = await second.Services.GetRequiredService<RoleCollection>().Get(Roles.Administrator, Cancellation);

            Assert.Contains(Leo, after!.Members);
        }
        finally
        {
            Forget(dataFolder);
        }
    }

    [Fact]
    public async Task A_restart_under_another_base_url_moves_the_pointers()
    {
        var dataFolder = Path.Combine(Path.GetTempPath(), "tangent-data-" + Guid.CreateVersion7().ToString("n"));
        try
        {
            await using (var first = await TangentSpaceFixture.StartAsync(Settings(dataFolder, "https://first.example"), ct: Cancellation))
                Assert.IsType<Outcome<TangentSpace>.Done>(await first.Services.GetRequiredService<WriteLoop>()
                    .Send(new Establish(Leo, "leo.example", "Field notes"), Cancellation));

            var moved = Settings(dataFolder, "https://second.example/notes", (FirstRun.OperatorSetting, Leo));
            foreach (var (key, value) in TestConnector()) moved[key] = value;
            await using var second = await TangentSpaceFixture.StartAsync(moved, ct: Cancellation);
            using var browser = new Browser(second);
            using var signedIn = await browser.SignIn("leo", Leo, Cancellation, handle: "leo.example", under: "/notes");
            using var ready = await browser.Follow(HttpMethod.Get, "/notes/first-run", Cancellation);
            var pointers = WebUtility.HtmlDecode(await ready.Content.ReadAsStringAsync(Cancellation));

            Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
            Assert.Contains("<link rel=\"tangent\" href=\"https://second.example/notes\">", pointers);
            Assert.Contains("location /notes/ {", pointers);
            Assert.Contains($"proxy_pass http://127.0.0.1:{second.Client.BaseAddress!.Port};", pointers);
            Assert.DoesNotContain("first.example", pointers);
        }
        finally
        {
            Forget(dataFolder);
        }
    }

    [Fact]
    public async Task A_second_first_run_is_refused_as_already_established_and_writes_nothing()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        Assert.IsType<Outcome<TangentSpace>.Done>(await writer.Send(new Establish(Leo, "leo.example", "Field notes"), Cancellation));
        var events = await Event.Count.Exact(Cancellation);

        var again = await writer.Send(new Establish(Ada, "ada.example", "Ada's place"), Cancellation);

        Assert.Equal(Establish.AlreadyEstablished, Assert.IsType<Outcome<TangentSpace>.Refused>(again).Reason);
        var space = await TangentSpace.Get(TangentSpace.SingleRow, Cancellation);
        Assert.Equal(("Field notes", Leo, 1L), (space?.Name, space?.OperatorId, space?.Version));
        Assert.Equal(events, await Event.Count.Exact(Cancellation));
        Assert.Null(await Participant.Get(Ada, Cancellation));
    }

    [Fact]
    public async Task The_proprietor_can_be_another_participant_by_handle_and_a_handle_that_names_nobody_is_refused()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector(PinnedToLeo), ct: Cancellation);
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        var studio = new Participant { Id = "did:plc:studio4example", Handle = "studio.example", Kind = ParticipantKind.Agent };
        Assert.IsType<Outcome<Participant>.Done>(await writer.Send(new Store<Participant>(studio), Cancellation));
        using var browser = new Browser(tangentSpace);
        using var signedIn = await browser.SignIn("leo", Leo, Cancellation, handle: "leo.example");
        var events = await Event.Count.Exact(Cancellation);

        using var nobody = await browser.SetUp("Field notes", Cancellation, proprietorHandle: "nobody.example");
        var refused = await nobody.Content.ReadAsStringAsync(Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, nobody.StatusCode);
        Assert.Contains($"<p class=\"field-failure\" id=\"first-run-handle-failure\">{WebUtility.HtmlEncode(Establish.NoParticipant("nobody.example"))}", refused);
        Assert.Contains("value=\"nobody.example\" aria-invalid=\"true\"", refused);
        Assert.Contains("value=\"Field notes\"", refused);
        Assert.Equal(0, await TangentSpace.Count.Exact(Cancellation));
        Assert.Equal(events, await Event.Count.Exact(Cancellation));

        using var named = await browser.SetUp("Field notes", Cancellation, proprietorHandle: "@Studio.Example");

        Assert.Equal(HttpStatusCode.SeeOther, named.StatusCode);
        var space = await TangentSpace.Get(TangentSpace.SingleRow, Cancellation);
        Assert.Equal((studio.Id, Leo), (space?.ProprietorId, space?.OperatorId));
    }

    [Theory]
    [InlineData(Standing.Admitted, true)]
    [InlineData(Standing.Banned, false)]
    public async Task A_handle_two_participants_hold_or_a_banned_participants_names_no_proprietor(Standing standing, bool shared)
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        Assert.IsType<Outcome<Participant>.Done>(await writer.Send(new Store<Participant>(
            new Participant { Id = "did:plc:studio4example", Handle = "studio.example", Standing = standing }), Cancellation));
        if (shared)
            Assert.IsType<Outcome<Participant>.Done>(await writer.Send(new Store<Participant>(
                new Participant { Id = "did:plc:other4example", Handle = "studio.example" }), Cancellation));

        var outcome = await writer.Send(new Establish(Leo, "leo.example", "Field notes", "studio.example"), Cancellation);

        Assert.Equal(shared ? Establish.SharedHandle("studio.example") : Establish.Banned("studio.example"),
            Assert.IsType<Outcome<TangentSpace>.Refused>(outcome).Reason);
        Assert.Equal(0, await TangentSpace.Count.Exact(Cancellation));
    }

    [Fact]
    public async Task An_operator_whose_profile_labels_them_an_agent_stays_a_person_and_one_who_chose_agent_is_refused()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        var labelled = new Participant { Id = Leo, Handle = "leo.example", Kind = ParticipantKind.Agent, KindSource = KindSource.ProfileLabel };
        var chose = new Participant { Id = Ada, Handle = "ada.example", Kind = ParticipantKind.Agent, KindSource = KindSource.OwnChoice };
        Assert.IsType<Outcome<Participant>.Done>(await writer.Send(new Store<Participant>(labelled), Cancellation));
        Assert.IsType<Outcome<Participant>.Done>(await writer.Send(new Store<Participant>(chose), Cancellation));

        var agent = await writer.Send(new Establish(Ada, "ada.example", "Ada's place"), Cancellation);

        Assert.IsType<Outcome<TangentSpace>.Refused>(agent);
        Assert.Equal(0, await TangentSpace.Count.Exact(Cancellation));

        Assert.IsType<Outcome<TangentSpace>.Done>(await writer.Send(new Establish(Leo, "leo.example", "Field notes"), Cancellation));
        var captured = await writer.Send(new CaptureProfile(new ProfileAnswer(Leo, "Leo", null, null, AgentLabel: true), DateTimeOffset.UtcNow), Cancellation);

        var operatorRow = Assert.IsType<Outcome<Participant>.Done>(captured).Result;
        Assert.Equal((ParticipantKind.Person, KindSource.OwnChoice), (operatorRow.Kind, operatorRow.KindSource));
        Assert.Equal(ParticipantKind.Person, (await Participant.Get(Leo, Cancellation))?.Kind);
    }

    [Fact]
    public void The_pointers_carry_the_base_urls_host_and_path_and_a_path_adds_the_proxy_rule()
    {
        var subdomain = Pointers.For("https://tangent.sylin.org");
        var path = Pointers.For("https://www.example.com/notes");

        Assert.Equal([Pointers.Link, Pointers.WellKnown, Pointers.Txt], subdomain.Select(pointer => pointer.Kind));
        Assert.All(subdomain, pointer => Assert.Contains("https://tangent.sylin.org", pointer.Text));
        Assert.Equal("<link rel=\"tangent\" href=\"https://tangent.sylin.org\">", subdomain[0].Text);
        Assert.Equal("https://tangent.sylin.org", subdomain[1].Text);
        Assert.Contains("https://sylin.org/.well-known/tangent", subdomain[1].Where);
        Assert.Equal("_tangent.sylin.org. IN TXT \"https://tangent.sylin.org\"", subdomain[2].Text);

        Assert.Equal([Pointers.Link, Pointers.WellKnown, Pointers.Txt, Pointers.Proxy], path.Select(pointer => pointer.Kind));
        Assert.All(path, pointer => Assert.Contains("/notes", pointer.Text));
        Assert.All(path.Take(3), pointer => Assert.Contains("https://www.example.com/notes", pointer.Text));
        Assert.Contains("location /notes/ {", path[3].Text);
        Assert.Contains("location = /notes {", path[3].Text);
        Assert.Contains("return 308 /notes/;", path[3].Text);
        Assert.Contains("www.example.com", path[3].Where);
    }

    [Fact]
    public void The_proxy_rule_passes_to_the_address_the_server_listens_on()
    {
        Assert.Equal("http://127.0.0.1:6000", Pointers.Upstream(["http://localhost:6000"]));
        Assert.Equal("http://127.0.0.1:8080", Pointers.Upstream(["https://tangent.example:8443", "http://[::]:8080"]));
        Assert.Equal("http://127.0.0.1:7000", Pointers.Upstream(["http://+:7000"]));
        Assert.Equal("http://10.0.0.5:7000", Pointers.Upstream(["http://10.0.0.5:7000"]));
        Assert.Equal(Pointers.FixedUpstream, Pointers.Upstream([]));
        Assert.Contains("proxy_pass http://127.0.0.1:6000;",
            Assert.Single(Pointers.For("https://www.example.com/notes", Pointers.Upstream(["http://localhost:6000"])), pointer => pointer.Kind == Pointers.Proxy).Text);
    }

    [Fact]
    public void The_proxy_rule_quotes_a_path_for_nginx_and_gives_none_for_a_path_nginx_cannot_express()
    {
        var spaced = Assert.Single(Pointers.For(Normalised("https://www.example.com/a%20b%5C")), pointer => pointer.Kind == Pointers.Proxy);
        var dollar = Assert.Single(Pointers.For(Normalised("https://www.example.com/a$b")), pointer => pointer.Kind == Pointers.Proxy);

        Assert.Contains("location = \"/a b\\\\\" {", spaced.Text);
        Assert.Contains("return 308 /a%20b%5C/;", spaced.Text);
        Assert.Contains("location \"/a b\\\\/\" {", spaced.Text);
        Assert.Equal("", dollar.Text);
        Assert.StartsWith("nginx cannot express the path /a$b safely", dollar.Where);
    }

    [Fact]
    public async Task A_fresh_host_has_no_row_until_first_run_and_its_start_log_names_the_configured_base_url()
    {
        var lines = new ConcurrentQueue<string>();
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(
            new Dictionary<string, string?> { [TangentModule.BaseUrlKey] = "https://fresh.example/notes" },
            services => services.AddSingleton<ILoggerProvider>(new Lines(lines)), Cancellation);

        Assert.Equal(0, await TangentSpace.Count.Exact(Cancellation));
        Assert.Contains(lines, line => line.StartsWith("Tangent Space at https://fresh.example/notes;", StringComparison.Ordinal));

        var outcome = await tangentSpace.Services.GetRequiredService<WriteLoop>()
            .Send(new Establish(Leo, "leo.example", "Field notes"), Cancellation);

        Assert.IsType<Outcome<TangentSpace>.Done>(outcome);
        Assert.Equal(1, await TangentSpace.Count.Exact(Cancellation));
    }

    /// <summary>A base URL as Koan holds it.</summary>
    private static string Normalised(string url) => new KoanWebBaseUrl(url).Configured!;

    private static Dictionary<string, string?> Settings(string dataFolder, string baseUrl, params (string Key, string Value)[] more)
    {
        var settings = new Dictionary<string, string?> { ["Tangent:DataFolder"] = dataFolder, [TangentModule.BaseUrlKey] = baseUrl };
        foreach (var (key, value) in more) settings[key] = value;
        return settings;
    }

    private static void Forget(string dataFolder)
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(dataFolder)) Directory.Delete(dataFolder, recursive: true);
    }

    private static async Task<(HttpStatusCode Status, string Body)> Read(TangentSpaceFixture tangentSpace, string path, string mediaType)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(mediaType));
        using var response = await tangentSpace.Client.SendAsync(request, Cancellation);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(Cancellation));
    }

    [GeneratedRegex("^Setup code (?<code>[0-9A-Z-]+):")]
    private static partial Regex SetupCodeLine();

    /// <summary>Keeps every formatted log line of the host it is added to.</summary>
    private sealed class Lines(ConcurrentQueue<string> lines) : ILoggerProvider, ILogger
    {
        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => lines.Enqueue(formatter(state, exception));

        public void Dispose()
        {
        }
    }
}
