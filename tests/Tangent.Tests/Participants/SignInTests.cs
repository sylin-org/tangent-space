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
    private const string BaseUrl = TangentSpaceFixture.DefaultBaseUrl;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

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
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);

        var html = await Read(tangentSpace, "/sign-in", "text/html");
        using var json = JsonDocument.Parse(await Read(tangentSpace, "/sign-in", "application/json"));
        using var metadata = JsonDocument.Parse(await Read(tangentSpace, "/auth/atproto/client-metadata.json", "application/json"));

        var comeBack = "/sign-in?return=" + Uri.EscapeDataString("/");
        Assert.Contains($"<form class=\"sign-in-form\" method=\"get\" action=\"{BaseUrl}/auth/atproto/challenge\" data-island=\"submitting\">", html);
        Assert.Contains("name=\"identifier\" type=\"text\" required autofocus", html);
        Assert.Contains($"<input type=\"hidden\" name=\"return\" value=\"{WebUtility.HtmlEncode(comeBack)}\" />", html);
        Assert.Contains("<span aria-current=\"page\" class=\"path-current path-only\">Sign in</span>", html);
        Assert.Contains("data-island=\"appearance\"", html);
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
    public async Task The_error_states_render_the_failure_inline_keep_the_handle_and_never_echo_the_query()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);

        var unverified = WebUtility.HtmlDecode(await Read(tangentSpace, $"/sign-in?failure={SignIn.VerificationFailed}&handle=ada.bsky.social", "text/html"));
        using var json = JsonDocument.Parse(await Read(tangentSpace, "/sign-in?failure=" + SignIn.VerificationFailed, "application/json"));
        var notFound = await Read(tangentSpace, $"/sign-in?failure={SignIn.HandleNotFound}&handle=nobody.example", "text/html");
        var other = await Read(tangentSpace, "/sign-in?failure=" + Uri.EscapeDataString("<b>provider said</b>") + "&handle=" + Uri.EscapeDataString("a b"), "text/html");

        Assert.Contains("<p class=\"sign-in-failure\" id=\"sign-in-failure\" role=\"alert\">Your provider didn’t confirm the sign-in. Continue, then approve it on your provider’s page.</p>", unverified);
        Assert.Contains("value=\"ada.bsky.social\" aria-invalid=\"false\" aria-describedby=\"sign-in-failure\"", unverified);
        Assert.Equal("Your provider didn’t confirm the sign-in. Continue, then approve it on your provider’s page.", json.RootElement.GetProperty("failure").GetString());
        Assert.Contains("value=\"nobody.example\" aria-invalid=\"true\" aria-describedby=\"sign-in-handle-failure\"", notFound);
        Assert.Contains("<p class=\"field-failure\" id=\"sign-in-handle-failure\">No AT Protocol account answers to this handle.", notFound);
        Assert.DoesNotContain("role=\"alert\"", notFound);
        Assert.Contains("role=\"alert\">Sign-in could not be completed. Check your handle and continue to try again.</p>", other);
        Assert.DoesNotContain("provider said", other);
        Assert.DoesNotContain("value=\"a b\"", other);
    }

    [Theory]
    [InlineData("%2F%09%2Fevil.example")]
    [InlineData("%2F%2Fevil.example%2F")]
    [InlineData("%2F%5Cevil.example%2F")]
    [InlineData("https%3A%2F%2Fevil.example%2F")]
    public async Task A_return_that_is_not_a_relative_path_of_visible_characters_falls_to_the_door(string destination)
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector, ct: Cancellation);
        using var browser = new Browser(tangentSpace);
        using var signedIn = await browser.SignIn("ret", "did:plc:ret4example", Cancellation);

        using var visitor = await tangentSpace.Client.SendAsync(Json("/sign-in?return=" + destination), Cancellation);
        using var visiting = JsonDocument.Parse(await visitor.Content.ReadAsStringAsync(Cancellation));
        using var participant = await browser.Client.SendAsync(Json("/sign-in?return=" + destination), Cancellation);

        Assert.NotNull(await Participant.Get("did:plc:ret4example", Cancellation));
        Assert.Equal("/", visiting.RootElement.GetProperty("return").GetString());
        Assert.Equal(HttpStatusCode.Redirect, participant.StatusCode);
        Assert.Equal("/", participant.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task A_production_host_over_an_http_base_url_refuses_to_start_naming_the_provider()
    {
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => TangentSpaceFixture.StartAsync(
            new Dictionary<string, string?> { [TangentModule.BaseUrlKey] = "http://localhost:5220" }, ct: Cancellation));

        Assert.Contains("provider 'atproto'", failure.Message);
        Assert.Contains("Koan:Web:ApplicationUrl", failure.Message);
    }

    [Fact]
    public async Task A_test_sign_in_with_a_did_subject_yields_a_koan_person_whose_external_identity_is_that_did()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector, ct: Cancellation);
        using var browser = new Browser(tangentSpace);
        const string did = "did:plc:ada4example";

        using var landed = await browser.SignIn("ada.example", did, Cancellation);

        Assert.Equal(HttpStatusCode.OK, landed.StatusCode);
        Assert.Equal("/", landed.RequestMessage!.RequestUri!.AbsolutePath);
        var person = await Person.Get(did, Cancellation);
        Assert.NotNull(person);
        var links = await ExternalIdentityLink.Query(link => link.IdentityId == did, Cancellation);
        var link = Assert.Single(links);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(did))), link.ProviderKeyHash);
        Assert.Equal("test", link.Provider);
    }

    [Theory]
    [InlineData(Admission.Join, Standing.Admitted)]
    [InlineData(Admission.Apply, Standing.Applied)]
    public async Task A_first_sign_in_creates_one_participant_with_no_form_a_person_with_the_standing_the_admission_gives(
        Admission admission, Standing standing)
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector, ct: Cancellation);
        await Given.AnEstablishedTangentSpace(tangentSpace, admission: admission, ct: Cancellation);
        using var browser = new Browser(tangentSpace);
        const string did = "did:plc:grace4example";

        using var signedIn = await browser.SignIn("grace", did, Cancellation, handle: "grace.example");

        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        Assert.Equal("/", signedIn.RequestMessage!.RequestUri!.AbsolutePath);
        var participant = await Participant.Get(did, Cancellation);
        Assert.NotNull(participant);
        Assert.Equal((ParticipantKind.Person, KindSource.ProfileLabel, standing, "grace.example"),
            (participant.Kind, participant.KindSource, participant.Standing, participant.Handle));
        var logged = Assert.Single(await Event.All(Cancellation));
        Assert.Equal((EventKind.StandingChanged, did, did), (logged.Kind, logged.ActorId, logged.TargetId));

        // A second sign-in by the same person creates nothing and appends nothing.
        using var again = await browser.SignIn("grace", did, Cancellation, handle: "grace.example");
        using var onward = await browser.Client.SendAsync(Json("/sign-in?return=%2Ft%2Fworkshop"), Cancellation);

        Assert.Equal("/", again.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Equal(HttpStatusCode.Redirect, onward.StatusCode);
        Assert.Equal("/t/workshop", onward.Headers.Location?.OriginalString);
        Assert.Equal(1, await Participant.Count.Exact(Cancellation));
        Assert.Equal(1, await Event.Count.Exact(Cancellation));
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
