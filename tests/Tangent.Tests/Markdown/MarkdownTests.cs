using AngleSharp.Html.Parser;
using Tangent.Participants;
using Tangent.Writer;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Posts;

/// <summary>A body's Markdown as HTML: the blocks and inlines a body uses rendered as expected; raw HTML, unsafe links
/// and unsafe images neutralised; a single line break kept; an image with an emphasis-only caption made a figure; and a
/// body that would render too large — every mention at its longest — or too deep, that would parse slowly, or that
/// mentions too many identities, refused at write with nothing kept.</summary>
public sealed class MarkdownTests
{
    [Fact]
    public void A_body_with_headings_emphasis_lists_a_pipe_table_code_and_a_link_renders_the_expected_html()
    {
        var html = MarkdownHtml.Render(
            "# A heading\n\n## A smaller one\n\nSome *emphasis* and **strong** words.\n\n- one\n- two\n\n1. first\n2. second\n\n"
            + "> A quote.\n\n| Day | Who |\n| --- | --- |\n| Friday | Lumen |\n\nInline `code` and a block:\n\n```\nlet x = 1 < 2;\n```\n\n"
            + "[A link](https://example.org/story \"The story\") and a [path](/t/workshop).\nA second line.").Html();

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
        // A single line break is kept as a break.
        Assert.Contains("<a href=\"/t/workshop\">path</a>.<br>\nA second line.</p>", html);
    }

