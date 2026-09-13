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
        Add(TopicCapability.Read, "read");
        Add(TopicCapability.Reply, "reply");
        Add(TopicCapability.ManageTopic, "manageTopic");
        Add(TopicCapability.ManageParticipants, "manageParticipants");
        Add(TopicCapability.EditOwnPost, "editOwnPost");
        Add(TopicCapability.DeleteOwnPost, "deleteOwnPost");
        Add(TopicCapability.RemovePost, "removePost");
        return new PermissionView(Role(policy), "topic", actions, Restrictions(policy));

        void Add(TopicCapability capability, string wireName)
        {
            if (TopicPermissionEvaluator.Evaluate(policy, capability).Allowed) actions.Add(wireName);
        }
    }

    public static PermissionView Post(RoomPolicy policy, string authorDid, bool removed)
    {
        var actions = new List<string>();
        Add(TopicCapability.Read, "read");
        Add(TopicCapability.RemovePost, "removePost");
        Add(TopicCapability.EditOwnPost, "editOwnPost");
        Add(TopicCapability.DeleteOwnPost, "deleteOwnPost");
        return new PermissionView(Role(policy), "post", actions, Restrictions(policy));

        void Add(TopicCapability capability, string wireName)
        {
            if (TopicPermissionEvaluator.EvaluatePost(policy, capability, authorDid, removed).Allowed) actions.Add(wireName);
        }
    }

    private static string Role(RoomPolicy p) => p.IsOwner ? "owner" : p.CanManage ? "moderator" : p.Role?.ToString().ToLowerInvariant() ?? (p.CanWrite ? "participant" : p.CanRead ? "reader" : "visitor");
    private static IReadOnlyDictionary<string, string> Restrictions(RoomPolicy p) => new Dictionary<string, string>
    {
        ["locked"] = p.Locked ? "true" : "false",
        ["editingAllowed"] = p.EditingAllowed ? "true" : "false",
        ["reason"] = p.Reason
    };
}
