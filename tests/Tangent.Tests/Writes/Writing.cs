using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Koan.Data.Core;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Delegates;
using Tangent.Participants;
using Tangent.Space;
using Tangent.Writer;
using Xunit;

namespace Tangent.Posts;

/// <summary>The place every proof of the write verbs starts from, and the requests they send. A public Tangent Space
/// with the workshop, an articles Tangent and the studio, which only administrators read; Ada and Bea are members, Mo a
/// moderator. A request goes as a person's browser sends a form, as JSON, or as a delegate's bearer; each answer is read
/// whole, without following a redirect.</summary>
internal static partial class Writing
{
    public const string BaseUrl = TangentSpaceFixture.DefaultBaseUrl;
    public const string Workshop = "workshop";
    public const string Articles = "articles";
    public const string Studio = "studio";
    public const string Ada = "did:plc:ada4writes";
    public const string Bea = "did:plc:bea4writes";
    public const string Mo = "did:plc:mo4writes";

    /// <summary>The header ASP.NET's antiforgery reads a JSON request's token from.</summary>
    public const string TokenHeader = "RequestVerificationToken";

    private const string DeviceGrant = "urn:ietf:params:oauth:grant-type:device_code";

    public static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>Koan's test connector signs people in, no profile job reaches the network, and a device polls as often as
    /// a proof asks.</summary>
    public static Dictionary<string, string?> Settings() => new()
    {
        ["Koan:Web:Auth:TestProvider:Enabled"] = "true",
        ["Tangent:Profile:BaseAddress"] = "http://127.0.0.1:1/",
        ["Tangent:Profile:Timeout"] = "00:00:01",
        ["Koan:Web:Auth:Server:DevicePollIntervalSeconds"] = "0"
    };

    /// <summary>The place, its Tangents and its people; then the writer.</summary>
    public static async Task<WriteLoop> Place(TangentSpaceFixture tangentSpace)
    {
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        await Given.AnEstablishedTangentSpace(tangentSpace, "Field notes", ct: Cancellation);
        await Given.ATangent(tangentSpace, Workshop, ct: Cancellation);
        await Given.ATangent(tangentSpace, Studio, Audience.Private, ct: Cancellation);
        Assert.IsType<Outcome<Tangents.Tangent>.Done>(await writer.Send(
            new Store<Tangents.Tangent>(Tangents.Tangent.Create(Articles, "Articles", "", PostKind.Article, 2)), Cancellation));
        await Given.AParticipant(tangentSpace, Ada, [Roles.Member], ct: Cancellation);
        await Given.AParticipant(tangentSpace, Bea, [Roles.Member], ct: Cancellation);
        await Given.AParticipant(tangentSpace, Mo, [Roles.Moderator], ct: Cancellation);
        return writer;
    }

    public static async Task<Post> Started(WriteLoop writer, string author, string title, string body, string tangent = Workshop)
        => Assert.IsType<Outcome<Post>.Done>(await writer.Send(new StartTopic(Tids.Mint(), tangent, title, body, author), Cancellation)).Result;

    public static async Task<Post> Replied(WriteLoop writer, Post root, string author, string body)
        => Assert.IsType<Outcome<Post>.Done>(await writer.Send(new Reply(Tids.Mint(), root.Id, body, author), Cancellation)).Result;

    /// <summary>A browser signed in as <paramref name="did"/>.</summary>
    public static async Task<Browser> SignedIn(TangentSpaceFixture tangentSpace, string did)
    {
        var browser = new Browser(tangentSpace);
        (await browser.SignIn(did.Split(':')[^1], did, Cancellation, handle: did.Split(':')[^1] + ".example")).Dispose();
        return browser;
    }

    /// <summary>A client that keeps no cookie and follows no redirect: a visitor, or an agent with a bearer.</summary>
    public static HttpClient Agent(TangentSpaceFixture tangentSpace)
        => new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { BaseAddress = tangentSpace.Client.BaseAddress };

