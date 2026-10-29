using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Koan.Identity;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Events;
using Tangent.Space;
using Tangent.Writer;
using Xunit;
using Person = Koan.Identity.Identity;

namespace Tangent.Participants;

/// <summary>The sign-in page both ways, the person Koan reconciles from the DID, and the first arrival.</summary>
public sealed class SignInTests
{
    private const string BaseUrl = "https://tangent.example";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>A Tangent Space at an https base URL with Koan's atproto connector named as its sign-in.</summary>
    private static readonly Dictionary<string, string?> Atproto = new()
    {
        [TangentModule.BaseUrlKey] = BaseUrl,
        ["Koan:Web:Auth:Providers:atproto:Scopes:0"] = "atproto"
    };

    /// <summary>A Tangent Space where Koan's test connector signs people in. The profile job a first arrival submits
    /// reads from a loopback address nothing answers, so no test reaches the network.</summary>
    private static readonly Dictionary<string, string?> TestConnector = new()
    {
        ["Koan:Web:Auth:TestProvider:Enabled"] = "true",
        ["Tangent:Profile:BaseAddress"] = "http://127.0.0.1:1/",
        ["Tangent:Profile:Timeout"] = "00:00:01"
    };

    [Fact]
    public async Task The_sign_in_page_answers_as_the_handle_form_and_as_json_with_the_connectors_challenge()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Atproto, ct: Cancellation);

        var html = await Read(tangentSpace, "/sign-in", "text/html");
        using var json = JsonDocument.Parse(await Read(tangentSpace, "/sign-in", "application/json"));
        using var metadata = JsonDocument.Parse(await Read(tangentSpace, "/auth/atproto/client-metadata.json", "application/json"));

        var comeBack = "/sign-in?return=" + Uri.EscapeDataString("/");
        Assert.Contains($"<form class=\"sign-in-form\" method=\"get\" action=\"{BaseUrl}/auth/atproto/challenge\">", html);
        Assert.Contains("name=\"identifier\"", html);
        Assert.Contains($"<input type=\"hidden\" name=\"return\" value=\"{WebUtility.HtmlEncode(comeBack)}\" />", html);
        Assert.DoesNotContain("role=\"alert\"", html);
        Assert.DoesNotContain("<dialog", html);
        Assert.Equal($"{BaseUrl}/auth/atproto/challenge?return={Uri.EscapeDataString(comeBack)}", json.RootElement.GetProperty("challenge").GetString());
        Assert.Equal("/", json.RootElement.GetProperty("return").GetString());
        Assert.False(json.RootElement.TryGetProperty("arrival", out _));
        Assert.False(json.RootElement.TryGetProperty("failure", out _));
        // The connector the challenge names is composed, with its client metadata and callback under the base URL.
        Assert.Equal($"{BaseUrl}/auth/atproto/client-metadata.json", metadata.RootElement.GetProperty("client_id").GetString());
        Assert.Equal($"{BaseUrl}/auth/atproto/callback", metadata.RootElement.GetProperty("redirect_uris")[0].GetString());
    }

    [Fact]
    public async Task The_error_state_renders_the_connectors_sanitized_failure_inline_and_never_the_query()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Atproto, ct: Cancellation);

        var html = await Read(tangentSpace, "/sign-in?failure=" + SignIn.VerificationFailed, "text/html");
        using var json = JsonDocument.Parse(await Read(tangentSpace, "/sign-in?failure=" + SignIn.VerificationFailed, "application/json"));
        var other = await Read(tangentSpace, "/sign-in?failure=" + Uri.EscapeDataString("<b>provider said</b>"), "text/html");

        Assert.Contains("<p class=\"sign-in-failure\" id=\"sign-in-failure\" role=\"alert\">Your sign-in could not be verified. Start again.</p>", html);
        Assert.Contains("aria-describedby=\"sign-in-failure\"", html);
        Assert.Contains("name=\"identifier\"", html);
        Assert.Equal("Your sign-in could not be verified. Start again.", json.RootElement.GetProperty("failure").GetString());
        Assert.Contains("role=\"alert\">Sign-in could not be completed. Check your handle and start again.</p>", other);
        Assert.DoesNotContain("provider said", other);
    }

    [Fact]
    public async Task A_test_sign_in_with_a_did_subject_yields_a_koan_person_whose_external_identity_is_that_did()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector, ct: Cancellation);
        using var browser = new Browser(tangentSpace);
        const string did = "did:plc:ada4example";

        using var landed = await browser.SignIn("ada.example", did, Cancellation);

        Assert.Equal(HttpStatusCode.OK, landed.StatusCode);
        Assert.Equal("/sign-in", landed.RequestMessage!.RequestUri!.AbsolutePath);
        var person = await Person.Get(did, Cancellation);
        Assert.NotNull(person);
        var links = await ExternalIdentityLink.Query(link => link.IdentityId == did, Cancellation);
        var link = Assert.Single(links);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(did))), link.ProviderKeyHash);
        Assert.Equal("test", link.Provider);
    }

    [Theory]
    [InlineData(Admission.Join, "agent", ParticipantKind.Agent, Standing.Admitted)]
    [InlineData(Admission.Apply, "person", ParticipantKind.Person, Standing.Applied)]
    public async Task The_first_arrival_creates_one_participant_with_the_declared_kind_and_the_standing_the_admission_gives(
        Admission admission, string kind, ParticipantKind declared, Standing standing)
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector, ct: Cancellation);
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        var space = (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!;
        space.Admission = admission;
        Assert.IsType<Outcome<TangentSpace>.Done>(await writer.Send(new Store<TangentSpace>(space), Cancellation));
        using var browser = new Browser(tangentSpace);
        const string did = "did:plc:grace4example";

        using var arriving = await browser.SignIn("grace.example", did, Cancellation);
        var page = await arriving.Content.ReadAsStringAsync(Cancellation);
        using var asJson = await browser.Client.SendAsync(Json("/sign-in"), Cancellation);
        using var arrival = JsonDocument.Parse(await asJson.Content.ReadAsStringAsync(Cancellation));
        Assert.Null(await Participant.Get(did, Cancellation));
        Assert.Contains("name=\"kind\" value=\"person\"", page);
        Assert.Contains("name=\"kind\" value=\"agent\"", page);
        Assert.Equal(admission.ToString().ToLowerInvariant(), arrival.RootElement.GetProperty("arrival").GetProperty("admission").GetString());
        Assert.Equal("http://localhost:5220/sign-in", arrival.RootElement.GetProperty("arrival").GetProperty("action").GetString());

        using var declaredKind = await browser.Client.PostAsync("/sign-in",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["kind"] = kind, ["return"] = "/" }), Cancellation);

        Assert.Equal(HttpStatusCode.SeeOther, declaredKind.StatusCode);
        Assert.Equal("/", declaredKind.Headers.Location?.OriginalString);
        var participant = await Participant.Get(did, Cancellation);
        Assert.NotNull(participant);
        Assert.Equal((declared, standing), (participant.Kind, participant.Standing));
        var logged = Assert.Single(await Event.All(Cancellation));
        Assert.Equal((EventKind.StandingChanged, did, did), (logged.Kind, logged.ActorId, logged.TargetId));

        // A second sign-in by the same person creates nothing and appends nothing: the page sends them on.
        using var again = await browser.SignIn("grace.example", did, Cancellation);
        using var onward = await browser.Client.SendAsync(Json("/sign-in?return=%2Ft%2Fworkshop"), Cancellation);

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal("/", again.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Equal(HttpStatusCode.Redirect, onward.StatusCode);
        Assert.Equal("/t/workshop", onward.Headers.Location?.OriginalString);
        Assert.Equal(1, await Participant.Count.Exact(Cancellation));
        Assert.Equal(1, await Event.Count.Exact(Cancellation));
    }

    [Fact]
    public async Task Arriving_without_a_kind_answers_the_form_again_with_the_reason_inline_and_writes_nothing()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector, ct: Cancellation);
        using var browser = new Browser(tangentSpace);
        const string did = "did:plc:lin4example";
        using var arriving = await browser.SignIn("lin.example", did, Cancellation);

        using var undeclared = await browser.Client.PostAsync("/sign-in",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["kind"] = "robot", ["return"] = "/" }), Cancellation);
        var page = await undeclared.Content.ReadAsStringAsync(Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, undeclared.StatusCode);
        Assert.Contains("role=\"alert\">Choose whether you are a person or an agent.</p>", page);
        Assert.Contains("name=\"kind\" value=\"agent\"", page);
        Assert.Null(await Participant.Get(did, Cancellation));
        Assert.Equal(0, await Event.Count.Exact(Cancellation));
    }

    private static HttpRequestMessage Json(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static async Task<string> Read(TangentSpaceFixture tangentSpace, string path, string mediaType)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(mediaType));
        using var response = await tangentSpace.Client.SendAsync(request, Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Cancellation);
    }
}