    [Fact]
    public void Raw_html_unsafe_links_and_images_from_unsafe_urls_are_neutralised_and_a_captioned_image_becomes_a_figure()
    {
        var html = MarkdownHtml.Render("<script>alert(1)</script>\n\n<img src=x onerror=alert(1)>\n\n"
            + "[script](javascript:alert%281%29) [entity](javascript&#58;alert(1)) [protocol](//evil.example/x) [back](https:\\\\evil.example)\n\n"
            + "![data](data:image/svg+xml;base64,PHN2Zz4=) ![remote](http://evil.example/pixel.png) ![local](/media/123)\n\n"
            + "<div style=\"position:fixed\">out</div> **bold**").Html();

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
            + "![Second](/media/second)\n\n*Next paragraph*\n\n![Third](/media/third)\n\n*Prose* with more words.\n\n"
            + "![Fourth](https://img/x.png)\n*Next line*").Html() + "</body>").QuerySelectorAll("figure");
        Assert.Equal(["Same paragraph", "Next paragraph", "Next line"], figures.Select(figure => figure.QuerySelector("figcaption")!.TextContent).ToList());
        Assert.Empty(figures[2].QuerySelectorAll("br"));
    }

    [Fact]
    public void Bodies_that_would_hold_too_many_elements_or_parse_slowly_are_refused_in_well_under_100_ms()
    {
        string Repeat(string unit, int count) => string.Concat(Enumerable.Repeat(unit, count));
        string Cut(string text) => text.Length <= Post.BodyLimit ? text : text[..Post.BodyLimit];
        PostBody.Of("warm *up*");
        var bodies = new Dictionary<string, string>
        {
            ["lines"] = Repeat("a\n", 32_767),
            ["one table row of 32K cells"] = Cut("|" + Repeat("a|", 16_384) + "\n|" + Repeat("-|", 16_384) + "\n"),
            ["one table row of 16K cells"] = "|" + Repeat("a|", 8_192) + "\n|" + Repeat("-|", 8_192) + "\n",
            ["nested list markers"] = Cut(Repeat("- ", 32_768)),
            ["paragraphs"] = Cut(Repeat("a\n\n", 21_845)),
            ["headings"] = Cut(Repeat("# a\n", 16_384)),
            ["list items"] = Cut(Repeat("- a\n", 16_384)),
            ["unclosed links"] = Repeat("[a](", 16_384)
        };
        foreach (var (name, text) in bodies)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var body = PostBody.Of(text);
            watch.Stop();
            Assert.True(body.Refusal == Post.BodyTooIntricate, $"{name}: {body.Refusal ?? "accepted"}");
            Assert.True(watch.ElapsedMilliseconds < 100, $"{name}: judged in {watch.ElapsedMilliseconds} ms.");
        }
    }

    [Fact]
    public async Task A_body_that_renders_too_large_or_too_deep_or_mentions_too_many_identities_is_refused_and_nothing_is_kept()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        var root = await Started(writer, Ada, "Lanterns", "How do you hang yours?");
        var before = await Counted();

        // Sixty kilobytes of pipes fit the body's limit and would render more than half a megabyte of table cells; twenty
        // thousand quote markers would nest past Markdig's limit: both hold far too many elements. A list nested a
        // thousand deep is within the element bound, and Markdig refuses its depth.
        var wide = "a|b\n-|-\n" + string.Concat(Enumerable.Repeat(new string('|', 200) + "\n", 300));
        var deep = new string('>', 20_000) + " a";
        var nested = string.Concat(Enumerable.Repeat("- ", 999)) + "a";
        var crowded = string.Join(' ', Enumerable.Range(0, Mentions.Limit + 1).Select(index => $"[@p{index}](at://did:plc:p{index})"));
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(wide) <= Post.BodyLimit);
        Assert.True(MarkdownHtml.Tractable(nested));

        foreach (var (body, reason) in new[] { (wide, Post.BodyTooIntricate), (deep, Post.BodyTooIntricate), (nested, Post.BodyRendersTooLong) })
        {
            Assert.Equal(reason, Assert.IsType<Outcome<Post>.Refused>(
                await writer.Send(new StartTopic(Tids.Mint(), Workshop, "Too much", body, Ada), Cancellation)).Reason);
            Assert.Equal(reason, Assert.IsType<Outcome<Post>.Refused>(
                await writer.Send(new Reply(Tids.Mint(), root.Id, body, Bea), Cancellation)).Reason);
            Assert.Equal(reason, Assert.IsType<Outcome<Post>.Refused>(
                await writer.Send(new EditPost(root.Id, 1, body, null, Ada), Cancellation)).Reason);
        }

        Assert.Equal(Post.TooManyMentions, Assert.IsType<Outcome<Post>.Refused>(
            await writer.Send(new StartTopic(Tids.Mint(), Workshop, "Everyone", crowded, Ada), Cancellation)).Reason);

        // One identity mentioned over and over: every mention is measured at its longest rendering, whatever handle Bea
        // holds when it is read, so the body is refused — two hundred on lines of their own by the rendered size, and the
        // body's limit of them in one paragraph already by its brackets.
        Assert.IsType<Outcome<Participant>.Done>(await writer.Send(new RecordHandle(Bea, new string('b', 243) + ".bsky"), Cancellation));
        var repeated = string.Concat(Enumerable.Repeat($"[x](at://{Bea})\n\n", 200));
        var paragraph = Fill($"[x](at://{Bea}) ");
        Assert.True(MarkdownHtml.Tractable(repeated));
        Assert.Equal(Post.BodyRendersTooLong, Assert.IsType<Outcome<Post>.Refused>(
            await writer.Send(new StartTopic(Tids.Mint(), Workshop, "Bea", repeated, Ada), Cancellation)).Reason);
        Assert.Equal(Post.BodyTooIntricate, Assert.IsType<Outcome<Post>.Refused>(
            await writer.Send(new StartTopic(Tids.Mint(), Workshop, "Bea", paragraph, Ada), Cancellation)).Reason);

        // Sixty-four kilobytes of unclosed links would take Markdig seconds to parse; the structure check refuses the body
        // where its command is built, in a few milliseconds and before any parse.
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var unclosed = PostBody.Of(string.Concat(Enumerable.Repeat("[a](", 16_384)));
        watch.Stop();
        Assert.Equal(Post.BodyTooIntricate, unclosed.Refusal);
        Assert.True(watch.ElapsedMilliseconds < 500, $"Judging the body took {watch.ElapsedMilliseconds} ms.");
        Assert.Equal(Post.BodyTooIntricate, Assert.IsType<Outcome<Post>.Refused>(
            await writer.Send(new Reply(Tids.Mint(), root.Id, unclosed, Bea), Cancellation)).Reason);
        Assert.Equal(Post.BodyTooIntricate, Assert.IsType<Outcome<Post>.Refused>(
            await writer.Send(new EditPost(root.Id, 1, string.Concat(Enumerable.Repeat("*a_", 3000)), null, Ada), Cancellation)).Reason);
        Assert.Equal(Post.BodyTooIntricate, Assert.IsType<Outcome<Post>.Refused>(
            await writer.Send(new StartTopic(Tids.Mint(), Workshop, "Brackets", new string('[', 3000), Ada), Cancellation)).Reason);
        Assert.Equal(before, await Counted());
        Assert.Equal(("How do you hang yours?", 1L), ((await Post.Get(root.Id, Cancellation))?.Body, (await Post.Get(root.Id, Cancellation))?.Version));
        Assert.Equal(PostWrites.About.Body, PostWrites.Concerning(Post.BodyRendersTooLong));
        Assert.Equal(PostWrites.About.Body, PostWrites.Concerning(Post.TooManyMentions));
        Assert.Equal(PostWrites.About.Body, PostWrites.Concerning(Post.BodyTooIntricate));
    }

    /// <summary>A unit repeated to just under the body's limit.</summary>
    private static string Fill(string unit) => string.Concat(Enumerable.Repeat(unit, (Post.BodyLimit - 16) / unit.Length));
}