    public static string TopicPath(Post root) => "/t/" + root.TangentId + "/" + root.Slug;

    public static string PostPath(Post root, Post post) => TopicPath(root) + "/" + post.Id;

    public static string AddressOf(Post root, Post post) => BaseUrl + PostPath(root, post);

    /// <summary>One answer, read whole.</summary>
    public sealed record Answered(HttpStatusCode Status, string? Location, string? MediaType, string Body)
    {
        public JsonElement Json => JsonDocument.Parse(Body).RootElement.Clone();

        /// <summary>The problem's gate, for a JSON answer that carries one.</summary>
        public string? Gate => MediaType is "application/problem+json" && Json.TryGetProperty("gate", out var gate) ? gate.GetString() : null;

        public string? Title => MediaType is "application/problem+json" && Json.TryGetProperty("title", out var title) ? title.GetString() : null;
    }

    /// <summary>Sends a request as JSON (<paramref name="json"/>) or as a page asks, with a bearer or an antiforgery
    /// header when given.</summary>
    public static async Task<Answered> Send(HttpClient client, HttpMethod method, string path, HttpContent? content = null, bool json = false,
        string? bearer = null, string? token = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(json ? "application/json" : "text/html"));
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (token is not null) request.Headers.Add(TokenHeader, token);
        using var response = await client.SendAsync(request, Cancellation);
        return new Answered(response.StatusCode, response.Headers.Location?.ToString(), response.Content.Headers.ContentType?.MediaType,
            await response.Content.ReadAsStringAsync(Cancellation));
    }

    public static StringContent JsonBody(object fields) => new(JsonSerializer.Serialize(fields), Encoding.UTF8, "application/json");

    public static FormUrlEncodedContent FormBody(IReadOnlyDictionary<string, string> fields) => new(fields);

    /// <summary>Posts a form as the page at <paramref name="page"/> holds it: its fields with the page's token.</summary>
    public static async Task<Answered> Form(Browser browser, string page, string action, Dictionary<string, string> fields)
    {
        foreach (var (name, value) in await browser.Token(page, Cancellation)) fields[name] = value;
        return await Send(browser.Client, HttpMethod.Post, action, FormBody(fields));
    }

    /// <summary>Posts JSON from a signed-in browser with the antiforgery token the page at <paramref name="page"/> holds,
    /// in the header.</summary>
    public static async Task<Answered> Json(Browser browser, string page, string action, object fields)
        => await Send(browser.Client, HttpMethod.Post, action, JsonBody(fields), json: true, token: (await browser.Token(page, Cancellation)).Values.Single());

    /// <summary>The value a form in a page's JSON gives the field named <paramref name="name"/>; null where it gives none.</summary>
    public static string? Valued(JsonElement form, string name)
    {
        var field = form.GetProperty("fields").EnumerateArray().Single(field => field.GetProperty("name").GetString() == name);
        return field.TryGetProperty("value", out var value) ? value.GetString() : null;
    }

    /// <summary>The names of a form's fields, in order.</summary>
    public static List<string> FieldNames(JsonElement form)
        => form.GetProperty("fields").EnumerateArray().Select(field => field.GetProperty("name").GetString()!).ToList();

    /// <summary>Makes the write a form in a page's JSON says how to make, as an agent with a bearer does, knowing nothing
    /// else: to the form's action, under the base URL the fixture's client answers for, with its method, as one JSON
    /// object of every field the page gives a value, as it gives it, and the others from <paramref name="filled"/>, which
    /// names only fields the form has.</summary>
    public static async Task<Answered> Follow(HttpClient client, JsonElement form, IReadOnlyDictionary<string, string> filled, string bearer)
    {
        var names = FieldNames(form);
        Assert.All(filled.Keys, name => Assert.Contains(name, names));
        var fields = new Dictionary<string, string>();
        foreach (var name in names)
        {
            if (filled.TryGetValue(name, out var mine)) fields[name] = mine;
            else if (Valued(form, name) is { } given) fields[name] = given;
        }

        var action = form.GetProperty("action").GetString()!;
        Assert.StartsWith(BaseUrl + "/", action);
        return await Send(client, new HttpMethod(form.GetProperty("method").GetString()!), action[BaseUrl.Length..], JsonBody(fields),
            json: true, bearer: bearer);
    }

    /// <summary>Makes the write a form in a page's JSON says how to make, as an agent signed in as itself does: with its
    /// session and the form's token, as one JSON object with the token in the header, or as a form with it in the field
    /// the token names. <paramref name="filled"/> names only fields the form has; every other field the page gives a value
    /// goes as it gives it.</summary>
    public static async Task<Answered> Follow(Browser session, JsonElement form, IReadOnlyDictionary<string, string> filled, bool json = true)
    {
        var names = FieldNames(form);
        Assert.All(filled.Keys, name => Assert.Contains(name, names));
        var fields = new Dictionary<string, string>();
        foreach (var name in names)
        {
            if (filled.TryGetValue(name, out var mine)) fields[name] = mine;
            else if (Valued(form, name) is { } given) fields[name] = given;
        }

        var action = form.GetProperty("action").GetString()!;
        Assert.StartsWith(BaseUrl + "/", action);
        var token = form.GetProperty("token");
        var method = new HttpMethod(form.GetProperty("method").GetString()!);
        if (json) return await Send(session.Client, method, action[BaseUrl.Length..], JsonBody(fields), json: true, token: token.GetProperty("value").GetString());
        fields[token.GetProperty("field").GetString()!] = token.GetProperty("value").GetString()!;
        return await Send(session.Client, method, action[BaseUrl.Length..], FormBody(fields));
    }

    /// <summary>The TID the new-topic form at <paramref name="html"/> carries.</summary>
    public static string MintedId(string html) => MintedField().Match(html) is { Success: true } match
        ? match.Groups["id"].Value
        : throw new InvalidOperationException("The page holds no minted id.");

    /// <summary>How many Posts and Events the place holds.</summary>
    public static async Task<(long Posts, long Events)> Counted()
        => (await Post.Count.Exact(Cancellation), await Events.Event.Count.Exact(Cancellation));

    /// <summary>How many snapshots of Posts the audit partition holds.</summary>
    public static async Task<int> Snapshots() => (await Data<Post, string>.All(AuditPartition.Name, Cancellation)).Count;

    /// <summary>The whole Device flow for the person signed in in <paramref name="browser"/>: their delegate's access
    /// token.</summary>
    public static async Task<string> Delegate(TangentSpaceFixture tangentSpace, Browser browser)
    {
        using var device = await JsonOf(tangentSpace.Client, "/oauth/device", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = DelegateClient.Id,
            ["resource"] = BaseUrl
        }));
        var deviceCode = device.RootElement.GetProperty("device_code").GetString()!;
        var code = device.RootElement.GetProperty("user_code").GetString()!;
        var token = await browser.Token(ConsentController.Path + "?" + ConsentController.CodeField + "=" + code, Cancellation);
        using (var approved = await browser.Client.PostAsync("/oauth/request/" + code.Replace("-", "") + "/approve",
                   new FormUrlEncodedContent(token), Cancellation))
            Assert.Equal(HttpStatusCode.Redirect, approved.StatusCode);
        using var granted = await JsonOf(tangentSpace.Client, "/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = DeviceGrant,
            ["device_code"] = deviceCode,
            ["client_id"] = DelegateClient.Id
        }));
        return granted.RootElement.GetProperty("access_token").GetString()!;
    }

    private static async Task<JsonDocument> JsonOf(HttpClient client, string path, HttpContent content)
    {
        using var response = await client.PostAsync(path, content, Cancellation);
        var body = await response.Content.ReadAsStringAsync(Cancellation);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path} answered {(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body);
    }

    [GeneratedRegex("<input type=\"hidden\" name=\"id\" value=\"(?<id>[^\"]+)\"")]
    private static partial Regex MintedField();
}
