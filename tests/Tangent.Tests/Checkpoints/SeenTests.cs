using System.Net;
using Koan.Data.Core.Commands;
using Tangent.Posts;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Events;

/// <summary>Saying "seen" is one act at <c>/seen</c>: with a Topic it moves the reader's mark there to the sequence its
/// page showed, never backwards, while a page's <c>GET</c> moves nothing; the Tangent page and the door follow it; a
/// window opened past a gap offers no form; and an edit that mentions the reader in a Post they already read is still
/// news on the door.</summary>
public sealed class SeenTests
{
    [Fact]
    public async Task Seen_with_a_topic_moves_the_mark_to_the_sequence_shown_never_backwards_a_get_moves_nothing_and_a_program_with_a_session_moves_its_own()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var root = await Started(Ada, "Lanterns", "How do you hang yours?");
        for (var reply = 0; reply < 3; reply++) await Replied(root, Mo, "From a hook, number " + reply + ".");
        using var bea = await SignedIn(tangentSpace, Bea);

        var page = await Send(bea.Client, HttpMethod.Get, TopicPath(root), json: true);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        var seen = page.Json.GetProperty("seen");
        Assert.Equal((BaseUrl + SeenController.Path, "POST", "3", root.Id), (seen.GetProperty("action").GetString(), seen.GetProperty("method").GetString(),
            Valued(seen, SeenController.ThroughField), Valued(seen, SeenController.TopicField)));
        var html = (await Send(bea.Client, HttpMethod.Get, TopicPath(root))).Body;
        Assert.Contains("<form class=\"topic-seen seen\"", html);
        Assert.Contains("Mark seen", html);
        Assert.Null(await SeenThrough(Bea, root));

        var two = await Form(bea, TopicPath(root), SeenController.Path, Fields(root, "2"));
        Assert.Equal((HttpStatusCode.SeeOther, BaseUrl + TopicPath(root), (long?)2), (two.Status, two.Location, await SeenThrough(Bea, root)));
        var three = await Form(bea, TopicPath(root), SeenController.Path, Fields(root, "3"));
        Assert.Equal((HttpStatusCode.SeeOther, (long?)3), (three.Status, await SeenThrough(Bea, root)));
        var back = await Json(bea, TopicPath(root), SeenController.Path, new { topic = root.Id, through = 1 });
        Assert.Equal((HttpStatusCode.OK, 3L, (long?)3), (back.Status, back.Json.GetProperty("seq").GetInt64(), await SeenThrough(Bea, root)));
        var past = await Form(bea, TopicPath(root), SeenController.Path, Fields(root, "4"));
        Assert.Equal((HttpStatusCode.BadRequest, (long?)3), (past.Status, await SeenThrough(Bea, root)));
        var nowhere = await Json(bea, TopicPath(root), SeenController.Path, new { topic = Tids.Mint(), through = 1 });
        Assert.Equal(HttpStatusCode.NotFound, nowhere.Status);

        var read = await Send(bea.Client, HttpMethod.Get, TopicPath(root), json: true);
        Assert.False(read.Json.TryGetProperty("seen", out _));
        Assert.Equal((long?)3, await SeenThrough(Bea, root));
        // Ada started the Topic, so her mark stands on its root.
        Assert.Equal((long?)0, await SeenThrough(Ada, root));

        using var ada = await SignedIn(tangentSpace, Ada);
        var sent = await Send(ada.Client, HttpMethod.Post, SeenController.Path, JsonBody(new { topic = root.Id, through = 2 }), json: true);
        Assert.Equal((HttpStatusCode.OK, "topic:" + root.Id, 2L), (sent.Status, sent.Json.GetProperty("scope").GetString(),
            sent.Json.GetProperty("seq").GetInt64()));
        Assert.Equal(((long?)2, (long?)3), (await SeenThrough(Ada, root), await SeenThrough(Bea, root)));

