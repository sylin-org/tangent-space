using System.Net;
using Tangent.Writer;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Posts;

/// <summary>Runs alone, so the process's count of Markdown parses moves only by what its proof does.</summary>
[CollectionDefinition(nameof(ParseCount), DisableParallelization = true)]
public sealed class ParseCount;

/// <summary>A body is judged where its command is built, so the writer never parses; and each Post version is parsed at
/// most once in this process, whatever reads it afterwards — the Topic page as HTML, its JSON, the Tangent page and the
/// door.</summary>
[Collection(nameof(ParseCount))]
public sealed class RenderOnceTests
{
    [Fact]
    public async Task The_writer_never_parses_a_body_and_every_read_parses_each_post_version_at_most_once()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        var paragraph = "A long note with *emphasis*, a [link](https://example.org/x) and a `code` span.\n\n";
        var body = $"For [@bea](at://{Bea}).\n\n" + string.Concat(Enumerable.Repeat(paragraph, 300));

        // Building each command judges its body: one parse apiece. Sending them parses nothing more.
        var rootId = Tids.Mint();
        var start = new StartTopic(rootId, Workshop, "Long reads", body, Ada);
        var built = MarkdownHtml.Parses;
        var root = Assert.IsType<Outcome<Post>.Done>(await writer.Send(start, Cancellation)).Result;
        var replies = new List<Reply> { new(Tids.Mint(), rootId, body + "One.", Bea), new(Tids.Mint(), rootId, body + "Two.", Mo) };
        built = MarkdownHtml.Parses;
        foreach (var reply in replies) Assert.IsType<Outcome<Post>.Done>(await writer.Send(reply, Cancellation));
        Assert.Equal(built, MarkdownHtml.Parses);

        // The first page parses each of its three Posts once; the page again, its JSON, the Tangent page and the door none.
        using var visitor = Agent(tangentSpace);
        var before = MarkdownHtml.Parses;
        var page = await Send(visitor, HttpMethod.Get, TopicPath(root));
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Equal(before + 3, MarkdownHtml.Parses);
        Assert.Equal(HttpStatusCode.OK, (await Send(visitor, HttpMethod.Get, TopicPath(root))).Status);
        Assert.Equal(HttpStatusCode.OK, (await Send(visitor, HttpMethod.Get, TopicPath(root), json: true)).Status);
        Assert.Equal(HttpStatusCode.OK, (await Send(visitor, HttpMethod.Get, "/t/" + Workshop)).Status);
        Assert.Equal(HttpStatusCode.OK, (await Send(visitor, HttpMethod.Get, "/")).Status);
        Assert.Equal(before + 3, MarkdownHtml.Parses);

        // An edit makes a new version: built once, sent without a parse, read once.
        var edit = new EditPost(root.Id, root.Version, body + "Edited.", null, Ada);
        built = MarkdownHtml.Parses;
        Assert.IsType<Outcome<Post>.Done>(await writer.Send(edit, Cancellation));
        Assert.Equal(built, MarkdownHtml.Parses);
        Assert.Contains("Edited.", (await Send(visitor, HttpMethod.Get, TopicPath(root))).Body);
        Assert.Equal(built + 1, MarkdownHtml.Parses);
    }
}
