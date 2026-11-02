using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Tangent.Access;
using Tangent.Posts;
using Tangent.Space;
using Tangent.Tools;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Render;

/// <summary>What a person reads where a request goes wrong: a refusal in the place's own frame, whoever answered it; the first
/// page for a page number that is none; and, for a session cookie that no longer holds or a JSON type with no body, the page they asked for.</summary>
public sealed class RefusalPageTests
{
    private const string Leo = "did:plc:leo4refusals";

    private const string Masthead = "class=\"masthead\"";

    [Fact]
    public async Task A_value_that_cannot_bind_reads_in_the_places_frame_with_its_field_and_json_reads_a_problem()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var root = await Started(Ada, "Lanterns", "How do you hang yours?");
        using var ada = await SignedIn(tangentSpace, Ada);
        var edit = PostPath(root, root) + "/edit";

        var page = await Form(ada, edit, edit, new Dictionary<string, string> { ["body"] = "New words.", ["version"] = "not-a-number" });

        Assert.Equal((HttpStatusCode.BadRequest, "text/html"), (page.Status, page.MediaType));
        Assert.Contains(Masthead, page.Body);
        Assert.Contains("href=\"" + BaseUrl + "/\"", page.Body);
        Assert.Contains("Field notes", page.Body);
        Assert.Contains("Not accepted", page.Body);
        Assert.Contains("Version: ", page.Body);
        Assert.Contains("<footer class=\"footer\"", page.Body);

