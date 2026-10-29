using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Koan.Data.Core;
using Koan.Identity.Roles;
using Koan.Security.Trust.Issuer;
using Koan.Web.Auth.Server;
using Koan.Web.Auth.Server.Protocol;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Events;
using Tangent.Lifecycle;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Space;
using Tangent.Tangents;
using Tangent.Writer;
using Xunit;

namespace Tangent.Delegates;

/// <summary>The delegated agent door: Koan's OAuth server under the base URL, the pre-registered client
/// <c>delegate</c> through the Device flow, the consent page a signed-in person decides on, a delegate's token read
/// as the person who approved it, the one antiforgery rule, and signing keys that outlive a restart and a restore.</summary>
public sealed class DelegatedDoorTests
{
    private const string BaseUrl = TangentSpaceFixture.DefaultBaseUrl;
    private const string Leo = "did:plc:leo4example";
    private const string DeviceGrant = "urn:ietf:params:oauth:grant-type:device_code";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>A Tangent Space where Koan's test connector signs people in, no profile job reaches the network, and a
    /// device may poll as often as a test asks, with <paramref name="settings"/> besides.</summary>
    private static Dictionary<string, string?> TestConnector(params (string Key, string Value)[] settings)
    {
        var all = new Dictionary<string, string?>
        {
            ["Koan:Web:Auth:TestProvider:Enabled"] = "true",
            ["Tangent:Profile:BaseAddress"] = "http://127.0.0.1:1/",
            ["Tangent:Profile:Timeout"] = "00:00:01",
            ["Koan:Web:Auth:Server:DevicePollIntervalSeconds"] = "0"
        };
        foreach (var (key, value) in settings) all[key] = value;
        return all;
    }

    [Fact]
    public async Task The_metadata_answers_under_the_base_url_with_the_device_flow_among_its_grants_and_no_development_client_exists()
    {
        await using (var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation))
        {
            using var metadata = await Json(tangentSpace.Client, "/.well-known/oauth-authorization-server");
            var root = metadata.RootElement;

            Assert.Equal(BaseUrl, root.GetProperty("issuer").GetString());
            Assert.Equal(
                (BaseUrl + "/oauth/authorize", BaseUrl + "/oauth/token", BaseUrl + "/oauth/device", BaseUrl + "/.well-known/jwks.json", BaseUrl + "/oauth/register"),
                (root.GetProperty("authorization_endpoint").GetString(), root.GetProperty("token_endpoint").GetString(),
                    root.GetProperty("device_authorization_endpoint").GetString(), root.GetProperty("jwks_uri").GetString(),
                    root.GetProperty("registration_endpoint").GetString()));
            Assert.Contains(DeviceGrant, Strings(root.GetProperty("grant_types_supported")));
            Assert.Equal(["none"], Strings(root.GetProperty("token_endpoint_auth_methods_supported")));

            Assert.Null(await OAuthClient.Get(AuthServerModule.DevExplorerClientId, Cancellation));
            var shared = await OAuthClient.Get(DelegateClient.Id, Cancellation);
            Assert.NotNull(shared);
            Assert.Equal((false, null, 0), (shared.IsDynamic, shared.ExpiresUtc, shared.RedirectUris.Count));
            Assert.Equal([OAuthGrantTypes.DeviceCode], shared.GrantTypes);
            Assert.Equal(1, await OAuthClient.Count.Exact(Cancellation));
            using var developmentToken = await tangentSpace.Client.GetAsync("/oauth/dev-token", Cancellation);
            Assert.Equal(HttpStatusCode.NotFound, developmentToken.StatusCode);
        }

