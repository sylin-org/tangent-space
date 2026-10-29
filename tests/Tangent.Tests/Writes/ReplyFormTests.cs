using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Tangent.Access;
using Tangent.Writer;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Posts;

/// <summary>The reply form under a Topic's replies: for a reader who may reply, a form that posts to the Topic's address
/// with the form's token and a TID minted as it renders, for the composer island to take; at a reply's own address, the
/// same form answering that reply; for a reader who may not reply, the gate's sentence. Sent as a browser without script
/// sends it, it replies and is sent on to the new Post's address.</summary>
public sealed partial class ReplyFormTests
{
    [Fact]
    public async Task A_reader_who_may_reply_holds_a_form_with_a_fresh_tid_and_the_token_and_sending_it_without_script_replies_and_lands_on_the_reply()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        var root = await Started(writer, Ada, "Lanterns at dusk", "Who lights theirs first?");
        using var bea = await SignedIn(tangentSpace, Bea);

        var (first, caching) = await Page(bea.Client, TopicPath(root));
        var (second, _) = await Page(bea.Client, TopicPath(root));

        var form = ReplyFormOf(first);
        Assert.Contains($"action=\"{BaseUrl}{TopicPath(root)}\"", form);
        Assert.Contains("data-island=\"composer\"", form);
        Assert.Contains($"&quot;participant&quot;:&quot;{Bea}&quot;", form);
        Assert.Contains($"&quot;scope&quot;:&quot;topic:{root.Id}&quot;", form);
        Assert.Contains($"&quot;locate&quot;:&quot;{BaseUrl}{TopicPath(root)}/&quot;", form);
        Assert.Matches("<input type=\"hidden\" name=\"__RequestVerificationToken[^\"]*\" value=\"[^\"]+\"", form);
        Assert.Contains("<input type=\"hidden\" name=\"parent\" />", form);
        Assert.Contains("<textarea class=\"post-form-body\" id=\"reply-body\" name=\"body\"", form);
        Assert.True(Tid.IsValid(MintedId(first)));
        Assert.NotEqual(MintedId(first), MintedId(second));
        // A page that carries a form token is one no cache keeps.
        Assert.Contains("no-store", caching);

        var minted = MintedId(first);
        var fields = new Dictionary<string, string> { ["id"] = minted, ["parent"] = "", ["body"] = "I do, at six." };
        var replied = await Form(bea, TopicPath(root), TopicPath(root), fields);

