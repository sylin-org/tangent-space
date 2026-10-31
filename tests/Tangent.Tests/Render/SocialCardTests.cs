using System.Net;
using System.Text.RegularExpressions;
using Koan.Identity.Roles;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Space;
using Tangent.Tangents;
using Tangent.Writer;
using Xunit;

namespace Tangent.Render;

/// <summary>What a link pasted elsewhere previews: the card of a Topic, an article, a Tangent and the door, only ever what a
/// visitor may read, and nothing of a removed Topic's words.</summary>
public sealed partial class SocialCardTests
{
    private const string BaseUrl = TangentSpaceFixture.DefaultBaseUrl;
    private const string Workshop = "the-workshop";
    private const string Back = "the-back-room";
    private const string Place = "Leo's place";
    private const string Reader = "did:plc:reader";

    private static readonly DateTimeOffset Start = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    private static readonly Dictionary<string, string?> TestConnector = new()
    {
        ["Koan:Web:Auth:TestProvider:Enabled"] = "true",
        ["Tangent:Profile:BaseAddress"] = "http://127.0.0.1:1/",
        ["Tangent:Profile:Timeout"] = "00:00:01"
    };

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_public_topic_previews_its_title_first_words_address_and_the_places_name_wherever_it_is_linked()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = await Established(tangentSpace);
        var topic = await Started(writer, "A first question", "The **first** words of it.");
        var reply = Assert.IsType<Outcome<Post>.Done>(await writer.Send(
            new Reply(Tids.Mint(), topic.Id, "Words of the reply.", "ada", At: Start.AddMinutes(1)), Cancellation)).Result;
        var address = $"{BaseUrl}/t/{Workshop}/{topic.Slug}";

