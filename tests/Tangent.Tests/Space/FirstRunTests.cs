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

/// <summary>First run: every page answers it until the place is established; the first person to sign in, or the
/// account the configuration names by handle or DID, sets it up by signing in, under the name their handle gives it,
/// and becomes its proprietor, operator and administrator; first run then prints the site's pointers.</summary>
public sealed class FirstRunTests
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
            var page = WebUtility.HtmlDecode(html);

            Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (htmlStatus, jsonStatus));
            Assert.Contains("<h1 id=\"first-run-title\" class=\"sign-in-title\">Set up this Tangent Space</h1>", html);
            // The one action signs in with Bluesky: the challenge, coming back to first run, naming no account.
            Assert.Matches(BlueskyAction("/first-run"), page);
            // An account on another server signs in by its handle, behind a quiet disclosure.
            Assert.Matches(AnotherServer("first-run-sign-in-handle", "/first-run"), page);
            Assert.Contains("<span class=\"panel-head-name\">New Tangent Space</span>", html);
            // Nothing before setup shows the base URL or the key that holds it.
            Assert.DoesNotContain(Code(BaseUrl), html);
            Assert.DoesNotContain(TangentModule.BaseUrlKey, html);
            Assert.DoesNotContain("<dialog", html);
            Assert.DoesNotContain("can set it up", html);
            Assert.Equal(TangentSpace.Unnamed, json.RootElement.GetProperty("name").GetString());
            Assert.Equal(BaseUrl + "/", json.RootElement.GetProperty("door").GetString());
            Assert.Equal(challenge, json.RootElement.GetProperty("challenge").GetString());
            Assert.Equal(Tangent.Participants.SignIn.AnotherServerText, json.RootElement.GetProperty("anotherServer").GetString());
            Assert.False(json.RootElement.TryGetProperty("baseUrl", out _));
            Assert.False(json.RootElement.TryGetProperty("setting", out _));
            Assert.False(json.RootElement.TryGetProperty("operator", out _));
        }

        var (liveStatus, live) = await Read(tangentSpace, "/health/live", "application/json");
        var (signInStatus, signIn) = await Read(tangentSpace, "/sign-in", "text/html");
        var (_, signInBody) = await Read(tangentSpace, "/sign-in?return=%2Ft%2Fworkshop", "application/json");
        using var signInJson = JsonDocument.Parse(signInBody);
        using var stylesheet = await tangentSpace.Client.GetAsync("/tangent.css", Cancellation);

        Assert.Equal(HttpStatusCode.OK, liveStatus);
        Assert.DoesNotContain("challenge", live);
        Assert.Equal(HttpStatusCode.OK, signInStatus);
        Assert.Contains("Sign in to reply and watch", signIn);
        // Before setup the sign-in page sends everyone on to first run, wherever they were going.
        Assert.Equal("/first-run", signInJson.RootElement.GetProperty("return").GetString());
        Assert.Equal(HttpStatusCode.OK, stylesheet.StatusCode);
        Assert.Equal("text/css", stylesheet.Content.Headers.ContentType?.MediaType);
        Assert.Equal(0, await TangentSpace.Count.Exact(Cancellation));
    }

    [Fact]
    public async Task An_unconfigured_place_is_set_up_by_the_first_sign_in_whose_signer_becomes_its_proprietor_operator_and_administrator_and_reads_its_pointers()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector(), ct: Cancellation);
        var (_, signInPage) = await Read(tangentSpace, "/sign-in", "application/json");
        var comeBack = ComeBack(signInPage);
        using var browser = new Browser(tangentSpace);

        using var landed = await browser.SignIn("leo", Leo, Cancellation, handle: "leo.sylin.org", comeBack: comeBack);
        var pointers = WebUtility.HtmlDecode(await landed.Content.ReadAsStringAsync(Cancellation));

        // Signed in through the sign-in page, the setter lands on first run's ready page.
        Assert.Equal("/sign-in?return=" + Uri.EscapeDataString("/first-run"), comeBack);
        Assert.Equal((HttpStatusCode.OK, "/first-run"), (landed.StatusCode, landed.RequestMessage!.RequestUri!.AbsolutePath));
        Assert.Contains("Leo's Tangent Space is ready", pointers);
        Assert.Contains($"<link rel=\"tangent\" href=\"{BaseUrl}\">", pointers);
        var space = await TangentSpace.Get(TangentSpace.SingleRow, Cancellation);
        Assert.NotNull(space);
        Assert.Equal(("Leo's Tangent Space", Leo, Leo), (space.Name, space.ProprietorId, space.OperatorId));
        Assert.Equal((Audience.Public, Admission.Join), (space.Audience, space.Admission));
        // A configured URL wins, so the sign-in's origin is not taken as the place's address.
        Assert.Null(space.Address);
        Assert.Equal(1, await TangentSpace.Count.Exact(Cancellation));
        var log = (await Event.All(Cancellation)).OrderBy(e => e.Seq).Select(e => (e.Kind, e.ActorId)).ToList();
        // Leo arrived at sign-in; the moderators' Tangent is created with the place.
        Assert.Equal([(EventKind.StandingChanged, Leo), (EventKind.TangentChanged, Leo), (EventKind.SpaceChanged, Leo)], log);
        // Setting the place up declares nothing about the setter: their kind is still their profile's.
        var participant = await Participant.Get(Leo, Cancellation);
        Assert.Equal((ParticipantKind.Person, KindSource.ProfileLabel, Standing.Admitted, "leo.sylin.org"),
            (participant?.Kind, participant?.KindSource, participant?.Standing, participant?.Handle));
        var administrator = await tangentSpace.Services.GetRequiredService<RoleCollection>().Get(Roles.Administrator, Cancellation);
        Assert.Contains(Leo, administrator!.Members);

        var (descriptorStatus, descriptor) = await Read(tangentSpace, "/tangent.json", "application/json");
        var (visitorStatus, _) = await Read(tangentSpace, "/first-run", "text/html");

        Assert.Equal(HttpStatusCode.OK, descriptorStatus);
        Assert.Equal("Leo's Tangent Space", JsonDocument.Parse(descriptor).RootElement.GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.NotFound, visitorStatus);
    }

    [Fact]
    public async Task A_configured_handle_matches_the_verified_handle_with_or_without_its_at_in_any_case()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector((FirstRun.OperatorSetting, "@Leo.Sylin.Org")), ct: Cancellation);
        var (_, visitor) = await Read(tangentSpace, "/first-run", "text/html");
        var (_, body) = await Read(tangentSpace, "/first-run", "application/json");
        using var json = JsonDocument.Parse(body);
        using var browser = new Browser(tangentSpace);

        using var landed = await browser.SignIn("leo", Leo, Cancellation, handle: "leo.sylin.org", comeBack: FirstRunController.Path);

        Assert.Matches(@"Only\s+<strong>@leo\.sylin\.org</strong>\s+can set it up\.", visitor);
        Assert.Equal("leo.sylin.org", json.RootElement.GetProperty("operator").GetString());
        Assert.Equal(HttpStatusCode.OK, landed.StatusCode);
        Assert.Contains("Leo's Tangent Space is ready", WebUtility.HtmlDecode(await landed.Content.ReadAsStringAsync(Cancellation)));
        Assert.Equal(Leo, (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))?.OperatorId);
    }

    [Fact]
    public async Task Anyone_but_a_configured_handle_lands_on_the_waiting_page_and_sets_nothing_up_and_the_handles_own_sign_in_sets_it_up()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector((FirstRun.OperatorSetting, "leo.sylin.org")), ct: Cancellation);
        using var ada = new Browser(tangentSpace);

        using var page = await ada.SignIn("ada", Ada, Cancellation, handle: "ada.example", comeBack: FirstRunController.Path);
        var html = await page.Content.ReadAsStringAsync(Cancellation);
        var (_, body) = await Read(ada, "/first-run");
        using var json = JsonDocument.Parse(body);

        Assert.Equal((HttpStatusCode.OK, "/first-run"), (page.StatusCode, page.RequestMessage!.RequestUri!.AbsolutePath));
        Assert.Contains("<h1 id=\"first-run-title\" class=\"sign-in-title\">Waiting for its operator</h1>", html);
        Assert.Contains("You’re signed in as <strong>@ada.example</strong>, and this place is still being set up: check back soon.", html);
        Assert.Matches(@"Only\s+<strong>@leo\.sylin\.org</strong>\s+can set it up\.", html);
        Assert.DoesNotContain("first-run-did", html);
        Assert.DoesNotContain("<form", html);
        Assert.Equal("leo.sylin.org", json.RootElement.GetProperty("operator").GetString());
        Assert.Equal(("ada.example", "leo.sylin.org"),
            (json.RootElement.GetProperty("waiting").GetProperty("handle").GetString(), json.RootElement.GetProperty("waiting").GetProperty("operatorHandle").GetString()));
        Assert.False(json.RootElement.TryGetProperty("challenge", out _));
        // The way out is the frame's account: its menu signs this person out and comes back to first run.
        var signOut = json.RootElement.GetProperty("reader").GetProperty("signOut").GetString();
        Assert.EndsWith("?return=" + Uri.EscapeDataString("/first-run"), signOut);
        Assert.Contains($"<a class=\"account-item\" href=\"{WebUtility.HtmlEncode(signOut)}\">", html);
        Assert.Contains($"<a class=\"first-run-switch\" href=\"{WebUtility.HtmlEncode(signOut)}\">Use another account</a>", html);
        Assert.Equal(0, await TangentSpace.Count.Exact(Cancellation));
        Assert.DoesNotContain(Ada, (await tangentSpace.Services.GetRequiredService<RoleCollection>().Get(Roles.Administrator, Cancellation))!.Members);

        using var leo = new Browser(tangentSpace);
        using var ready = await leo.SignIn("leo", Leo, Cancellation, handle: "leo.sylin.org", comeBack: FirstRunController.Path);

        Assert.Contains("Leo's Tangent Space is ready", WebUtility.HtmlDecode(await ready.Content.ReadAsStringAsync(Cancellation)));
        var space = await TangentSpace.Get(TangentSpace.SingleRow, Cancellation);
        Assert.Equal(("Leo's Tangent Space", Leo, Leo), (space?.Name, space?.ProprietorId, space?.OperatorId));
    }

    [Fact]
    public async Task A_configured_did_names_its_account_by_the_handle_it_arrived_with_and_lets_only_that_account_set_up()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector((FirstRun.OperatorSetting, Leo)), ct: Cancellation);
        using var ada = new Browser(tangentSpace);
        using var adaIn = await ada.SignIn("ada", Ada, Cancellation, handle: "ada.example", comeBack: FirstRunController.Path);
        var beforeLeo = await adaIn.Content.ReadAsStringAsync(Cancellation);
        // Leo has arrived without setting the place up, as when his sign-in's setup did not complete.
        Assert.IsType<Outcome<Participant>.Done>(await tangentSpace.Services.GetRequiredService<WriteLoop>()
            .Send(new FirstArrival(Leo, "leo.example"), Cancellation));

        using var known = await ada.Follow(HttpMethod.Get, "/", Cancellation);
        var afterLeo = await known.Content.ReadAsStringAsync(Cancellation);
        var (_, body) = await Read(ada, "/first-run");
        using var json = JsonDocument.Parse(body);

        Assert.Matches($@"Only\s+{Regex.Escape(Code(Leo))}\s+can set it up\.", beforeLeo);
        Assert.Matches($@"Only\s+<strong>@leo\.example</strong>\s+<span class=""first-run-did"">\({Regex.Escape(Code(Leo))}\)</span>\s+can set it up\.", afterLeo);
        Assert.Equal((Leo, "leo.example"),
            (json.RootElement.GetProperty("operator").GetString(), json.RootElement.GetProperty("waiting").GetProperty("operatorHandle").GetString()));
        Assert.Equal(0, await TangentSpace.Count.Exact(Cancellation));

        using var leo = new Browser(tangentSpace);
        using var ready = await leo.SignIn("leo", Leo, Cancellation, handle: "leo.example", comeBack: FirstRunController.Path);

        Assert.Equal((HttpStatusCode.OK, "/first-run"), (ready.StatusCode, ready.RequestMessage!.RequestUri!.AbsolutePath));
        var space = await TangentSpace.Get(TangentSpace.SingleRow, Cancellation);
        Assert.Equal(("Leo's Tangent Space", Leo, Leo), (space?.Name, space?.ProprietorId, space?.OperatorId));
    }

    [Fact]
    public async Task A_sign_in_to_an_established_place_changes_nothing_about_it()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector(), ct: Cancellation);
        using var leo = new Browser(tangentSpace);
        (await leo.SignIn("leo", Leo, Cancellation, handle: "leo.example", comeBack: FirstRunController.Path)).Dispose();
        var established = await TangentSpace.Get(TangentSpace.SingleRow, Cancellation);
        var events = await Event.Count.Exact(Cancellation);

        using var again = await leo.SignIn("leo", Leo, Cancellation, handle: "leo.example", comeBack: FirstRunController.Path);
        using var ada = new Browser(tangentSpace);
        using var adaIn = await ada.SignIn("ada", Ada, Cancellation, handle: "ada.example");

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal((HttpStatusCode.OK, "/"), (adaIn.StatusCode, adaIn.RequestMessage!.RequestUri!.AbsolutePath));
        var space = await TangentSpace.Get(TangentSpace.SingleRow, Cancellation);
        Assert.Equal((established?.Name, established?.OperatorId, established?.Version), (space?.Name, space?.OperatorId, space?.Version));
        // Ada's arrival is the one thing written.
        Assert.Equal(events + 1, await Event.Count.Exact(Cancellation));
        Assert.Equal([Leo], (await tangentSpace.Services.GetRequiredService<RoleCollection>().Get(Roles.Administrator, Cancellation))!.Members);
    }

    [Fact]
    public async Task Of_two_sign_ins_at_once_to_an_unconfigured_place_one_sets_it_up_and_the_other_lands_where_first_run_sends_it()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector(), ct: Cancellation);
        using var leo = new Browser(tangentSpace);
        using var ada = new Browser(tangentSpace);

        var landed = await Task.WhenAll(
            leo.SignIn("leo", Leo, Cancellation, handle: "leo.example", comeBack: FirstRunController.Path),
            ada.SignIn("ada", Ada, Cancellation, handle: "ada.example", comeBack: FirstRunController.Path));
        try
        {
            var space = await TangentSpace.Get(TangentSpace.SingleRow, Cancellation);
            Assert.NotNull(space);
            var (winner, loser) = space.OperatorId == Leo ? (landed[0], landed[1]) : (landed[1], landed[0]);

            Assert.Contains(space.OperatorId, new[] { Leo, Ada });
            Assert.Equal(HttpStatusCode.OK, winner.StatusCode);
            Assert.Contains($"{space.Name} is ready", WebUtility.HtmlDecode(await winner.Content.ReadAsStringAsync(Cancellation)));
            Assert.Equal(HttpStatusCode.NotFound, loser.StatusCode);
            Assert.Single(await Event.All(Cancellation), e => e.Kind == EventKind.SpaceChanged);
            Assert.Equal([space.OperatorId!], (await tangentSpace.Services.GetRequiredService<RoleCollection>().Get(Roles.Administrator, Cancellation))!.Members);
        }
        finally
        {
            foreach (var response in landed) response.Dispose();
        }
    }

    [Fact]
    public async Task Once_established_a_start_under_another_configured_operator_starts_and_warns_once_and_one_naming_the_operator_does_not()
    {
        var dataFolder = Path.Combine(Path.GetTempPath(), "tangent-data-" + Guid.CreateVersion7().ToString("n"));
        try
        {
            await using (var first = await TangentSpaceFixture.StartAsync(Settings(dataFolder, BaseUrl, (FirstRun.OperatorSetting, Leo)), ct: Cancellation))
                Assert.IsType<Outcome<TangentSpace>.Done>(await first.Services.GetRequiredService<WriteLoop>()
                    .Send(new Establish(Leo, "leo.example", "Field notes"), Cancellation));

            var other = new ConcurrentQueue<string>();
            await using (var second = await TangentSpaceFixture.StartAsync(Settings(dataFolder, BaseUrl, (FirstRun.OperatorSetting, "ada.example")),
                services => services.AddSingleton<ILoggerProvider>(new Lines(other)), Cancellation))
                Assert.Equal(Leo, (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))?.OperatorId);
            var same = new ConcurrentQueue<string>();
            await using (var third = await TangentSpaceFixture.StartAsync(Settings(dataFolder, BaseUrl, (FirstRun.OperatorSetting, "@Leo.Example")),
                services => services.AddSingleton<ILoggerProvider>(new Lines(same)), Cancellation))
                Assert.Equal(Leo, (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))?.OperatorId);

            var warning = Assert.Single(other, line => line.StartsWith(FirstRun.OperatorSetting + " names ", StringComparison.Ordinal));
            Assert.Equal($"{FirstRun.OperatorSetting} names ada.example, but this Tangent Space's operator is {Leo}; the setting matters only before setup, so this start ignores it", warning);
            Assert.DoesNotContain(same, line => line.StartsWith(FirstRun.OperatorSetting + " names ", StringComparison.Ordinal));
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
    public async Task An_account_its_profile_labels_an_agent_sets_the_place_up_and_keeps_its_kind()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        var agent = new Participant { Id = Ada, Handle = "ada.example", Kind = ParticipantKind.Agent, KindSource = KindSource.ProfileLabel };
        Assert.IsType<Outcome<Participant>.Done>(await writer.Send(new Store<Participant>(agent), Cancellation));

        var outcome = await writer.Send(new Establish(Ada, "ada.example", TangentSpace.NameFor("ada.example")), Cancellation);

        var space = Assert.IsType<Outcome<TangentSpace>.Done>(outcome).Result;
        Assert.Equal(("Ada's Tangent Space", Ada, Ada), (space.Name, space.ProprietorId, space.OperatorId));
        var after = await Participant.Get(Ada, Cancellation);
        Assert.Equal((ParticipantKind.Agent, KindSource.ProfileLabel), (after?.Kind, after?.KindSource));
    }

    [Theory]
    [InlineData("leo.sylin.org", "Leo's Tangent Space")]
    [InlineData("@ada.bsky.social", "Ada's Tangent Space")]
    [InlineData("lantern-works.example", "Lantern-works's Tangent Space")]
    [InlineData("", TangentSpace.PlainName)]
    public void The_name_is_the_first_label_of_the_handle_capitalised_as_theirs_and_plain_without_one(string handle, string name)
        => Assert.Equal(name, TangentSpace.NameFor(handle));

    [Fact]
    public void A_label_too_long_for_a_name_gives_the_plain_one()
        => Assert.Equal(TangentSpace.PlainName, TangentSpace.NameFor(new string('a', TangentSpace.NameLimit) + ".example"));

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
        Assert.StartsWith("nginx cannot express the path `/a$b` safely", dollar.Where);
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

    /// <summary>Code as a sentence sets it: in runs between slashes, a break opportunity after each slash.</summary>
    private static string Code(string text)
        => "<code class=\"code-text\">" + string.Join("<wbr />", text.Split('/').Select((run, index) => index < text.Split('/').Length - 1
            ? $"<span class=\"code-run\">{WebUtility.HtmlEncode(run)}/</span>"
            : $"<span class=\"code-run\">{WebUtility.HtmlEncode(run)}</span>")) + "</code>";

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

    /// <summary>The page at <paramref name="path"/> as JSON, read by a signed-in browser.</summary>
    private static async Task<(HttpStatusCode Status, string Body)> Read(Browser browser, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await browser.Client.SendAsync(request, Cancellation);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(Cancellation));
    }

    /// <summary>Where a challenge comes back to, as the page's JSON carries it.</summary>
    private static string ComeBack(string body)
        => Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(
            new Uri(JsonDocument.Parse(body).RootElement.GetProperty("challenge").GetString()!).Query)["return"].ToString();

    /// <summary>The action that signs in with Bluesky, in a decoded page: a form to the connector's challenge that
    /// carries only where it comes back, so it names no account.</summary>
    private static string BlueskyAction(string comeBack)
        => $@"<form class=""sign-in-form"" method=""get"" action=""{Regex.Escape(BaseUrl)}/auth/atproto/challenge"" data-island=""submitting"">\s*"
            + $@"<input type=""hidden"" name=""return"" value=""{Regex.Escape(comeBack)}"" />\s*"
            + @"<button type=""submit"" class=""sign-in-submit"" data-busy-label=""Continuing…"">Sign in with Bluesky</button>\s*</form>";

    /// <summary>The handle form for an account on another server, in a decoded page, closed behind its disclosure: a
    /// form to the same challenge that sends the typed handle as its identifier.</summary>
    private static string AnotherServer(string field, string comeBack)
        => @"<details class=""sign-in-other"">\s*<summary class=""sign-in-other-toggle"">Use another server</summary>\s*"
            + $@"<form class=""sign-in-form"" method=""get"" action=""{Regex.Escape(BaseUrl)}/auth/atproto/challenge"" data-island=""submitting"">[\s\S]*?"
            + $@"<input id=""{field}"" name=""identifier"" type=""text"" required[\s\S]*?"
            + $@"<input type=""hidden"" name=""return"" value=""{Regex.Escape(comeBack)}"" />[\s\S]*?</form>\s*</details>";

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
