using System.Net;
using System.Text.Json;
using Tangent.Access;
using Tangent.Space;
using Tangent.Writer;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Posts;

/// <summary>A search at <c>/search</c>, one resource both ways: a Post is found by a word of its title or body, and by the
/// start of the word the text ends in, a page at a time, each a line one click from the Post; a Post in a Tangent the
/// reader may not view is never found, on any page, by any length of page; a removed Post is not found; and the masthead's
/// box is a form that works without JavaScript.</summary>
public sealed class SearchTests
{
    private const string Leo = "did:plc:leo4search";

    private static string Path(string words, int page = 1) => "/search?q=" + Uri.EscapeDataString(words) + (page > 1 ? "&page=" + page : "");

    private static async Task<(TangentSpaceFixture Space, WriteLoop Writer)> Open()
    {
        var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        return (tangentSpace, writer);
    }

    private static List<string?> Addresses(JsonElement page)
        => page.GetProperty("hits").EnumerateArray().Select(hit => hit.GetProperty("address").GetString()).ToList();

    [Fact]
    public async Task A_word_of_a_title_and_a_word_of_a_body_find_their_post_as_a_page_and_as_json_each_a_line_linked_to_the_post()
    {
        var (tangentSpace, writer) = await Open();
        await using var space = tangentSpace;
        var root = await Started(writer, Ada, "Copper lanterns", "How the glass is cut.");
        var reply = await Replied(writer, root, Bea, "Annealing **matters** most, as [@ada](at://" + Ada + ") says.");

        var byTitle = (await Send(tangentSpace.Client, HttpMethod.Get, Path("lanterns"), json: true)).Json;
        Assert.Equal([BaseUrl + TopicPath(root)], Addresses(byTitle));
        Assert.Equal("lanterns", byTitle.GetProperty("text").GetString());
        var title = byTitle.GetProperty("hits")[0];
        Assert.Equal(("topic", "Copper lanterns", Workshop), (title.GetProperty("kind").GetString(), title.GetProperty("topic").GetString(), title.GetProperty("tangent").GetString()));
        Assert.Equal("How the glass is cut.", string.Concat(title.GetProperty("snippet").EnumerateArray().Select(run => run.GetProperty("text").GetString())));

        var byBody = (await Send(tangentSpace.Client, HttpMethod.Get, Path("annealing"), json: true)).Json;
        Assert.Equal([AddressOf(root, reply)], Addresses(byBody));
        var body = byBody.GetProperty("hits")[0];
        Assert.Equal(("reply", "Copper lanterns"), (body.GetProperty("kind").GetString(), body.GetProperty("topic").GetString()));
        var runs = body.GetProperty("snippet").EnumerateArray().ToList();
        Assert.Equal("Annealing", Assert.Single(runs, run => run.GetProperty("marked").GetBoolean()).GetProperty("text").GetString());
        var words = string.Concat(runs.Select(run => run.GetProperty("text").GetString()));
        Assert.Equal("Annealing matters most, as @" + Ada + ".example says.", words);

        var page = await Send(tangentSpace.Client, HttpMethod.Get, Path("annealing"));
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains($"<a class=\"notification-link\" href=\"{AddressOf(root, reply)}\">Copper lanterns</a>", page.Body);
        Assert.Contains("<mark class=\"search-mark\">Annealing</mark> matters most", page.Body);
    }

    [Fact]
    public async Task The_word_the_text_ends_in_finds_its_post_by_its_start_and_only_that_word()
    {
        var (tangentSpace, writer) = await Open();
        await using var space = tangentSpace;
        var root = await Started(writer, Ada, "Copper lanterns", "How the glass is cut.");

        Assert.Equal([BaseUrl + TopicPath(root)], Addresses((await Send(tangentSpace.Client, HttpMethod.Get, Path("lant"), json: true)).Json));
        Assert.Empty(Addresses((await Send(tangentSpace.Client, HttpMethod.Get, Path("lant porcelain"), json: true)).Json));
    }

