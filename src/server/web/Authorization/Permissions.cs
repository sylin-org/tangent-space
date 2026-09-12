using TangentSpace.Rooms;

namespace TangentSpace.Authorization;

/// <summary>Single small permission projection shared by HTTP and MCP callers.</summary>
public static class Permissions
{
    public static PermissionView Server(bool owner, bool canCreateTangent = false) => new(owner ? "owner" : "participant", "server",
        owner ? new[] { "read", "manageServer", "createTangent" } : canCreateTangent ? new[] { "read", "createTangent" } : new[] { "read" },
        new Dictionary<string, string>());
    public static PermissionView Topic(RoomPolicy policy)
    {
        var actions = new List<string>();
        if (policy.CanRead) actions.Add("read");
        if (policy.CanWrite && !policy.Locked) actions.Add("reply");
        if (policy.CanManage) actions.Add("manageTopic");
        if (policy.CanManage) actions.Add("manageParticipants");
        if (policy.CanWrite && policy.EditingAllowed && !policy.Locked) actions.Add("editOwnPost");
        if (policy.CanWrite && !policy.Locked) actions.Add("deleteOwnPost");
        if (policy.CanManage && policy.CanRead) actions.Add("removePost");
        return new PermissionView(Role(policy), "topic", actions, Restrictions(policy));
    }

    public static PermissionView Post(RoomPolicy policy, string authorDid, bool removed)
    {
        var topic = Topic(policy);
        var actions = topic.AllowedActions.Where(a => a is "read" or "removePost").ToList();
        var own = string.Equals(policy.ActorParticipantId, authorDid, StringComparison.Ordinal);
        if (own || removed) actions.Remove("removePost");
        if (own && policy.CanWrite && policy.EditingAllowed && !policy.Locked && !removed) actions.Add("editOwnPost");
        if (own && policy.CanWrite && !policy.Locked && !removed) actions.Add("deleteOwnPost");
        return new PermissionView(Role(policy), "post", actions, Restrictions(policy));
    }

    private static string Role(RoomPolicy p) => p.IsOwner ? "owner" : p.CanManage ? "moderator" : p.Role?.ToString().ToLowerInvariant() ?? (p.CanWrite ? "participant" : p.CanRead ? "reader" : "visitor");
    private static IReadOnlyDictionary<string, string> Restrictions(RoomPolicy p) => new Dictionary<string, string>
    {
        ["locked"] = p.Locked ? "true" : "false",
        ["editingAllowed"] = p.EditingAllowed ? "true" : "false",
        ["reason"] = p.Reason
    };
}
