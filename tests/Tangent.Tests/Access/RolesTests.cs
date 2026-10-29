using Koan.Identity.Roles;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Space;
using Xunit;

namespace Tangent.Access;

/// <summary>The roles a first start seeds on Koan's RoleCollection, once.</summary>
public sealed class RolesTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_first_start_seeds_the_four_roles_with_their_bits_once_and_a_second_start_seeds_nothing_twice()
    {
        var dataFolder = Path.Combine(Path.GetTempPath(), "tangent-data-" + Guid.CreateVersion7().ToString("n"));
        try
        {
            await using (var first = await TangentSpaceFixture.StartAsync(Settings(dataFolder), ct: Cancellation))
            {
                Assert.Equal(4, await Role.Count.Exact(Cancellation));
                Assert.Equal(Bit.View, await BitsOf(Roles.Everyone));
                Assert.Equal(Roles.MemberBits, await BitsOf(Roles.Member));
                Assert.Equal(Roles.ModeratorBits, await BitsOf(Roles.Moderator));
                Assert.Equal(Bit.Administrator, await BitsOf(Roles.Administrator));
                Assert.Equal([Bits.Token(Bit.Administrator)], (await Role.Get(Roles.Administrator, Cancellation))!.Permissions);
                Assert.All(await Role.All(Cancellation), role => Assert.Empty(role.Members));

                var roles = first.Services.GetRequiredService<RoleCollection>();
                await roles.SetPermissions(Roles.Member, Bits.Tokens(Roles.MemberBits | Bit.Moderate), Cancellation);
                await roles.Delete(Roles.Moderator, Cancellation);
            }

            await using var second = await TangentSpaceFixture.StartAsync(Settings(dataFolder), ct: Cancellation);

            Assert.Equal(4, await Role.Count.Exact(Cancellation));
            Assert.Equal(Roles.MemberBits | Bit.Moderate, await BitsOf(Roles.Member));
            Assert.Equal(Roles.ModeratorBits, await BitsOf(Roles.Moderator));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(dataFolder)) Directory.Delete(dataFolder, recursive: true);
        }
    }

    [Fact]
    public async Task Everyone_is_seeded_with_view_on_a_public_place_and_nothing_on_a_private_one()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var roles = tangentSpace.Services.GetRequiredService<RoleCollection>();
        Assert.True(await roles.Delete(Roles.Everyone, Cancellation));

        await Roles.Seed(roles, Audience.Private, Cancellation);

        var everyone = await Role.Get(Roles.Everyone, Cancellation);
        Assert.NotNull(everyone);
        Assert.Empty(everyone.Permissions);
        Assert.Equal(4, await Role.Count.Exact(Cancellation));
    }

    private static async Task<Bit> BitsOf(string roleId) => Bits.InTokens((await Role.Get(roleId, Cancellation))!.Permissions);

    private static Dictionary<string, string?> Settings(string dataFolder) => new() { ["Tangent:DataFolder"] = dataFolder };
}
