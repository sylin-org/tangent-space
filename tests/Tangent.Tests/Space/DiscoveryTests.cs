using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Participants;
using Tangent.Writer;
using Xunit;

namespace Tangent.Space;

/// <summary>How a client that holds only a base URL learns the Tangent Space: the descriptor, <c>llms.txt</c>, and the
/// path install behind the proxy rule first run prints.</summary>
public sealed partial class DiscoveryTests
{
    private const string Leo = "did:plc:leo";
    private const string Ada = "did:plc:ada";
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_public_descriptor_states_the_place_its_people_its_links_and_the_delegated_door()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        await Establish(tangentSpace, "Field notes", operatorDid: Leo, proprietorHandle: "ada.example");

        using var response = await tangentSpace.Client.GetAsync("/tangent.json", Cancellation);
        using var descriptor = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));
        var root = descriptor.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Field notes", root.GetProperty("name").GetString());
        Assert.Equal(TangentSpaceFixture.DefaultBaseUrl, root.GetProperty("address").GetString());
        Assert.Equal(1, root.GetProperty("api").GetInt32());
        Assert.Equal("public", root.GetProperty("audience").GetString());
        Assert.Equal("ada.example", root.GetProperty("proprietor").GetProperty("handle").GetString());
        Assert.Equal(Ada, root.GetProperty("proprietor").GetProperty("did").GetString());
        Assert.Equal("leo.example", root.GetProperty("operator").GetProperty("handle").GetString());
        Assert.Equal(Leo, root.GetProperty("operator").GetProperty("did").GetString());
        Assert.Equal("https://tangent.test/", root.GetProperty("door").GetString());
        Assert.Equal("https://tangent.test/llms.txt", root.GetProperty("llms").GetString());
        Assert.Equal("https://tangent.test/.well-known/oauth-authorization-server", root.GetProperty("delegated").GetProperty("authorizationServer").GetString());
        Assert.Equal("delegate", root.GetProperty("delegated").GetProperty("clientId").GetString());
        Assert.False(root.TryGetProperty("did", out _));
        Assert.False(root.TryGetProperty("publication", out _));
    }

    [Fact]
    public async Task A_private_descriptor_states_only_what_a_visitor_needs_to_sign_in()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        await Establish(tangentSpace, "Off the record", operatorDid: Leo, audience: Audience.Private);

        using var response = await tangentSpace.Client.GetAsync("/tangent.json", Cancellation);
        using var descriptor = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { "name", "address", "audience", "delegated", "api" }.Order(),
            descriptor.RootElement.EnumerateObject().Select(property => property.Name).Order());
        Assert.Equal("private", descriptor.RootElement.GetProperty("audience").GetString());
        Assert.Equal("delegate", descriptor.RootElement.GetProperty("delegated").GetProperty("clientId").GetString());
        Assert.DoesNotContain(Leo, await Body(tangentSpace, "/tangent.json"));
        Assert.DoesNotContain("leo.example", await Body(tangentSpace, "/tangent.json"));
    }

    [Fact]
    public async Task Llms_txt_is_plain_text_with_its_sections_and_every_address_absolute_under_the_base_url()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        await Establish(tangentSpace, "Field notes", operatorDid: Leo);

        using var response = await tangentSpace.Client.GetAsync("/llms.txt", Cancellation);
        var text = await response.Content.ReadAsStringAsync(Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("# Field notes\n", text.ReplaceLineEndings("\n"));
        Assert.Contains("## Reading", text);
        Assert.Contains("## Acting as a person's delegate", text);
        Assert.Contains("`Accept: application/json`", text);
        Assert.Contains("https://tangent.test/ is the door", text);
        Assert.Contains("`gate`", text);
        Assert.Contains("https://tangent.test/.well-known/oauth-authorization-server", text);
        Assert.Contains("Device flow", text);
        Assert.Contains("`delegate`", text);
        Assert.Contains("consent page", text);
        var addresses = Addresses().Matches(text).Select(match => match.Value).ToList();
        Assert.NotEmpty(addresses);
        Assert.All(addresses, address => Assert.StartsWith("https://tangent.test/", address));
        Assert.DoesNotContain(Leo, text);
    }

    [Fact]
    public async Task The_descriptor_etag_holds_while_the_row_does_answers_304_and_changes_with_a_rename()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var space = await Establish(tangentSpace, "Field notes", operatorDid: Leo);

        using var first = await tangentSpace.Client.GetAsync("/tangent.json", Cancellation);
        using var second = await tangentSpace.Client.GetAsync("/tangent.json", Cancellation);
        var tag = first.Headers.ETag;
        Assert.NotNull(tag);
        Assert.Equal(tag, second.Headers.ETag);
        Assert.True(first.Headers.CacheControl?.Public);
        Assert.Equal(TimeSpan.FromMinutes(5), first.Headers.CacheControl?.MaxAge);

        foreach (var path in new[] { "/tangent.json", "/llms.txt" })
        {
            using var held = await tangentSpace.Client.GetAsync(path, Cancellation);
            using var conditional = new HttpRequestMessage(HttpMethod.Get, path);
            conditional.Headers.IfNoneMatch.Add(held.Headers.ETag!);
            using var unchanged = await tangentSpace.Client.SendAsync(conditional, Cancellation);
            Assert.Equal(HttpStatusCode.NotModified, unchanged.StatusCode);
        }

        space.Name = "Field notes, renamed";
        Assert.IsType<Outcome<TangentSpace>.Done>(await tangentSpace.Services.GetRequiredService<WriteLoop>().Send(new Store<TangentSpace>(space), Cancellation));

        using var stale = new HttpRequestMessage(HttpMethod.Get, "/tangent.json");
        stale.Headers.IfNoneMatch.Add(tag);
        using var renamed = await tangentSpace.Client.SendAsync(stale, Cancellation);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.NotEqual(tag, renamed.Headers.ETag);
        Assert.Contains("Field notes, renamed", await renamed.Content.ReadAsStringAsync(Cancellation));
    }

    [Fact]
    public async Task The_pointers_name_the_address_the_descriptor_states()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        await Establish(tangentSpace, "Field notes", operatorDid: Leo);
        using var descriptor = JsonDocument.Parse(await Body(tangentSpace, "/tangent.json"));
        var address = descriptor.RootElement.GetProperty("address").GetString()!;

        var pointers = Pointers.For(address);

        Assert.Equal($"<link rel=\"tangent\" href=\"{address}\">", pointers.Single(pointer => pointer.Kind == Pointers.Link).Text);
        Assert.Equal(address, pointers.Single(pointer => pointer.Kind == Pointers.WellKnown).Text);
        Assert.Contains($"IN TXT \"{address}\"", pointers.Single(pointer => pointer.Kind == Pointers.Txt).Text);
    }

    [Fact]
    public async Task Behind_the_printed_proxy_rule_a_path_install_answers_the_door_and_both_files_at_the_path_only()
    {
        const string baseUrl = "https://www.example.com/notes";
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(new Dictionary<string, string?>
        {
            ["Koan:Web:ApplicationUrl"] = baseUrl,
            ["Koan:Web:TrustedProxies:0"] = "127.0.0.1"
        }, ct: Cancellation);
        await Establish(tangentSpace, "Field notes", operatorDid: Leo);

        // The rule as first run prints it for the address the server listens on: the path kept, the upstream named
        // without a path of its own, the scheme and the client's address forwarded.
        var upstream = Pointers.Upstream([tangentSpace.Client.BaseAddress!.ToString()]);
        var rule = Pointers.For(baseUrl, upstream).Single(pointer => pointer.Kind == Pointers.Proxy).Text;
        Assert.Contains("location /notes/ {", rule);
        Assert.Contains($"proxy_pass {upstream};", rule);
        Assert.Contains("proxy_set_header X-Forwarded-Proto $scheme;", rule);
        Assert.Contains("proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;", rule);
        Assert.DoesNotMatch(@"proxy_pass [^;]*/;", rule);

        // A request as that rule sends it: the path as the client wrote it, the scheme and client address forwarded.
        async Task<HttpResponseMessage> ThroughTheProxy(string path, string? accept = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Add("X-Forwarded-Proto", "https");
            request.Headers.Add("X-Forwarded-For", "203.0.113.9");
            if (accept is not null) request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
            return await tangentSpace.Client.SendAsync(request, Cancellation);
        }

        using var door = await ThroughTheProxy("/notes/", "application/json");
        using var doorJson = JsonDocument.Parse(await door.Content.ReadAsStringAsync(Cancellation));
        using var page = await ThroughTheProxy("/notes/", "text/html");
        using var descriptor = await ThroughTheProxy("/notes/tangent.json");
        using var descriptorJson = JsonDocument.Parse(await descriptor.Content.ReadAsStringAsync(Cancellation));
        using var llms = await ThroughTheProxy("/notes/llms.txt");
        var llmsText = await llms.Content.ReadAsStringAsync(Cancellation);

        Assert.Equal(HttpStatusCode.OK, door.StatusCode);
        Assert.Equal(baseUrl, doorJson.RootElement.GetProperty("baseUrl").GetString());
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains($"{baseUrl}/tangent.js", await page.Content.ReadAsStringAsync(Cancellation));
        Assert.Equal(HttpStatusCode.OK, descriptor.StatusCode);
        Assert.Equal(baseUrl, descriptorJson.RootElement.GetProperty("address").GetString());
        Assert.Equal($"{baseUrl}/", descriptorJson.RootElement.GetProperty("door").GetString());
        Assert.Equal($"{baseUrl}/llms.txt", descriptorJson.RootElement.GetProperty("llms").GetString());
        Assert.Equal($"{baseUrl}/.well-known/oauth-authorization-server",
            descriptorJson.RootElement.GetProperty("delegated").GetProperty("authorizationServer").GetString());
        Assert.Equal(HttpStatusCode.OK, llms.StatusCode);
        Assert.All(Addresses().Matches(llmsText).Select(match => match.Value), address => Assert.StartsWith($"{baseUrl}/", address));
        Assert.Contains($"{baseUrl}/tangent.json", llmsText);

        foreach (var outside in new[] { "/", "/tangent.json", "/llms.txt", "/notesx/tangent.json" })
        {
            using var missing = await ThroughTheProxy(outside, "application/json");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
    }

    private static async Task<string> Body(TangentSpaceFixture tangentSpace, string path)
        => await tangentSpace.Client.GetStringAsync(path, Cancellation);

    /// <summary>Establishes the place through the writer: Leo as operator, and as proprietor unless another handle is named.</summary>
    private static async Task<TangentSpace> Establish(TangentSpaceFixture tangentSpace, string name, string operatorDid,
        Audience audience = Audience.Public, string? proprietorHandle = null)
    {
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        Assert.IsType<Outcome<Participant>.Done>(await writer.Send(new Store<Participant>(new Participant { Id = Ada, Handle = "ada.example" }), Cancellation));
        var outcome = await writer.Send(new Establish(operatorDid, "leo.example", name, proprietorHandle), Cancellation);
        var space = Assert.IsType<Outcome<TangentSpace>.Done>(outcome).Result;
        if (audience == Audience.Private)
        {
            space.Audience = audience;
            Assert.IsType<Outcome<TangentSpace>.Done>(await writer.Send(new Store<TangentSpace>(space), Cancellation));
        }

        return space;
    }

    [GeneratedRegex(@"https?://[^\s)`>]+")]
    private static partial Regex Addresses();
}
