using System.Net;
using System.Text.Json;
using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Offers;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Space;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Events;

/// <summary>Notifications both ways: the count every page's frame shows holds only what targets the reader, that
/// someone else did and that they may view; it reads past the checkpoint opening them moves, which only their post
/// does, a stream past it carries what raises it, and past its bound it reads "at least". The page lists them newest
/// first a page at a time from a sealed cursor, never one in a Tangent the reader may not view, as a page and as JSON;
/// above them the operator reads the open Offer, computed and never stored, decided once from there.</summary>
public sealed class NotificationsTests
{
    private const string Leo = "did:plc:leo4notifications";

    [Fact]
    public async Task The_count_holds_only_events_that_target_the_reader_that_someone_else_did_and_that_they_may_view()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        var mine = await Started(Ada, "Lanterns", "Who lights theirs first?");
        await Replied(mine, Bea, "I do, at six.");
        await Started(Bea, "A question for Ada", $"Over to [@ada](at://{Ada}).");
        await Replied(mine, Ada, "And mine at seven.");
        var secret = await Started(Leo, "Studio notes", "Only for us.", Studio);
        Assert.IsType<Outcome<Event>.Done>(await new Logged(EventKind.Mention, Leo, Ada, secret).Send(Cancellation));
        Assert.IsType<Outcome<Event>.Done>(await new Logged(EventKind.StandingChanged, Ada, Ada).Send(Cancellation));
        Assert.IsType<Outcome<Event>.Done>(await new Logged(EventKind.Mention, Ada, Bea, mine).Send(Cancellation));

        using var ada = await SignedIn(tangentSpace, Ada);
        var door = (await Send(ada.Client, HttpMethod.Get, "/", json: true)).Json;
        var unseen = door.GetProperty("who").GetProperty("notifications");
        Assert.Equal((2, false, BaseUrl + Notifications.Path),
            (unseen.GetProperty("count").GetInt32(), unseen.GetProperty("atLeast").GetBoolean(), unseen.GetProperty("address").GetString()));

        // Every page's frame shows it, as the button that opens them, and every signed-in page carries it both ways.
        foreach (var path in new[] { "/", TopicPath(mine), "/t/" + Workshop, "/t", "/p/ada4writes.example" })
        {
            var page = await Send(ada.Client, HttpMethod.Get, path);
            Assert.Equal(HttpStatusCode.OK, page.Status);
            Assert.Contains($"<form class=\"masthead-unseen-form\" method=\"post\" action=\"{BaseUrl}/notifications\"><input type=\"hidden\" name=\"through\"", page.Body);
            Assert.Contains("<button class=\"masthead-unseen\" aria-label=\"2 new notifications\"", Collapsed(page.Body));
            Assert.Contains("<span class=\"masthead-unseen-count\">2</span>", page.Body);
            Assert.Contains(AccountItem, page.Body);
        }

