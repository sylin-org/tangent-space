using System.Net;
using System.Text.Json;
using Koan.Mcp.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Space;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Tools;

/// <summary>An agent at <c>/mcp</c> reads the place the way a person follows links: <c>read</c> takes any address of the
/// place and answers what it answers as JSON, as the caller, through the same pipeline a page's request takes; every failure
/// there is one Problem; and what the place says of itself is one text.</summary>
public sealed class ReadTests
{
    private static JsonElement Meta(Called called) => called.Meta;

    [Fact]
    public async Task With_no_address_read_answers_the_door_as_whoever_calls()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);
        using var tools = await ToolClient.Connect(tangentSpace, ada);

        var door = await tools.Call("read");

        Assert.False(door.Failed, door.Text);
        Assert.Equal(Ada, door.Structured!.Value.GetProperty("who").GetProperty("id").GetString());
        Assert.Equal(BaseUrl + "/", Meta(door).GetProperty("address").GetString());
    }

    [Fact]
    public async Task A_topics_address_answers_the_topic_pages_json_and_a_roots_tid_follows_the_redirect_to_its_slug()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var root = await Started(Bea, "Lanterns at dusk", "Who lights theirs first?");
        using var ada = await SignedIn(tangentSpace, Ada);
        using var tools = await ToolClient.Connect(tangentSpace, ada);

        var topic = await tools.Call("read", new { address = BaseUrl + TopicPath(root) });
        var page = await Send(ada.Client, HttpMethod.Get, TopicPath(root), json: true);
        Assert.Equal((200, HttpStatusCode.OK), (topic.Status, page.Status));
        Assert.Equal(page.Json.GetProperty("title").GetString(), topic.Structured!.Value.GetProperty("title").GetString());
        Assert.Equal(root.Id, topic.Structured!.Value.GetProperty("root").GetProperty("id").GetString());

        // A root's TID is answered with a redirect to its slug's address, which read follows once.
        var byId = await tools.Call("read", new { address = "/t/" + root.TangentId + "/" + root.Id });
        Assert.False(byId.Failed, byId.Text);
        Assert.Equal(root.Id, byId.Structured!.Value.GetProperty("root").GetProperty("id").GetString());
        Assert.Equal(BaseUrl + TopicPath(root), Meta(byId).GetProperty("address").GetString());
    }

    [Fact]
    public async Task Another_hosts_address_is_refused_with_a_problem_naming_the_address()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);
        using var tools = await ToolClient.Connect(tangentSpace, ada);

        var elsewhere = await tools.Call("read", new { address = "https://elsewhere.example/t/workshop" });

        Assert.True(elsewhere.Failed);
        Assert.Equal((400, "invalid"), (elsewhere.Status, elsewhere.ErrorCode));
        Assert.Contains("elsewhere.example", Meta(elsewhere).GetProperty("problem").GetProperty("errors").GetProperty("address")[0].GetString());
    }

    [Fact]
    public async Task A_visitor_on_a_private_place_reads_401_with_the_problem_and_the_challenge_the_page_sent()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Given.AnEstablishedTangentSpace(tangentSpace, "Field notes", Audience.Private, ct: Cancellation);
        using var visitor = await ToolClient.Connect(tangentSpace);
        using var http = Agent(tangentSpace);
        using var asked = new HttpRequestMessage(HttpMethod.Get, "/");
        asked.Headers.Accept.ParseAdd("application/json");
        using var page = await http.SendAsync(asked, Cancellation);

        var door = await visitor.Call("read");

        Assert.Equal((HttpStatusCode.Unauthorized, 401, "gated"), (page.StatusCode, door.Status, door.ErrorCode));
        Assert.Equal("gated", Meta(door).GetProperty("problem").GetProperty("kind").GetString());
        // The challenge is what the page sent, carried as it was.
        var sent = page.Headers.WwwAuthenticate.ToString();
        Assert.Equal(sent.Length > 0 ? sent : null, Meta(door).TryGetProperty("challenge", out var challenge) ? challenge.GetString() : null);
    }

    [Fact]
    public async Task A_tool_called_with_a_value_that_cannot_bind_answers_the_problem_naming_the_field()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var root = await Started(Bea, "Lanterns", "How do you hang yours?");
        using var ada = await SignedIn(tangentSpace, Ada);
        using var tools = await ToolClient.Connect(tangentSpace, ada);

        var body = await tools.Call("seen", new { through = "soon" });
        var route = await tools.Call("reply", new { tangent = Workshop, topic = 5, body = "Typed." });

        foreach (var (called, field) in new[] { (body, "through"), (route, "topic") })
        {
            Assert.Equal((400, "invalid"), (called.Status, called.ErrorCode));
            var errors = Meta(called).GetProperty("problem").GetProperty("errors");
            Assert.Contains(errors.EnumerateObject(), named => string.Equals(named.Name, field, StringComparison.OrdinalIgnoreCase));
            Assert.False(Meta(called).GetProperty("landed").GetBoolean());
        }
        Assert.Empty((await Post.Query(post => post.TopicId == root.Id && post.ParentId != null, Cancellation)));
    }

    [Fact]
    public async Task A_write_takes_the_address_a_reading_returned_in_place_of_its_values()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var root = await Started(Bea, "Lanterns", "How do you hang yours?");
        using var ada = await SignedIn(tangentSpace, Ada);
        using var tools = await ToolClient.Connect(tangentSpace, ada);

        var reply = await tools.Call("reply", new { address = BaseUrl + TopicPath(root), body = "From the eaves." });

        Assert.False(reply.Failed, reply.Text);
        Assert.True(Meta(reply).GetProperty("landed").GetBoolean());
        Assert.Equal(root.Id, (await Post.Get(reply.Structured!.Value.GetProperty("id").GetString()!, Cancellation))!.TopicId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Llms_txt_tangent_json_and_the_instructions_each_hold_the_one_texts_sentences_and_no_others(bool reads)
    {
        var settings = Settings();
        settings["Koan:Mcp:ReadByAddress"] = reads ? "true" : "false";
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(settings, ct: Cancellation);
        await Place(tangentSpace);
        var space = (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!;
        var text = LlmsText.For(space, BaseUrl, addressed: true, reads);
        using var visitor = await ToolClient.Connect(tangentSpace);
        using var http = Agent(tangentSpace);

        Assert.Equal(text.Llms, await http.GetStringAsync("/llms.txt", Cancellation));
        using var descriptor = JsonDocument.Parse(await http.GetStringAsync("/tangent.json", Cancellation));
        Assert.Equal(text.Summary, descriptor.RootElement.GetProperty("summary").GetString());
        Assert.Equal(text.Instructions, visitor.Initialized.GetProperty("instructions").GetString());

        // The instructions are lines of llms.txt, and say read only where the edge offers it.
        var lines = text.Llms.Split('\n').ToHashSet();
        Assert.All(text.Instructions.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1), line => Assert.Contains(line, lines));
        Assert.Equal(reads, text.Llms.Contains("`read`", StringComparison.Ordinal));
        Assert.Equal(reads, tangentSpace.Services.GetRequiredService<IOptions<McpServerOptions>>().Value.ReadByAddress);
        Assert.Equal(reads, (await visitor.Tools()).ContainsKey("read"));
    }
}
