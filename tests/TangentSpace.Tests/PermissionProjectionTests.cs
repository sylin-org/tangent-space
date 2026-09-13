using TangentSpace.Authorization;
using TangentSpace.Rooms;
using Xunit;

namespace TangentSpace.Tests;

public sealed class PermissionProjectionTests
{
    private const string Actor = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Other = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public void Existing_manager_wire_actions_and_order_are_preserved()
    {
        var policy = Policy();
        Assert.Equal(new[] { "read", "reply", "manageTopic", "manageParticipants", "editOwnPost", "deleteOwnPost", "removePost", "reportPost" },
            Permissions.Topic(policy).AllowedActions);
        Assert.Equal(new[] { "read", "editOwnPost", "deleteOwnPost" }, Permissions.Post(policy, Actor, false).AllowedActions);
        Assert.Equal(new[] { "read", "removePost", "reportPost" }, Permissions.Post(policy, Other, false).AllowedActions);
        Assert.Equal(new[] { "read" }, Permissions.Post(policy, Other, true).AllowedActions);
        Assert.Equal("moderator", Permissions.Topic(policy).Role);
        Assert.Equal("topic", Permissions.Topic(policy).Scope);
        Assert.Equal("post", Permissions.Post(policy, Actor, false).Scope);
    }

    [Fact]
    public void Locked_topic_keeps_management_but_not_author_writes()
    {
        var policy = Policy() with { Locked = true, CanWrite = false, Reason = "topic-locked" };
        Assert.Equal(new[] { "read", "manageTopic", "manageParticipants", "removePost", "reportPost" }, Permissions.Topic(policy).AllowedActions);
        Assert.Equal(new[] { "read" }, Permissions.Post(policy, Actor, false).AllowedActions);
        Assert.Equal("true", Permissions.Topic(policy).Restrictions["locked"]);
        Assert.Equal("topic-locked", Permissions.Topic(policy).Restrictions["reason"]);
    }

    [Fact]
    public void Inconsistent_unreadable_policy_cannot_offer_post_mutations()
    {
        var policy = Policy() with { CanRead = false };
        Assert.Equal(new[] { "manageTopic", "manageParticipants" }, Permissions.Topic(policy).AllowedActions);
        Assert.Empty(Permissions.Post(policy, Actor, false).AllowedActions);
        Assert.Empty(Permissions.Post(policy, Other, false).AllowedActions);
    }

    [Fact]
    public void Empty_identity_does_not_gain_author_actions()
    {
        var policy = Policy() with { ActorParticipantId = "", CanManage = false };
        Assert.Equal(new[] { "read" }, Permissions.Post(policy, "", false).AllowedActions);
    }

    private static RoomPolicy Policy() => new("workshop", Actor, 42, 3, RoomAdmission.SignedIn,
        RoomSpaceState.Local, null, RoomRole.Manager, false, true, true, true, false, "allowed", true, false);
}
