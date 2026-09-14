using TangentSpace.Authorization;
using Xunit;

namespace TangentSpace.Tests;

public sealed class TangentPermissionVocabularyTests
{
    [Fact]
    public void Permission_tokens_are_global_stable_and_unique()
    {
        var tokens = TangentPermissions.Catalog.Select(permission => permission.Token).ToArray();

        Assert.All(tokens, token => Assert.StartsWith("global:", token, StringComparison.Ordinal));
        Assert.Equal(tokens.Length, tokens.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(tokens.ToHashSet(StringComparer.Ordinal), TangentPermissions.All);
    }

    [Fact]
    public void Owner_has_every_global_permission()
    {
        Assert.Equal("role:owner", TangentBuiltInRoles.Owner.Token);
        Assert.Equal(TangentPermissions.All, TangentBuiltInRoles.Owner.Permissions);
    }

    [Fact]
    public void Administrator_can_steward_content_without_becoming_host_owner()
    {
        var permissions = TangentBuiltInRoles.Administrator.Permissions;

        Assert.Equal("role:administrator", TangentBuiltInRoles.Administrator.Token);
        Assert.DoesNotContain(TangentPermissions.ManageHost, permissions);
        Assert.DoesNotContain(TangentPermissions.ManageRoles, permissions);
        Assert.Contains(TangentPermissions.ManageTangents, permissions);
        Assert.Contains(TangentPermissions.ManageTopics, permissions);
        Assert.Contains(TangentPermissions.CreatePosts, permissions);
        Assert.Contains(TangentPermissions.RemovePosts, permissions);
    }

    [Fact]
    public void Member_is_a_local_access_token_not_a_global_bypass()
    {
        Assert.Equal("role:member", TangentBuiltInRoles.Member.Token);
        Assert.Empty(TangentBuiltInRoles.Member.Permissions);
    }
}
