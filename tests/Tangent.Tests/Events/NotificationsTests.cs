using System.Net;
using System.Text.Json;
using Koan.Data.Core;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Offers;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Space;
using Tangent.Writer;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Events;

/// <summary>Notifications both ways: the count every page's frame shows holds only what targets the reader, that
/// someone else did and that they may view; it reads past the checkpoint opening them moves, a stream past it carries
/// what raises it, and past its bound it reads "at least". The page lists them newest first a page at a time from a
/// sealed cursor, never one in a Tangent the reader may not view, as a page and as JSON; an Offer among them keeps its
/// answers while open, stays open when merely seen, is decided once from there and then says how.</summary>
public sealed class NotificationsTests
{
    private const string Leo = "did:plc:leo4notifications";

    [Fact]
    public async Task The_count_holds_only_events_that_target_the_reader_that_someone_else_did_and_that_they_may_view()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        var mine = await Started(writer, Ada, "Lanterns", "Who lights theirs first?");
        await Replied(writer, mine, Bea, "I do, at six.");
        await Started(writer, Bea, "A question for Ada", $"Over to [@ada](at://{Ada}).");
        await Replied(writer, mine, Ada, "And mine at seven.");
        var secret = await Started(writer, Leo, "Studio notes", "Only for us.", Studio);
        Assert.IsType<Outcome<Event>.Done>(await writer.Send(new Logged(EventKind.Mention, Leo, Ada, secret), Cancellation));
        Assert.IsType<Outcome<Event>.Done>(await writer.Send(new Logged(EventKind.StandingChanged, Ada, Ada), Cancellation));
        Assert.IsType<Outcome<Event>.Done>(await writer.Send(new Logged(EventKind.Mention, Ada, Bea, mine), Cancellation));

        using var ada = await SignedIn(tangentSpace, Ada);
        var door = (await Send(ada.Client, HttpMethod.Get, "/", json: true)).Json;
        var unseen = door.GetProperty("who").GetProperty("notifications");
        Assert.Equal((2, false, BaseUrl + Notifications.Path),
            (unseen.GetProperty("count").GetInt32(), unseen.GetProperty("atLeast").GetBoolean(), unseen.GetProperty("address").GetString()));

        // Every page's frame shows it, linked, and every signed-in page carries it both ways.
        foreach (var path in new[] { "/", TopicPath(mine), "/t/" + Workshop, "/t", "/p/ada4writes.example" })
        {
            var page = await Send(ada.Client, HttpMethod.Get, path);
            Assert.Equal(HttpStatusCode.OK, page.Status);
            Assert.Contains($"<a class=\"masthead-unseen\" href=\"{BaseUrl}/notifications\" aria-label=\"2 new notifications\"", page.Body);
            Assert.Contains("<span class=\"masthead-unseen-count\">2</span>", page.Body);
            Assert.Contains($"<a class=\"account-item\" href=\"{BaseUrl}/notifications\">", page.Body);
        }

