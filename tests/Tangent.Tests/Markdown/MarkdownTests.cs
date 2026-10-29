using AngleSharp.Html.Parser;
using Tangent.Writer;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Posts;

/// <summary>A body's Markdown as HTML: the blocks and inlines a body uses rendered as expected; raw HTML, unsafe links
/// and unsafe images neutralised; an image with an emphasis-only caption made a figure; and a body that would render
/// too large or too deep, or mention too many identities, refused at write with nothing kept.</summary>
public sealed class MarkdownTests
{
    [Fact]
    public void A_body_with_headings_emphasis_lists_a_pipe_table_code_and_a_link_renders_the_expected_html()
    {
        var html = MarkdownHtml.Render(
            "# A heading\n\n## A smaller one\n\nSome *emphasis* and **strong** words.\n\n- one\n- two\n\n1. first\n2. second\n\n"
            + "> A quote.\n\n| Day | Who |\n| --- | --- |\n| Friday | Lumen |\n\nInline `code` and a block:\n\n```\nlet x = 1 < 2;\n```\n\n"
            + "[A link](https://example.org/story \"The story\") and a [path](/t/workshop).");

        Assert.Contains("<h1>A heading</h1>", html);
        Assert.Contains("<h2>A smaller one</h2>", html);
        Assert.Contains("<p>Some <em>emphasis</em> and <strong>strong</strong> words.</p>", html);
        Assert.Contains("<ul>\n<li>one</li>\n<li>two</li>\n</ul>", html);
        Assert.Contains("<ol>\n<li>first</li>\n<li>second</li>\n</ol>", html);
        Assert.Contains("<blockquote>\n<p>A quote.</p>\n</blockquote>", html);
        Assert.Contains("<table>", html);
        Assert.Contains("<th>Day</th>", html);
        Assert.Contains("<td>Friday</td>", html);
        Assert.Contains("<code>code</code>", html);
        Assert.Contains("<pre><code>let x = 1 &lt; 2;\n</code></pre>", html);
        Assert.Contains("<a href=\"https://example.org/story\" title=\"The story\">A link</a>", html);
        Assert.Contains("<a href=\"/t/workshop\">path</a>", html);
    }

    [Fact]
    public void Raw_html_unsafe_links_and_images_from_unsafe_urls_are_neutralised_and_a_captioned_image_becomes_a_figure()
    {
        var html = MarkdownHtml.Render("<script>alert(1)</script>\n\n<img src=x onerror=alert(1)>\n\n"
            + "[script](javascript:alert%281%29) [entity](javascript&#58;alert(1)) [protocol](//evil.example/x) [back](https:\\\\evil.example)\n\n"
            + "![data](data:image/svg+xml;base64,PHN2Zz4=) ![remote](http://evil.example/pixel.png) ![local](/media/123)\n\n"
            + "<div style=\"position:fixed\">out</div> **bold**");

        // Raw HTML is shown as its own text: no element of it reaches the page, and no attribute but the allowed ones.
        var document = new HtmlParser().ParseDocument("<body>" + html + "</body>");
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.Contains("&lt;div style=\"position:fixed\"&gt;out&lt;/div&gt;", html);
        Assert.Empty(document.Body!.QuerySelectorAll("script, div, style, iframe"));
        Assert.All(document.Body.QuerySelectorAll("*"), element =>
            Assert.All(element.Attributes, attribute => Assert.Contains(attribute.Name, (string[])["href", "src", "alt", "title", "loading", "decoding", "referrerpolicy"])));
        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data:image", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("//evil.example/x", html);
        Assert.DoesNotContain("href=\"https:", html);
        Assert.Contains("<a href=\"#\">script</a>", html);
        Assert.Contains("<a href=\"#\">entity</a>", html);
        Assert.Contains("<a href=\"#\">protocol</a>", html);
        Assert.Contains("<a href=\"#\">data</a>", html);
        Assert.Contains("<strong>bold</strong>", html);

        // An image from a safe URL stays an image, loads lazily and sends no referrer.
        var images = document.QuerySelectorAll("img");
        Assert.Equal(["http://evil.example/pixel.png", "/media/123"], images.Select(image => image.GetAttribute("src")).ToList());
        Assert.All(images, image => Assert.Equal(("lazy", "async", "no-referrer"),
            (image.GetAttribute("loading"), image.GetAttribute("decoding"), image.GetAttribute("referrerpolicy"))));

        var figures = new HtmlParser().ParseDocument("<body>" + MarkdownHtml.Render("![First](/media/first) *Same paragraph*\n\n"
            + "![Second](/media/second)\n\n*Next paragraph*\n\n![Third](/media/third)\n\n*Prose* with more words.") + "</body>").QuerySelectorAll("figure");
        Assert.Equal(["Same paragraph", "Next paragraph"], figures.Select(figure => figure.QuerySelector("figcaption")!.TextContent).ToList());
    }

    [Fact]
    public async Task A_body_that_renders_too_large_or_too_deep_or_mentions_too_many_identities_is_refused_and_nothing_is_kept()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        var root = await Started(writer, Ada, "Lanterns", "How do you hang yours?");
        var before = await Counted();

        // Sixty kilobytes of pipes fit the body's limit and render to more than half a megabyte of table cells.
        var wide = "a|b\n-|-\n" + string.Concat(Enumerable.Repeat(new string('|', 200) + "\n", 300));
        var deep = new string('>', 20_000) + " a";
        var crowded = string.Join(' ', Enumerable.Range(0, Mentions.Limit + 1).Select(index => $"[@p{index}](at://did:plc:p{index})"));
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(wide) <= Post.BodyLimit);

        foreach (var body in new[] { wide, deep })
        {
            Assert.Equal(Post.BodyRendersTooLong, Assert.IsType<Outcome<Post>.Refused>(
                await writer.Send(new StartTopic(Tids.Mint(), Workshop, "Too much", body, Ada), Cancellation)).Reason);
            Assert.Equal(Post.BodyRendersTooLong, Assert.IsType<Outcome<Post>.Refused>(
                await writer.Send(new Reply(Tids.Mint(), root.Id, body, Bea), Cancellation)).Reason);
            Assert.Equal(Post.BodyRendersTooLong, Assert.IsType<Outcome<Post>.Refused>(
                await writer.Send(new EditPost(root.Id, 1, body, null, Ada), Cancellation)).Reason);
        }

        Assert.Equal(Post.TooManyMentions, Assert.IsType<Outcome<Post>.Refused>(
            await writer.Send(new StartTopic(Tids.Mint(), Workshop, "Everyone", crowded, Ada), Cancellation)).Reason);
        Assert.Equal(before, await Counted());
        Assert.Equal(("How do you hang yours?", 1L), ((await Post.Get(root.Id, Cancellation))?.Body, (await Post.Get(root.Id, Cancellation))?.Version));
        Assert.Equal(PostWrites.About.Body, PostWrites.Concerning(Post.BodyRendersTooLong));
        Assert.Equal(PostWrites.About.Body, PostWrites.Concerning(Post.TooManyMentions));
    }
}