        var visitor = await Send(Agent(tangentSpace), HttpMethod.Post, SeenController.Path, JsonBody(new { topic = root.Id, through = 2 }), json: true);
        Assert.Equal((HttpStatusCode.Unauthorized, Access.Capabilities.SignIn.Word), (visitor.Status, visitor.Word));
    }

    [Fact]
    public async Task After_seen_the_tangent_page_no_longer_marks_the_topic_new_and_the_door_no_longer_lists_it_until_a_later_reply()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var root = await Started(Ada, "Lanterns", "How do you hang yours?");
        await Replied(root, Mo, "From a hook.");
        using var bea = await SignedIn(tangentSpace, Bea);

        Assert.True(await NewOnTangentPage(bea, root));
        Assert.Equal(2, await NewOnDoor(bea));

        var seen = await Form(bea, TopicPath(root), SeenController.Path, Fields(root, "1"));
        Assert.Equal(HttpStatusCode.SeeOther, seen.Status);

        Assert.False(await NewOnTangentPage(bea, root));
        Assert.Equal(0, await NewOnDoor(bea));
        Assert.True((await Send(bea.Client, HttpMethod.Get, "/", json: true)).Json.GetProperty("news").GetProperty("empty").GetBoolean());

        await Replied(root, Mo, "Or a nail, at a pinch.");

        Assert.True(await NewOnTangentPage(bea, root));
        Assert.Equal(1, await NewOnDoor(bea));
    }

    [Fact]
    public async Task A_window_opened_past_a_gap_offers_no_seen_form_and_reading_from_the_first_unread_to_the_end_moves_the_mark_to_the_last_reply()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var root = await Started(Ada, "Lanterns", "How do you hang yours?");
        var replies = new List<Post>();
        for (var reply = 1; reply <= 60; reply++) replies.Add(await Replied(root, Mo, "Reply number " + reply + "."));
        using var bea = await SignedIn(tangentSpace, Bea);
        await Form(bea, TopicPath(root), SeenController.Path, Fields(root, "10"));
        Assert.Equal((long?)10, await SeenThrough(Bea, root));

        var anchored = await Send(bea.Client, HttpMethod.Get, PostPath(root, replies[49]), json: true);
        Assert.Equal(HttpStatusCode.OK, anchored.Status);
        Assert.True(anchored.Json.GetProperty("from").GetInt64() > 11, "The window around the fiftieth reply leaves a gap after the mark.");
        Assert.False(anchored.Json.TryGetProperty("seen", out _));
        Assert.DoesNotContain("Mark seen", (await Send(bea.Client, HttpMethod.Get, PostPath(root, replies[49]))).Body);
        Assert.Equal((long?)10, await SeenThrough(Bea, root));

        if (anchored.Json.TryGetProperty("newer", out var newer))
            Assert.False((await Send(bea.Client, HttpMethod.Get, newer.GetString()![BaseUrl.Length..], json: true)).Json.TryGetProperty("seen", out _));

        for (var turn = 0; turn < 10 && await SeenThrough(Bea, root) < 60; turn++)
        {
            var page = (await Send(bea.Client, HttpMethod.Get, TopicPath(root), json: true)).Json;
            var seen = page.GetProperty("seen");
            Assert.Equal((await SeenThrough(Bea, root))!.Value + 1, page.GetProperty("from").GetInt64());
            var posted = await Form(bea, TopicPath(root), SeenController.Path, Fields(root, Valued(seen, SeenController.ThroughField)!));
            Assert.Equal(HttpStatusCode.SeeOther, posted.Status);
        }

        Assert.Equal((long?)60, await SeenThrough(Bea, root));
    }

    [Fact]
    public async Task A_mention_an_edit_writes_in_a_post_the_reader_already_read_is_listed_on_the_door()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var root = await Started(Ada, "Lanterns", "How do you hang yours?");
        var old = await Replied(root, Mo, "From a hook.");
        using var bea = await SignedIn(tangentSpace, Bea);
        await Form(bea, TopicPath(root), SeenController.Path, Fields(root, "1"));
        Assert.True((await Send(bea.Client, HttpMethod.Get, "/", json: true)).Json.GetProperty("news").GetProperty("empty").GetBoolean());

        Assert.IsType<Outcome<Post>.Done>(await new EditPost(old.Id, 1, PostBody.Of($"From a hook, [@bea](/p/{Bea})."), null, Mo).Send(Cancellation));

        var mentions = (await Send(bea.Client, HttpMethod.Get, "/", json: true)).Json.GetProperty("news").GetProperty("mentions");
        Assert.Equal((1, old.Id), (mentions.GetProperty("count").GetInt32(), mentions.GetProperty("posts")[0].GetProperty("postId").GetString()));
        Assert.Equal((long?)1, await SeenThrough(Bea, root));
    }

    [Fact]
    public async Task An_authors_own_write_moves_their_mark_only_where_they_had_read_to_the_end_so_it_never_passes_what_they_had_not_read()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var root = await Started(Ada, "Lanterns", "How do you hang yours?");
        Assert.Equal((long?)0, await SeenThrough(Ada, root));

        var own = await Replied(root, Ada, "Mine hangs from the beam.");
        Assert.Equal((long?)own.Sequence, await SeenThrough(Ada, root));

        var others = await Replied(root, Mo, "From a hook.");
        var after = await Replied(root, Ada, "A hook, then.");
        Assert.Equal((long?)own.Sequence, await SeenThrough(Ada, root));
        Assert.True(after.Sequence > others.Sequence);

        // The reply she has not read stays new to her, her own never is.
        using var ada = await SignedIn(tangentSpace, Ada);
        var page = (await Send(ada.Client, HttpMethod.Get, PostPath(root, root), json: true)).Json;
        var marked = page.GetProperty("replies").EnumerateArray().ToDictionary(post => post.GetProperty("id").GetString()!, post => post.GetProperty("new").GetBoolean());
        Assert.Equal((false, true, false), (marked[own.Id], marked[others.Id], marked[after.Id]));
    }

    [Fact]
    public async Task A_reply_in_a_topic_the_author_never_said_seen_on_sets_their_mark_so_their_own_post_is_new_nowhere()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var root = await Started(Ada, "Lanterns", "How do you hang yours?");
        await Replied(root, Mo, "From a hook.");
        using var bea = await SignedIn(tangentSpace, Bea);
        Assert.Null(await SeenThrough(Bea, root));

        var mine = await Replied(root, Bea, "From the beam.");

        Assert.Equal((long?)mine.Sequence, await SeenThrough(Bea, root));
        Assert.False(await NewOnTangentPage(bea, root));
        Assert.Equal(0, await NewOnDoor(bea));
    }

    [Fact]
    public async Task Seen_on_a_topic_the_sender_may_not_view_answers_as_for_no_topic_and_writes_nothing()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Keeper, [Access.Roles.Administrator], ct: Cancellation);
        var secret = await Started(Keeper, "Studio notes", "Only for us.", Studio);
        using var bea = await SignedIn(tangentSpace, Bea);

        var asJson = await Json(bea, "/", SeenController.Path, new { topic = secret.Id, through = 0 });
        var asForm = await Form(bea, "/", SeenController.Path, Fields(secret, "0"));

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (asJson.Status, asForm.Status));
        Assert.Null(await SeenThrough(Bea, secret));
        Assert.Equal(new Outcome<Checkpoint>.Refused(Seen.NoTopic), await new Seen(Bea, 0, secret.Id).Send(Cancellation));
        Assert.Null(await SeenThrough(Bea, secret));
    }

    private static readonly string Keeper = Persona.Id("keeper4seen");

    private static Dictionary<string, string> Fields(Post root, string through)
        => new() { [SeenController.TopicField] = root.Id, [SeenController.ThroughField] = through };

    /// <summary>The sequence of a participant's mark on a Topic; null where they have none.</summary>
    private static async Task<long?> SeenThrough(string participantId, Post root)
        => (await Checkpoint.Get(Checkpoint.Key(participantId, Scope.Topic(root.Id)), Cancellation))?.Seq;

    /// <summary>Whether the Tangent page marks the Topic new to the reader.</summary>
    private static async Task<bool> NewOnTangentPage(Participants.Browser browser, Post root)
    {
        var topics = (await Send(browser.Client, HttpMethod.Get, "/t/" + root.TangentId, json: true)).Json.GetProperty("topics");
        return topics.EnumerateArray().Single(topic => topic.GetProperty("id").GetString() == root.Id).GetProperty("new").GetBoolean();
    }

    /// <summary>How many Posts the door lists as new in the workshop.</summary>
    private static async Task<int> NewOnDoor(Participants.Browser browser)
    {
        var perTangent = (await Send(browser.Client, HttpMethod.Get, "/", json: true)).Json.GetProperty("news").GetProperty("perTangent");
        return perTangent.EnumerateArray().Where(tangent => tangent.GetProperty("slug").GetString() == Workshop)
            .Sum(tangent => tangent.GetProperty("topics").GetInt32() + tangent.GetProperty("replies").GetInt32());
    }
}
