using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace Tangent.Participants;

/// <summary>A browser for one Tangent Space: it keeps cookies and follows redirects one at a time, so a test can sign in
/// through Koan's test connector the way a person does and see every answer on the way.</summary>
public sealed partial class Browser : IDisposable
{
    /// <summary>The Koan test connector's persona cookie: the persona's name and its subject, which the connector's
    /// userinfo answers as <c>sub</c>.</summary>
    private const string PersonaCookie = "_tp_user";

    /// <summary>The test connector's authorize step, which takes custom claims as <c>claim.{type}</c>.</summary>
    private const string Authorize = "/.testoauth/authorize";

    private readonly CookieContainer _jar = new();
    private readonly Uri _address;

    public Browser(TangentSpaceFixture tangentSpace)
    {
        _address = tangentSpace.Client.BaseAddress!;
        Client = new HttpClient(new HttpClientHandler { CookieContainer = _jar, AllowAutoRedirect = false }) { BaseAddress = _address };
    }

    public HttpClient Client { get; }

    /// <summary>Signs in through Koan's test connector as a persona whose subject is <paramref name="did"/>, coming back
    /// to the sign-in page, and answers with where the redirects ended. A <paramref name="handle"/> reaches the
    /// sign-in as the verified handle claim the atproto connector carries.</summary>
    public async Task<HttpResponseMessage> SignIn(string name, string did, CancellationToken ct, string? handle = null)
    {
        _jar.Add(_address, new Cookie(PersonaCookie, Uri.EscapeDataString(name + "|" + did)) { Path = "/" });
        return await Follow(HttpMethod.Get, "/auth/test/challenge?return=" + Uri.EscapeDataString("/sign-in?return=/"), ct,
            rewrite: target => handle is not null && target.AbsolutePath == Authorize
                ? new Uri(target + "&claim." + Uri.EscapeDataString("atproto:handle") + "=" + Uri.EscapeDataString(handle))
                : target);
    }

    /// <summary>Declares a kind on the arrival form the sign-in page holds, with the form's token, as the page posts
    /// it.</summary>
    public async Task<HttpResponseMessage> Arrive(string kind, CancellationToken ct)
    {
        using var page = await Follow(HttpMethod.Get, "/sign-in", ct);
        var html = await page.Content.ReadAsStringAsync(ct);
        var token = TokenField().Match(html);
        if (!token.Success) throw new InvalidOperationException("The sign-in page holds no arrival form.");
        return await Client.PostAsync("/sign-in", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            [WebUtility.HtmlDecode(token.Groups["field"].Value)] = WebUtility.HtmlDecode(token.Groups["value"].Value),
            ["kind"] = kind,
            ["return"] = "/"
        }), ct);
    }

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

    [GeneratedRegex("<input type=\"hidden\" name=\"(?<field>__RequestVerificationToken[^\"]*)\" value=\"(?<value>[^\"]+)\"")]
    private static partial Regex TokenField();
}