        // Someone with nothing for them reads no count, and the way to their notifications stays in the account menu.
        using var mo = await SignedIn(tangentSpace, Mo);
        var quiet = await Send(mo.Client, HttpMethod.Get, "/");
        Assert.Contains($"<a class=\"masthead-unseen\" href=\"{BaseUrl}/notifications\" hidden aria-label=\"0 new notifications\"", quiet.Body);
        Assert.Contains($"<a class=\"account-item\" href=\"{BaseUrl}/notifications\">", quiet.Body);
        using var bea = await SignedIn(tangentSpace, Bea);
        Assert.Equal(1, Count(await Send(bea.Client, HttpMethod.Get, "/", json: true)));
        var visitor = await Send(Agent(tangentSpace), HttpMethod.Get, "/");
        Assert.DoesNotContain("masthead-unseen", visitor.Body);
    }

    [Fact]
    public async Task Opening_the_notifications_moves_their_checkpoint_through_what_they_showed_and_the_count_reads_past_it()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        var mine = await Started(writer, Ada, "Lanterns", "Who lights theirs first?");
        await Replied(writer, mine, Bea, "I do, at six.");
        var newest = await Replied(writer, mine, Mo, "At dusk, always.");
        using var ada = await SignedIn(tangentSpace, Ada);
        var door = (await Send(ada.Client, HttpMethod.Get, "/", json: true)).Json;
        Assert.Equal(2, door.GetProperty("who").GetProperty("notifications").GetProperty("count").GetInt32());
        // The door names them with how many while there are some, though it lists both replies itself.
        Assert.Equal(2, door.GetProperty("news").GetProperty("replies").GetProperty("count").GetInt32());
        Assert.Equal((2, BaseUrl + Notifications.Path), (door.GetProperty("news").GetProperty("notifications").GetProperty("count").GetInt32(),
            door.GetProperty("news").GetProperty("notifications").GetProperty("address").GetString()));
        Assert.Contains($"<a class=\"watch-button door-notifications\" href=\"{BaseUrl}/notifications\">2 new notifications</a>",
            (await Send(ada.Client, HttpMethod.Get, "/")).Body);

        var opened = await Send(ada.Client, HttpMethod.Get, Notifications.Path);
        Assert.Equal(HttpStatusCode.OK, opened.Status);
        // The page's own frame already reads past what it showed.
        Assert.Contains($"<a class=\"masthead-unseen\" href=\"{BaseUrl}/notifications\" hidden", opened.Body);
        Assert.Contains("no-store", await CacheControl(ada.Client, Notifications.Path));
        await Reached.Settled(tangentSpace, Cancellation);
        var through = (await Created(newest)).Seq;
        Assert.Equal(through, (await Checkpoint.Get(Checkpoint.Key(Ada, Scope.Notifications), Cancellation))!.Seq);
        Assert.Null(await Checkpoint.Get(Checkpoint.Key(Ada, Scope.Place), Cancellation));

        // The count drops on every page, the masthead shows nothing, and the door, which still lists the replies past its
        // own checkpoint, names no notifications.
        door = (await Send(ada.Client, HttpMethod.Get, "/", json: true)).Json;
        Assert.Equal(0, door.GetProperty("who").GetProperty("notifications").GetProperty("count").GetInt32());
        Assert.Equal(2, door.GetProperty("news").GetProperty("replies").GetProperty("count").GetInt32());
        Assert.False(door.GetProperty("news").TryGetProperty("notifications", out _));
        Assert.Contains($"<a class=\"masthead-unseen\" href=\"{BaseUrl}/notifications\" hidden", (await Send(ada.Client, HttpMethod.Get, "/")).Body);

        // A new reply counts past it, and opening them again moves it on, never back.
        await Replied(writer, mine, Bea, "Then the harbour.");
        Assert.Equal(1, Count(await Send(ada.Client, HttpMethod.Get, "/t/" + Workshop, json: true)));
        var json = (await Send(ada.Client, HttpMethod.Get, Notifications.Path, json: true)).Json;
        Assert.Equal((through, through + 1), (json.GetProperty("from").GetInt64(), json.GetProperty("through").GetInt64()));
        Assert.Equal(new[] { true, false, false }, json.GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("new").GetBoolean()));
        await Reached.Settled(tangentSpace, Cancellation);
        Assert.Equal(through + 1, (await Checkpoint.Get(Checkpoint.Key(Ada, Scope.Notifications), Cancellation))!.Seq);
        Assert.Equal(through + 1, Assert.IsType<Outcome<Checkpoint>.Done>(await writer.Send(new SeeNotifications(Ada, through), Cancellation)).Result.Seq);
        Assert.Equal(0, Count(await Send(ada.Client, HttpMethod.Get, "/", json: true)));

        // Only a notification of the reader's moves it.
        Assert.Equal(new Outcome<Checkpoint>.Refused(SeeNotifications.NoNotification),
            await writer.Send(new SeeNotifications(Bea, through + 1), Cancellation));
    }

    [Fact]
    public async Task A_reply_committed_after_a_page_reaches_its_stream_past_what_the_count_read_as_a_notification_for_the_reader()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        var mine = await Started(writer, Ada, "Lanterns", "Who lights theirs first?");
        await Replied(writer, mine, Bea, "I do, at six.");
        using var ada = await SignedIn(tangentSpace, Ada);
        var page = (await Send(ada.Client, HttpMethod.Get, TopicPath(mine), json: true)).Json;
        var unseen = page.GetProperty("who").GetProperty("notifications");
        var counted = unseen.GetProperty("through").GetInt64();
        Assert.Equal(1, unseen.GetProperty("count").GetInt32());

        await using var stream = await Streamed.Open(ada.Client, EventsController.Path + "?since=" + counted);
        Assert.Equal("open", (await stream.Any()).Comment);
        var reply = await Replied(writer, mine, Mo, "At dusk, always.");
        var frame = await stream.Next();

        // The masthead's island adds one for this row (src/client/islands/unseen.ts): past what the count read, for the
        // reader, by someone else; and the count the next page reads agrees.
        Assert.True(frame.Seq > counted);
        Assert.Equal((reply.Id, Ada, Mo), (frame.Json.GetProperty("post").GetString(), frame.Json.GetProperty("target").GetString(),
            frame.Json.GetProperty("actor").GetString()));
        Assert.Equal(2, Count(await Send(ada.Client, HttpMethod.Get, "/", json: true)));
    }

    [Fact]
    public async Task Past_its_bound_the_count_reads_at_least()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        var theirs = await Started(writer, Bea, "Lanterns", "Who lights theirs first?");
        Assert.IsType<Outcome<int>.Done>(await writer.Send(new Many(EventKind.Mention, Bea, Ada, theirs, Notifications.CountLimit + 5), Cancellation));
        using var ada = await SignedIn(tangentSpace, Ada);

        var unseen = (await Send(ada.Client, HttpMethod.Get, "/", json: true)).Json.GetProperty("who").GetProperty("notifications");
        Assert.True(unseen.GetProperty("atLeast").GetBoolean());
        Assert.Equal(Notifications.CountLimit + 1, unseen.GetProperty("count").GetInt32());
        Assert.Equal((await Event.All(Cancellation)).Max(e => e.Seq), unseen.GetProperty("through").GetInt64());
        var html = WebUtility.HtmlDecode((await Send(ada.Client, HttpMethod.Get, "/")).Body);
        Assert.Contains("<span class=\"masthead-unseen-count\">99+</span>", html);
        Assert.Contains($"aria-label=\"At least {Notifications.CountLimit + 1} new notifications\"", html);

        // The door names them in the same words.
        var news = (await Send(ada.Client, HttpMethod.Get, "/", json: true)).Json.GetProperty("news");
        Assert.True(news.GetProperty("notifications").GetProperty("atLeast").GetBoolean());
        Assert.Contains($"<a class=\"watch-button door-notifications\" href=\"{BaseUrl}/notifications\">At least {Notifications.CountLimit + 1} new notifications</a>", html);
    }

    [Fact]
    public async Task The_page_lists_them_newest_first_a_page_at_a_time_from_a_sealed_cursor_and_refuses_a_forged_one()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        var mine = await Started(writer, Ada, "Lanterns", "Who lights theirs first?");
        var replies = new List<Post>();
        for (var index = 0; index < Notifications.PageSize + 5; index++) replies.Add(await Replied(writer, mine, Bea, $"Reply number {index}."));
        using var ada = await SignedIn(tangentSpace, Ada);

        var first = (await Send(ada.Client, HttpMethod.Get, Notifications.Path, json: true)).Json;
        var shown = Posts(first);
        Assert.Equal(replies.AsEnumerable().Reverse().Take(Notifications.PageSize).Select(reply => AddressOf(mine, reply)), shown);
        Assert.False(first.GetProperty("continued").GetBoolean());
        var next = first.GetProperty("next").GetString()!;
        Assert.StartsWith(BaseUrl + Notifications.Path + "?after=", next);

        var second = (await Send(ada.Client, HttpMethod.Get, next[BaseUrl.Length..], json: true)).Json;
        Assert.Equal(replies.Take(5).Reverse().Select(reply => AddressOf(mine, reply)), Posts(second));
        Assert.True(second.GetProperty("continued").GetBoolean());
        Assert.False(second.TryGetProperty("next", out _));
        // What was new when the reader opened them stays new on the pages after, and the checkpoint stays where the first
        // page put it.
        Assert.All(second.GetProperty("entries").EnumerateArray(), entry => Assert.True(entry.GetProperty("new").GetBoolean()));
        await Reached.Settled(tangentSpace, Cancellation);
        Assert.Equal((await Created(replies[^1])).Seq, (await Checkpoint.Get(Checkpoint.Key(Ada, Scope.Notifications), Cancellation))!.Seq);
        Assert.Contains("Newest notifications", (await Send(ada.Client, HttpMethod.Get, next[BaseUrl.Length..])).Body);

        // A cursor this server did not seal, or one altered, is refused.
        var cursor = Uri.UnescapeDataString(next[(next.IndexOf("after=", StringComparison.Ordinal) + "after=".Length)..]);
        var altered = cursor[..^2] + (cursor[^2] == 'A' ? "B" : "A") + cursor[^1];
        foreach (var forged in new[] { "12", "forged", altered })
        {
            var refused = await Send(ada.Client, HttpMethod.Get, Notifications.Path + "?after=" + Uri.EscapeDataString(forged), json: true);
            Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
            Assert.Equal("Not a page of your notifications", refused.Title);
        }

        // A visitor is asked to sign in.
        var visitor = await Send(Agent(tangentSpace), HttpMethod.Get, Notifications.Path, json: true);
        Assert.Equal((HttpStatusCode.Unauthorized, Capabilities.SignIn), (visitor.Status, visitor.Gate));
    }

    [Fact]
    public async Task Each_says_what_happened_and_links_to_what_it_is_about_both_ways_and_one_in_a_tangent_the_reader_may_not_view_is_absent()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        var mine = await Started(writer, Ada, "Lanterns", "Who lights theirs first?");
        var reply = await Replied(writer, mine, Bea, "I do, at six.");
        var asked = await Started(writer, Bea, "A question for Ada", $"Over to [@ada](at://{Ada}).");
        var taken = Assert.IsType<Outcome<Post>.Done>(await writer.Send(
            new StartTopic(Tids.Mint(), Workshop, "Six o'clock lanterns", "", Mo, OriginId: mine.Id), Cancellation)).Result;
        Assert.IsType<Outcome<Participant>.Done>(await writer.Send(new ChangeRole(Leo, Ada, Roles.Moderator, Give: true), Cancellation));
        var secret = await Started(writer, Leo, "Studio notes", "Only for us.", Studio);
        Assert.IsType<Outcome<Event>.Done>(await writer.Send(new Logged(EventKind.Mention, Leo, Ada, secret), Cancellation));
        // Ada is a moderator now, which the studio does not open to; Leo reads it.
        using var ada = await SignedIn(tangentSpace, Ada);

        var json = (await Send(ada.Client, HttpMethod.Get, Notifications.Path, json: true)).Json;
        var entries = json.GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal(new[] { "role", "taken", "mention", "reply" }, entries.Select(entry => entry.GetProperty("kind").GetString()));
        Assert.All(entries, entry => Assert.True(entry.GetProperty("new").GetBoolean()));
        var role = entries[0];
        Assert.Equal((Leo, "moderator", true, BaseUrl + "/p/ada4writes.example"),
            (role.GetProperty("who").GetProperty("id").GetString(), role.GetProperty("role").GetString(), role.GetProperty("given").GetBoolean(),
                role.GetProperty("address").GetString()));
        Assert.Equal((Mo, BaseUrl + TopicPath(taken), "Six o'clock lanterns"),
            (entries[1].GetProperty("who").GetProperty("id").GetString(), entries[1].GetProperty("address").GetString(), entries[1].GetProperty("topic").GetString()));
        Assert.Equal((Bea, BaseUrl + TopicPath(asked), "A question for Ada"),
            (entries[2].GetProperty("who").GetProperty("id").GetString(), entries[2].GetProperty("address").GetString(), entries[2].GetProperty("topic").GetString()));
        Assert.Equal((Bea, AddressOf(mine, reply), "Lanterns", Workshop, "I do, at six.", false),
            (entries[3].GetProperty("who").GetProperty("id").GetString(), entries[3].GetProperty("address").GetString(), entries[3].GetProperty("topic").GetString(),
                entries[3].GetProperty("tangent").GetString(), entries[3].GetProperty("line").GetString(), entries[3].GetProperty("removed").GetBoolean()));
        var text = json.GetRawText();
        Assert.DoesNotContain("Studio notes", text);
        Assert.DoesNotContain(secret.Id, text);
        Assert.DoesNotContain(Studio, text);

        var html = WebUtility.HtmlDecode((await Send(ada.Client, HttpMethod.Get, Notifications.Path)).Body);
        Assert.Contains($"<strong>{Name(Leo)}</strong> <a class=\"notification-link\" href=\"{BaseUrl}/p/ada4writes.example\">made you a moderator.</a>", html);
        Assert.Contains($"<strong>{Name(Mo)}</strong> took your post on a tangent: <a class=\"notification-link\" href=\"{BaseUrl}{TopicPath(taken)}\">Six o'clock lanterns</a>", html);
        Assert.Contains($"<strong>{Name(Bea)}</strong> mentioned you in <a class=\"notification-link\" href=\"{BaseUrl}{TopicPath(asked)}\">A question for Ada</a>", html);
        Assert.Contains($"<strong>{Name(Bea)}</strong> replied to you in <a class=\"notification-link\" href=\"{AddressOf(mine, reply)}\">Lanterns</a>", html);
        Assert.Contains("<p class=\"notification-opening\">I do, at six.</p>", html);
        Assert.DoesNotContain("Studio notes", html);
        Assert.DoesNotContain("<dialog", html);
    }

    [Fact]
    public async Task An_offer_merely_seen_stays_open_answered_from_the_notifications_is_decided_once_and_then_says_how()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Reached.Unconfigured(), ct: Cancellation);
        await Reached.APlace(tangentSpace, Leo, address: null, Cancellation);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        await Reached.On(tangentSpace, "https://tangent.example.org", Cancellation, times: 3);
        await Reached.Settled(tangentSpace, Cancellation);
        var offer = Assert.Single(await Offer.All(Cancellation));
        var made = Assert.Single(await Event.All(Cancellation), e => e.Kind == EventKind.OfferMade);
        var loopback = tangentSpace.Client.BaseAddress!.GetLeftPart(UriPartial.Authority);
        using var leo = new Browser(tangentSpace);
        (await leo.SignIn("leo", Leo, Cancellation, handle: "leo.example")).Dispose();

        // Seen, both ways: its answers are there, the checkpoint moves past it, and it stays open.
        var json = (await Send(leo.Client, HttpMethod.Get, Notifications.Path, json: true)).Json;
        var entry = Assert.Single(json.GetProperty("entries").EnumerateArray());
        var offered = entry.GetProperty("offer");
        Assert.Equal(("offer", offer.Id, "address", "https://tangent.example.org", "open", $"{loopback}/offers/use", $"{loopback}/offers/decline"),
            (entry.GetProperty("kind").GetString(), offered.GetProperty("id").GetString(), offered.GetProperty("kind").GetString(),
                offered.GetProperty("subject").GetString(), offered.GetProperty("state").GetString(), offered.GetProperty("use").GetProperty("action").GetString(),
                offered.GetProperty("decline").GetProperty("action").GetString()));
        Assert.Equal((offer.Id, offer.Id), (Valued(offered.GetProperty("use"), OfferController.Field), Valued(offered.GetProperty("decline"), OfferController.Field)));
        Assert.NotEmpty(offered.GetProperty("use").GetProperty("token").GetProperty("value").GetString()!);
        await Reached.Settled(tangentSpace, Cancellation);
        Assert.Equal(made.Seq, (await Checkpoint.Get(Checkpoint.Key(Leo, Scope.Notifications), Cancellation))!.Seq);
        Assert.Equal(OfferState.Open, (await Offer.Get(offer.Id, Cancellation))!.State);
        var html = WebUtility.HtmlDecode((await Send(leo.Client, HttpMethod.Get, Notifications.Path)).Body);
        Assert.Contains("This place is being reached at <strong>https://tangent.example.org</strong>. Use it as its address?", html);
        Assert.Contains($"<form method=\"post\" action=\"{loopback}/offers/decline\">", html);
        Assert.Contains($"<input type=\"hidden\" name=\"offer\" value=\"{offer.Id}\" />", html);
        Assert.Equal(OfferState.Open, (await Offer.Get(offer.Id, Cancellation))!.State);

        // Answered from there, once: a second answer changes nothing, and the other one is refused.
        var token = await leo.Token(Notifications.Path, Cancellation);
        var declined = await Answer(leo, OfferController.DeclinePath, offer.Id, token);
        Assert.Equal((HttpStatusCode.SeeOther, "/"), (declined.Status, declined.Location));
        var decidedAt = (await Offer.Get(offer.Id, Cancellation))!.DecidedAt;
        Assert.Equal(OfferState.Declined, (await Offer.Get(offer.Id, Cancellation))!.State);
        Assert.Equal(HttpStatusCode.SeeOther, (await Answer(leo, OfferController.DeclinePath, offer.Id, token)).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await Answer(leo, OfferController.UsePath, offer.Id, token)).Status);
        Assert.Equal((OfferState.Declined, decidedAt), ((await Offer.Get(offer.Id, Cancellation))!.State, (await Offer.Get(offer.Id, Cancellation))!.DecidedAt));

        // Decided, it says how, with no answers.
        json = (await Send(leo.Client, HttpMethod.Get, Notifications.Path, json: true)).Json;
        offered = Assert.Single(json.GetProperty("entries").EnumerateArray()).GetProperty("offer");
        Assert.Equal("declined", offered.GetProperty("state").GetString());
        Assert.False(offered.TryGetProperty("use", out _));
        Assert.False(offered.TryGetProperty("decline", out _));
        html = WebUtility.HtmlDecode((await Send(leo.Client, HttpMethod.Get, Notifications.Path)).Body);
        Assert.Contains($"You declined <a class=\"notification-link\" href=\"{loopback}/first-run\">https://tangent.example.org</a> as this place’s address.", html);
        Assert.DoesNotContain("/offers/use", html);
    }

    [Fact]
    public async Task Rows_the_reader_may_not_view_move_neither_the_count_nor_where_it_reads_through_and_a_newest_page_of_them_offers_the_older_one()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        var mine = await Started(writer, Ada, "Lanterns", "Who lights theirs first?");
        var visible = await Replied(writer, mine, Bea, "I do, at six.");
        var secret = await Started(writer, Leo, "Studio notes", "Only for us.", Studio);
        using var ada = await SignedIn(tangentSpace, Ada);
        var shownThrough = (await Created(visible)).Seq;
        Assert.Equal((1, false, shownThrough), Unseen(await Send(ada.Client, HttpMethod.Get, "/", json: true)));

        // Replies to Ada in the studio, which she may not view, move neither the count nor where it reads through.
        Assert.IsType<Outcome<int>.Done>(await writer.Send(new Many(EventKind.PostCreated, Leo, Ada, secret, 3), Cancellation));
        Assert.Equal((1, false, shownThrough), Unseen(await Send(ada.Client, HttpMethod.Get, TopicPath(mine), json: true)));

        // Ninety-nine of them and one she may see fill the read: the count is that one, not a lower bound.
        Assert.IsType<Outcome<int>.Done>(await writer.Send(new Many(EventKind.PostCreated, Leo, Ada, secret, Notifications.CountLimit - 3), Cancellation));
        Assert.Equal((1, false, shownThrough), Unseen(await Send(ada.Client, HttpMethod.Get, "/", json: true)));
        Assert.Contains("<span class=\"masthead-unseen-count\">1</span>", WebUtility.HtmlDecode((await Send(ada.Client, HttpMethod.Get, "/")).Body));

        // A hundred of them after the newest she may see: the count finds none and says nothing of them, and the door
        // names nothing.
        Assert.IsType<Outcome<int>.Done>(await writer.Send(new Many(EventKind.PostCreated, Leo, Ada, secret, 1), Cancellation));
        var door = (await Send(ada.Client, HttpMethod.Get, "/", json: true)).Json;
        Assert.Equal((0, false, 0L), Unseen(door));
        Assert.False(door.GetProperty("news").TryGetProperty("notifications", out _));

        // The newest page holds none she may see: it says so, offers the older page and marks nothing; the older page
        // shows her reply, and marks it seen.
        var newest = await Send(ada.Client, HttpMethod.Get, Notifications.Path, json: true);
        Assert.Empty(newest.Json.GetProperty("entries").EnumerateArray());
        var next = newest.Json.GetProperty("next").GetString()!;
        Assert.Equal((0L, 0L), (newest.Json.GetProperty("from").GetInt64(), newest.Json.GetProperty("through").GetInt64()));
        var html = WebUtility.HtmlDecode((await Send(ada.Client, HttpMethod.Get, Notifications.Path)).Body);
        Assert.Contains("Nothing to show among the newest; older notifications follow.", html);
        Assert.DoesNotContain("Nothing for you yet", html);
        Assert.Contains("Older notifications →", html);
        await Reached.Settled(tangentSpace, Cancellation);
        Assert.Null(await Checkpoint.Get(Checkpoint.Key(Ada, Scope.Notifications), Cancellation));

        var older = (await Send(ada.Client, HttpMethod.Get, next[BaseUrl.Length..], json: true)).Json;
        Assert.Equal([AddressOf(mine, visible)], Posts(older));
        Assert.Equal(shownThrough, older.GetProperty("through").GetInt64());
        await Reached.Settled(tangentSpace, Cancellation);
        Assert.Equal(shownThrough, (await Checkpoint.Get(Checkpoint.Key(Ada, Scope.Notifications), Cancellation))!.Seq);
        Assert.DoesNotContain("Studio notes", newest.Body + older.GetRawText());
    }

    [Fact]
    public async Task A_delegate_reads_its_persons_notifications_and_leaves_them_unseen_and_the_persons_own_opening_sees_them()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        var mine = await Started(writer, Ada, "Lanterns", "Who lights theirs first?");
        var reply = await Replied(writer, mine, Bea, "I do, at six.");
        using var ada = await SignedIn(tangentSpace, Ada);
        var bearer = await Delegate(tangentSpace, ada);
        using var agent = Agent(tangentSpace);

        var read = await Send(agent, HttpMethod.Get, Notifications.Path, json: true, bearer: bearer);
        Assert.Equal(HttpStatusCode.OK, read.Status);
        Assert.Equal([AddressOf(mine, reply)], Posts(read.Json));
        Assert.Equal((0L, 0L, 1), (read.Json.GetProperty("from").GetInt64(), read.Json.GetProperty("through").GetInt64(),
            read.Json.GetProperty("who").GetProperty("notifications").GetProperty("count").GetInt32()));
        await Reached.Settled(tangentSpace, Cancellation);
        Assert.Null(await Checkpoint.Get(Checkpoint.Key(Ada, Scope.Notifications), Cancellation));
        Assert.Equal(1, Count(await Send(ada.Client, HttpMethod.Get, "/", json: true)));

        Assert.Equal(HttpStatusCode.OK, (await Send(ada.Client, HttpMethod.Get, Notifications.Path)).Status);
        await Reached.Settled(tangentSpace, Cancellation);
        Assert.Equal(0, Count(await Send(ada.Client, HttpMethod.Get, "/", json: true)));
        Assert.Equal(0, Count(await Send(agent, HttpMethod.Get, "/", json: true, bearer: bearer)));
    }

    [Fact]
    public async Task The_page_answers_its_list_without_waiting_when_the_writers_queue_is_full()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        var mine = await Started(writer, Ada, "Lanterns", "Who lights theirs first?");
        var reply = await Replied(writer, mine, Bea, "I do, at six.");
        using var ada = await SignedIn(tangentSpace, Ada);

        var release = new TaskCompletionSource();
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = writer.Send(new Hold(release.Task, running), Cancellation);
        await running.Task.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
        var queued = new List<Task<Outcome<bool>>>();
        while (queued.Count <= WriteLoop.QueueBound && writer.TrySend(new Hold(Task.CompletedTask)) is { } sent) queued.Add(sent);
        Assert.Equal(WriteLoop.QueueBound, queued.Count);
        try
        {
            var page = await Send(ada.Client, HttpMethod.Get, Notifications.Path, json: true).WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
            Assert.Equal(HttpStatusCode.OK, page.Status);
            Assert.Equal([AddressOf(mine, reply)], Posts(page.Json));
            Assert.Equal(0L, page.Json.GetProperty("through").GetInt64());
        }
        finally
        {
            release.SetResult();
            await held;
            await Task.WhenAll(queued);
        }

        // Nothing was marked; the next opening marks it.
        Assert.Null(await Checkpoint.Get(Checkpoint.Key(Ada, Scope.Notifications), Cancellation));
        Assert.Equal(HttpStatusCode.OK, (await Send(ada.Client, HttpMethod.Get, Notifications.Path)).Status);
        await Reached.Settled(tangentSpace, Cancellation);
        Assert.Equal((await Created(reply)).Seq, (await Checkpoint.Get(Checkpoint.Key(Ada, Scope.Notifications), Cancellation))!.Seq);
    }

    /// <summary>The name a participant written by <see cref="Given.AParticipant"/> shows until they sign in: the handle it
    /// gives them.</summary>
    private static string Name(string id) => id + ".example";

    /// <summary>How many notifications the reader has not seen, whether that is a lower bound, and where the count reads
    /// through, as a page's JSON says.</summary>
    private static (int Count, bool AtLeast, long Through) Unseen(Answered page) => Unseen(page.Json);

    private static (int Count, bool AtLeast, long Through) Unseen(JsonElement page)
    {
        var unseen = page.GetProperty("who").GetProperty("notifications");
        return (unseen.GetProperty("count").GetInt32(), unseen.GetProperty("atLeast").GetBoolean(), unseen.GetProperty("through").GetInt64());
    }

    /// <summary>How many notifications the reader has not seen, as a page's JSON says.</summary>
    private static int Count(Answered page) => page.Json.GetProperty("who").GetProperty("notifications").GetProperty("count").GetInt32();

    /// <summary>The addresses a page of notifications links to, in its order.</summary>
    private static List<string> Posts(JsonElement page)
        => page.GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("address").GetString()!).ToList();

    private static async Task<Event> Created(Post post)
        => (await Event.All(Cancellation)).Single(e => e.Kind == EventKind.PostCreated && e.PostId == post.Id);

    private static async Task<string> CacheControl(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path, Cancellation);
        return response.Headers.CacheControl?.ToString() ?? "";
    }

    private static async Task<Answered> Answer(Browser browser, string path, string offerId, Dictionary<string, string> token)
    {
        var fields = new Dictionary<string, string>(token) { [OfferController.Field] = offerId };
        return await Send(browser.Client, HttpMethod.Post, path, FormBody(fields));
    }

    /// <summary>Appends one event of <paramref name="Kind"/> by <paramref name="ActorId"/> targeting
    /// <paramref name="TargetId"/>, about <paramref name="Post"/> where one is named, numbered by the writer: for a proof
    /// that needs one no command writes, such as a mention in a Tangent its target may not view.</summary>
    private sealed record Logged(string Kind, string ActorId, string TargetId, Post? Post = null) : ICommand<Event>
    {
        public async Task<Outcome<Event>> Run(Sequences sequences, CancellationToken ct)
        {
            var row = Event.Next(sequences, Kind, DateTimeOffset.UtcNow, tangentId: Post?.TangentId, topicId: Post?.TopicId, postId: Post?.Id,
                actorId: ActorId, targetId: TargetId);
            await row.Save(ct);
            return new Outcome<Event>.Done(row);
        }
    }

    /// <summary>Holds the writer until <paramref name="Until"/> completes, saying when it began through
    /// <paramref name="Running"/>; writes nothing.</summary>
    private sealed record Hold(Task Until, TaskCompletionSource? Running = null) : ICommand<bool>
    {
        public async Task<Outcome<bool>> Run(Sequences sequences, CancellationToken ct)
        {
            Running?.TrySetResult();
            await Until;
            return new Outcome<bool>.Done(true);
        }
    }

    /// <summary>Appends <paramref name="Times"/> events of <paramref name="Kind"/> about <paramref name="Root"/> by
    /// <paramref name="ActorId"/> targeting <paramref name="TargetId"/>, in one transaction.</summary>
    private sealed record Many(string Kind, string ActorId, string TargetId, Post Root, int Times) : ICommand<int>
    {
        public async Task<Outcome<int>> Run(Sequences sequences, CancellationToken ct)
        {
            for (var index = 0; index < Times; index++)
                await Event.Next(sequences, Kind, DateTimeOffset.UtcNow, tangentId: Root.TangentId, topicId: Root.Id, postId: Root.Id,
                    actorId: ActorId, targetId: TargetId).Save(ct);
            return new Outcome<int>.Done(Times);
        }
    }
}
