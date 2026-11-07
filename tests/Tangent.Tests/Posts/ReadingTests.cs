using System.Net;
using System.Text.Json;
using Koan.Data.Core.Commands;
using Koan.Identity.Roles;
using Tangent.Access;
using Tangent.Capture;
using Tangent.Space;
using Tangent.Tangents;
using Xunit;

namespace Tangent.Posts;

/// <summary>What a reader meets beyond a Topic's Posts, through the place: an edited Post's earlier versions, with who
/// wrote each, and an article's outline with its headings given the ids it opens them at.</summary>
public sealed class ReadingTests
{
    private const string Workshop = "the-workshop";
    private const string Articles = "articles";
    private static readonly DateTimeOffset Start = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_edited_post_offers_its_earlier_versions_and_its_link_and_reads_them_with_who_wrote_each()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        await Place(tangentSpace);
        var topic = Assert.IsType<Outcome<Post>.Done>(await new Started(Tids.Mint(), Workshop, "A question", "The first words.", "ada", Start).Send(Cancellation)).Result;
        Assert.IsType<Outcome<Post>.Done>(await new Edited(topic.Id, topic.Version, "The second words.", null, "leo", Start.AddHours(1)).Send(Cancellation));

        var page = await Read(tangentSpace.Client, $"/t/{Workshop}/{topic.Slug}", "text/html");
        Assert.Contains("class=\"post-history\"", page);
        Assert.Contains($"/t/{Workshop}/{topic.Slug}/{topic.Id}/history", page);
        Assert.Contains("Copy link", page);

        using var history = JsonDocument.Parse(await Read(tangentSpace.Client, $"/t/{Workshop}/{topic.Slug}/{topic.Id}/history", "application/json"));
        var versions = history.RootElement.GetProperty("versions");
        Assert.Equal(1, versions.GetArrayLength());
        Assert.Equal(1, versions[0].GetProperty("number").GetInt64());
        Assert.Equal("The first words.", versions[0].GetProperty("body").GetString());
        Assert.Equal("ada", versions[0].GetProperty("by").GetProperty("id").GetString());
        Assert.False(history.RootElement.GetProperty("more").GetBoolean());
    }

    [Fact]
    public async Task An_article_reads_with_its_outline_and_its_headings_open_at_the_outline_s_ids()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        await Place(tangentSpace);
        var article = Assert.IsType<Outcome<Post>.Done>(await new Started(Tids.Mint(), Articles, "Mending", "Opening.\n\n## The seam\n\nWords.\n\n### Habits\n\nMore.\n\n## The seam\n\nAgain.", "ada", Start).Send(Cancellation)).Result;

        var page = await Read(tangentSpace.Client, $"/t/{Articles}/{article.Slug}", "text/html");
        Assert.Contains("In this article", page);
        Assert.Contains("<h2 id=\"section-the-seam\">The seam</h2>", page);
        Assert.Contains("<h3 id=\"section-habits\">Habits</h3>", page);
        Assert.Contains("<h2 id=\"section-the-seam-2\">The seam</h2>", page);
        Assert.Contains("href=\"#section-habits\"", page);
    }

    private static async Task Place(TangentSpaceFixture tangentSpace)
    {
        await Given.AnEstablishedTangentSpace(tangentSpace, ct: Cancellation);
        await Given.AParticipant(tangentSpace, "leo", [Roles.Administrator], ct: Cancellation);
        await Given.AParticipant(tangentSpace, "ada", [Roles.Member], ct: Cancellation);
        Assert.IsType<Outcome<Tangents.Tangent>.Done>(await new CreateTangent(Workshop, "The workshop", "Where things get made.", PostKind.Topic, 1, Audience.Public, "leo").Send(Cancellation));
        Assert.IsType<Outcome<Tangents.Tangent>.Done>(await new CreateTangent(Articles, "Articles", "Pieces.", PostKind.Article, 2, Audience.Public, "leo").Send(Cancellation));
    }

    private static async Task<string> Read(HttpClient client, string path, string accept)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.ParseAdd(accept);
        using var response = await client.SendAsync(request, Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Cancellation);
    }
}
