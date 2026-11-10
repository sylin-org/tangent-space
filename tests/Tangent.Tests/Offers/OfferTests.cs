using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Koan.Data.Core.Commands;
using Koan.Web.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Capture;
using Tangent.Events;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Space;
using Xunit;

namespace Tangent.Offers;

/// <summary>Offers of an address: a public https origin the place is reached at while its address is local is open to
/// its operator, computed from what the place sees and never stored, and nothing else is; the operator alone reads it on
/// the door, both ways, and uses it, which makes the origin the base URL, or declines it, which keeps it from ever being
/// offered again and opens the next; a configured URL leaves none open.</summary>
public sealed class OfferTests
{
    private static readonly string Leo = Persona.Id("leo4offers");
    private static readonly string Ada = Persona.Id("ada4offers");
    private const string Public = "https://tangent.example.org";
    private const string Other = "https://www.example.org";
    private const string OnAPort = "https://tangent.example.org:8443";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_public_https_origin_seen_while_the_address_is_local_is_open_to_the_operator_and_nothing_is_written()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Reached.Unconfigured(), ct: Cancellation);
        await Reached.APlace(tangentSpace, Leo, address: "http://127.0.0.1:5220", Cancellation);

        await Reached.On(tangentSpace, Public, Cancellation, times: 3);

        Assert.Equal(Public, await Reached.Open(tangentSpace, Cancellation));
        Assert.Equal("address/https%3A%2F%2Ftangent.example.org", Offer.IdFor(OfferKind.Address, Public));
        Assert.Empty(await Offer.All(Cancellation));
        Assert.Empty(await Event.All(Cancellation));
    }

    [Fact]
    public async Task An_https_origin_on_a_port_of_its_own_is_not_offered_and_answering_it_finds_nothing_while_the_same_host_on_the_default_port_is_offered()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Reached.Unconfigured(), ct: Cancellation);
        await APlaceWithNews(tangentSpace);

        await Reached.On(tangentSpace, OnAPort, Cancellation, times: 3);
        Assert.Null(await Reached.Open(tangentSpace, Cancellation));
        using var leo = await SignedIn(tangentSpace, "leo", Leo);
        using (var used = await Answer(leo, OfferController.UsePath, OnAPort))
            Assert.Equal(HttpStatusCode.NotFound, used.StatusCode);
        Assert.Null((await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!.Address);
        Assert.Empty(await Offer.All(Cancellation));

        await Reached.On(tangentSpace, Public, Cancellation);
        Assert.Equal(Public, await Reached.Open(tangentSpace, Cancellation));
    }

    [Fact]
    public async Task A_place_whose_row_keeps_an_address_on_a_port_of_its_own_starts_on_each_request_s_origin_and_its_used_offer_still_lands()
    {
        var dataFolder = Path.Combine(Path.GetTempPath(), "tangent-data-" + Guid.CreateVersion7().ToString("n"));
        try
        {
            await using (var kept = await TangentSpaceFixture.StartAsync(Reached.Unconfigured(("Tangent:DataFolder", dataFolder)), ct: Cancellation))
            {
                await APlaceWithNews(kept, OnAPort);
                var used = Offer.Decided(OfferKind.Address, OnAPort, OfferState.Accepted);
                Assert.IsType<Outcome<Offer>.Done>(await new Store<Offer>(used).Send(Cancellation));
            }

            await using var restarted = await TangentSpaceFixture.StartAsync(Reached.Unconfigured(("Tangent:DataFolder", dataFolder)), ct: Cancellation);
            var baseUrl = restarted.Services.GetRequiredService<KoanWebBaseUrl>();
            Assert.Null(baseUrl.Accepted);
            Assert.True(baseUrl.DerivesFromRequests);
            Assert.Equal(OnAPort, (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!.Address);

            using var leo = await SignedIn(restarted, "leo", Leo);
            using var again = await Answer(leo, OfferController.UsePath, OnAPort);
            Assert.Equal(HttpStatusCode.SeeOther, again.StatusCode);
            Assert.Null(baseUrl.Accepted);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(dataFolder)) Directory.Delete(dataFolder, recursive: true);
        }
    }

    [Fact]
    public async Task With_nothing_else_configured_a_proxy_on_loopback_is_believed_and_the_public_origin_it_names_is_offered()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(
            new Dictionary<string, string?> { [TangentModule.BaseUrlKey] = "" }, ct: Cancellation);
        await Reached.APlace(tangentSpace, Leo, address: null, Cancellation);

        await Reached.On(tangentSpace, Public, Cancellation);

        Assert.Equal(Public, await Reached.Open(tangentSpace, Cancellation));
    }

    [Fact]
    public async Task An_origin_seen_before_setup_is_offered_once_the_sign_in_sets_the_place_up()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Reached.Unconfigured(), ct: Cancellation);
        await Reached.On(tangentSpace, Public, Cancellation, times: 2);
        Assert.Null(await Reached.Open(tangentSpace, Cancellation));

        using var browser = new Browser(tangentSpace);
        (await browser.SignIn("leo", Leo, Cancellation, handle: "leo.example", comeBack: FirstRunController.Path)).Dispose();

        Assert.Equal(Leo, (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!.OperatorId);
        Assert.Equal(Public, await Reached.Open(tangentSpace, Cancellation));
        using var door = await Json(browser.Client, "/");
        Assert.Equal((Public, 2L), (door.RootElement.GetProperty("offer").GetProperty("subject").GetString(),
            door.RootElement.GetProperty("offer").GetProperty("count").GetInt64()));
    }

    [Fact]
    public async Task Loopback_private_unspecified_and_http_origins_are_not_offered_and_the_public_one_seen_first_is()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Reached.Unconfigured(), ct: Cancellation);
        await Reached.APlace(tangentSpace, Leo, address: null, Cancellation);

        foreach (var origin in new[] { "https://localhost:8443", "https://tangent.localhost", "https://192.168.1.10", "https://10.0.0.7:8443", "https://0.0.0.0", "http://tangent.example.org" })
            await Reached.On(tangentSpace, origin, Cancellation, times: 3);
        Assert.Null(await Reached.Open(tangentSpace, Cancellation));

        await Reached.On(tangentSpace, Other, Cancellation);
        await Reached.On(tangentSpace, Public, Cancellation, times: 2);
        Assert.Equal(Other, await Reached.Open(tangentSpace, Cancellation));
        Assert.Empty(await Offer.All(Cancellation));
    }

    [Fact]
    public async Task The_open_offer_stays_put_however_many_requests_arrive_on_others_and_an_answer_lands_for_any_origin_offerable()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Reached.Unconfigured(), ct: Cancellation);
        await APlaceWithNews(tangentSpace);
        await Reached.On(tangentSpace, Public, Cancellation);
        using var leo = await SignedIn(tangentSpace, "leo", Leo);
        using (var door = await Json(leo.Client, "/"))
            Assert.Equal(Public, door.RootElement.GetProperty("offer").GetProperty("subject").GetString());

        // A burst on another host, forged or not, takes nothing from the line the operator read.
        await Reached.On(tangentSpace, Other, Cancellation, times: 20);
        Assert.Equal(Public, await Reached.Open(tangentSpace, Cancellation));

        // The operator may still answer the other, which is seen, offerable and undecided; the line stays where it was.
        using (var declined = await Answer(leo, OfferController.DeclinePath, Other))
            Assert.Equal(HttpStatusCode.SeeOther, declined.StatusCode);
        Assert.Equal((Other, OfferState.Declined), (Assert.Single(await Offer.All(Cancellation)).Subject, Assert.Single(await Offer.All(Cancellation)).State));
        Assert.Equal(Public, await Reached.Open(tangentSpace, Cancellation));

        // An origin never seen is no answer of the operator's.
        using (var unseen = await Answer(leo, OfferController.DeclinePath, "https://never.example.org"))
            Assert.Equal(HttpStatusCode.NotFound, unseen.StatusCode);
    }

    [Fact]
    public async Task Nothing_is_offered_once_the_address_is_public_or_where_a_url_is_configured()
    {
        await using (var addressed = await TangentSpaceFixture.StartAsync(Reached.Unconfigured(), ct: Cancellation))
        {
            await Reached.APlace(addressed, Leo, address: Public, Cancellation);
            await Reached.On(addressed, Other, Cancellation);
            Assert.Null(await Reached.Open(addressed, Cancellation));
        }

        await using var configured = await TangentSpaceFixture.StartAsync(
            new Dictionary<string, string?> { ["Koan:Web:TrustedProxies:0"] = "127.0.0.1" }, ct: Cancellation);
        await Reached.APlace(configured, Leo, address: null, Cancellation);
        await Reached.On(configured, Other, Cancellation);
        Assert.Null(await Reached.Open(configured, Cancellation));
        Assert.Empty(await Offer.All(Cancellation));
        Assert.Empty(await Event.All(Cancellation));
    }

    [Fact]
    public async Task The_door_shows_the_open_offer_to_the_operator_alone_both_ways()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Reached.Unconfigured(), ct: Cancellation);
        await APlaceWithNews(tangentSpace);
        await Reached.On(tangentSpace, Public, Cancellation, times: 14);
        var loopback = tangentSpace.Client.BaseAddress!.GetLeftPart(UriPartial.Authority);
        using var leo = await SignedIn(tangentSpace, "leo", Leo);
        using var ada = await SignedIn(tangentSpace, "ada", Ada);

        using (var door = await Json(leo.Client, "/"))
        {
            var offered = door.RootElement.GetProperty("offer");
            Assert.Equal(("address", Public, 14L, $"{loopback}/offers/use", $"{loopback}/offers/decline"),
                (offered.GetProperty("kind").GetString(), offered.GetProperty("subject").GetString(),
                    offered.GetProperty("count").GetInt64(), offered.GetProperty("use").GetProperty("action").GetString(),
                    offered.GetProperty("decline").GetProperty("action").GetString()));
            Assert.Equal(Public, Writing.Valued(offered.GetProperty("use"), OfferController.Field));
            Assert.True(offered.GetProperty("since").GetDateTimeOffset() <= DateTimeOffset.UtcNow);
            Assert.NotEmpty(offered.GetProperty("use").GetProperty("token").GetProperty("value").GetString()!);
        }

        var html = WebUtility.HtmlDecode(await Html(leo.Client, "/"));
        Assert.Contains($"This place is being reached at <strong>{Public}</strong> — 14 requests since ", html);
        Assert.Contains($"<form class=\"use-offer\" method=\"post\" action=\"{loopback}/offers/use\">", html);
        Assert.Contains($"<form class=\"decline-offer\" method=\"post\" action=\"{loopback}/offers/decline\">", html);
        Assert.Contains($"<input type=\"hidden\" name=\"origin\" value=\"{Public}\" />", html);

        // Anyone else reads a door without it, both ways.
        using (var door = await Json(ada.Client, "/"))
            Assert.False(door.RootElement.TryGetProperty("offer", out _));
        Assert.DoesNotContain("being reached at", await Html(ada.Client, "/"));
        using (var door = await Json(tangentSpace.Client, "/"))
            Assert.False(door.RootElement.TryGetProperty("offer", out _));

        // Nor may anyone else answer it: to them it does not exist.
        using (var posted = await Answer(ada, OfferController.UsePath, Public))
            Assert.Equal(HttpStatusCode.NotFound, posted.StatusCode);
        using (var posted = await Answer(ada, OfferController.DeclinePath, Public))
            Assert.Equal(HttpStatusCode.NotFound, posted.StatusCode);
        Assert.Empty(await Offer.All(Cancellation));
        Assert.Equal(Public, await Reached.Open(tangentSpace, Cancellation));
        Assert.Null(tangentSpace.Services.GetRequiredService<KoanWebBaseUrl>().Current);
    }

    [Fact]
    public async Task Using_an_offer_makes_its_origin_the_base_url_of_the_descriptor_and_the_pointers_and_accepts_it_and_a_second_post_changes_nothing()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Reached.Unconfigured(), ct: Cancellation);
        await APlaceWithNews(tangentSpace);
        await Reached.On(tangentSpace, Public, Cancellation);
        using var leo = await SignedIn(tangentSpace, "leo", Leo);
        var baseUrl = tangentSpace.Services.GetRequiredService<KoanWebBaseUrl>();
        var id = Offer.IdFor(OfferKind.Address, Public);

        // Koan holds the origin only once the command has judged and kept it: refused there, nothing changes.
        Assert.Equal(new Outcome<Offer>.Refused(Decision.NoOffer), await PlaceAddress.Use(baseUrl, new UseOffer(Public, Ada, Public)));
        Assert.Null(baseUrl.Current);
        Assert.Null((await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!.Address);
        Assert.Null(await Offer.Get(id, Cancellation));

        using (var used = await Answer(leo, OfferController.UsePath, Public))
        {
            Assert.Equal(HttpStatusCode.SeeOther, used.StatusCode);
            Assert.Equal(FirstRunController.Path, used.Headers.Location?.OriginalString);
        }

        Assert.Equal(Public, baseUrl.Current);
        var space = (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!;
        Assert.Equal(Public, space.Address);
        var accepted = (await Offer.Get(id, Cancellation))!;
        Assert.Equal((OfferState.Accepted, Public), (accepted.State, accepted.Subject));

        // Asked for at the loopback address, every link is the origin's.
        using (var descriptor = await Json(tangentSpace.Client, "/tangent.json"))
            Assert.Equal((Public, Public + "/"), (descriptor.RootElement.GetProperty("address").GetString(), descriptor.RootElement.GetProperty("door").GetString()));
        var pointers = WebUtility.HtmlDecode(await Html(leo.Client, FirstRunController.Path));
        Assert.Contains($"It answers at <a href=\"{Public}/\">", pointers);
        Assert.Contains($"<link rel=\"tangent\" href=\"{Public}\">", pointers);
        using (var door = await Json(leo.Client, "/"))
        {
            Assert.Equal(Public + "/", door.RootElement.GetProperty("address").GetString());
            Assert.False(door.RootElement.TryGetProperty("offer", out _));
        }

        var events = await Event.Log.Highest(Cancellation);
        using (var again = await Answer(leo, OfferController.UsePath, Public))
        {
            Assert.Equal(HttpStatusCode.SeeOther, again.StatusCode);
            Assert.Equal(FirstRunController.Path, again.Headers.Location?.OriginalString);
        }

        Assert.Equal(Public, baseUrl.Current);
        Assert.Equal(space.Version, (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!.Version);
        Assert.Equal((OfferState.Accepted, Public), ((await Offer.Get(id, Cancellation))!.State, (await Offer.Get(id, Cancellation))!.Subject));
        Assert.Equal(events, await Event.Log.Highest(Cancellation));
    }

    [Fact]
    public async Task A_use_behind_a_busy_store_finishes_whatever_becomes_of_its_request_and_leaves_the_origin_held()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Reached.Unconfigured(), ct: Cancellation);
        await APlaceWithNews(tangentSpace);
        await Reached.On(tangentSpace, Public, Cancellation);
        var baseUrl = tangentSpace.Services.GetRequiredService<KoanWebBaseUrl>();

        var release = new TaskCompletionSource();
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = new Hold(release.Task, running).Send(Cancellation);
        await running.Task.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
        var used = PlaceAddress.Use(baseUrl, new UseOffer(Public, Leo, Public));
        release.SetResult();
        await held;

        Assert.IsType<Outcome<Offer>.Done>(await used);
        Assert.Equal(Public, baseUrl.Current);
        Assert.Equal(Public, (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!.Address);
        Assert.Equal(OfferState.Accepted, (await Offer.Get(Offer.IdFor(OfferKind.Address, Public), Cancellation))!.State);
    }

    [Fact]
    public async Task Not_this_one_declines_the_offer_lands_on_the_door_and_opens_the_next_origin_and_a_declined_origin_is_never_offered_again()
    {
        var dataFolder = Path.Combine(Path.GetTempPath(), "tangent-data-" + Guid.CreateVersion7().ToString("n"));
        try
        {
            await using (var tangentSpace = await TangentSpaceFixture.StartAsync(Reached.Unconfigured(("Tangent:DataFolder", dataFolder)), ct: Cancellation))
            {
                await APlaceWithNews(tangentSpace);
                // One address is offered at a time, the one seen first: the other waits.
                await Reached.On(tangentSpace, Public, Cancellation, times: 3);
                await Reached.On(tangentSpace, Other, Cancellation, times: 2);
                Assert.Equal(Public, await Reached.Open(tangentSpace, Cancellation));
                using var leo = await SignedIn(tangentSpace, "leo", Leo);

                using (var declined = await Answer(leo, OfferController.DeclinePath, Public))
                {
                    Assert.Equal(HttpStatusCode.SeeOther, declined.StatusCode);
                    Assert.Equal("/", declined.Headers.Location?.OriginalString);
                }

                Assert.Equal(OfferState.Declined, Assert.Single(await Offer.All(Cancellation)).State);
                Assert.Equal(Other, await Reached.Open(tangentSpace, Cancellation));
                using (var door = await Json(leo.Client, "/"))
                    Assert.Equal(Other, door.RootElement.GetProperty("offer").GetProperty("subject").GetString());
                Assert.Null(tangentSpace.Services.GetRequiredService<KoanWebBaseUrl>().Current);

                // Declined, never again: neither by the same answer twice nor by the other answer.
                using (var again = await Answer(leo, OfferController.DeclinePath, Public))
                    Assert.Equal(HttpStatusCode.SeeOther, again.StatusCode);
                using (var refused = await Answer(leo, OfferController.UsePath, Public))
                    Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
                Assert.Equal(new Outcome<Offer>.Refused(Offer.NotOpen), await new DeclineOffer(Public, Leo).Send(Cancellation));
                Assert.Null(tangentSpace.Services.GetRequiredService<KoanWebBaseUrl>().Current);
                using (var declinedNext = await Answer(leo, OfferController.DeclinePath, Other))
                    Assert.Equal(HttpStatusCode.SeeOther, declinedNext.StatusCode);
                Assert.Null(await Reached.Open(tangentSpace, Cancellation));
            }

            // Nor after a restart, when Koan sees it again as a new origin.
            await using var restarted = await TangentSpaceFixture.StartAsync(Reached.Unconfigured(("Tangent:DataFolder", dataFolder)), ct: Cancellation);
            await Reached.On(restarted, Public, Cancellation);
            Assert.Null(await Reached.Open(restarted, Cancellation));
            Assert.Equal(2, (await Offer.All(Cancellation)).Count(row => row.State == OfferState.Declined));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(dataFolder)) Directory.Delete(dataFolder, recursive: true);
        }
    }

    [Fact]
    public async Task After_a_restart_no_offer_is_open_until_its_origin_is_seen_again_and_then_it_counts_from_there()
    {
        var dataFolder = Path.Combine(Path.GetTempPath(), "tangent-data-" + Guid.CreateVersion7().ToString("n"));
        try
        {
            await using (var first = await TangentSpaceFixture.StartAsync(Reached.Unconfigured(("Tangent:DataFolder", dataFolder)), ct: Cancellation))
            {
                await APlaceWithNews(first);
                await Reached.On(first, Public, Cancellation, times: 3);
                Assert.Equal(Public, await Reached.Open(first, Cancellation));
            }

            await using var restarted = await TangentSpaceFixture.StartAsync(Reached.Unconfigured(("Tangent:DataFolder", dataFolder)), ct: Cancellation);
            using var leo = await SignedIn(restarted, "leo", Leo);
            using (var door = await Json(leo.Client, "/"))
                Assert.False(door.RootElement.TryGetProperty("offer", out _));

            await Reached.On(restarted, Public, Cancellation, times: 2);
            using (var door = await Json(leo.Client, "/"))
                Assert.Equal(2, door.RootElement.GetProperty("offer").GetProperty("count").GetInt64());
            Assert.Empty(await Offer.All(Cancellation));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(dataFolder)) Directory.Delete(dataFolder, recursive: true);
        }
    }

    /// <summary>Keeps the store until <paramref name="Until"/> completes, saying when it began through
    /// <paramref name="Running"/>.</summary>
    private sealed record Hold(Task Until, TaskCompletionSource? Running = null) : ICommand<bool>
    {
        public async Task<Outcome<bool>> Run(CancellationToken ct)
        {
            Running?.TrySetResult();
            await Until;
            return new Outcome<bool>.Done(true);
        }
    }

    /// <summary>A place Leo set up and operates, with no address yet, where Ada is a member and Leo started a Topic, so
    /// her door has news and the form that marks it seen.</summary>
    private static async Task APlaceWithNews(TangentSpaceFixture tangentSpace, string? address = null)
    {
        await Reached.APlace(tangentSpace, Leo, address, Cancellation);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        await Given.AParticipant(tangentSpace, Ada, [Roles.Member], ct: Cancellation);
        await Given.ATangent(tangentSpace, "workshop", ct: Cancellation);
        Assert.IsType<Outcome<Post>.Done>(await new Started(Tids.Mint(), "workshop", "Glazes", "Which glaze holds up?", Leo).Send(Cancellation));
    }

    private static async Task<Browser> SignedIn(TangentSpaceFixture tangentSpace, string name, string did)
    {
        var browser = new Browser(tangentSpace);
        (await browser.SignIn(name, did, Cancellation, handle: name + ".example")).Dispose();
        return browser;
    }

    /// <summary>Posts one answer to the Offer of <paramref name="origin"/>, as its form does.</summary>
    private static async Task<HttpResponseMessage> Answer(Browser browser, string path, string origin)
    {
        var fields = new Dictionary<string, string> { [OfferController.Field] = origin };
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