    [Fact]
    public async Task A_page_ends_where_the_next_begins_and_names_the_pages_either_side_and_the_last_names_no_next()
    {
        var (tangentSpace, writer) = await Open();
        await using var space = tangentSpace;
        var posts = new List<string>();
        for (var number = 0; number < SearchController.PageSize + 1; number++)
            posts.Add(BaseUrl + TopicPath(await Started(writer, Ada, "Lantern " + number, "A lantern.")));

        var first = (await Send(tangentSpace.Client, HttpMethod.Get, Path("lantern"), json: true)).Json;
        Assert.Equal(SearchController.PageSize, first.GetProperty("hits").GetArrayLength());
        Assert.Equal(BaseUrl + Path("lantern", 2), first.GetProperty("next").GetString());
        Assert.False(first.TryGetProperty("previous", out _));

        var second = (await Send(tangentSpace.Client, HttpMethod.Get, Path("lantern", 2), json: true)).Json;
        Assert.Equal(1, second.GetProperty("hits").GetArrayLength());
        Assert.False(second.TryGetProperty("next", out _));
        Assert.Equal(BaseUrl + Path("lantern"), second.GetProperty("previous").GetString());
        Assert.Equal(posts.Order(), Addresses(first).Concat(Addresses(second)).Order());
    }

    [Fact]
    public async Task A_full_page_with_nothing_after_it_names_no_next_page()
    {
        var (tangentSpace, writer) = await Open();
        await using var space = tangentSpace;
        for (var number = 0; number < SearchController.PageSize; number++) await Started(writer, Ada, "Lantern " + number, "A lantern.");

        var first = (await Send(tangentSpace.Client, HttpMethod.Get, Path("lantern"), json: true)).Json;

        Assert.Equal(SearchController.PageSize, first.GetProperty("hits").GetArrayLength());
        Assert.False(first.TryGetProperty("next", out _));
    }

    [Fact]
    public async Task A_post_in_a_private_tangent_is_never_found_by_a_reader_who_may_not_view_it_and_says_nothing_of_itself()
    {
        var (tangentSpace, writer) = await Open();
        await using var space = tangentSpace;
        var open = await Started(writer, Ada, "An open lantern", "It lights the way.");
        // More secrets than a page holds, each outranking the open Post, so a page judged after it was read would be empty.
        for (var number = 0; number < SearchController.PageSize + 2; number++)
            await Started(writer, Leo, "Lantern lantern " + number, "Lantern lantern, the secret passphrase.", Studio);

        foreach (var client in new[] { tangentSpace.Client, (await SignedIn(tangentSpace, Bea)).Client })
        {
            var read = await Send(client, HttpMethod.Get, Path("lantern"), json: true);
            Assert.Equal([BaseUrl + TopicPath(open)], Addresses(read.Json));
            Assert.False(read.Json.TryGetProperty("next", out _));
            Assert.DoesNotContain("passphrase", read.Body);

            // Words only the private Post holds answer exactly as words nobody holds.
            var held = await Send(client, HttpMethod.Get, Path("passphrase"));
            var none = await Send(client, HttpMethod.Get, Path("zzzzzzzz"));
            Assert.Equal(HttpStatusCode.OK, held.Status);
            Assert.Contains("Nothing you can read here holds those words.", held.Body);
            Assert.Contains("Nothing you can read here holds those words.", none.Body);
            Assert.Empty(Addresses((await Send(client, HttpMethod.Get, Path("passphrase"), json: true)).Json));
        }

        using var leo = await SignedIn(tangentSpace, Leo);
        var administrator = (await Send(leo.Client, HttpMethod.Get, Path("passphrase"), json: true)).Json;
        Assert.Equal(SearchController.PageSize, Addresses(administrator).Count);
        Assert.All(Addresses(administrator), address => Assert.StartsWith(BaseUrl + "/t/" + Studio + "/", address));
    }

