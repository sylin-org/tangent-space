using System.Diagnostics;
using System.Net;
using Koan.Identity.Roles;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Space;
using Tangent.Tangents;
using Tangent.Writer;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Access;

/// <summary>Nothing bounds how many Tangents a place holds or how many members a role holds: a place of eleven hundred
/// Tangents, each created by command, evaluates access on the last as on the first and moves it; and a member role of
/// more than four thousand ninety-six members joins one more who then posts; a move among tied orders across many pages
/// changes only the pair's place; and the door, the list of Tangents and the moderators' Tangent reach every Tangent the
/// reader may view however many come before them.</summary>
public sealed class PlaceSizeTests(ITestOutputHelper output)
{
    private const string Leo = "did:plc:leo4size";
    private const string Ada = "did:plc:ada4size";
    private const string Mo = "did:plc:mo4size";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static string Slug(int index) => "t" + index.ToString("0000", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task A_place_of_eleven_hundred_tangents_evaluates_access_on_the_last_one_and_moves_it()
    {
        const int count = 1100;
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        await Given.AnEstablishedTangentSpace(tangentSpace, ct: Cancellation);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        await Given.AParticipant(tangentSpace, Ada, [Roles.Member], ct: Cancellation);

        var clock = Stopwatch.StartNew();
        for (var index = 1; index <= count; index++)
        {
            var audience = index == count ? Audience.Private : Audience.Public;
            Assert.IsType<Outcome<Tangents.Tangent>.Done>(await writer.Send(
                new CreateTangent(Slug(index), "Tangent " + index, "", PostKind.Topic, index, audience, Leo), Cancellation));
        }

        output.WriteLine($"{count} Tangents created in {clock.Elapsed}");
        Assert.Equal(count, await Tangents.Tangent.Count.Exact(Cancellation));

        var last = Slug(count);
        var administrator = await Capabilities.Load(Leo, last, null, Cancellation);
        Assert.True(administrator.Exists);
        Assert.True(administrator.Holds(Bit.View | Bit.Post | Bit.Topic | Bit.ManageTangent));
        var member = await Capabilities.Load(Ada, last, null, Cancellation);
        Assert.Equal((false, Capabilities.Absent), (member.Exists, member.Gate(Bit.View)));
        Assert.False((await Capabilities.Load(null, last, null, Cancellation)).Exists);

        var beside = Slug(count - 1);
        var open = await Capabilities.Load(Ada, beside, null, Cancellation);
        Assert.True(open.Exists);
        Assert.True(open.Holds(Bit.View | Bit.Post | Bit.Topic));
        var visitor = await Capabilities.Load(null, beside, null, Cancellation);
        Assert.Equal((true, Capabilities.SignIn), (visitor.Exists, visitor.Gate(Bit.Post)));

        // A move of the last Tangent reads past the first thousand and changes the places of two.
        Assert.IsType<Outcome<Tangents.Tangent>.Done>(await writer.Send(new ChangeTangent(last, Leo, Move: Move.Earlier), Cancellation));
        Assert.Equal((count - 1, count), ((await Tangents.Tangent.Get(last, Cancellation))!.Order, (await Tangents.Tangent.Get(beside, Cancellation))!.Order));
        Assert.Equal(1, (await Tangents.Tangent.Get(Slug(1), Cancellation))!.Order);
    }

    [Fact]
    public async Task A_member_role_of_more_than_four_thousand_ninety_six_members_joins_one_more_who_then_posts()
    {
        const int others = 4097;
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        await Given.AnEstablishedTangentSpace(tangentSpace, ct: Cancellation);
        await Given.ATangent(tangentSpace, "workshop", ct: Cancellation);
        var roles = tangentSpace.Services.GetRequiredService<RoleCollection>();

        var clock = Stopwatch.StartNew();
        for (var index = 0; index < others; index++)
            await roles.Add(Roles.Member, "did:plc:member" + index.ToString("0000", System.Globalization.CultureInfo.InvariantCulture), Cancellation);
        output.WriteLine($"{others} members joined in {clock.Elapsed}");

        var ada = await Given.AParticipant(tangentSpace, Ada, [], ct: Cancellation);
        Assert.True(await MemberRole.Join(roles, ada, Cancellation));

        Assert.Equal(others + 1, await RoleMember.Count.Exact(Cancellation));
        var capabilities = await Capabilities.Load(Ada, "workshop", null, Cancellation);
        Assert.True(capabilities.Holds(Bit.View | Bit.Post | Bit.Topic));
        Assert.Equal("Member", Roles.Word(capabilities.RoleIds));
    }

    [Fact]
    public async Task A_move_among_tied_orders_across_pages_changes_only_the_pairs_place_and_leaves_a_total_order()
    {
        const int count = 150;
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        await Given.AnEstablishedTangentSpace(tangentSpace, ct: Cancellation);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        // Every Tangent at order one, as Tangents created at once can stand: the slug orders them.
        for (var index = 1; index <= count; index++) await Given.ATangent(tangentSpace, Slug(index), ct: Cancellation);
        var expected = Enumerable.Range(1, count).Select(Slug).ToList();

        Assert.IsType<Outcome<Tangents.Tangent>.Done>(await writer.Send(new ChangeTangent(Slug(20), Leo, Move: Move.Earlier), Cancellation));
        (expected[18], expected[19]) = (expected[19], expected[18]);
        Assert.Equal(expected, await InOrder());

        Assert.IsType<Outcome<Tangents.Tangent>.Done>(await writer.Send(new ChangeTangent(Slug(120), Leo, Move: Move.Later), Cancellation));
        (expected[119], expected[120]) = (expected[120], expected[119]);
        Assert.Equal(expected, await InOrder());

        var orders = (await Tangents.Tangent.All(Cancellation)).Select(tangent => tangent.Order).ToList();
        Assert.Equal(orders.Count, orders.Distinct().Count());

        static async Task<List<string>> InOrder() => (await Tangents.Tangent.All(Cancellation))
            .OrderBy(tangent => tangent.Order).ThenBy(tangent => tangent.Id, StringComparer.Ordinal).Select(tangent => tangent.Id).ToList();
    }

    [Fact]
    public async Task The_door_and_the_list_of_tangents_reach_those_the_reader_may_view_past_sixty_others()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        await Given.AnEstablishedTangentSpace(tangentSpace, ct: Cancellation);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        await Given.AParticipant(tangentSpace, Ada, [Roles.Member], ct: Cancellation);
        // Orders dense from one: sixty Tangents Ada may not view, then sixty she may.
        for (var index = 1; index <= 120; index++)
            Assert.IsType<Outcome<Tangents.Tangent>.Done>(await writer.Send(new CreateTangent(Slug(index), "Tangent " + index, "", PostKind.Topic,
                index, index <= 60 ? Audience.Private : Audience.Public, Leo), Cancellation));
        using var ada = await SignedIn(tangentSpace, Ada);

        var door = await Send(ada.Client, HttpMethod.Get, "/", json: true);
        Assert.Equal(Enumerable.Range(61, 50).Select(Slug), Slugs(door.Json.GetProperty("tangents")));
        Assert.True(door.Json.GetProperty("moreTangents").GetBoolean());
        var all = new Uri(door.Json.GetProperty("allTangents").GetString()!);
        Assert.Contains("All Tangents", (await Send(ada.Client, HttpMethod.Get, "/")).Body);

        var first = (await Send(ada.Client, HttpMethod.Get, all.PathAndQuery, json: true)).Json;
        Assert.Equal(Enumerable.Range(61, 50).Select(Slug), Slugs(first.GetProperty("tangents")));
        var next = new Uri(first.GetProperty("next").GetString()!);
        // The cursor is sealed: it holds neither the last Tangent's order nor its slug, and nothing forged or altered is
        // taken.
        var sealedCursor = Uri.UnescapeDataString(next.Query[(TangentsController.After.Length + 2)..]);
        Assert.DoesNotContain("110", sealedCursor);
        Assert.DoesNotContain(Slug(110), sealedCursor);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(ada.Client, HttpMethod.Get, "/t?after=110." + Slug(110), json: true)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(ada.Client, HttpMethod.Get, "/t?after=1." + Slug(1), json: true)).Status);
        var altered = sealedCursor[..^2] + (sealedCursor[^2] == 'A' ? 'B' : 'A') + sealedCursor[^1];
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Send(ada.Client, HttpMethod.Get, "/t?after=" + Uri.EscapeDataString(altered), json: true)).Status);
        var second = (await Send(ada.Client, HttpMethod.Get, next.PathAndQuery, json: true)).Json;
        Assert.Equal(Enumerable.Range(111, 10).Select(Slug), Slugs(second.GetProperty("tangents")));
        Assert.True(Missing(second, "next"));
        Assert.Contains("More Tangents", (await Send(ada.Client, HttpMethod.Get, all.PathAndQuery)).Body);

        // The switcher on a Tangent's page lists the same first page and sends the reader to all of them.
        var page = (await Send(ada.Client, HttpMethod.Get, "/t/" + Slug(61))).Body;
        Assert.Contains("href=\"" + all + "\"", page);
    }

    [Fact]
    public async Task A_moderator_reaches_the_moderators_tangent_from_the_door_past_sixty_tangents()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        await Given.AnEstablishedTangentSpace(tangentSpace, ct: Cancellation);
        Assert.IsType<Outcome<Tangents.Tangent>.Done>(await writer.Send(new CreateModeratorsTangent(), Cancellation));
        await Given.AParticipant(tangentSpace, Mo, [Roles.Moderator], ct: Cancellation);
        await Given.AParticipant(tangentSpace, Ada, [Roles.Member], ct: Cancellation);
        for (var index = 1; index <= 60; index++) await Given.ATangent(tangentSpace, Slug(index), ct: Cancellation);
        using var mo = await SignedIn(tangentSpace, Mo);
        using var ada = await SignedIn(tangentSpace, Ada);

        var asMo = Slugs((await Send(mo.Client, HttpMethod.Get, "/", json: true)).Json.GetProperty("tangents"));
        Assert.Equal(51, asMo.Count);
        Assert.Equal(ModeratorsTangent.Slug, asMo[^1]);
        Assert.Contains("/t/" + ModeratorsTangent.Slug, (await Send(mo.Client, HttpMethod.Get, "/")).Body);
        Assert.DoesNotContain(ModeratorsTangent.Slug, Slugs((await Send(ada.Client, HttpMethod.Get, "/", json: true)).Json.GetProperty("tangents")));
    }

    /// <summary>A reading that meets a Tangent twice, as one moved while the reading went on can be met, keeps it once.</summary>
    [Fact]
    public void A_tangent_read_twice_in_one_reading_is_kept_once()
    {
        var read = new HashSet<string>(StringComparer.Ordinal);
        Tangents.Tangent Row(string slug) => Tangents.Tangent.Create(slug, slug, "", PostKind.Topic, 1);
        Assert.Equal(["a", "b"], ViewableTangents.Fresh([Row("a"), Row("b")], read).Select(row => row.Id));
        Assert.Equal(["c"], ViewableTangents.Fresh([Row("b"), Row("c")], read).Select(row => row.Id));
    }

    private static List<string> Slugs(System.Text.Json.JsonElement tangents)
        => tangents.EnumerateArray().Select(tangent => tangent.GetProperty("slug").GetString()!).ToList();

    private static bool Missing(System.Text.Json.JsonElement value, string name)
        => !value.TryGetProperty(name, out var member) || member.ValueKind == System.Text.Json.JsonValueKind.Null;
}