        Assert.Equal((HttpStatusCode.SeeOther, BaseUrl + TopicPath(root) + "/" + minted), (replied.Status, replied.Location));
        var reply = await Post.Get(minted, Cancellation);
        Assert.Equal((root.Id, root.Id, Bea, "I do, at six."), (reply?.TopicId, reply?.ParentId, reply?.AuthorId, reply?.Body));
        var (landed, _) = await Page(bea.Client, new Uri(replied.Location!).AbsolutePath);
        Assert.Contains("<article id=\"" + minted + "\"", landed);
        Assert.NotEqual(minted, MintedId(landed));
        // Landing on their own reply, the reader goes on replying to the Topic.
        Assert.Contains("<input type=\"hidden\" name=\"parent\" />", ReplyFormOf(landed));
    }

    [Fact]
    public async Task At_a_replys_address_the_form_answers_that_reply_and_sending_it_replies_to_it()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        var root = await Started(writer, Ada, "Wicks", "Cotton or hemp?");
        var cotton = await Replied(writer, root, Ada, "Cotton, always.");
        using var bea = await SignedIn(tangentSpace, Bea);

        var (atTopic, _) = await Page(bea.Client, TopicPath(root));
        var (atReply, _) = await Page(bea.Client, PostPath(root, cotton));

        Assert.Contains("<p class=\"reply-form-to\" data-reply-to hidden>", ReplyFormOf(atTopic));
        var form = ReplyFormOf(atReply);
        Assert.Contains($"<input type=\"hidden\" name=\"parent\" value=\"{cotton.Id}\" />", form);
        Assert.Contains("<p class=\"reply-form-to\" data-reply-to>", form);
        Assert.Matches($"Replying to <a href=\"#{cotton.Id}\" data-reply-to-post>[^<]+</a>", form);
        Assert.Contains($"<a href=\"{BaseUrl}{TopicPath(root)}#reply\" data-reply-to-topic>", form);

        var minted = MintedId(atReply);
        var replied = await Form(bea, PostPath(root, cotton), TopicPath(root),
            new Dictionary<string, string> { ["id"] = minted, ["parent"] = cotton.Id, ["body"] = "Hemp burns cleaner." });

        Assert.Equal((HttpStatusCode.SeeOther, BaseUrl + TopicPath(root) + "/" + minted), (replied.Status, replied.Location));
        Assert.Equal(cotton.Id, (await Post.Get(minted, Cancellation))?.ParentId);
    }

    [Fact]
    public async Task At_a_removed_replys_address_the_form_answers_the_topic_and_never_the_tombstone()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        var root = await Started(writer, Ada, "Glazes", "Which one cracks least?");
        var gone = await Replied(writer, root, Ada, "Said and taken back.");
        var row = (await Post.Get(gone.Id, Cancellation))!;
        row.Removed = true;
        row.RemovedBy = Ada;
        row.RemovedAt = DateTimeOffset.UtcNow;
        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new Store<Post>(row), Cancellation));
        using var bea = await SignedIn(tangentSpace, Bea);

        var (atTombstone, _) = await Page(bea.Client, PostPath(root, gone));

        var form = ReplyFormOf(atTombstone);
        Assert.Contains("<input type=\"hidden\" name=\"parent\" />", form);
        Assert.Contains("<p class=\"reply-form-to\" data-reply-to hidden>", form);
    }

    [Fact]
    public async Task A_topics_root_tid_under_its_tangent_answers_a_redirect_to_the_topic_while_a_slug_that_looks_like_a_tid_names_its_own_topic()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        var root = await Started(writer, Ada, "Kiln days", "First and third Thursday.");
        var reply = await Replied(writer, root, Bea, "Noted.");
        var elsewhere = await Started(writer, Ada, "Wicks", "Cotton or hemp?", Articles);
        // A Topic whose title yields a slug in a TID's shape: its slug names it, whatever TID it equals.
        var lookalike = await Started(writer, Bea, Tids.Mint(), "A title that is a TID.");
        using var visitor = Agent(tangentSpace);

        var found = await Send(visitor, HttpMethod.Get, "/t/" + Workshop + "/" + root.Id, json: true);
        var ofReply = await Send(visitor, HttpMethod.Get, "/t/" + Workshop + "/" + reply.Id, json: true);
        var ofOtherTangent = await Send(visitor, HttpMethod.Get, "/t/" + Workshop + "/" + elsewhere.Id, json: true);
        var unknown = await Send(visitor, HttpMethod.Get, "/t/" + Workshop + "/" + Tids.Mint(), json: true);
        var bySlug = await Send(visitor, HttpMethod.Get, "/t/" + Workshop + "/" + lookalike.Slug, json: true);

        Assert.Equal((HttpStatusCode.Found, BaseUrl + TopicPath(root)), (found.Status, found.Location));
        Assert.Equal(HttpStatusCode.NotFound, ofReply.Status);
        Assert.Equal(HttpStatusCode.NotFound, ofOtherTangent.Status);
        Assert.Equal(HttpStatusCode.NotFound, unknown.Status);
        Assert.True(Tid.IsValid(lookalike.Slug));
        Assert.Equal((HttpStatusCode.OK, lookalike.Id), (bySlug.Status, bySlug.Json.GetProperty("id").GetString()));
        var landed = await Send(visitor, HttpMethod.Get, new Uri(found.Location!).AbsolutePath, json: true);
        Assert.Equal(root.Id, landed.Json.GetProperty("id").GetString());
    }

    [Fact]
    public async Task A_reader_who_may_not_reply_reads_the_gates_sentence_and_holds_no_form()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        var open = await Started(writer, Ada, "Open", "Anyone may answer.");
        var closed = await Started(writer, Ada, "Closed", "Said and done.");
        var row = (await Post.Get(closed.Id, Cancellation))!;
        row.Locked = true;
        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new Store<Post>(row), Cancellation));
        using var bea = await SignedIn(tangentSpace, Bea);
        using var visitor = Agent(tangentSpace);

        var (visiting, _) = await Page(visitor, TopicPath(open));
        var (locked, _) = await Page(bea.Client, TopicPath(closed));

        Assert.DoesNotContain("class=\"reply-form\"", visiting);
        Assert.Contains($"<p class=\"topic-closed\" data-gate=\"{Capabilities.SignIn}\">", visiting);
        Assert.Matches(Regex.Escape(PostWrites.Sentence(Capabilities.SignIn)) + " <a href=\"[^\"]*/sign-in\\?return=[^\"]+\">Sign in</a>", visiting);
        Assert.DoesNotContain("class=\"reply-form\"", locked);
        Assert.Contains($"<p class=\"topic-closed\" data-gate=\"{Capabilities.Locked}\">", locked);
        Assert.Contains(PostWrites.Sentence(Capabilities.Locked), locked);
    }

    /// <summary>A page as a browser reads it: its HTML and its <c>Cache-Control</c>.</summary>
    private static async Task<(string Html, string Caching)> Page(HttpClient client, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        using var response = await client.SendAsync(request, Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadAsStringAsync(Cancellation), response.Headers.CacheControl?.ToString() ?? "");
    }

    private static string ReplyFormOf(string html) => ReplyForm().Match(html) is { Success: true } match
        ? match.Value
        : throw new InvalidOperationException("The page holds no reply form.");

    [GeneratedRegex("<form class=\"reply-form\".*?</form>", RegexOptions.Singleline)]
    private static partial Regex ReplyForm();
}
