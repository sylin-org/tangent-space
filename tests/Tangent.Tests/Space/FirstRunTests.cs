using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Koan.Identity.Roles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tangent.Access;
using Tangent.Events;
using Tangent.Participants;
using Tangent.Writer;
using Xunit;

namespace Tangent.Space;

/// <summary>First run: every page answers it until the place is established; the operator names the place, sees the
/// configured base URL, declares the proprietor and becomes operator and administrator; the page then prints the
/// site's pointers.</summary>
public sealed class FirstRunTests
{
    private const string BaseUrl = TangentSpaceFixture.DefaultBaseUrl;
    private const string Leo = "did:plc:leo4example";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>A Tangent Space where Koan's test connector signs people in, whose profile job reads from a loopback
    /// address nothing answers.</summary>
    private static readonly Dictionary<string, string?> TestConnector = new()
    {
        ["Koan:Web:Auth:TestProvider:Enabled"] = "true",
        ["Tangent:Profile:BaseAddress"] = "http://127.0.0.1:1/",
        ["Tangent:Profile:Timeout"] = "00:00:01"
    };

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
    public async Task First_run_by_a_signed_in_person_establishes_the_place_and_makes_them_its_operator_and_an_administrator()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector, ct: Cancellation);
        using var browser = new Browser(tangentSpace);
        using var signedIn = await browser.SignIn("leo", Leo, Cancellation, handle: "leo.example");
        using var form = await browser.Follow(HttpMethod.Get, "/first-run", Cancellation);
        var page = await form.Content.ReadAsStringAsync(Cancellation);

        Assert.Contains("You’re signed in as <strong>@leo.example</strong>", page);
        Assert.Contains($"<span class=\"first-run-address\" aria-labelledby=\"first-run-address-label\">{BaseUrl}</span>", page);
        Assert.Contains("<code>Koan:Web:ApplicationUrl</code>", page);

        using var established = await browser.SetUp("Field notes", Cancellation);

        Assert.Equal(HttpStatusCode.SeeOther, established.StatusCode);
        Assert.Equal("/first-run", established.Headers.Location?.OriginalString);
        var space = await TangentSpace.Get(TangentSpace.SingleRow, Cancellation);
        Assert.NotNull(space);
        Assert.Equal(("Field notes", BaseUrl, Leo, Leo), (space.Name, space.BaseUrl, space.ProprietorId, space.OperatorId));
        Assert.Equal((Audience.Public, Admission.Join), (space.Audience, space.Admission));
        Assert.Equal(1, await TangentSpace.Count.Exact(Cancellation));
        var log = (await Event.All(Cancellation)).OrderBy(e => e.Seq).Select(e => (e.Kind, e.ActorId)).ToList();
        Assert.Equal([(EventKind.StandingChanged, Leo), (EventKind.SpaceChanged, Leo)], log);
        var participant = await Participant.Get(Leo, Cancellation);
        Assert.Equal((ParticipantKind.Person, Standing.Admitted, "leo.example"), (participant?.Kind, participant?.Standing, participant?.Handle));
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
    public async Task A_second_first_run_is_refused_as_already_established_and_writes_nothing()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        Assert.IsType<Outcome<TangentSpace>.Done>(await writer.Send(new Establish(Leo, "leo.example", "Field notes", BaseUrl), Cancellation));
        var events = await Event.Count.Exact(Cancellation);

        var again = await writer.Send(new Establish("did:plc:ada4example", "ada.example", "Ada's place", BaseUrl), Cancellation);

        Assert.Equal(Establish.AlreadyEstablished, Assert.IsType<Outcome<TangentSpace>.Refused>(again).Reason);
        var space = await TangentSpace.Get(TangentSpace.SingleRow, Cancellation);
        Assert.Equal(("Field notes", Leo, 1L), (space?.Name, space?.OperatorId, space?.Version));
        Assert.Equal(events, await Event.Count.Exact(Cancellation));
        Assert.Null(await Participant.Get("did:plc:ada4example", Cancellation));
    }

    [Fact]
    public async Task The_proprietor_can_be_another_participant_by_handle_and_a_handle_that_names_nobody_is_refused()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector, ct: Cancellation);
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        var studio = new Participant { Id = "did:plc:studio4example", Handle = "studio.example", Kind = ParticipantKind.Agent };
        Assert.IsType<Outcome<Participant>.Done>(await writer.Send(new Store<Participant>(studio), Cancellation));
        using var browser = new Browser(tangentSpace);
        using var signedIn = await browser.SignIn("leo", Leo, Cancellation, handle: "leo.example");

        using var nobody = await browser.SetUp("Field notes", Cancellation, proprietorHandle: "nobody.example");
        var refused = await nobody.Content.ReadAsStringAsync(Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, nobody.StatusCode);
        Assert.Contains($"<p class=\"field-failure\" id=\"first-run-handle-failure\">{WebUtility.HtmlEncode(Establish.NoParticipant("nobody.example"))}", refused);
        Assert.Contains("value=\"nobody.example\" aria-invalid=\"true\"", refused);
        Assert.Contains("value=\"Field notes\"", refused);
        Assert.Equal(0, await TangentSpace.Count.Exact(Cancellation));
        Assert.Equal(0, await Event.Count.Exact(Cancellation));

        using var named = await browser.SetUp("Field notes", Cancellation, proprietorHandle: "@Studio.Example");

        Assert.Equal(HttpStatusCode.SeeOther, named.StatusCode);
        var space = await TangentSpace.Get(TangentSpace.SingleRow, Cancellation);
        Assert.Equal((studio.Id, Leo), (space?.ProprietorId, space?.OperatorId));
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
        Assert.Contains("www.example.com", path[3].Where);
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
            .Send(new Establish(Leo, "leo.example", "Field notes", "https://fresh.example/notes"), Cancellation);

        Assert.IsType<Outcome<TangentSpace>.Done>(outcome);
        Assert.Equal(1, await TangentSpace.Count.Exact(Cancellation));
    }

    private static async Task<(HttpStatusCode Status, string Body)> Read(TangentSpaceFixture tangentSpace, string path, string mediaType)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(mediaType));
        using var response = await tangentSpace.Client.SendAsync(request, Cancellation);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(Cancellation));
    }

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
