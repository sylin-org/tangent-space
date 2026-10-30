using System.Diagnostics;
using Koan.Identity.Roles;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Space;
using Tangent.Tangents;
using Tangent.Writer;
using Xunit;

namespace Tangent.Access;

/// <summary>Nothing bounds how many Tangents a place holds or how many members a role holds: a place of eleven hundred
/// Tangents, each created by command, evaluates access on the last as on the first and moves it; and a member role of
/// more than four thousand ninety-six members joins one more who then posts.</summary>
public sealed class PlaceSizeTests(ITestOutputHelper output)
{
    private const string Leo = "did:plc:leo4size";
    private const string Ada = "did:plc:ada4size";

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
}
