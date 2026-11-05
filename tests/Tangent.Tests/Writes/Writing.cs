using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Space;
using Xunit;

namespace Tangent.Posts;

/// <summary>The place every proof of the write verbs starts from, and the requests they send. A public Tangent Space
/// with the workshop, an articles Tangent and the studio, which only administrators read; Ada and Bea are members, Mo a
/// moderator. A request goes as a person's browser sends a form from a page of the place, or as JSON; each answer is read
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

    public static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>Koan's test connector signs people in, and no profile job reaches the network.</summary>
    public static Dictionary<string, string?> Settings() => new()
    {
        ["Koan:Web:Auth:TestProvider:Enabled"] = "true",
        ["Tangent:Profile:BaseAddress"] = "http://127.0.0.1:1/",
        ["Tangent:Profile:Timeout"] = "00:00:01"
    };

    /// <summary>The place, its Tangents and its people.</summary>
    public static async Task Place(TangentSpaceFixture tangentSpace)
    {
        await Given.AnEstablishedTangentSpace(tangentSpace, "Field notes", ct: Cancellation);
        await Given.ATangent(tangentSpace, Workshop, ct: Cancellation);
        await Given.ATangent(tangentSpace, Studio, Audience.Private, ct: Cancellation);
        Assert.IsType<Outcome<Tangents.Tangent>.Done>(await new Store<Tangents.Tangent>(Tangents.Tangent.Create(Articles, "Articles", "", PostKind.Article, 2)).Send(Cancellation));
        await Given.AParticipant(tangentSpace, Ada, [Roles.Member], ct: Cancellation);
        await Given.AParticipant(tangentSpace, Bea, [Roles.Member], ct: Cancellation);
        await Given.AParticipant(tangentSpace, Mo, [Roles.Moderator], ct: Cancellation);
    }

    public static async Task<Post> Started(string author, string title, string body, string tangent = Workshop)
        => Assert.IsType<Outcome<Post>.Done>(await new StartTopic(Tids.Mint(), tangent, title, body, author).Send(Cancellation)).Result;

    public static async Task<Post> Replied(Post root, string author, string body)
        => Assert.IsType<Outcome<Post>.Done>(await new Reply(Tids.Mint(), root.Id, body, author).Send(Cancellation)).Result;

    /// <summary>A browser signed in as <paramref name="did"/>.</summary>
    public static async Task<Browser> SignedIn(TangentSpaceFixture tangentSpace, string did)
    {
        var browser = new Browser(tangentSpace);
        (await browser.SignIn(did.Split(':')[^1], did, Cancellation, handle: did.Split(':')[^1] + ".example")).Dispose();
        return browser;
    }

    /// <summary>A client that keeps no cookie and follows no redirect: a visitor.</summary>
    public static HttpClient Agent(TangentSpaceFixture tangentSpace)
        => new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { BaseAddress = tangentSpace.Client.BaseAddress };

    public static string TopicPath(Post root) => "/t/" + root.TangentId + "/" + root.Slug;

    public static string PostPath(Post root, Post post) => TopicPath(root) + "/" + post.Id;

    public static string AddressOf(Post root, Post post) => BaseUrl + PostPath(root, post);

    /// <summary>One answer, read whole.</summary>
    public sealed record Answered(HttpStatusCode Status, string? Location, string? MediaType, string Body)
    {
        public JsonElement Json => JsonDocument.Parse(Body).RootElement.Clone();

        /// <summary>The problem's word, which every problem carries.</summary>
        public string? Word => MediaType is "application/problem+json" && Json.TryGetProperty("word", out var word) ? word.GetString() : null;

        public string? Title => MediaType is "application/problem+json" && Json.TryGetProperty("title", out var title) ? title.GetString() : null;

        /// <summary>The problem's kind, which every problem carries: invalid, absent, gated, conflict or failed.</summary>
        public string? Kind => MediaType is "application/problem+json" && Json.TryGetProperty("kind", out var kind) ? kind.GetString() : null;
    }

    /// <summary>Sends a request as JSON (<paramref name="json"/>) or as a page asks, with a bearer when given, and, from
    /// <paramref name="page"/>, as a browser sends it from that page of the place: same-origin, with its origin and the
    /// page as its referrer.</summary>
    public static async Task<Answered> Send(HttpClient client, HttpMethod method, string path, HttpContent? content = null, bool json = false,
        string? bearer = null, string? page = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(json ? "application/json" : "text/html"));
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (page is not null)
        {
            request.Headers.Add("Sec-Fetch-Site", "same-origin");
            request.Headers.Add("Origin", BaseUrl);
            request.Headers.Referrer = new Uri(BaseUrl + page);
        }
        using var response = await client.SendAsync(request, Cancellation);
        return new Answered(response.StatusCode, response.Headers.Location?.ToString(), response.Content.Headers.ContentType?.MediaType,
            await response.Content.ReadAsStringAsync(Cancellation));
    }

    public static StringContent JsonBody(object fields) => new(JsonSerializer.Serialize(fields), Encoding.UTF8, "application/json");

    public static FormUrlEncodedContent FormBody(IReadOnlyDictionary<string, string> fields) => new(fields);

    /// <summary>Posts a form from the page at <paramref name="page"/>, as its browser does.</summary>
    public static Task<Answered> Form(Browser browser, string page, string action, Dictionary<string, string> fields)
        => Send(browser.Client, HttpMethod.Post, action, FormBody(fields), page: page);

    /// <summary>Posts JSON from the page at <paramref name="page"/>, as a signed-in browser's script does.</summary>
    public static Task<Answered> Json(Browser browser, string page, string action, object fields)
        => Send(browser.Client, HttpMethod.Post, action, JsonBody(fields), json: true, page: page);

    /// <summary>The value a form in a page's JSON gives the field named <paramref name="name"/>; null where it gives none.</summary>
    public static string? Valued(JsonElement form, string name)
    {
        var field = form.GetProperty("fields").EnumerateArray().Single(field => field.GetProperty("name").GetString() == name);
        return field.TryGetProperty("value", out var value) ? value.GetString() : null;
    }

    /// <summary>The names of a form's fields, in order.</summary>
    public static List<string> FieldNames(JsonElement form)
        => form.GetProperty("fields").EnumerateArray().Select(field => field.GetProperty("name").GetString()!).ToList();

    /// <summary>Makes the write a form in a page's JSON says how to make, as an agent signed in as itself does, knowing
    /// nothing else: to the form's action, under the base URL the fixture's client answers for, with its method and its
    /// session, as one JSON object or as a form. <paramref name="filled"/> names only fields the form has; every other
    /// field the page gives a value goes as it gives it.</summary>
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
        var method = new HttpMethod(form.GetProperty("method").GetString()!);
        return json
            ? await Send(session.Client, method, action[BaseUrl.Length..], JsonBody(fields), json: true)
            : await Send(session.Client, method, action[BaseUrl.Length..], FormBody(fields));
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

    [GeneratedRegex("<input type=\"hidden\" name=\"id\" value=\"(?<id>[^\"]+)\"")]
    private static partial Regex MintedField();
}