        // Under a base URL with a path, the issuer and every endpoint carry the path.
        const string underPath = "https://example.test/notes";
        await using var notes = await TangentSpaceFixture.StartAsync(
            new Dictionary<string, string?> { [TangentModule.BaseUrlKey] = underPath }, ct: Cancellation);
        using var underMetadata = await Json(notes.Client, "/notes/.well-known/oauth-authorization-server");
        Assert.Equal(underPath, underMetadata.RootElement.GetProperty("issuer").GetString());
        Assert.Equal(underPath + "/oauth/device", underMetadata.RootElement.GetProperty("device_authorization_endpoint").GetString());
        using var device = await Json(notes.Client, "/notes/oauth/device", Form(("client_id", DelegateClient.Id), ("resource", underPath)));
        Assert.Equal(underPath + "/consent", device.RootElement.GetProperty("verification_uri").GetString());
    }

    [Fact]
    public async Task A_loopback_client_registers_itself_and_the_consent_page_shows_its_name_and_redirect_and_nothing_else_registers()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector(), ct: Cancellation);
        await Place(tangentSpace);
        const string redirect = "http://127.0.0.1:43123/callback";

        using var registered = await Json(tangentSpace.Client, "/oauth/register",
            JsonContent("{\"client_name\":\"Companion Lobby\",\"redirect_uris\":[\"" + redirect + "\"]}"), HttpStatusCode.Created);
        var clientId = registered.RootElement.GetProperty("client_id").GetString()!;
        Assert.Equal("none", registered.RootElement.GetProperty("token_endpoint_auth_method").GetString());
        using var refused = await tangentSpace.Client.PostAsync("/oauth/register",
            JsonContent("{\"client_name\":\"Elsewhere\",\"redirect_uris\":[\"https://elsewhere.example/callback\"]}"), Cancellation);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        using var browser = new Browser(tangentSpace);
        (await browser.SignIn("leo", Leo, Cancellation, handle: "leo.example")).Dispose();
        var verifier = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var authorize = "/oauth/authorize?response_type=code&client_id=" + clientId + "&redirect_uri=" + Uri.EscapeDataString(redirect)
            + "&code_challenge=" + challenge + "&code_challenge_method=S256&resource=" + Uri.EscapeDataString(BaseUrl) + "&state=s1";
        using var consent = await browser.Follow(HttpMethod.Get, authorize, Cancellation);
        var page = await consent.Content.ReadAsStringAsync(Cancellation);
        var rid = new Uri(consent.RequestMessage!.RequestUri!.ToString()).Query.Split("rid=")[1];

        Assert.Equal(HttpStatusCode.OK, consent.StatusCode);
        Assert.Equal("/consent", consent.RequestMessage.RequestUri!.AbsolutePath);
        Assert.Contains("<strong>Companion Lobby</strong> asks to act as you in Field notes.", page);
        Assert.Contains("<span class=\"code-run\">127.0.0.1:43123/</span>", page);
        Assert.Contains($"action=\"{BaseUrl}/oauth/request/{rid}/approve\"", page);
        using (var decision = await Json(browser.Client, "/consent?rid=" + rid))
            Assert.Equal(("Companion Lobby", redirect), (decision.RootElement.GetProperty("decision").GetProperty("client").GetString(),
                decision.RootElement.GetProperty("decision").GetProperty("redirect").GetString()));

        var token = await browser.Token("/consent?rid=" + rid, Cancellation);
        using var approved = await browser.Client.PostAsync("/oauth/request/" + rid + "/approve", new FormUrlEncodedContent(token), Cancellation);
        Assert.Equal(HttpStatusCode.Redirect, approved.StatusCode);
        var back = approved.Headers.Location!;
        Assert.StartsWith(redirect + "?code=", back.ToString());
        var code = back.Query.Split('&').Single(pair => pair.StartsWith("?code=", StringComparison.Ordinal))[6..];
        using var granted = await Json(tangentSpace.Client, "/oauth/token", Form(("grant_type", "authorization_code"), ("code", Uri.UnescapeDataString(code)),
            ("client_id", clientId), ("redirect_uri", redirect), ("code_verifier", verifier)));
        Assert.False(string.IsNullOrEmpty(granted.RootElement.GetProperty("access_token").GetString()));
    }

    [Fact]
    public async Task The_device_flow_takes_a_visitor_through_sign_in_and_the_code_to_an_approval_that_answers_the_client_an_access_token()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector(), ct: Cancellation);
        await Place(tangentSpace);
        var device = await Device(tangentSpace);
        Assert.Equal(BaseUrl + "/consent", device.VerificationUri);
        Assert.Equal(BaseUrl + AtCode(device.Code), device.Complete);

        using var browser = new Browser(tangentSpace);
        using (var visitor = await browser.Client.GetAsync(new Uri(device.Complete).PathAndQuery, Cancellation))
        {
            Assert.Equal(HttpStatusCode.Redirect, visitor.StatusCode);
            Assert.Equal(BaseUrl + "/sign-in?return=" + Uri.EscapeDataString(AtCode(device.Code)), visitor.Headers.Location?.ToString());
        }

        (await browser.SignIn("leo", Leo, Cancellation, handle: "leo.example")).Dispose();
        var entry = await Html(browser, "/consent");
        Assert.Contains("<h1 id=\"consent-title\" class=\"sign-in-title\">Let an agent act as you</h1>", entry);
        Assert.Contains("name=\"user_code\"", entry);
        Assert.Contains($"<form class=\"sign-in-form\" method=\"get\" action=\"{BaseUrl}/consent\"", entry);

        // The code as a person types it: lower case, without its hyphen.
        var request = await Html(browser, AtCode(device.Code.Replace("-", "").ToLowerInvariant()));
        Assert.Contains($"The device or agent that showed you the code <strong>{device.Code}</strong> asks to act as you in Field notes.", request);
        Assert.Contains("It reads and acts here as <strong>@leo.example</strong>, with everything you can do, and never more.", request);
        Assert.Contains("Any device or agent can show a code like this one, so this page can’t name it", WebUtility.HtmlDecode(request));
        Assert.Contains($"action=\"{BaseUrl}/oauth/request/{device.Code.Replace("-", "")}/approve\"", request);
        Assert.Contains($"action=\"{BaseUrl}/oauth/request/{device.Code.Replace("-", "")}/deny\"", request);
        using (var pending = await Poll(tangentSpace, device))
            Assert.Equal("authorization_pending", await Error(pending));

        using var approved = await Decide(browser, device, "approve");
        Assert.Equal(HttpStatusCode.Redirect, approved.StatusCode);
        Assert.StartsWith(BaseUrl + "/consent/done?outcome=", approved.Headers.Location?.ToString());
        var done = await Html(browser, approved.Headers.Location!.PathAndQuery);
        Assert.Contains("<h1 id=\"consent-title\" class=\"sign-in-title\">You approved the agent</h1>", done);
        Assert.Contains("Go back to the device or agent: it acts as you here now.", done);

        // The done page says only what Koan wrote for this person: a plain address, a forged outcome and the genuine one
        // read by a visitor show nothing decided.
        Assert.Contains("class=\"sign-in-title\">Nothing to show</h1>", await Html(browser, "/consent/done"));
        Assert.Contains("class=\"sign-in-title\">Nothing to show</h1>", await Html(browser, "/consent/done?outcome=Approved"));
        using (var visitor = new Browser(tangentSpace))
            Assert.Contains("class=\"sign-in-title\">Nothing to show</h1>", await Html(visitor, approved.Headers.Location!.PathAndQuery));

        using var granted = await Poll(tangentSpace, device);
        using var body = JsonDocument.Parse(await granted.Content.ReadAsStringAsync(Cancellation));
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        Assert.False(string.IsNullOrEmpty(body.RootElement.GetProperty("access_token").GetString()));
        Assert.Equal("Bearer", body.RootElement.GetProperty("token_type").GetString());
        Assert.Equal(900, body.RootElement.GetProperty("expires_in").GetInt32());
        Assert.True(body.RootElement.TryGetProperty("refresh_token", out _));
    }

    [Fact]
    public async Task Under_a_base_url_with_a_path_the_consent_page_and_sign_in_return_with_the_path_and_the_person_decides_there()
    {
        const string underPath = "https://www.example.com/notes";
        await using var notes = await TangentSpaceFixture.StartAsync(TestConnector((TangentModule.BaseUrlKey, underPath)), ct: Cancellation);
        await Place(notes);
        using var device = await Json(notes.Client, "/notes/oauth/device", Form(("client_id", DelegateClient.Id), ("resource", underPath)));
        var code = device.RootElement.GetProperty("user_code").GetString()!;
        var consent = "/notes" + AtCode(code);

        // The visitor is sent to sign in with the consent page, base path and code, as where to come back to.
        using var browser = new Browser(notes);
        using (var visitor = await browser.Client.GetAsync(consent, Cancellation))
            Assert.Equal(underPath + "/sign-in?return=" + Uri.EscapeDataString(consent), visitor.Headers.Location?.ToString());

        // The sign-in page hands the connector a return under the base path, which Koan keeps (SEC-0029).
        using var signIn = await Json(notes.Client, "/notes/sign-in?return=" + Uri.EscapeDataString(consent));
        var comeBack = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(
            new Uri(signIn.RootElement.GetProperty("challenge").GetString()!).Query)["return"].ToString();
        Assert.Equal("/notes/sign-in?return=" + Uri.EscapeDataString(consent), comeBack);
        using var arrived = await browser.SignIn("leo", Leo, Cancellation, handle: "leo.example", under: "/notes", comeBack: comeBack);
        var page = await arrived.Content.ReadAsStringAsync(Cancellation);

        Assert.Equal(HttpStatusCode.OK, arrived.StatusCode);
        Assert.Equal(consent, arrived.RequestMessage!.RequestUri!.PathAndQuery);
        Assert.Contains($"The device or agent that showed you the code <strong>{code}</strong> asks to act as you in Field notes.", page);
        Assert.Contains($"action=\"{underPath}/oauth/request/{code.Replace("-", "")}/approve\"", page);

        using var approved = await browser.Client.PostAsync("/notes/oauth/request/" + code.Replace("-", "") + "/approve",
            new FormUrlEncodedContent(await browser.Token(consent, Cancellation)), Cancellation);
        Assert.StartsWith(underPath + "/consent/done?outcome=", approved.Headers.Location?.ToString());
        Assert.Contains("class=\"sign-in-title\">You approved the agent</h1>", await Html(browser, approved.Headers.Location!.PathAndQuery));
    }

    [Fact]
    public async Task With_a_delegates_token_and_no_cookie_a_private_tangent_answers_with_the_persons_can_and_without_it_answers_absent()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector(), ct: Cancellation);
        await PrivatePlace(tangentSpace);
        using var browser = new Browser(tangentSpace);
        (await browser.SignIn("leo", Leo, Cancellation, handle: "leo.example")).Dispose();
        var token = await Delegate(tangentSpace, browser);

        using var asPerson = await Json(browser.Client, "/t/studio");
        using var asDelegate = await Json(tangentSpace.Client, "/t/studio", bearer: token);
        var visitor = await Answer(tangentSpace.Client, "/t/studio");
        var missing = await Answer(tangentSpace.Client, "/t/nowhere");

        Assert.Equal("studio", asDelegate.RootElement.GetProperty("slug").GetString());
        Assert.Equal(Strings(asPerson.RootElement.GetProperty("can")), Strings(asDelegate.RootElement.GetProperty("can")));
        Assert.Contains("ManageTangent", Strings(asDelegate.RootElement.GetProperty("can")));
        Assert.Equal((HttpStatusCode.NotFound, Capabilities.Absent), (visitor.Status, visitor.Gate));
        Assert.Equal((missing.Status, missing.Title, missing.Gate), (visitor.Status, visitor.Title, visitor.Gate));

        // A token Koan's issuer signed for another resource is not this Tangent Space's: it is refused, never read as
        // the person.
        var elsewhere = tangentSpace.Services.GetRequiredService<IIssuer>().Issue(
            new TrustClaims { Subject = Leo }, TimeSpan.FromMinutes(5), audience: "https://elsewhere.example");
        using (var foreign = new HttpRequestMessage(HttpMethod.Get, "/t/studio") { Headers = { Authorization = new AuthenticationHeaderValue("Bearer", elsewhere) } })
        {
            foreign.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var refused = await tangentSpace.Client.SendAsync(foreign, Cancellation);
            Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
            Assert.Equal("Bearer error=\"invalid_token\"", refused.Headers.WwwAuthenticate.ToString());
            Assert.Equal("", await refused.Content.ReadAsStringAsync(Cancellation));
        }

        // A delegate cannot decide for the person: the consent page refuses its token, and Koan's approve takes no
        // post without the page's form token.
        var another = await Device(tangentSpace);
        var page = await Answer(tangentSpace.Client, AtCode(another.Code), bearer: token);
        using var approve = new HttpRequestMessage(HttpMethod.Post, "/oauth/request/" + another.Code.Replace("-", "") + "/approve")
        {
            Content = Form(("unused", "")),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) }
        };
        using var approval = await tangentSpace.Client.SendAsync(approve, Cancellation);
        Assert.Equal(HttpStatusCode.Forbidden, page.Status);
        Assert.Equal(HttpStatusCode.BadRequest, approval.StatusCode);
        using var stillPending = await Poll(tangentSpace, another);
        Assert.Equal("authorization_pending", await Error(stillPending));
    }

    [Fact]
    public async Task Deny_a_wrong_code_and_an_expired_code_each_end_without_a_token_and_the_wrong_code_is_said_under_its_field()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector(), ct: Cancellation);
        await Place(tangentSpace);
        using var browser = new Browser(tangentSpace);
        (await browser.SignIn("leo", Leo, Cancellation, handle: "leo.example")).Dispose();

        var denied = await Device(tangentSpace);
        using (var deny = await Decide(browser, denied, "deny"))
        {
            Assert.StartsWith(BaseUrl + "/consent/done?outcome=", deny.Headers.Location?.ToString());
            var done = await Html(browser, deny.Headers.Location!.PathAndQuery);
            Assert.Contains("<h1 id=\"consent-title\" class=\"sign-in-title\">You denied the agent</h1>", done);
            Assert.Contains("It was told no and holds nothing.", done);
        }
        using (var poll = await Poll(tangentSpace, denied))
            Assert.Equal("access_denied", await Error(poll));

        var waiting = await Device(tangentSpace);
        var wrong = await Answer(browser.Client, AtCode("ZZZZ-ZZZZ"), html: true);
        Assert.Equal(HttpStatusCode.BadRequest, wrong.Status);
        Assert.Contains("<p class=\"field-failure\" id=\"consent-code-failure\">No request is waiting for that code: it may be mistyped or expired.", wrong.Body);
        Assert.Contains("value=\"ZZZZ-ZZZZ\"", wrong.Body);
        Assert.Contains("aria-invalid=\"true\"", wrong.Body);
        Assert.DoesNotContain("/approve", wrong.Body);
        using (var poll = await Poll(tangentSpace, waiting))
            Assert.Equal("authorization_pending", await Error(poll));

        var expired = await Device(tangentSpace);
        var row = Assert.Single(await DeviceCode.Query(stored => stored.UserCode == expired.Code.Replace("-", ""), Cancellation));
        row.ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        await row.Save(Cancellation);
        var late = await Answer(browser.Client, AtCode(expired.Code), html: true);
        Assert.Equal(HttpStatusCode.BadRequest, late.Status);
        Assert.Contains("id=\"consent-code-failure\">No request is waiting for that code", late.Body);
        using (var poll = await Poll(tangentSpace, expired))
            Assert.Equal("expired_token", await Error(poll));
    }

    [Fact]
    public async Task A_session_post_without_its_form_token_is_refused_and_a_bearer_post_is_not_asked_for_one()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector((FirstRun.OperatorSetting, Leo)), ct: Cancellation);
        using var browser = new Browser(tangentSpace);
        (await browser.SignIn("leo", Leo, Cancellation, handle: "leo.example")).Dispose();
        var fields = new Dictionary<string, string> { ["name"] = "Field notes", ["proprietor"] = "me", ["accountable"] = "yes" };

        var session = await Answer(browser.Client, "/first-run", content: new FormUrlEncodedContent(fields));
        Assert.Equal((HttpStatusCode.BadRequest, "Open the form again"), (session.Status, session.Title));
        Assert.Equal(0, await TangentSpace.Count.Exact(Cancellation));

        var token = tangentSpace.Services.GetRequiredService<IIssuer>().Issue(new TrustClaims { Subject = Leo }, TimeSpan.FromMinutes(5), audience: BaseUrl);
        using var bearer = new HttpRequestMessage(HttpMethod.Post, "/first-run")
        {
            Content = new FormUrlEncodedContent(fields),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) }
        };
        // An agent's client, which follows no redirect: the answer is the post's own.
        using var agent = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = tangentSpace.Client.BaseAddress };
        using var established = await agent.SendAsync(bearer, Cancellation);
        Assert.Equal(HttpStatusCode.SeeOther, established.StatusCode);
        Assert.Equal(("Field notes", Leo), ((await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))?.Name, (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))?.OperatorId));

        // Koan's approve, posted from a session without the consent page's token, is refused the same way.
        var device = await Device(tangentSpace);
        var unsigned = await Answer(browser.Client, "/oauth/request/" + device.Code.Replace("-", "") + "/approve", content: Form(("unused", "")));
        Assert.Equal((HttpStatusCode.BadRequest, "Open the form again"), (unsigned.Status, unsigned.Title));
        using var poll = await Poll(tangentSpace, device);
        Assert.Equal("authorization_pending", await Error(poll));
    }

    [Fact]
    public async Task A_session_post_to_koans_approve_carrying_a_bearer_but_no_form_token_is_refused_and_with_the_token_approves()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector(), ct: Cancellation);
        await Place(tangentSpace);
        using var browser = new Browser(tangentSpace);
        (await browser.SignIn("leo", Leo, Cancellation, handle: "leo.example")).Dispose();
        var mallory = tangentSpace.Services.GetRequiredService<IIssuer>().Issue(
            new TrustClaims { Subject = "did:plc:mallory4example" }, TimeSpan.FromMinutes(5), audience: BaseUrl);
        var device = await Device(tangentSpace);
        var approve = "/oauth/request/" + device.Code.Replace("-", "") + "/approve";

        // Leo's session cookie rides along, the decision is the session's, and a valid bearer stands in for nothing.
        using (var forged = new HttpRequestMessage(HttpMethod.Post, approve)
               {
                   Content = Form(("unused", "")),
                   Headers = { Authorization = new AuthenticationHeaderValue("Bearer", mallory) }
               })
        {
            using var refused = await browser.Client.SendAsync(forged, Cancellation);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Null(refused.Headers.Location);
        }

        using (var poll = await Poll(tangentSpace, device))
            Assert.Equal("authorization_pending", await Error(poll));
        using var approved = await Decide(browser, device, "approve");
        Assert.StartsWith(BaseUrl + "/consent/done?outcome=", approved.Headers.Location?.ToString());
    }

    [Fact]
    public async Task A_waiting_code_whose_client_is_not_allowed_the_device_flow_is_refused_under_its_field_with_nothing_to_approve()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector(), ct: Cancellation);
        await Place(tangentSpace);
        var impostor = await Register(tangentSpace, "Field notes official app", "http://127.0.0.1:1/cb");
        // A code the client asked for before Koan allowed the Device flow only to the clients that name it, still waiting.
        var waiting = new DeviceCode
        {
            Id = "waiting-before-the-grant",
            UserCode = "CDFGHJKM",
            ClientId = impostor,
            Resource = BaseUrl,
            Status = DeviceCode.StatusPending,
            IntervalSeconds = 0,
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(10)
        };
        await waiting.Save(Cancellation);
        using var browser = new Browser(tangentSpace);
        (await browser.SignIn("leo", Leo, Cancellation, handle: "leo.example")).Dispose();

        var refused = await Answer(browser.Client, AtCode("CDFG-HJKM"), html: true);
        var page = WebUtility.HtmlDecode(refused.Body);

        Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
        Assert.Contains("id=\"consent-code-failure\">This code was not issued for this Tangent Space’s device door, so it can’t be approved here.</p>", page);
        Assert.Contains("value=\"CDFG-HJKM\"", page);
        Assert.DoesNotContain("Field notes official app", page);
        Assert.DoesNotContain("/approve", page);
        Assert.DoesNotContain("/deny", page);
        using var poll = await tangentSpace.Client.PostAsync("/oauth/token",
            Form(("grant_type", DeviceGrant), ("device_code", waiting.Id), ("client_id", impostor)), Cancellation);
        Assert.Equal("authorization_pending", await Error(poll));
    }

    [Fact]
    public async Task A_token_issued_before_a_restart_verifies_after_it_on_the_same_data_folder_and_after_a_restore_of_its_backup()
    {
        var dataFolder = Path.Combine(Path.GetTempPath(), "tangent-data-" + Guid.CreateVersion7().ToString("n"));
        var backups = Path.Combine(Path.GetTempPath(), "tangent-backups-" + Guid.CreateVersion7().ToString("n"));
        var restoredFolder = Path.Combine(Path.GetTempPath(), "tangent-restored-" + Guid.CreateVersion7().ToString("n"));
        try
        {
            string token;
            List<string> keys;
            await using (var first = await TangentSpaceFixture.StartAsync(TestConnector(("Tangent:DataFolder", dataFolder)), ct: Cancellation))
            {
                await PrivatePlace(first);
                using var browser = new Browser(first);
                (await browser.SignIn("leo", Leo, Cancellation, handle: "leo.example")).Dispose();
                token = await Delegate(first, browser);
                keys = await Kids(first);
                Assert.Equal(HttpStatusCode.OK, (await Answer(first.Client, "/t/studio", bearer: token)).Status);
            }

            await using (var restarted = await TangentSpaceFixture.StartAsync(TestConnector(("Tangent:DataFolder", dataFolder)), ct: Cancellation))
            {
                Assert.Equal(keys, await Kids(restarted));
                using var page = await Json(restarted.Client, "/t/studio", bearer: token);
                Assert.Contains("ManageTangent", Strings(page.RootElement.GetProperty("can")));
            }

            var backup = Backup.Take(dataFolder, backups);
            Backup.Restore(backup, restoredFolder, replace: false);
            await using var restored = await TangentSpaceFixture.StartAsync(TestConnector(("Tangent:DataFolder", restoredFolder)), ct: Cancellation);
            Assert.Equal(keys, await Kids(restored));
            using var restoredPage = await Json(restored.Client, "/t/studio", bearer: token);
            Assert.Equal("studio", restoredPage.RootElement.GetProperty("slug").GetString());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var folder in new[] { dataFolder, backups, restoredFolder })
                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task A_self_registered_client_is_refused_the_device_flow_so_no_code_it_asked_for_reaches_the_consent_page()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector(), ct: Cancellation);
        await Place(tangentSpace);
        using var registered = await Json(tangentSpace.Client, "/oauth/register",
            JsonContent("{\"client_name\":\"Field notes official app\",\"redirect_uris\":[\"http://127.0.0.1:1/cb\"],\"grant_types\":[\"" + DeviceGrant + "\"]}"),
            HttpStatusCode.Created);
        var impostor = registered.RootElement.GetProperty("client_id").GetString()!;
        var devices = await DeviceCode.Count.Exact(Cancellation);

        using var device = await tangentSpace.Client.PostAsync("/oauth/device", Form(("client_id", impostor), ("resource", BaseUrl)), Cancellation);

        Assert.Equal(["authorization_code"], Strings(registered.RootElement.GetProperty("grant_types")));
        Assert.Equal("unauthorized_client", await Error(device));
        Assert.Equal(devices, await DeviceCode.Count.Exact(Cancellation));
        // The shared client is the only one allowed the Device flow, so every code the consent page finds is its.
        Assert.Equal([DelegateClient.Id], (await OAuthClient.All(Cancellation)).Where(client => client.AllowsGrant(DeviceGrant)).Select(client => client.Id));
    }

    [Fact]
    public async Task A_delegate_client_row_without_the_device_flow_is_given_it_at_the_next_start()
    {
        var dataFolder = Path.Combine(Path.GetTempPath(), "tangent-data-" + Guid.CreateVersion7().ToString("n"));
        try
        {
            await using (var first = await TangentSpaceFixture.StartAsync(TestConnector(("Tangent:DataFolder", dataFolder)), ct: Cancellation))
            {
                var row = (await OAuthClient.Get(DelegateClient.Id, Cancellation))!;
                row.GrantTypes = [];
                await row.Save(Cancellation);
                Assert.False((await OAuthClient.Get(DelegateClient.Id, Cancellation))!.AllowsGrant(DeviceGrant));
            }

            await using var restarted = await TangentSpaceFixture.StartAsync(TestConnector(("Tangent:DataFolder", dataFolder)), ct: Cancellation);
            Assert.Equal([OAuthGrantTypes.DeviceCode], (await OAuthClient.Get(DelegateClient.Id, Cancellation))!.GrantTypes);
            Assert.Equal(BaseUrl + "/consent", (await Device(restarted)).VerificationUri);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(dataFolder)) Directory.Delete(dataFolder, recursive: true);
        }
    }

    [Fact]
    public async Task A_bearer_decides_nothing_at_the_authorize_or_request_endpoints_so_a_remembered_consent_issues_no_code_to_a_delegate()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector(), ct: Cancellation);
        await Place(tangentSpace);
        const string redirect = "http://127.0.0.1:43123/callback";
        var clientId = await Register(tangentSpace, "Companion Lobby", redirect);
        using var browser = new Browser(tangentSpace);
        (await browser.SignIn("leo", Leo, Cancellation, handle: "leo.example")).Dispose();

        // The person connects the loopback client once, so Koan remembers the consent for it.
        var (verifier, challenge) = Pkce();
        using var consent = await browser.Follow(HttpMethod.Get, Authorize(clientId, redirect, challenge), Cancellation);
        var rid = consent.RequestMessage!.RequestUri!.Query.Split("rid=")[1];
        using (var approved = await browser.Client.PostAsync("/oauth/request/" + rid + "/approve",
                   new FormUrlEncodedContent(await browser.Token("/consent?rid=" + rid, Cancellation)), Cancellation))
        {
            var code = approved.Headers.Location!.Query.Split('&').Single(pair => pair.StartsWith("?code=", StringComparison.Ordinal))[6..];
            using var exchanged = await Json(tangentSpace.Client, "/oauth/token", Form(("grant_type", "authorization_code"), ("code", Uri.UnescapeDataString(code)),
                ("client_id", clientId), ("redirect_uri", redirect), ("code_verifier", verifier)));
        }

        using (var remembered = await browser.Client.GetAsync(Authorize(clientId, redirect, Pkce().Challenge), Cancellation))
            Assert.StartsWith(redirect + "?code=", remembered.Headers.Location?.ToString());

        var token = await Delegate(tangentSpace, browser);
        using var agent = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = tangentSpace.Client.BaseAddress };
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var another = await Device(tangentSpace);
        var codes = await AuthorizationCode.Count.Exact(Cancellation);

        using var authorize = await agent.GetAsync(Authorize(clientId, redirect, Pkce().Challenge), Cancellation);
        using var request = await agent.GetAsync("/oauth/request/" + another.Code.Replace("-", ""), Cancellation);
        using var deny = await agent.PostAsync("/oauth/request/" + another.Code.Replace("-", "") + "/deny", Form(("unused", "")), Cancellation);

        // Koan reads only the person's session there: the remembered consent sends the delegate to the consent page
        // instead of to its client and the request reads as nobody signed in; a denial without the page's form token
        // never reaches Koan.
        Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);
        Assert.StartsWith(BaseUrl + "/consent?rid=", new Uri(new Uri(BaseUrl), authorize.Headers.Location!).AbsoluteUri);
        Assert.Equal(HttpStatusCode.OK, request.StatusCode);
        using (var projection = JsonDocument.Parse(await request.Content.ReadAsStringAsync(Cancellation)))
            Assert.False(projection.RootElement.GetProperty("user").GetProperty("loggedIn").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, deny.StatusCode);
        Assert.Equal(codes, await AuthorizationCode.Count.Exact(Cancellation));
        using var poll = await Poll(tangentSpace, another);
        Assert.Equal("authorization_pending", await Error(poll));
    }

    [Fact]
    public async Task The_page_and_koan_count_code_look_ups_from_one_address_against_one_allowance()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(
            TestConnector(("Koan:Web:Auth:Server:UserCodeVerificationRateLimitPerMinute", "3")), ct: Cancellation);
        await Place(tangentSpace);
        using var browser = new Browser(tangentSpace);
        (await browser.SignIn("leo", Leo, Cancellation, handle: "leo.example")).Dispose();

        for (var attempt = 0; attempt < 3; attempt++)
            Assert.Equal(HttpStatusCode.BadRequest, (await Answer(browser.Client, AtCode("ZZZZ-ZZZZ"), html: true)).Status);
        using var koan = await tangentSpace.Client.GetAsync("/oauth/request/ZZZZZZZZ", Cancellation);

        Assert.Equal(HttpStatusCode.TooManyRequests, koan.StatusCode);
    }

    [Fact]
    public async Task A_session_whose_request_carries_another_authorization_scheme_reads_the_door_as_the_person()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector(), ct: Cancellation);
        await Place(tangentSpace);
        using var browser = new Browser(tangentSpace);
        (await browser.SignIn("leo", Leo, Cancellation, handle: "leo.example")).Dispose();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes("proxy:secret"))) }
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var door = await browser.Client.SendAsync(request, Cancellation);
        using var json = JsonDocument.Parse(await door.Content.ReadAsStringAsync(Cancellation));

        Assert.Equal(HttpStatusCode.OK, door.StatusCode);
        Assert.Equal(Leo, json.RootElement.GetProperty("who").GetProperty("id").GetString());
    }

    [Fact]
    public async Task Marking_the_door_seen_takes_a_bearer_without_a_form_token_and_refuses_a_session_without_one()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector(), ct: Cancellation);
        await PrivatePlace(tangentSpace);
        using var browser = new Browser(tangentSpace);
        (await browser.SignIn("leo", Leo, Cancellation, handle: "leo.example")).Dispose();
        var token = await Delegate(tangentSpace, browser);
        var through = (await Event.HighWaterMark.Load(Cancellation)).ToString(System.Globalization.CultureInfo.InvariantCulture);

        var session = await Answer(browser.Client, "/", content: Form((DoorController.Through, through)));
        Assert.Equal((HttpStatusCode.BadRequest, "Open the door again"), (session.Status, session.Title));
        Assert.Null(await Checkpoint.Get(Checkpoint.Key(Leo, Scope.Place), Cancellation));

        using var agent = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = tangentSpace.Client.BaseAddress };
        using var marked = new HttpRequestMessage(HttpMethod.Post, "/")
        {
            Content = Form((DoorController.Through, through)),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) }
        };
        using var seen = await agent.SendAsync(marked, Cancellation);

        Assert.Equal(HttpStatusCode.SeeOther, seen.StatusCode);
        Assert.Equal(through, (await Checkpoint.Get(Checkpoint.Key(Leo, Scope.Place), Cancellation))?.Seq.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>An established place, Field notes, where Leo is an administrator.</summary>
    private static async Task Place(TangentSpaceFixture tangentSpace)
    {
        await Given.AnEstablishedTangentSpace(tangentSpace, "Field notes", ct: Cancellation);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
    }

    /// <summary>The place with a private Tangent, the studio, which Leo, an administrator, may view and a visitor may
    /// not.</summary>
    private static async Task PrivatePlace(TangentSpaceFixture tangentSpace)
    {
        await Place(tangentSpace);
        Assert.IsType<Outcome<Tangents.Tangent>.Done>(await tangentSpace.Services.GetRequiredService<WriteLoop>().Send(
            new CreateTangent("studio", "The studio", "", PostKind.Topic, 1, Audience.Private, Leo), Cancellation));
    }

    /// <summary>A loopback client registers itself, as Koan's dynamic registration lets anyone; its id.</summary>
    private static async Task<string> Register(TangentSpaceFixture tangentSpace, string name, string redirect)
    {
        using var registered = await Json(tangentSpace.Client, "/oauth/register",
            JsonContent("{\"client_name\":\"" + name + "\",\"redirect_uris\":[\"" + redirect + "\"]}"), HttpStatusCode.Created);
        return registered.RootElement.GetProperty("client_id").GetString()!;
    }

    /// <summary>A PKCE verifier and its S256 challenge.</summary>
    private static (string Verifier, string Challenge) Pkce()
    {
        var verifier = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (verifier, Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_'));
    }

    /// <summary>The code flow's authorize request for a loopback client, for this Tangent Space as its resource.</summary>
    private static string Authorize(string clientId, string redirect, string challenge)
        => "/oauth/authorize?response_type=code&client_id=" + clientId + "&redirect_uri=" + Uri.EscapeDataString(redirect)
           + "&code_challenge=" + challenge + "&code_challenge_method=S256&resource=" + Uri.EscapeDataString(BaseUrl) + "&state=s1";

    private sealed record DeviceRequest(string DeviceCode, string Code, string VerificationUri, string Complete);

    /// <summary>The client <c>delegate</c> asks for a code, for this Tangent Space as its resource.</summary>
    private static async Task<DeviceRequest> Device(TangentSpaceFixture tangentSpace)
    {
        using var json = await Json(tangentSpace.Client, "/oauth/device", Form(("client_id", DelegateClient.Id), ("resource", BaseUrl)));
        var root = json.RootElement;
        return new DeviceRequest(root.GetProperty("device_code").GetString()!, root.GetProperty("user_code").GetString()!,
            root.GetProperty("verification_uri").GetString()!, root.GetProperty("verification_uri_complete").GetString()!);
    }

    /// <summary>The client's token request for a device code.</summary>
    private static Task<HttpResponseMessage> Poll(TangentSpaceFixture tangentSpace, DeviceRequest device)
        => tangentSpace.Client.PostAsync("/oauth/token",
            Form(("grant_type", DeviceGrant), ("device_code", device.DeviceCode), ("client_id", DelegateClient.Id)), Cancellation);

    /// <summary>The person opens the request under the device's code and posts approve or deny with the page's token.</summary>
    private static async Task<HttpResponseMessage> Decide(Browser browser, DeviceRequest device, string decision)
    {
        var token = await browser.Token(AtCode(device.Code), Cancellation);
        return await browser.Client.PostAsync("/oauth/request/" + device.Code.Replace("-", "") + "/" + decision, new FormUrlEncodedContent(token), Cancellation);
    }

    /// <summary>The whole Device flow for the signed-in person in <paramref name="browser"/>: the delegate's access
    /// token.</summary>
    private static async Task<string> Delegate(TangentSpaceFixture tangentSpace, Browser browser)
    {
        var device = await Device(tangentSpace);
        using (var approved = await Decide(browser, device, "approve"))
            Assert.Equal(HttpStatusCode.Redirect, approved.StatusCode);
        using var granted = await Poll(tangentSpace, device);
        using var body = JsonDocument.Parse(await granted.Content.ReadAsStringAsync(Cancellation));
        return body.RootElement.GetProperty("access_token").GetString()!;
    }

    /// <summary>The key ids the server publishes.</summary>
    private static async Task<List<string>> Kids(TangentSpaceFixture tangentSpace)
    {
        using var jwks = await Json(tangentSpace.Client, "/.well-known/jwks.json");
        return jwks.RootElement.GetProperty("keys").EnumerateArray().Select(key => key.GetProperty("kid").GetString()!).Order().ToList();
    }

    /// <summary>The OAuth error a token request answered, which carries no token.</summary>
    private static async Task<string> Error(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));
        Assert.False(body.RootElement.TryGetProperty("access_token", out _));
        return body.RootElement.GetProperty("error").GetString()!;
    }

    /// <summary>The consent page with a device's code, as its <c>verification_uri_complete</c> carries it.</summary>
    private static string AtCode(string code) => ConsentController.Path + "?" + ConsentController.CodeField + "=" + code;

    private static FormUrlEncodedContent Form(params (string Key, string Value)[] fields)
        => new(fields.Select(field => new KeyValuePair<string, string>(field.Key, field.Value)));

    private static StringContent JsonContent(string json) => new(json, Encoding.UTF8, "application/json");

    private static async Task<JsonDocument> Json(HttpClient client, string path, HttpContent? content = null, HttpStatusCode expected = HttpStatusCode.OK,
        string? bearer = null)
    {
        using var request = new HttpRequestMessage(content is null ? HttpMethod.Get : HttpMethod.Post, path) { Content = content };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        using var response = await client.SendAsync(request, Cancellation);
        var body = await response.Content.ReadAsStringAsync(Cancellation);
        Assert.True(response.StatusCode == expected, $"{path} answered {(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body);
    }

    private static async Task<string> Html(Browser browser, string path)
    {
        using var response = await browser.Follow(HttpMethod.Get, path, Cancellation);
        var html = await response.Content.ReadAsStringAsync(Cancellation);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path} answered {(int)response.StatusCode}: {html}");
        return html;
    }

    private sealed record Answered(HttpStatusCode Status, string? Title, string? Gate, string Body);

    /// <summary>What a request answers, as JSON or as the page: its status, and the problem's title and gate.</summary>
    private static async Task<Answered> Answer(HttpClient client, string path, string? bearer = null, HttpContent? content = null, bool html = false)
    {
        using var request = new HttpRequestMessage(content is null ? HttpMethod.Get : HttpMethod.Post, path) { Content = content };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(html ? "text/html" : "application/json"));
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        using var response = await client.SendAsync(request, Cancellation);
        var body = await response.Content.ReadAsStringAsync(Cancellation);
        if (html || response.Content.Headers.ContentType?.MediaType is not ("application/problem+json" or "application/json"))
            return new Answered(response.StatusCode, null, null, body);
        using var json = JsonDocument.Parse(body);
        return new Answered(response.StatusCode,
            json.RootElement.TryGetProperty("title", out var title) ? title.GetString() : null,
            json.RootElement.TryGetProperty("gate", out var gate) ? gate.GetString() : null,
            body);
    }

    private static List<string> Strings(JsonElement array) => array.EnumerateArray().Select(item => item.GetString()!).ToList();
}
