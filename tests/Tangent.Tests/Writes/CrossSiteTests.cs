using System.Net;
using System.Net.Http.Headers;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Posts;

/// <summary>A write answers to its sender's session cookie and to one check at the place's edge: a page of another site
/// that posts with the reader's cookie is refused and writes nothing; the place's own page writes, and so does a program
/// that sends the cookie and no browser's headers, as Companion Lobby's adapter does.</summary>
public sealed class CrossSiteTests
{
    [Fact]
    public async Task A_pages_form_writes_a_cross_site_post_is_refused_and_a_program_with_the_cookie_writes()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var root = await Started(Ada, "Lanterns", "How do you hang yours?");
        using var ada = await SignedIn(tangentSpace, Ada);
        var page = TopicPath(root);

        var fromPage = await Form(ada, page, page, new Dictionary<string, string> { ["body"] = "From the page." });
        Assert.Equal(HttpStatusCode.SeeOther, fromPage.Status);

        var before = await Counted();
        foreach (var (header, value) in new[] { ("Sec-Fetch-Site", "cross-site"), ("Sec-Fetch-Site", "same-site"), ("Origin", "https://elsewhere.example"), ("Origin", "null") })
        {
            using var forged = new HttpRequestMessage(HttpMethod.Post, page) { Content = FormBody(new Dictionary<string, string> { ["body"] = "Forged." }) };
            forged.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
            forged.Headers.Add(header, value);
            using var refused = await ada.Client.SendAsync(forged, Cancellation);
            Assert.True(refused.StatusCode == HttpStatusCode.Forbidden, header + ": " + value);
        }
        Assert.Equal(before, await Counted());

        var program = await Send(ada.Client, HttpMethod.Post, page, JsonBody(new { body = "From a program." }), json: true);
        Assert.Equal((HttpStatusCode.Created, Ada), (program.Status, program.Json.GetProperty("authorId").GetString()));
    }
}
