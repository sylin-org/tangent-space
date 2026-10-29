using Koan.Identity.Roles;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Tangent.Access;

/// <summary>The roles a first start seeds on Koan's RoleCollection, once, and the administrator permission every start
/// keeps on the administrator role.</summary>
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
                // Everyone holds no bit of its own: a public place gives everyone View through its audience.
                Assert.Empty((await Role.Get(Roles.Everyone, Cancellation))!.Permissions);
                Assert.Equal(Roles.MemberBits, await BitsOf(Roles.Member));
                Assert.Equal(Roles.ModeratorBits, await BitsOf(Roles.Moderator));
                Assert.Equal(Bit.Administrator, await BitsOf(Roles.Administrator));
                Assert.Equal([Bits.Token(Bit.Administrator)], (await Role.Get(Roles.Administrator, Cancellation))!.Permissions);
                Assert.All(await Role.All(Cancellation), role => Assert.Empty(role.Members));

                var roles = first.Services.GetRequiredService<RoleCollection>();
                await roles.SetPermissions(Roles.Member, Bits.Tokens(Roles.MemberBits | Bit.Moderate), Cancellation);
                await roles.SetPermissions(Roles.Everyone, Bits.Tokens(Bit.Post), Cancellation);
                await roles.Delete(Roles.Moderator, Cancellation);
            }

            await using var second = await TangentSpaceFixture.StartAsync(Settings(dataFolder), ct: Cancellation);

            Assert.Equal(4, await Role.Count.Exact(Cancellation));
            Assert.Equal(Roles.MemberBits | Bit.Moderate, await BitsOf(Roles.Member));
            Assert.Equal(Bit.Post, await BitsOf(Roles.Everyone));
            Assert.Equal(Roles.ModeratorBits, await BitsOf(Roles.Moderator));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(dataFolder)) Directory.Delete(dataFolder, recursive: true);
        }
    }

    [Fact]
    public async Task A_start_gives_the_administrator_role_the_administrator_permission_it_lacks_and_takes_nothing_from_it()
    {
        var dataFolder = Path.Combine(Path.GetTempPath(), "tangent-data-" + Guid.CreateVersion7().ToString("n"));
        try
        {
            await using (var first = await TangentSpaceFixture.StartAsync(Settings(dataFolder), ct: Cancellation))
            {
                await first.Services.GetRequiredService<RoleCollection>()
                    .SetPermissions(Roles.Administrator, ["global:Administrator", Bits.Token(Bit.ManageSpace)], Cancellation);
                Assert.Equal(Bit.ManageSpace, await BitsOf(Roles.Administrator));
            }

            await using var second = await TangentSpaceFixture.StartAsync(Settings(dataFolder), ct: Cancellation);

            var permissions = (await Role.Get(Roles.Administrator, Cancellation))!.Permissions;
            Assert.Equal(new[] { "global:Administrator", Bits.Token(Bit.ManageSpace), RoleTokens.Administrator }.Order(StringComparer.Ordinal),
                permissions.Order(StringComparer.Ordinal));
            Assert.Equal(Bit.Administrator | Bit.ManageSpace, await BitsOf(Roles.Administrator));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(dataFolder)) Directory.Delete(dataFolder, recursive: true);
        }
    }

    private static async Task<Bit> BitsOf(string roleId) => Bits.InTokens((await Role.Get(roleId, Cancellation))!.Permissions);

    private static Dictionary<string, string?> Settings(string dataFolder) => new() { ["Tangent:DataFolder"] = dataFolder };
}
