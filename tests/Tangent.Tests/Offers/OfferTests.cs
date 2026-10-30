using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Koan.Web.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Events;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Space;
using Tangent.Writer;
using Xunit;

namespace Tangent.Offers;

/// <summary>Offers of an address: a public https origin the place is reached at while its address is local becomes one
/// Offer to its operator, with one <c>offer.made</c> targeted at them, and nothing else does; the operator alone reads it
/// on the door, both ways, and uses it, which makes the origin the base URL, or declines it, which keeps it from ever
/// being offered again; a configured URL lapses what is open.</summary>
public sealed class OfferTests
{
    private const string Leo = "did:plc:leo4offers";
    private const string Ada = "did:plc:ada4offers";
    private const string Public = "https://tangent.example.org";
    private const string Other = "https://www.example.org";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_public_https_origin_seen_while_the_address_is_local_makes_one_offer_and_one_offer_made_targeted_at_the_operator()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Reached.Unconfigured(), ct: Cancellation);
        await Reached.APlace(tangentSpace, Leo, address: "http://127.0.0.1:5220", Cancellation);

        await Reached.On(tangentSpace, Public, Cancellation, times: 3);
        await Reached.Settled(tangentSpace, Cancellation);

        var offer = Assert.Single(await Offer.All(Cancellation));
        Assert.Equal((Offer.IdFor(OfferKind.Address, Public), OfferKind.Address, Public, Leo, OfferState.Open, (DateTimeOffset?)null),
            (offer.Id, offer.Kind, offer.Subject, offer.DeciderId, offer.State, offer.DecidedAt));
        Assert.Equal("address/https%3A%2F%2Ftangent.example.org", offer.Id);
        var made = Assert.Single(await Event.All(Cancellation), e => e.Kind == EventKind.OfferMade);
        Assert.Equal((Leo, offer.Id, (string?)null, (string?)null), (made.TargetId, made.OfferId, made.ActorId, made.TangentId));
    }

    [Fact]
    public async Task With_nothing_else_configured_a_proxy_on_loopback_is_believed_and_the_public_origin_it_names_is_offered()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(
            new Dictionary<string, string?> { [TangentModule.BaseUrlKey] = "" }, ct: Cancellation);
        await Reached.APlace(tangentSpace, Leo, address: null, Cancellation);

        await Reached.On(tangentSpace, Public, Cancellation);
        await Reached.Settled(tangentSpace, Cancellation);

        var offer = Assert.Single(await Offer.All(Cancellation));
        Assert.Equal((Public, Leo, OfferState.Open), (offer.Subject, offer.DeciderId, offer.State));
    }

    [Fact]
    public async Task An_origin_seen_before_setup_is_offered_once_the_sign_in_sets_the_place_up()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Reached.Unconfigured(), ct: Cancellation);
        await Reached.On(tangentSpace, Public, Cancellation, times: 2);
        await Reached.Settled(tangentSpace, Cancellation);
        Assert.Empty(await Offer.All(Cancellation));

        using var browser = new Browser(tangentSpace);
        (await browser.SignIn("leo", Leo, Cancellation, handle: "leo.example", comeBack: FirstRunController.Path)).Dispose();
        await Reached.Settled(tangentSpace, Cancellation);

        var offer = Assert.Single(await Offer.All(Cancellation));
        Assert.Equal((Public, Leo, OfferState.Open), (offer.Subject, offer.DeciderId, offer.State));
        Assert.Equal(Leo, (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!.OperatorId);
    }

    [Fact]
    public async Task A_new_origin_that_meets_a_full_writer_is_dropped_without_waiting_and_offered_at_the_next_commit()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Reached.Unconfigured(), ct: Cancellation);
        await Reached.APlace(tangentSpace, Leo, address: null, Cancellation);
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        var release = new TaskCompletionSource();
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = writer.Send(new Hold(release.Task, running), Cancellation);
        // The writer runs the held command before the queue is filled, so no slot opens once the origin arrives.
        await running.Task.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
        var queued = new List<Task<Outcome<bool>>>();
        while (queued.Count <= WriteLoop.QueueBound && writer.TrySend(new Hold(Task.CompletedTask)) is { } sent) queued.Add(sent);
        Assert.Equal(WriteLoop.QueueBound, queued.Count);

        // The request that brings the origin goes on at once, and its offer is not queued behind the writer.
        using (var bounded = CancellationTokenSource.CreateLinkedTokenSource(Cancellation))
        {
            bounded.CancelAfter(TimeSpan.FromSeconds(10));
            await Reached.On(tangentSpace, Public, bounded.Token);
        }
        release.SetResult();
        await held;
        await Task.WhenAll(queued);
        await Reached.Settled(tangentSpace, Cancellation);
        Assert.Empty(await Offer.All(Cancellation));

        // An ordinary commit, with no Offer open, retries what was dropped.
        await Given.AParticipant(tangentSpace, Ada, [Roles.Member], ct: Cancellation);
        Assert.Equal(Public, (await Eventually(async () => (await Offer.All(Cancellation)).SingleOrDefault()))?.Subject);
    }

    [Fact]
    public async Task A_use_the_writer_finishes_after_the_store_stops_waiting_answers_that_it_is_under_way_and_the_origin_is_held_once_it_commits()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Reached.Unconfigured(),
            services => services.AddSingleton(provider => new PlaceAddress(provider.GetRequiredService<WriteLoop>(), TimeSpan.FromMilliseconds(300))),
            Cancellation);
        await APlaceWithNews(tangentSpace);
        await Reached.On(tangentSpace, Public, Cancellation);
        await Reached.Settled(tangentSpace, Cancellation);
        var offer = Assert.Single(await Offer.All(Cancellation));
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        var baseUrl = tangentSpace.Services.GetRequiredService<KoanWebBaseUrl>();
        using var leo = await SignedIn(tangentSpace, "leo", Leo);
        var form = await leo.Token("/", Cancellation);

        var release = new TaskCompletionSource();
        var held = writer.Send(new Hold(release.Task), Cancellation);
        using (var posted = await Answer(leo, OfferController.UsePath, offer.Id, form))
        {
            Assert.Equal(HttpStatusCode.Accepted, posted.StatusCode);
            Assert.Contains($"This is taking longer than usual; the place moves to {Public} as soon as it’s done.",
                WebUtility.HtmlDecode(await posted.Content.ReadAsStringAsync(Cancellation)));
        }
        Assert.Null(baseUrl.Current);

        release.SetResult();
        await held;
        Assert.Equal(Public, await Eventually(() => Task.FromResult(baseUrl.Current)));
        Assert.Equal(Public, (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!.Address);
        Assert.Equal(OfferState.Accepted, (await Offer.Get(offer.Id, Cancellation))!.State);
    }

    [Fact]
    public async Task Loopback_private_unspecified_and_http_origins_make_no_offer_and_neither_does_a_repeat()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Reached.Unconfigured(), ct: Cancellation);
        await Reached.APlace(tangentSpace, Leo, address: null, Cancellation);

        foreach (var origin in new[] { "https://localhost:8443", "https://tangent.localhost", "https://192.168.1.10", "https://10.0.0.7:8443", "https://0.0.0.0", "http://tangent.example.org" })
            await Reached.On(tangentSpace, origin, Cancellation);
        await Reached.Settled(tangentSpace, Cancellation);
        Assert.Empty(await Offer.All(Cancellation));

        await Reached.On(tangentSpace, Public, Cancellation);
        await Reached.Settled(tangentSpace, Cancellation);
        await Reached.On(tangentSpace, Public, Cancellation, times: 2);
        await Reached.Settled(tangentSpace, Cancellation);

        Assert.Equal(Public, Assert.Single(await Offer.All(Cancellation)).Subject);
        Assert.Single(await Event.All(Cancellation), e => e.Kind == EventKind.OfferMade);
    }

    [Fact]
    public async Task Nothing_is_offered_once_the_address_is_public_or_where_a_url_is_configured()
    {
        await using (var addressed = await TangentSpaceFixture.StartAsync(Reached.Unconfigured(), ct: Cancellation))
        {
            await Reached.APlace(addressed, Leo, address: Public, Cancellation);
            await Reached.On(addressed, Other, Cancellation);
            await Reached.Settled(addressed, Cancellation);
            Assert.Empty(await Offer.All(Cancellation));
        }

        await using var configured = await TangentSpaceFixture.StartAsync(
            new Dictionary<string, string?> { ["Koan:Web:TrustedProxies:0"] = "127.0.0.1" }, ct: Cancellation);
        await Reached.APlace(configured, Leo, address: null, Cancellation);
        await Reached.On(configured, Other, Cancellation);
        await Reached.Settled(configured, Cancellation);
        Assert.Empty(await Offer.All(Cancellation));
        Assert.Empty(await Event.All(Cancellation));
    }

    [Fact]
    public async Task The_door_shows_an_open_offer_to_the_operator_alone_both_ways_and_the_log_reaches_only_them()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Reached.Unconfigured(), ct: Cancellation);
        await APlaceWithNews(tangentSpace);
        await Reached.On(tangentSpace, Public, Cancellation, times: 14);
        await Reached.Settled(tangentSpace, Cancellation);
        var offer = Assert.Single(await Offer.All(Cancellation));
        var loopback = tangentSpace.Client.BaseAddress!.GetLeftPart(UriPartial.Authority);
        using var leo = await SignedIn(tangentSpace, "leo", Leo);
        using var ada = await SignedIn(tangentSpace, "ada", Ada);

        using (var door = await Json(leo.Client, "/"))
        {
            var offered = Assert.Single(door.RootElement.GetProperty("offers").EnumerateArray());
            Assert.Equal((offer.Id, "address", Public, 14L, $"{loopback}/offers/use", $"{loopback}/offers/decline"),
                (offered.GetProperty("offer").GetString(), offered.GetProperty("kind").GetString(), offered.GetProperty("subject").GetString(),
                    offered.GetProperty("count").GetInt64(), offered.GetProperty("use").GetProperty("action").GetString(),
                    offered.GetProperty("decline").GetProperty("action").GetString()));
            Assert.Equal(offer.Id, Writing.Valued(offered.GetProperty("use"), OfferController.Field));
            Assert.True(offered.GetProperty("since").GetDateTimeOffset() <= DateTimeOffset.UtcNow);
            Assert.NotEmpty(offered.GetProperty("use").GetProperty("token").GetProperty("value").GetString()!);
        }

        var html = WebUtility.HtmlDecode(await Html(leo.Client, "/"));
        Assert.Contains($"This place is being reached at <strong>{Public}</strong> — 14 requests since ", html);
        Assert.Contains($"<form method=\"post\" action=\"{loopback}/offers/use\">", html);
        Assert.Contains($"<form method=\"post\" action=\"{loopback}/offers/decline\">", html);
        Assert.Contains($"<input type=\"hidden\" name=\"offer\" value=\"{offer.Id}\" />", html);

        // Anyone else reads a door without it, both ways.
        using (var door = await Json(ada.Client, "/"))
            Assert.False(door.RootElement.TryGetProperty("offers", out _));
        Assert.DoesNotContain("being reached at", await Html(ada.Client, "/"));
        using (var door = await Json(tangentSpace.Client, "/"))
            Assert.False(door.RootElement.TryGetProperty("offers", out _));

        // The log carries offer.made to the operator alone.
        using (var poll = await Json(leo.Client, "/events?since=0"))
            Assert.Contains(poll.RootElement.GetProperty("events").EnumerateArray(),
                row => row.GetProperty("kind").GetString() == EventKind.OfferMade && row.GetProperty("offer").GetString() == offer.Id);
        using (var poll = await Json(ada.Client, "/events?since=0"))
            Assert.DoesNotContain(poll.RootElement.GetProperty("events").EnumerateArray(), row => row.GetProperty("kind").GetString() == EventKind.OfferMade);

        // Nor may anyone else answer it: to them it does not exist.
        using (var posted = await Answer(ada, OfferController.UsePath, offer.Id))
            Assert.Equal(HttpStatusCode.NotFound, posted.StatusCode);
        using (var posted = await Answer(ada, OfferController.DeclinePath, offer.Id))
            Assert.Equal(HttpStatusCode.NotFound, posted.StatusCode);
        Assert.Equal(OfferState.Open, (await Offer.Get(offer.Id, Cancellation))!.State);
        Assert.Null(tangentSpace.Services.GetRequiredService<KoanWebBaseUrl>().Current);
    }

    [Fact]
    public async Task Using_an_offer_makes_its_origin_the_base_url_of_the_descriptor_and_the_pointers_and_accepts_it_and_a_second_post_changes_nothing()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Reached.Unconfigured(), ct: Cancellation);
        await APlaceWithNews(tangentSpace);
        await Reached.On(tangentSpace, Public, Cancellation);
        await Reached.Settled(tangentSpace, Cancellation);
        var offer = Assert.Single(await Offer.All(Cancellation));
        using var leo = await SignedIn(tangentSpace, "leo", Leo);
        // The form as the door held it, kept open: once the offer is used, the door has no form to take a token from.
        var form = await leo.Token("/", Cancellation);
        var baseUrl = tangentSpace.Services.GetRequiredService<KoanWebBaseUrl>();

        // Koan holds the origin only once the writer has judged and kept it: refused there, nothing changes.
        Assert.Equal(new Outcome<Offer>.Refused(Decision.NoOffer), await PlaceAddress.Use(baseUrl, new UseOffer(offer.Id, Ada, Public), Cancellation));
        Assert.Null(baseUrl.Current);
        Assert.Null((await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!.Address);
        Assert.Equal(OfferState.Open, (await Offer.Get(offer.Id, Cancellation))!.State);

        using (var used = await Answer(leo, OfferController.UsePath, offer.Id, form))
        {
            Assert.Equal(HttpStatusCode.SeeOther, used.StatusCode);
            Assert.Equal(FirstRunController.Path, used.Headers.Location?.OriginalString);
        }

        Assert.Equal(Public, baseUrl.Current);
        var space = (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!;
        Assert.Equal(Public, space.Address);
        var accepted = (await Offer.Get(offer.Id, Cancellation))!;
        Assert.Equal(OfferState.Accepted, accepted.State);
        Assert.NotNull(accepted.DecidedAt);

        // Asked for at the loopback address, every link is the origin's.
        using (var descriptor = await Json(tangentSpace.Client, "/tangent.json"))
            Assert.Equal((Public, Public + "/"), (descriptor.RootElement.GetProperty("address").GetString(), descriptor.RootElement.GetProperty("door").GetString()));
        var pointers = WebUtility.HtmlDecode(await Html(leo.Client, FirstRunController.Path));
        Assert.Contains($"It answers at <a href=\"{Public}/\">", pointers);
        Assert.Contains($"<link rel=\"tangent\" href=\"{Public}\">", pointers);
        using (var door = await Json(leo.Client, "/"))
        {
            Assert.Equal(Public + "/", door.RootElement.GetProperty("address").GetString());
            Assert.False(door.RootElement.TryGetProperty("offers", out _));
        }

        var events = await Event.HighWaterMark.Load(Cancellation);
        using (var again = await Answer(leo, OfferController.UsePath, offer.Id, form))
        {
            Assert.Equal(HttpStatusCode.SeeOther, again.StatusCode);
            Assert.Equal(FirstRunController.Path, again.Headers.Location?.OriginalString);
        }

        Assert.Equal(Public, baseUrl.Current);
        Assert.Equal(space.Version, (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!.Version);
        Assert.Equal(accepted.DecidedAt, (await Offer.Get(offer.Id, Cancellation))!.DecidedAt);
        Assert.Equal(events, await Event.HighWaterMark.Load(Cancellation));
    }

    [Fact]
    public async Task A_request_that_ends_once_the_writer_has_its_use_still_leaves_the_origin_held_and_a_later_post_holds_an_address_kept_but_not_held()
    {
        await using (var tangentSpace = await TangentSpaceFixture.StartAsync(Reached.Unconfigured(), ct: Cancellation))
        {
            await APlaceWithNews(tangentSpace);
            await Reached.On(tangentSpace, Public, Cancellation);
            await Reached.Settled(tangentSpace, Cancellation);
            var offer = Assert.Single(await Offer.All(Cancellation));
            var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
            var baseUrl = tangentSpace.Services.GetRequiredService<KoanWebBaseUrl>();

            // The writer is busy, the use is queued behind it, and the request that sent it ends.
            var release = new TaskCompletionSource();
            var held = writer.Send(new Hold(release.Task), Cancellation);
            using var request = new CancellationTokenSource();
            var used = PlaceAddress.Use(baseUrl, new UseOffer(offer.Id, Leo, Public), request.Token);
            request.Cancel();
            release.SetResult();
            await held;

            Assert.IsType<Outcome<Offer>.Done>(await used);
            Assert.Equal(Public, baseUrl.Current);
            Assert.Equal(Public, (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!.Address);
            Assert.Equal(OfferState.Accepted, (await Offer.Get(offer.Id, Cancellation))!.State);
        }

        // An address the row keeps from a used offer that Koan does not hold, as when a use's hold was lost: the next post
        // of that offer holds it.
        await using var kept = await TangentSpaceFixture.StartAsync(Reached.Unconfigured(), ct: Cancellation);
        await APlaceWithNews(kept);
        var keptWriter = kept.Services.GetRequiredService<WriteLoop>();
        var space = (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!;
        space.Address = Public;
        Assert.IsType<Outcome<TangentSpace>.Done>(await keptWriter.Send(new Store<TangentSpace>(space), Cancellation));
        var accepted = Offer.Make(OfferKind.Address, Public, Leo, DateTimeOffset.UtcNow);
        accepted.Accept(DateTimeOffset.UtcNow);
        Assert.IsType<Outcome<Offer>.Done>(await keptWriter.Send(new Store<Offer>(accepted), Cancellation));
        var keptUrl = kept.Services.GetRequiredService<KoanWebBaseUrl>();
        Assert.Null(keptUrl.Current);
        using var leo = await SignedIn(kept, "leo", Leo);

        using (var again = await Answer(leo, OfferController.UsePath, accepted.Id))
        {
            Assert.Equal(HttpStatusCode.SeeOther, again.StatusCode);
            Assert.Equal(FirstRunController.Path, again.Headers.Location?.OriginalString);
        }
        Assert.Equal(Public, keptUrl.Current);
    }

    [Fact]
    public async Task Not_this_one_declines_the_offer_lands_on_the_door_and_offers_the_origin_that_waited_and_a_declined_origin_is_never_offered_again()
    {
        var dataFolder = Path.Combine(Path.GetTempPath(), "tangent-data-" + Guid.CreateVersion7().ToString("n"));
        try
        {
            await using (var tangentSpace = await TangentSpaceFixture.StartAsync(Reached.Unconfigured(("Tangent:DataFolder", dataFolder)), ct: Cancellation))
            {
                await APlaceWithNews(tangentSpace);
                await Reached.On(tangentSpace, Public, Cancellation);
                await Reached.Settled(tangentSpace, Cancellation);
                // One address is offered at a time: this one waits.
                await Reached.On(tangentSpace, Other, Cancellation, times: 2);
                await Reached.Settled(tangentSpace, Cancellation);
                var offer = Assert.Single(await Offer.All(Cancellation));
                using var leo = await SignedIn(tangentSpace, "leo", Leo);

                using (var declined = await Answer(leo, OfferController.DeclinePath, offer.Id))
                {
                    Assert.Equal(HttpStatusCode.SeeOther, declined.StatusCode);
                    Assert.Equal("/", declined.Headers.Location?.OriginalString);
                }

                Assert.Equal(OfferState.Declined, (await Offer.Get(offer.Id, Cancellation))!.State);
                var next = (await Offer.All(Cancellation)).Single(row => row.Id != offer.Id);
                Assert.Equal((Other, OfferState.Open), (next.Subject, next.State));
                using (var door = await Json(leo.Client, "/"))
                    Assert.Equal(Other, Assert.Single(door.RootElement.GetProperty("offers").EnumerateArray()).GetProperty("subject").GetString());
                Assert.Null(tangentSpace.Services.GetRequiredService<KoanWebBaseUrl>().Current);

                // Declined, never again: neither by the same answer twice nor by another command.
                using (var again = await Answer(leo, OfferController.DeclinePath, offer.Id))
                    Assert.Equal(HttpStatusCode.SeeOther, again.StatusCode);
                Assert.Equal(new Outcome<Offer>.Refused(MakeOffer.Made),
                    await tangentSpace.Services.GetRequiredService<WriteLoop>().Send(new MakeOffer(Public), Cancellation));
                using (var refused = await Answer(leo, OfferController.UsePath, offer.Id))
                    Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
                Assert.Null(tangentSpace.Services.GetRequiredService<KoanWebBaseUrl>().Current);
                Assert.Equal(OfferState.Open, (await Offer.Get(next.Id, Cancellation))!.State);
                using (var declinedNext = await Answer(leo, OfferController.DeclinePath, next.Id))
                    Assert.Equal(HttpStatusCode.SeeOther, declinedNext.StatusCode);
            }

            // Nor after a restart, when Koan reports it again as a new origin.
            await using var restarted = await TangentSpaceFixture.StartAsync(Reached.Unconfigured(("Tangent:DataFolder", dataFolder)), ct: Cancellation);
            await Reached.On(restarted, Public, Cancellation);
            await Reached.Settled(restarted, Cancellation);
            Assert.All(await Offer.All(Cancellation), row => Assert.Equal(OfferState.Declined, row.State));
            Assert.Equal(2, (await Event.All(Cancellation)).Count(e => e.Kind == EventKind.OfferMade));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(dataFolder)) Directory.Delete(dataFolder, recursive: true);
        }
    }

    [Fact]
    public async Task After_a_restart_an_open_offer_shows_without_evidence_until_its_origin_is_seen_again()
    {
        var dataFolder = Path.Combine(Path.GetTempPath(), "tangent-data-" + Guid.CreateVersion7().ToString("n"));
        try
        {
            await using (var first = await TangentSpaceFixture.StartAsync(Reached.Unconfigured(("Tangent:DataFolder", dataFolder)), ct: Cancellation))
            {
                await APlaceWithNews(first);
                await Reached.On(first, Public, Cancellation, times: 3);
                await Reached.Settled(first, Cancellation);
            }

            await using var restarted = await TangentSpaceFixture.StartAsync(Reached.Unconfigured(("Tangent:DataFolder", dataFolder)), ct: Cancellation);
            using var leo = await SignedIn(restarted, "leo", Leo);
            using (var door = await Json(leo.Client, "/"))
            {
                var offered = Assert.Single(door.RootElement.GetProperty("offers").EnumerateArray());
                Assert.Equal(Public, offered.GetProperty("subject").GetString());
                Assert.False(offered.TryGetProperty("count", out _));
                Assert.False(offered.TryGetProperty("since", out _));
            }
            Assert.Contains($"This place is being reached at <strong>{Public}</strong>. Use it as its address?", WebUtility.HtmlDecode(await Html(leo.Client, "/")));

            // Seen again, it counts from here, and a rising count makes no new offer.
            await Reached.On(restarted, Public, Cancellation, times: 2);
            await Reached.Settled(restarted, Cancellation);
            using (var door = await Json(leo.Client, "/"))
                Assert.Equal(2, Assert.Single(door.RootElement.GetProperty("offers").EnumerateArray()).GetProperty("count").GetInt64());
            Assert.Single(await Offer.All(Cancellation));
            Assert.Single(await Event.All(Cancellation), e => e.Kind == EventKind.OfferMade);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(dataFolder)) Directory.Delete(dataFolder, recursive: true);
        }
    }

    [Fact]
    public async Task A_configured_url_lapses_the_open_offers_and_the_door_shows_none()
    {
        var dataFolder = Path.Combine(Path.GetTempPath(), "tangent-data-" + Guid.CreateVersion7().ToString("n"));
        try
        {
            await using (var unconfigured = await TangentSpaceFixture.StartAsync(Reached.Unconfigured(("Tangent:DataFolder", dataFolder)), ct: Cancellation))
            {
                await APlaceWithNews(unconfigured);
                await Reached.On(unconfigured, Public, Cancellation);
                await Reached.Settled(unconfigured, Cancellation);
                Assert.Equal(OfferState.Open, Assert.Single(await Offer.All(Cancellation)).State);
            }

            await using var configured = await TangentSpaceFixture.StartAsync(new Dictionary<string, string?>
            {
                ["Tangent:DataFolder"] = dataFolder,
                ["Koan:Web:Auth:TestProvider:Enabled"] = "true",
                ["Tangent:Profile:BaseAddress"] = "http://127.0.0.1:1/",
                ["Tangent:Profile:Timeout"] = "00:00:01"
            }, ct: Cancellation);
            await Reached.Settled(configured, Cancellation);

            var lapsed = Assert.Single(await Offer.All(Cancellation));
            Assert.Equal(OfferState.Lapsed, lapsed.State);
            Assert.NotNull(lapsed.DecidedAt);
            using var leo = await SignedIn(configured, "leo", Leo);
            using var door = await Json(leo.Client, "/");
            Assert.False(door.RootElement.TryGetProperty("offers", out _));
            Assert.Equal(TangentSpaceFixture.DefaultBaseUrl + "/", door.RootElement.GetProperty("address").GetString());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(dataFolder)) Directory.Delete(dataFolder, recursive: true);
        }
    }

    /// <summary>What <paramref name="read"/> answers once it answers something, read every tenth of a second for at most
    /// ten seconds; null if it never does.</summary>
    private static async Task<T?> Eventually<T>(Func<Task<T?>> read) where T : class
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (await read() is { } found) return found;
            await Task.Delay(TimeSpan.FromMilliseconds(100), Cancellation);
        }

        return null;
    }

    /// <summary>A command that keeps the writer busy until <see cref="Until"/> completes, writing nothing.</summary>
    /// <summary>Holds the writer until <paramref name="Until"/> completes, saying when it began through
    /// <paramref name="Running"/>.</summary>
    private sealed record Hold(Task Until, TaskCompletionSource? Running = null) : ICommand<bool>
    {
        public async Task<Outcome<bool>> Run(Sequences sequences, CancellationToken ct)
        {
            Running?.TrySetResult();
            await Until;
            return new Outcome<bool>.Done(true);
        }
    }

    /// <summary>A place Leo set up and operates, with no address yet, where Ada is a member and Leo started a Topic, so
    /// her door has news and the form that marks it seen.</summary>
    private static async Task APlaceWithNews(TangentSpaceFixture tangentSpace)
    {
        await Reached.APlace(tangentSpace, Leo, address: null, Cancellation);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        await Given.AParticipant(tangentSpace, Ada, [Roles.Member], ct: Cancellation);
        await Given.ATangent(tangentSpace, "workshop", ct: Cancellation);
        Assert.IsType<Outcome<Post>.Done>(await tangentSpace.Services.GetRequiredService<WriteLoop>().Send(
            new StartTopic(Tids.Mint(), "workshop", "Glazes", "Which glaze holds up?", Leo), Cancellation));
    }

    private static async Task<Browser> SignedIn(TangentSpaceFixture tangentSpace, string name, string did)
    {
        var browser = new Browser(tangentSpace);
        (await browser.SignIn(name, did, Cancellation, handle: name + ".example")).Dispose();
        return browser;
    }

    /// <summary>Posts one answer to an Offer, as its form does, with the token of <paramref name="form"/>, or of the form
    /// the workshop's page holds now.</summary>
    private static async Task<HttpResponseMessage> Answer(Browser browser, string path, string offerId, Dictionary<string, string>? form = null)
    {
        var fields = new Dictionary<string, string>(form ?? await browser.Token("/t/workshop", Cancellation));
        fields[OfferController.Field] = offerId;
        return await browser.Client.PostAsync(path, new FormUrlEncodedContent(fields), Cancellation);
    }

    private static async Task<JsonDocument> Json(HttpClient client, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await client.SendAsync(request, Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));
    }

    private static async Task<string> Html(HttpClient client, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        using var response = await client.SendAsync(request, Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync(Cancellation);
    }
}