        foreach (var path in new[] { $"/t/{Workshop}/{topic.Slug}", $"/t/{Workshop}/{topic.Slug}/{reply.Id}" })
        {
            var html = await Page(tangentSpace.Client, path);

            Assert.Equal("A first question", Meta(html, "og:title"));
            Assert.Equal("The first words of it.", Meta(html, "og:description"));
            Assert.Equal(address, Meta(html, "og:url"));
            Assert.Equal(Place, Meta(html, "og:site_name"));
            Assert.Equal("website", Meta(html, "og:type"));
            Assert.Equal("summary", Twitter(html));
            Assert.Contains($"<link rel=\"canonical\" href=\"{address}\"", html);
            Assert.Single(TitleElement().Matches(html));
        }
    }

    [Fact]
    public async Task An_article_says_it_is_one_and_a_long_opening_is_cut_at_a_word()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = await Established(tangentSpace);
        var article = await Started(writer, "A published piece", string.Concat(Enumerable.Repeat("word ", 100)), PostKind.Article);

        var html = await Page(tangentSpace.Client, $"/t/{Workshop}/{article.Slug}");

        Assert.Equal("article", Meta(html, "og:type"));
        var description = Meta(html, "og:description")!;
        Assert.True(description.Length <= TangentPage.OpeningLimit + 1, description);
        Assert.EndsWith("word…", description);
    }

    [Fact]
    public async Task A_description_carries_no_markup_and_names_no_one()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = await Established(tangentSpace);
        await Given.AParticipant(tangentSpace, "did:plc:ines", [Roles.Member], ct: Cancellation);
        var topic = await Started(writer, "A question",
            "[@ines](at://did:plc:ines), thanks to [@ines](at://did:plc:ines).\n\n## Heading\n\nThe *words*, `code`, [a page](/about) and ![a sketch](/media/sketch.png) <b>here</b>.");

        var html = await Page(tangentSpace.Client, $"/t/{Workshop}/{topic.Slug}");
        var description = Meta(html, "og:description")!;

        Assert.StartsWith("thanks to. Heading The words, code, a page", description);
        foreach (var absent in new[] { "ines", "did:", "at://", "@", "#", "*", "`", "[", "](", "/about", "/media" })
            Assert.DoesNotContain(absent, description);
        Assert.DoesNotContain("<b>here", html);
    }

    [Fact]
    public async Task A_members_only_topic_previews_nothing_for_a_visitor_or_on_its_members_own_page()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector, ct: Cancellation);
        var writer = await Established(tangentSpace);
        var roles = tangentSpace.Services.GetRequiredService<RoleCollection>();
        await Overwrites.SetAudience(roles, Back, Audience.Private, Cancellation);
        Assert.IsType<Outcome<Tangents.Tangent>.Done>(await writer.Send(new Store<Tangents.Tangent>(Tangents.Tangent.Create(Back, "The back room", "Kept close.", PostKind.Topic, 2)), Cancellation));
        await Overwrites.SetAccess(roles, Back, Roles.Member, RoleAccess.Post, Cancellation);
        var inside = Assert.IsType<Outcome<Post>.Done>(await writer.Send(
            new StartTopic(Tids.Mint(), Back, "Said in the back", "Secret words.", "ada", null, Start), Cancellation)).Result;
        using var member = new Browser(tangentSpace);
        (await member.SignIn("reader", Reader, Cancellation)).Dispose();

        foreach (var path in new[] { $"/t/{Back}", $"/t/{Back}/{inside.Slug}" })
        {
            var refused = await Answer(tangentSpace.Client, path);
            Assert.NotEqual(HttpStatusCode.OK, refused.Status);
            NoCard(refused.Html);
            Assert.DoesNotContain("Secret words", refused.Html);
            Assert.DoesNotContain("Said in the back", refused.Html);

            NoCard(await Page(member.Client, path));
        }

        Assert.Contains("Secret words", await Page(member.Client, $"/t/{Back}/{inside.Slug}"));
    }

    [Fact]
    public async Task A_private_place_previews_nothing_for_a_visitor_or_a_participant()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector, ct: Cancellation);
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        await Given.AnEstablishedTangentSpace(tangentSpace, "Off the record", Audience.Private, ct: Cancellation);
        await Given.AParticipant(tangentSpace, "ada", [Roles.Member], ct: Cancellation);
        await Given.ATangent(tangentSpace, Workshop, ct: Cancellation);
        var topic = await Started(writer, "Said in private", "Private words.");
        using var member = new Browser(tangentSpace);
        (await member.SignIn("reader", Reader, Cancellation)).Dispose();

        foreach (var path in new[] { "/", $"/t/{Workshop}", $"/t/{Workshop}/{topic.Slug}" })
        {
            var visitor = await Answer(tangentSpace.Client, path);
            Assert.Equal(HttpStatusCode.Unauthorized, visitor.Status);
            NoCard(visitor.Html);
            Assert.DoesNotContain("Private words", visitor.Html);
            Assert.DoesNotContain("Said in private", visitor.Html);

            NoCard(await Page(member.Client, path));
        }
    }

    [Fact]
    public async Task A_removed_topic_says_nothing_of_its_words()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = await Established(tangentSpace);
        var topic = await Started(writer, "A question", "These words go.");
        Assert.Equal("These words go.", Meta(await Page(tangentSpace.Client, $"/t/{Workshop}/{topic.Slug}"), "og:description"));

        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new RemovePost(topic.Id, "leo", Start.AddMinutes(1)), Cancellation));

        var html = await Page(tangentSpace.Client, $"/t/{Workshop}/{topic.Slug}");
        Assert.Null(Meta(html, "og:description"));
        Assert.DoesNotContain("twitter:description", html);
        Assert.DoesNotContain("These words go", html);
    }

    [Fact]
    public async Task A_tangent_and_the_door_preview_as_themselves_and_the_door_takes_the_places_new_name()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = await Established(tangentSpace);

        var tangent = await Page(tangentSpace.Client, $"/t/{Workshop}");
        Assert.Equal("The workshop", Meta(tangent, "og:title"));
        Assert.Equal("Where things get made.", Meta(tangent, "og:description"));
        Assert.Equal($"{BaseUrl}/t/{Workshop}", Meta(tangent, "og:url"));
        Assert.Equal(Place, Meta(tangent, "og:site_name"));

        var door = await Page(tangentSpace.Client, "/");
        Assert.Equal(Place, Meta(door, "og:title"));
        Assert.Null(Meta(door, "og:description"));
        Assert.Equal($"{BaseUrl}/", Meta(door, "og:url"));

        Assert.IsType<Outcome<TangentSpace>.Done>(await writer.Send(
            new ChangeTangentSpace("leo", "The new name", "What is here."), Cancellation));

        var renamed = await Page(tangentSpace.Client, "/");
        Assert.Equal("The new name", Meta(renamed, "og:title"));
        Assert.Equal("The new name", Meta(renamed, "og:site_name"));
        Assert.Equal("What is here.", Meta(renamed, "og:description"));
    }

    [Fact]
    public async Task A_fragment_carries_no_card()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = await Established(tangentSpace);
        var topic = await Started(writer, "A question", "The first words.");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/t/{Workshop}/{topic.Slug}");
        request.Headers.Accept.ParseAdd("text/html");
        request.Headers.Add(HtmlFormatter.FragmentHeader, "1");
        using var response = await tangentSpace.Client.SendAsync(request, Cancellation);

        NoCard(await response.Content.ReadAsStringAsync(Cancellation));
    }

    private static async Task<WriteLoop> Established(TangentSpaceFixture tangentSpace)
    {
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        await Given.AnEstablishedTangentSpace(tangentSpace, Place, ct: Cancellation);
        await Given.AParticipant(tangentSpace, "leo", [Roles.Administrator], ct: Cancellation);
        await Given.AParticipant(tangentSpace, "ada", [Roles.Member], ct: Cancellation);
        Assert.IsType<Outcome<Tangents.Tangent>.Done>(await writer.Send(
            new CreateTangent(Workshop, "The workshop", "Where things get made.", PostKind.Topic, 1, Audience.Public, "leo"), Cancellation));
        return writer;
    }

    private static async Task<Post> Started(WriteLoop writer, string title, string body, PostKind? kind = null)
        => Assert.IsType<Outcome<Post>.Done>(await writer.Send(new StartTopic(Tids.Mint(), Workshop, title, body, "ada", kind, Start), Cancellation)).Result;

    private static async Task<(HttpStatusCode Status, string Html)> Answer(HttpClient client, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.ParseAdd("text/html");
        using var response = await client.SendAsync(request, Cancellation);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(Cancellation));
    }

    private static async Task<string> Page(HttpClient client, string path)
    {
        var (status, html) = await Answer(client, path);
        Assert.Equal(HttpStatusCode.OK, status);
        return html;
    }

    private static void NoCard(string html)
    {
        Assert.DoesNotContain("og:", html);
        Assert.DoesNotContain("twitter:", html);
        Assert.DoesNotContain("rel=\"canonical\"", html);
    }

    private static string? Meta(string html, string property)
    {
        var match = Regex.Match(html, $"<meta property=\"{Regex.Escape(property)}\" content=\"([^\"]*)\"");
        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : null;
    }

    private static string? Twitter(string html)
    {
        var match = Regex.Match(html, "<meta name=\"twitter:card\" content=\"([^\"]*)\"");
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex("<title>")]
    private static partial Regex TitleElement();
}