        // Someone with nothing for them reads no count, and the way to their notifications stays in the account menu.
        using var mo = await SignedIn(tangentSpace, Mo);
        var quiet = await Send(mo.Client, HttpMethod.Get, "/");
        Assert.Contains("<button class=\"masthead-unseen\" hidden aria-label=\"0 new notifications\"", quiet.Body);
        Assert.Contains(AccountItem, quiet.Body);
        using var bea = await SignedIn(tangentSpace, Bea);
        Assert.Equal(1, Count(await Send(bea.Client, HttpMethod.Get, "/", json: true)));
        var visitor = await Send(Agent(tangentSpace), HttpMethod.Get, "/");
        Assert.DoesNotContain("masthead-unseen", visitor.Body);
    }

    [Fact]
    public async Task Opening_the_notifications_posts_the_mark_through_the_newest_and_lands_on_them_with_what_was_new_marked_and_reading_them_marks_nothing()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var mine = await Started(Ada, "Lanterns", "Who lights theirs first?");
        await Replied(mine, Bea, "I do, at six.");
        var newest = await Replied(mine, Mo, "At dusk, always.");
        using var ada = await SignedIn(tangentSpace, Ada);
        await Given.BeenAtTheDoor(tangentSpace, Ada, Cancellation);
        var door = (await Send(ada.Client, HttpMethod.Get, "/", json: true)).Json;
        Assert.Equal(2, door.GetProperty("who").GetProperty("notifications").GetProperty("count").GetInt32());
        // The JSON carries the form that opens them, as every page's HTML does.
        var open = door.GetProperty("who").GetProperty("notifications").GetProperty("open");
        Assert.Equal((BaseUrl + Notifications.Path, "POST"), (open.GetProperty("action").GetString(), open.GetProperty("method").GetString()));
        // The door names them with how many while there are some, though it lists both replies itself.
        Assert.Equal(2, door.GetProperty("news").GetProperty("replies").GetProperty("count").GetInt32());
        Assert.Equal((2, BaseUrl + Notifications.Path), (door.GetProperty("news").GetProperty("notifications").GetProperty("count").GetInt32(),
            door.GetProperty("news").GetProperty("notifications").GetProperty("address").GetString()));
        var doorHtml = (await Send(ada.Client, HttpMethod.Get, "/")).Body;
        Assert.Contains($"<form class=\"door-notifications\" method=\"post\" action=\"{BaseUrl}/notifications\"><input type=\"hidden\" name=\"through\" value=\"{(await Created(newest)).Seq}\" /><button class=\"watch-button\">2 new notifications</button></form>",
            doorHtml);

        // Reading them marks nothing: both stay new, and the frame still counts them.
        var read = await Send(ada.Client, HttpMethod.Get, Notifications.Path);
        Assert.Equal(HttpStatusCode.OK, read.Status);
        Assert.Contains("<span class=\"masthead-unseen-count\">2</span>", read.Body);
        Assert.Contains("no-store", await CacheControl(ada.Client, Notifications.Path));
        Assert.Null(await Checkpoint.Get(Checkpoint.Key(Ada, Scope.Notifications), Cancellation));

        // Opening them posts the mark and lands on them, the replies still marked new and the page's own frame past them.
        var marked = await Opened(ada, "/");
        Assert.Equal((HttpStatusCode.SeeOther, Notifications.Path + "?from=0"), (marked.Status, marked.Location));
        var through = (await Created(newest)).Seq;
        Assert.Equal(through, (await Checkpoint.Get(Checkpoint.Key(Ada, Scope.Notifications), Cancellation))!.Seq);
        Assert.Equal(0, (await Checkpoint.Get(Checkpoint.Key(Ada, Scope.Place), Cancellation))!.Seq);
        var landed = await Send(ada.Client, HttpMethod.Get, marked.Location!, json: true);
        Assert.Equal((0L, through), (landed.Json.GetProperty("from").GetInt64(), landed.Json.GetProperty("through").GetInt64()));
        Assert.All(landed.Json.GetProperty("entries").EnumerateArray(), entry => Assert.True(entry.GetProperty("new").GetBoolean()));
        Assert.Equal(0, Count(landed));
        Assert.Contains("<button class=\"masthead-unseen\" hidden", (await Send(ada.Client, HttpMethod.Get, marked.Location!)).Body);

        // The count drops on every page, the masthead shows nothing, and the door, which still lists the replies past its
        // own checkpoint, names no notifications.
        door = (await Send(ada.Client, HttpMethod.Get, "/", json: true)).Json;
        Assert.Equal(0, door.GetProperty("who").GetProperty("notifications").GetProperty("count").GetInt32());
        Assert.Equal(2, door.GetProperty("news").GetProperty("replies").GetProperty("count").GetInt32());
        Assert.False(door.GetProperty("news").TryGetProperty("notifications", out _));
        Assert.Contains("<button class=\"masthead-unseen\" hidden", (await Send(ada.Client, HttpMethod.Get, "/")).Body);

        // A new reply counts past it, and opening them again moves it on, never back; with nothing new it moves nothing
        // and lands on them.
        await Replied(mine, Bea, "Then the harbour.");
        Assert.Equal(1, Count(await Send(ada.Client, HttpMethod.Get, "/t/" + Workshop, json: true)));
        var again = await Opened(ada, "/t/" + Workshop);
        var json = (await Send(ada.Client, HttpMethod.Get, again.Location!, json: true)).Json;
        Assert.Equal((through, through + 1), (json.GetProperty("from").GetInt64(), json.GetProperty("through").GetInt64()));
        Assert.Equal(new[] { true, false, false }, json.GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("new").GetBoolean()));
        Assert.Equal(through + 1, (await Checkpoint.Get(Checkpoint.Key(Ada, Scope.Notifications), Cancellation))!.Seq);
        var still = await Opened(ada, "/");
        Assert.Equal((HttpStatusCode.SeeOther, Notifications.Path), (still.Status, still.Location));
        Assert.Equal(through + 1, Assert.IsType<Outcome<Checkpoint>.Done>(await new SeeNotifications(Ada, through).Send(Cancellation)).Result.Seq);
        Assert.Equal(0, Count(await Send(ada.Client, HttpMethod.Get, "/", json: true)));

        // Only a notification of the reader's moves it, and only a signed-in reader opens them.
        Assert.Equal(new Outcome<Checkpoint>.Refused(SeeNotifications.NoNotification),
            await new SeeNotifications(Bea, through + 1).Send(Cancellation));
        var visitor = await Send(Agent(tangentSpace), HttpMethod.Post, Notifications.Path, json: true);
        Assert.Equal((HttpStatusCode.Unauthorized, Capabilities.SignIn.Word), (visitor.Status, visitor.Gate));
    }

    [Fact]
    public async Task Opening_them_marks_them_seen_through_what_the_reading_reached_so_what_arrived_after_stays_new()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var mine = await Started(Ada, "Lanterns", "Who lights theirs first?");
        var read = await Replied(mine, Bea, "I do, at six.");
        using var ada = await SignedIn(tangentSpace, Ada);

        var page = (await Send(ada.Client, HttpMethod.Get, Notifications.Path, json: true)).Json;
        var through = Valued(page.GetProperty("who").GetProperty("notifications").GetProperty("open"), Notifications.ThroughField);
        Assert.Equal((await Created(read)).Seq.ToString(System.Globalization.CultureInfo.InvariantCulture), through);

        // A reply arrives between the reading and the call; marking what was read leaves it new.
        await Replied(mine, Mo, "At dusk, always.");
        var marked = await Json(ada, Notifications.Path, Notifications.Path, new { through = long.Parse(through!, System.Globalization.CultureInfo.InvariantCulture) });
        Assert.Equal(HttpStatusCode.SeeOther, marked.Status);
        Assert.Equal((await Created(read)).Seq, (await Checkpoint.Get(Checkpoint.Key(Ada, Scope.Notifications), Cancellation))!.Seq);
        Assert.Equal(1, Count(await Send(ada.Client, HttpMethod.Get, Notifications.Path, json: true)));

        // A call that says nothing of what it read marks nothing.
        var unsaid = await Json(ada, Notifications.Path, Notifications.Path, new { });
        Assert.Equal(HttpStatusCode.BadRequest, unsaid.Status);
        Assert.Equal((await Created(read)).Seq, (await Checkpoint.Get(Checkpoint.Key(Ada, Scope.Notifications), Cancellation))!.Seq);
    }

    [Fact]
    public async Task A_reply_committed_after_a_page_reaches_its_stream_past_what_the_count_read_as_a_notification_for_the_reader()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var mine = await Started(Ada, "Lanterns", "Who lights theirs first?");
        await Replied(mine, Bea, "I do, at six.");
        using var ada = await SignedIn(tangentSpace, Ada);
        var page = (await Send(ada.Client, HttpMethod.Get, TopicPath(mine), json: true)).Json;
        var unseen = page.GetProperty("who").GetProperty("notifications");
        var counted = unseen.GetProperty("through").GetInt64();
        Assert.Equal(1, unseen.GetProperty("count").GetInt32());

        await using var stream = await Streamed.Open(ada.Client, EventsController.Path + "?since=" + counted);
        Assert.Equal("open", (await stream.Any()).Comment);
        var reply = await Replied(mine, Mo, "At dusk, always.");
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
        await Place(tangentSpace);
        var theirs = await Started(Bea, "Lanterns", "Who lights theirs first?");
        Assert.IsType<Outcome<int>.Done>(await new Many(EventKind.Mention, Bea, Ada, theirs, Notifications.CountLimit + 5).Send(Cancellation));
        using var ada = await SignedIn(tangentSpace, Ada);
        await Given.BeenAtTheDoor(tangentSpace, Ada, Cancellation);

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
        Assert.Contains($"<form class=\"door-notifications\" method=\"post\" action=\"{BaseUrl}/notifications\"><input type=\"hidden\" name=\"through\"", html);
        Assert.Contains($"<button class=\"watch-button\">At least {Notifications.CountLimit + 1} new notifications</button></form>", html);
    }

    [Fact]
    public async Task The_page_lists_them_newest_first_a_page_at_a_time_from_a_sealed_cursor_and_refuses_a_forged_one()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var mine = await Started(Ada, "Lanterns", "Who lights theirs first?");
        var replies = new List<Post>();
        for (var index = 0; index < Notifications.PageSize + 5; index++) replies.Add(await Replied(mine, Bea, $"Reply number {index}."));
        using var ada = await SignedIn(tangentSpace, Ada);

        var opened = await Opened(ada, "/");
        var first = (await Send(ada.Client, HttpMethod.Get, opened.Location!, json: true)).Json;
        var shown = Posts(first);
        Assert.Equal(replies.AsEnumerable().Reverse().Take(Notifications.PageSize).Select(reply => AddressOf(mine, reply)), shown);
        Assert.False(first.GetProperty("continued").GetBoolean());
        var next = first.GetProperty("next").GetString()!;
        Assert.StartsWith(BaseUrl + Notifications.Path + "?after=", next);

        var second = (await Send(ada.Client, HttpMethod.Get, next[BaseUrl.Length..], json: true)).Json;
        Assert.Equal(replies.Take(5).Reverse().Select(reply => AddressOf(mine, reply)), Posts(second));
        Assert.True(second.GetProperty("continued").GetBoolean());
        Assert.False(second.TryGetProperty("next", out _));
        // What was new when the reader opened them stays new on the pages after, and the checkpoint stays where opening
        // them put it.
        Assert.All(second.GetProperty("entries").EnumerateArray(), entry => Assert.True(entry.GetProperty("new").GetBoolean()));
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
        Assert.Equal((HttpStatusCode.Unauthorized, Capabilities.SignIn.Word), (visitor.Status, visitor.Gate));
    }

    [Fact]
    public async Task Each_says_what_happened_and_links_to_what_it_is_about_both_ways_and_one_in_a_tangent_the_reader_may_not_view_is_absent()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        var mine = await Started(Ada, "Lanterns", "Who lights theirs first?");
        var reply = await Replied(mine, Bea, "I do, at six.");
        var asked = await Started(Bea, "A question for Ada", $"Over to [@ada](at://{Ada}).");
        var taken = Assert.IsType<Outcome<Post>.Done>(await new StartTopic(Tids.Mint(), Workshop, "Six o'clock lanterns", "", Mo, OriginId: mine.Id).Send(Cancellation)).Result;
        Assert.IsType<Outcome<Participant>.Done>(await new ChangeRole(Leo, Ada, Roles.Moderator, Give: true).Send(Cancellation));
        var secret = await Started(Leo, "Studio notes", "Only for us.", Studio);
        Assert.IsType<Outcome<Event>.Done>(await new Logged(EventKind.Mention, Leo, Ada, secret).Send(Cancellation));
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
    public async Task The_open_offer_is_computed_above_the_notifications_with_its_answers_and_once_decided_from_there_is_gone()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Reached.Unconfigured(), ct: Cancellation);
        await Reached.APlace(tangentSpace, Leo, address: null, Cancellation);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        await Reached.On(tangentSpace, Origin, Cancellation, times: 3);
        var loopback = tangentSpace.Client.BaseAddress!.GetLeftPart(UriPartial.Authority);
        using var leo = new Browser(tangentSpace);
        (await leo.SignIn("leo", Leo, Cancellation, handle: "leo.example")).Dispose();

        // Read, both ways: the Offer is above the notifications with its evidence and its answers, and nothing is stored.
        var json = (await Send(leo.Client, HttpMethod.Get, Notifications.Path, json: true)).Json;
        Assert.Empty(json.GetProperty("entries").EnumerateArray());
        var offered = json.GetProperty("offer");
        Assert.Equal(("address", Origin, 3L, $"{loopback}/offers/use", $"{loopback}/offers/decline"),
            (offered.GetProperty("kind").GetString(), offered.GetProperty("subject").GetString(), offered.GetProperty("count").GetInt64(),
                offered.GetProperty("use").GetProperty("action").GetString(), offered.GetProperty("decline").GetProperty("action").GetString()));
        Assert.Equal((Origin, Origin), (Valued(offered.GetProperty("use"), OfferController.Field), Valued(offered.GetProperty("decline"), OfferController.Field)));
        Assert.NotEmpty(offered.GetProperty("use").GetProperty("token").GetProperty("value").GetString()!);
        var html = WebUtility.HtmlDecode((await Send(leo.Client, HttpMethod.Get, Notifications.Path)).Body);
        Assert.Contains($"This place is being reached at <strong>{Origin}</strong> — 3 requests since", html);
        Assert.Contains($"<form method=\"post\" action=\"{loopback}/offers/decline\">", html);
        Assert.Contains($"<input type=\"hidden\" name=\"origin\" value=\"{Origin}\" />", html);
        Assert.Empty(await Offer.All(Cancellation));

        // Answered from there, once: a second answer changes nothing, and the other one is refused.
        var declined = await Answer(leo, OfferController.DeclinePath, Origin);
        Assert.Equal((HttpStatusCode.SeeOther, "/"), (declined.Status, declined.Location));
        var decided = Assert.Single(await Offer.All(Cancellation));
        Assert.Equal((Offer.IdFor(OfferKind.Address, Origin), OfferState.Declined), (decided.Id, decided.State));
        Assert.Equal(HttpStatusCode.SeeOther, (await Answer(leo, OfferController.DeclinePath, Origin)).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await Answer(leo, OfferController.UsePath, Origin)).Status);
        Assert.Equal((OfferState.Declined, decided.DecidedAt), ((await Offer.Get(decided.Id, Cancellation))!.State, (await Offer.Get(decided.Id, Cancellation))!.DecidedAt));

        // Decided, it is gone from there.
        Assert.False((await Send(leo.Client, HttpMethod.Get, Notifications.Path, json: true)).Json.TryGetProperty("offer", out _));
        Assert.DoesNotContain("/offers/use", (await Send(leo.Client, HttpMethod.Get, Notifications.Path)).Body);
    }

    [Fact]
    public async Task Rows_the_reader_may_not_view_move_neither_the_count_nor_where_it_reads_through_and_a_newest_page_of_them_offers_the_older_one()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        var mine = await Started(Ada, "Lanterns", "Who lights theirs first?");
        var visible = await Replied(mine, Bea, "I do, at six.");
        var secret = await Started(Leo, "Studio notes", "Only for us.", Studio);
        using var ada = await SignedIn(tangentSpace, Ada);
        var shownThrough = (await Created(visible)).Seq;
        Assert.Equal((1, false, shownThrough), Unseen(await Send(ada.Client, HttpMethod.Get, "/", json: true)));

        // Replies to Ada in the studio, which she may not view, move neither the count nor where it reads through.
        Assert.IsType<Outcome<int>.Done>(await new Many(EventKind.PostCreated, Leo, Ada, secret, 3).Send(Cancellation));
        Assert.Equal((1, false, shownThrough), Unseen(await Send(ada.Client, HttpMethod.Get, TopicPath(mine), json: true)));

        // Ninety-nine of them and one she may see fill the read: the count is that one, not a lower bound.
        Assert.IsType<Outcome<int>.Done>(await new Many(EventKind.PostCreated, Leo, Ada, secret, Notifications.CountLimit - 3).Send(Cancellation));
        Assert.Equal((1, false, shownThrough), Unseen(await Send(ada.Client, HttpMethod.Get, "/", json: true)));
        Assert.Contains("<span class=\"masthead-unseen-count\">1</span>", WebUtility.HtmlDecode((await Send(ada.Client, HttpMethod.Get, "/")).Body));

        // A hundred of them after the newest she may see: the count finds none and says nothing of them, and the door
        // names nothing.
        Assert.IsType<Outcome<int>.Done>(await new Many(EventKind.PostCreated, Leo, Ada, secret, 1).Send(Cancellation));
        var door = (await Send(ada.Client, HttpMethod.Get, "/", json: true)).Json;
        Assert.Equal((0, false, 0L), Unseen(door));
        Assert.False(door.GetProperty("news").TryGetProperty("notifications", out _));

        // The newest page holds none she may see: it says so and offers the older page, which shows her reply; neither
        // marks anything.
        var newest = await Send(ada.Client, HttpMethod.Get, Notifications.Path, json: true);
        Assert.Empty(newest.Json.GetProperty("entries").EnumerateArray());
        var next = newest.Json.GetProperty("next").GetString()!;
        Assert.Equal((0L, 0L), (newest.Json.GetProperty("from").GetInt64(), newest.Json.GetProperty("through").GetInt64()));
        var html = WebUtility.HtmlDecode((await Send(ada.Client, HttpMethod.Get, Notifications.Path)).Body);
        Assert.Contains("Nothing to show among the newest; older notifications follow.", html);
        Assert.DoesNotContain("Nothing for you yet", html);
        Assert.Contains("Older notifications →", html);

        var older = (await Send(ada.Client, HttpMethod.Get, next[BaseUrl.Length..], json: true)).Json;
        Assert.Equal([AddressOf(mine, visible)], Posts(older));
        Assert.Equal(0L, older.GetProperty("through").GetInt64());
        Assert.Null(await Checkpoint.Get(Checkpoint.Key(Ada, Scope.Notifications), Cancellation));
        Assert.DoesNotContain("Studio notes", newest.Body + older.GetRawText());
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

    private static Task<Answered> Answer(Browser browser, string path, string origin)
        => Form(browser, Notifications.Path, path, new Dictionary<string, string> { [OfferController.Field] = origin });

    /// <summary>The public origin the Offer proofs reach the place at.</summary>
    private const string Origin = "https://tangent.example.org";

    /// <summary>The account menu's way to the notifications: a form that opens them.</summary>
    private const string AccountItem = $"<form method=\"post\" action=\"{BaseUrl}/notifications\"><input type=\"hidden\" name=\"through\"";

    /// <summary>The page's markup with each run of whitespace between tags taken out, so a tag spread over lines reads as
    /// one.</summary>
    private static string Collapsed(string html) => System.Text.RegularExpressions.Regex.Replace(html, @">\s+<", "><");

    /// <summary>Appends one event of <paramref name="Kind"/> by <paramref name="ActorId"/> targeting
    /// <paramref name="TargetId"/>, about <paramref name="Post"/> where one is named: for a proof
    /// that needs one no command writes, such as a mention in a Tangent its target may not view.</summary>
    private sealed record Logged(string Kind, string ActorId, string TargetId, Post? Post = null) : ICommand<Event>
    {
        public async Task<Outcome<Event>> Run(CancellationToken ct)
        {
            return new Outcome<Event>.Done(await Event.Append(Kind, DateTimeOffset.UtcNow, ct, tangentId: Post?.TangentId, topicId: Post?.TopicId, postId: Post?.Id,
                actorId: ActorId, targetId: TargetId));
        }
    }

    /// <summary>Appends <paramref name="Times"/> events of <paramref name="Kind"/> about <paramref name="Root"/> by
    /// <paramref name="ActorId"/> targeting <paramref name="TargetId"/>, in one transaction.</summary>
    private sealed record Many(string Kind, string ActorId, string TargetId, Post Root, int Times) : ICommand<int>
    {
        public async Task<Outcome<int>> Run(CancellationToken ct)
        {
            for (var index = 0; index < Times; index++)
                await Event.Append(Kind, DateTimeOffset.UtcNow, ct, tangentId: Root.TangentId, topicId: Root.Id, postId: Root.Id,
                    actorId: ActorId, targetId: TargetId);
            return new Outcome<int>.Done(Times);
        }
    }
}