    [Fact]
    public async Task A_removed_post_is_not_found_even_by_the_title_it_keeps()
    {
        var (tangentSpace, writer) = await Open();
        await using var space = tangentSpace;
        var root = await Started(writer, Ada, "Copper lanterns", "How the glass is cut.");
        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new RemovePost(root.Id, Ada, null), Cancellation));

        Assert.Empty(Addresses((await Send(tangentSpace.Client, HttpMethod.Get, Path("lanterns"), json: true)).Json));
    }

    [Fact]
    public async Task A_private_place_answers_a_visitor_the_sign_in()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Given.AnEstablishedTangentSpace(tangentSpace, "Field notes", Audience.Private, ct: Cancellation);

        var html = await Send(Agent(tangentSpace), HttpMethod.Get, Path("lantern"));
        Assert.Equal(HttpStatusCode.Unauthorized, html.Status);
        Assert.DoesNotContain("masthead-search", html.Body);
        Assert.Equal(Capabilities.SignIn, (await Send(Agent(tangentSpace), HttpMethod.Get, Path("lantern"), json: true)).Gate);
    }

    [Fact]
    public async Task A_banned_participant_finds_nothing_here_to_search()
    {
        var (tangentSpace, writer) = await Open();
        await using var space = tangentSpace;
        await Started(writer, Ada, "Copper lanterns", "How the glass is cut.");
        const string gone = "did:plc:gone4search";
        await Given.AParticipant(tangentSpace, gone, [], Participants.Standing.Banned, Cancellation);
        using var banned = await SignedIn(tangentSpace, gone);

        var read = await Send(banned.Client, HttpMethod.Get, Path("lanterns"), json: true);

        Assert.Equal(HttpStatusCode.NotFound, read.Status);
        Assert.DoesNotContain("lanterns", read.Body);
    }

    [Fact]
    public async Task Words_or_a_page_that_cannot_be_searched_say_why_and_no_words_ask_for_some()
    {
        var (tangentSpace, _) = await Open();
        await using var space = tangentSpace;

        var punctuation = await Send(tangentSpace.Client, HttpMethod.Get, Path("--"));
        Assert.Equal(HttpStatusCode.BadRequest, punctuation.Status);
        Assert.Contains(PostSearch.NeedsText, punctuation.Body);
        Assert.Equal(PostSearch.TextTooLong, (await Send(tangentSpace.Client, HttpMethod.Get, Path(new string('a', PostSearch.TextLimit + 1)), json: true)).Json.GetProperty("refused").GetString());
        foreach (var page in new[] { "0", "101", "x", "-1" })
        {
            var refused = await Send(tangentSpace.Client, HttpMethod.Get, Path("lantern") + "&page=" + page, json: true);
            Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
            Assert.Equal(PostSearch.NeedsPage, refused.Json.GetProperty("refused").GetString());
        }

        var none = await Send(tangentSpace.Client, HttpMethod.Get, "/search");
        Assert.Equal(HttpStatusCode.OK, none.Status);
        Assert.Contains("Type the words", none.Body);
    }

    [Fact]
    public async Task A_snippet_is_words_and_never_markup_whatever_a_body_holds()
    {
        var (tangentSpace, writer) = await Open();
        await using var space = tangentSpace;
        await Started(writer, Ada, "Tricks", "A lantern <script>alert(1)</script> &amp; <img src=x onerror=alert(2)> here.");

        var page = await Send(tangentSpace.Client, HttpMethod.Get, Path("lantern"));

        Assert.DoesNotContain("<script>alert", page.Body);
        Assert.DoesNotContain("<img src=x", page.Body);
    }

    [Fact]
    public async Task The_masthead_holds_a_box_that_is_a_plain_form_to_the_search_and_keeps_the_words_there()
    {
        var (tangentSpace, writer) = await Open();
        await using var space = tangentSpace;

        var door = await Send(tangentSpace.Client, HttpMethod.Get, "/");
        Assert.Contains($"<form class=\"masthead-search\" role=\"search\" method=\"get\" action=\"{BaseUrl}/search\" data-island=\"search\"", door.Body);
        Assert.Contains("name=\"q\"", door.Body);
        Assert.Contains("value=\"copper &amp; glass\"", (await Send(tangentSpace.Client, HttpMethod.Get, Path("copper & glass"))).Body);
        Assert.DoesNotContain("masthead-search", (await Send(tangentSpace.Client, HttpMethod.Get, "/sign-in")).Body);
    }

    [Fact]
    public async Task A_search_is_never_stored()
    {
        var (tangentSpace, _) = await Open();
        await using var space = tangentSpace;

        using var response = await tangentSpace.Client.GetAsync(Path("lantern"), Cancellation);

        Assert.True(response.Headers.CacheControl?.NoStore);
    }
}
