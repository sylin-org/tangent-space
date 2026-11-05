using System.Net;
using System.Net.Http.Headers;

namespace Tangent.Participants;

/// <summary>A browser for one Tangent Space: it keeps cookies and follows redirects one at a time, so a test can sign in
/// through Koan's test connector the way a person does and see every answer on the way.</summary>
public sealed class Browser : IDisposable
{
    /// <summary>The Koan test connector's persona cookie: the persona's name and its subject, which the connector's
    /// userinfo answers as <c>sub</c>.</summary>
    private const string PersonaCookie = "_tp_user";

    /// <summary>The test connector's authorize step, which takes custom claims as <c>claim.{type}</c>.</summary>
    private const string Authorize = "/.testoauth/authorize";

    private readonly CookieContainer _jar = new();
    private readonly Uri _address;

    private readonly TangentSpaceFixture _tangentSpace;

    public Browser(TangentSpaceFixture tangentSpace)
    {
        _tangentSpace = tangentSpace;
        _address = tangentSpace.Client.BaseAddress!;
        Client = new HttpClient(new HttpClientHandler { CookieContainer = _jar, AllowAutoRedirect = false }) { BaseAddress = _address };
    }

    public HttpClient Client { get; }

    /// <summary>Signs in through Koan's test connector as <paramref name="who"/>, coming back to the sign-in page, and
    /// answers with where the redirects ended: a persona's id signs in as the persona, their logins bound to them first
    /// (<see cref="Persona.Bind"/>); a DID signs in as whoever holds that account here, a newcomer the first time. The
    /// account's DID reaches the sign-in as the claim the atproto connector carries, and so does a
    /// <paramref name="handle"/>, as its verified handle; <paramref name="under"/> is the base URL's path, when it has one;
    /// <paramref name="comeBack"/> is where the challenge returns, the sign-in page sending the person on to the door unless
    /// it is given.</summary>
    public async Task<HttpResponseMessage> SignIn(string name, string who, CancellationToken ct, string? handle = null, string under = "",
        string? comeBack = null)
    {
        if (Persona.Is(who)) await Persona.Bind(_tangentSpace, who, ct);
        var did = Persona.Did(who);
        _jar.Add(_address, new Cookie(PersonaCookie, Uri.EscapeDataString(name + "|" + did)) { Path = "/" });
        return await Follow(HttpMethod.Get, under + "/auth/test/challenge?return=" + Uri.EscapeDataString(comeBack ?? under + "/sign-in?return=" + under + "/"), ct,
            rewrite: target => target.AbsolutePath.EndsWith(Authorize, StringComparison.Ordinal) && !target.Query.Contains("claim.", StringComparison.Ordinal)
                ? new Uri(target + Claim("atproto:did", did) + (handle is null ? "" : Claim("atproto:handle", handle)))
                : target);
    }

    /// <summary>Uses another account, as the account menu's form does: a same-site POST to Koan's binding of a login of
    /// <paramref name="provider"/>, whose challenge the test connector answers as the account <paramref name="did"/> with
    /// <paramref name="handle"/>, coming back to <paramref name="comeBack"/>, or to <paramref name="failure"/> refused;
    /// answers with where the redirects ended.</summary>
    public async Task<HttpResponseMessage> UseAnotherAccount(string did, string handle, CancellationToken ct, string comeBack = "/",
        string failure = "/settings/connections?another=1", string provider = "test")
    {
        _jar.Add(_address, new Cookie(PersonaCookie, Uri.EscapeDataString(handle.Split('.')[0] + "|" + did)) { Path = "/" });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/me/connections/" + provider + "/link")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["identifier"] = handle, ["return"] = comeBack, ["failure"] = failure })
        };
        request.Headers.Add("Sec-Fetch-Site", "same-origin");
        request.Headers.Add("Origin", _address.GetLeftPart(UriPartial.Authority));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        var started = await Client.SendAsync(request, ct);
        if ((int)started.StatusCode is < 300 or >= 400 || started.Headers.Location is null) return started;
        var next = new Uri(new Uri(_address, "/me/connections/" + provider + "/link"), started.Headers.Location);
        started.Dispose();
        return await Follow(HttpMethod.Get, next.PathAndQuery, ct,
            rewrite: target => target.AbsolutePath.EndsWith(Authorize, StringComparison.Ordinal) && !target.Query.Contains("claim.", StringComparison.Ordinal)
                ? new Uri(target + Claim("atproto:did", did) + Claim("atproto:handle", handle))
                : target);
    }

    private static string Claim(string type, string value) => "&claim." + Uri.EscapeDataString(type) + "=" + Uri.EscapeDataString(value);

    /// <summary>Sends a request and follows its redirects as a browser does, answering with the last response.</summary>
    public async Task<HttpResponseMessage> Follow(HttpMethod method, string path, CancellationToken ct, HttpContent? content = null,
        Func<Uri, Uri>? rewrite = null)
    {
        var target = new Uri(_address, path);
        for (var hop = 0; hop < 10; hop++)
        {
            target = rewrite?.Invoke(target) ?? target;
            using var request = new HttpRequestMessage(method, target) { Content = content };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
            var response = await Client.SendAsync(request, ct);
            if ((int)response.StatusCode is < 300 or >= 400 || response.Headers.Location is null) return response;
            target = new Uri(target, response.Headers.Location);
            method = HttpMethod.Get;
            content = null;
            response.Dispose();
        }
        throw new InvalidOperationException($"More than ten redirects from {path}.");
    }

    public void Dispose() => Client.Dispose();

}