        var json = await Json(ada, edit, edit, new { body = "New words.", version = "not-a-number" });
        Assert.Equal((HttpStatusCode.BadRequest, "application/problem+json"), (json.Status, json.MediaType));
    }

    [Fact]
    public async Task A_form_past_its_limit_and_the_places_own_refusals_read_in_the_same_frame()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var root = await Started(Ada, "Lanterns", "How do you hang yours?");
        using var ada = await SignedIn(tangentSpace, Ada);
        using var visitor = Agent(tangentSpace);

        var fields = await ada.Token(TopicPath(root), Cancellation);
        fields["body"] = new string('x', (int)PostWrites.RequestLimit * 2);
        var large = await Send(ada.Client, HttpMethod.Post, TopicPath(root), FormBody(fields));
        Assert.Equal((HttpStatusCode.RequestEntityTooLarge, "text/html"), (large.Status, large.MediaType));
        Assert.Contains(Masthead, large.Body);
        Assert.Contains("Too large", large.Body);

        // The place's own: nothing at an address, and a gate, each with the way to sign in for a visitor.
        var nothing = await Send(visitor, HttpMethod.Get, "/t/" + Workshop + "/nothing-here");
        Assert.Equal(HttpStatusCode.NotFound, nothing.Status);
        Assert.Contains(Masthead, nothing.Body);
        Assert.Contains("class=\"masthead-sign-in\"", nothing.Body);
        var reply = await Send(visitor, HttpMethod.Post, TopicPath(root), FormBody(new Dictionary<string, string> { ["body"] = "Anyone?" }));
        Assert.Equal(HttpStatusCode.Unauthorized, reply.Status);
        Assert.Contains(Masthead, reply.Body);
        Assert.Contains("data-gate=\"sign-in\"", reply.Body);
        Assert.Contains("href=\"" + BaseUrl + "/sign-in\"", reply.Body);

        // A fragment is the component alone.
        using var fragment = new HttpRequestMessage(HttpMethod.Get, "/t/" + Workshop + "/nothing-here");
        fragment.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        fragment.Headers.Add(HtmlFormatter.FragmentHeader, "1");
        using var alone = await visitor.SendAsync(fragment, Cancellation);
        Assert.DoesNotContain(Masthead, await alone.Content.ReadAsStringAsync(Cancellation));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("99999999999")]
    public async Task A_page_that_is_no_number_reads_the_first_page_of_the_roles_and_a_tangents_access_on_both_doors(string page)
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        using var leo = await SignedIn(tangentSpace, Leo);
        var bearer = await Delegate(tangentSpace, leo);
        using var http = Agent(tangentSpace);
        using var tools = await ToolClient.Connect(tangentSpace, bearer);

        foreach (var (path, tool, arguments) in new (string, string, object)[]
                 {
                     (RolesController.Path, "read_roles", new { page }),
                     ("/t/" + Workshop + "/settings/access", "read_tangent_access", new { tangent = Workshop, page })
                 })
        {
            var first = await Send(http, HttpMethod.Get, path, json: true, bearer: bearer);
            var bad = await Send(http, HttpMethod.Get, path + "?page=" + page, json: true, bearer: bearer);
            Assert.Equal(HttpStatusCode.OK, bad.Status);
            Assert.Equal(first.Json.GetProperty("roles").GetArrayLength(), bad.Json.GetProperty("roles").GetArrayLength());
            Assert.Equal(JsonValueKindOfPrevious(first), JsonValueKindOfPrevious(bad));
            Assert.Equal(200, (await tools.Call(tool, arguments)).Status);

            // A page that is a number is still that page.
            var second = await Send(http, HttpMethod.Get, path + "?page=2", json: true, bearer: bearer);
            Assert.Equal(System.Text.Json.JsonValueKind.String, JsonValueKindOfPrevious(second));
        }
    }

    private static System.Text.Json.JsonValueKind JsonValueKindOfPrevious(Answered answered)
        => answered.Json.TryGetProperty("previous", out var previous) ? previous.ValueKind : System.Text.Json.JsonValueKind.Null;

    /// <summary>A request that sends a session cookie no longer good, as a browser sends it whoever asks.</summary>
    private static async Task<Answered> WithStaleCookie(HttpClient client, string path, bool json)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(json ? "application/json" : "text/html"));
        request.Headers.Add("Cookie", StaleCookie);
        using var response = await client.SendAsync(request, Cancellation);
        return new Answered(response.StatusCode, response.Headers.Location?.ToString(), response.Content.Headers.ContentType?.MediaType,
            await response.Content.ReadAsStringAsync(Cancellation));
    }

    private const string StaleCookie = ".AspNetCore.Koan.cookie=CfDJ8tampered-this-never-was-a-ticket";

    [Fact]
    public async Task A_session_cookie_that_no_longer_holds_reads_as_a_visitor_on_every_page_of_a_public_place()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var root = await Started(Ada, "Lanterns", "How do you hang yours?");
        using var client = Agent(tangentSpace);

        foreach (var path in new[] { "/", "/t/" + Workshop, TopicPath(root) })
        {
            foreach (var json in new[] { true, false })
            {
                var visitor = await Send(client, HttpMethod.Get, path, json: json);
                var stale = await WithStaleCookie(client, path, json);
                Assert.Equal(HttpStatusCode.OK, visitor.Status);
                Assert.Equal((visitor.Status, visitor.MediaType), (stale.Status, stale.MediaType));
                if (json) Assert.False(stale.Json.TryGetProperty("who", out var who) && who.ValueKind == System.Text.Json.JsonValueKind.Object);
                else Assert.Contains("class=\"masthead-sign-in\"", stale.Body);
            }
        }
    }

    [Fact]
    public async Task On_a_place_nobody_has_set_up_a_session_cookie_that_no_longer_holds_still_goes_to_first_run()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        using var client = Agent(tangentSpace);

        var visitor = await Send(client, HttpMethod.Get, "/");
        var stale = await WithStaleCookie(client, "/", json: false);

        Assert.Equal(HttpStatusCode.OK, stale.Status);
        Assert.Contains("first-run", stale.Body);
        Assert.Equal((visitor.Status, visitor.MediaType), (stale.Status, stale.MediaType));
    }

    [Fact]
    public async Task A_read_that_says_json_and_sends_no_body_answers_as_any_read_does()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        using var client = Agent(tangentSpace);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/") { Content = new ByteArrayContent([]) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var answer = await client.SendAsync(request, Cancellation);

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
    }
}
