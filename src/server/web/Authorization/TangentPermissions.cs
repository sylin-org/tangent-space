namespace TangentSpace.Authorization;

/// <summary>
/// Tangent Space's application vocabulary for permissions that apply everywhere on a Host.
/// Resource-specific access is expressed separately as role tokens such as <c>role:member</c>.
/// Koan treats both forms as opaque tokens; Tangent owns their meaning and presentation.
/// </summary>
public static class TangentPermissions
{
    public const string ReadHost = "global:host_read";
    public const string ManageHost = "global:host_manage";
    public const string ManageRoles = "global:role_manage";
    public const string ManageParticipants = "global:participant_manage";
    public const string CreateTangents = "global:tangent_create";
    public const string ReadTangents = "global:tangent_read";
    public const string ManageTangents = "global:tangent_manage";
    public const string CreateTopics = "global:topic_create";
    public const string ReadTopics = "global:topic_read";
    public const string ManageTopics = "global:topic_manage";
    public const string CreatePosts = "global:post_create";
    public const string RemovePosts = "global:post_remove";

    public static readonly IReadOnlyList<TangentPermissionDefinition> Catalog =
    [
        new(ReadHost, "Enter server", "See this Tangent Space server regardless of its local access list."),
        new(ManageHost, "Manage server", "Change server identity, operation and access defaults."),
        new(ManageRoles, "Manage roles", "Create roles, change their permissions and manage their members."),
        new(ManageParticipants, "Manage participants", "Approve, suspend and restore participants."),
        new(CreateTangents, "Create Tangents", "Create a Tangent anywhere on this server."),
        new(ReadTangents, "See every Tangent", "See Tangents regardless of their local access list."),
        new(ManageTangents, "Manage every Tangent", "Change details and access for every Tangent."),
        new(CreateTopics, "Create Topics anywhere", "Create a Topic in every visible Tangent."),
        new(ReadTopics, "See every Topic", "See Topics regardless of their local access list."),
        new(ManageTopics, "Manage every Topic", "Change details and access for every Topic."),
        new(CreatePosts, "Post anywhere", "Reply in every visible Topic."),
        new(RemovePosts, "Remove any Post", "Moderate Posts in every visible Topic.")
    ];

    public static readonly IReadOnlySet<string> All = Catalog
        .Select(permission => permission.Token)
        .ToHashSet(StringComparer.Ordinal);
}

public static class TangentBuiltInRoles
{
    public const string OwnerKey = "owner";
    public const string AdministratorKey = "administrator";
    public const string MemberKey = "member";

    public static readonly TangentRoleSeed Owner = new(
        OwnerKey,
        "Owner",
        "The accountable human server owner.",
        "#f4b942",
        TangentPermissions.All);

    public static readonly TangentRoleSeed Administrator = new(
        AdministratorKey,
        "Administrators",
        "Trusted stewards who can manage Tangents, Topics and Posts across the server.",
        "#ee6c4d",
        new HashSet<string>(StringComparer.Ordinal)
        {
            TangentPermissions.ReadHost,
            TangentPermissions.ManageParticipants,
            TangentPermissions.CreateTangents,
            TangentPermissions.ReadTangents,
            TangentPermissions.ManageTangents,
            TangentPermissions.CreateTopics,
            TangentPermissions.ReadTopics,
            TangentPermissions.ManageTopics,
            TangentPermissions.CreatePosts,
            TangentPermissions.RemovePosts
        });

    public static readonly TangentRoleSeed Member = new(
        MemberKey,
        "Member",
        "A server member. Local access lists decide where they can act.",
        "#9b7ede",
        new HashSet<string>(StringComparer.Ordinal));

    public static readonly IReadOnlyList<TangentRoleSeed> Defaults = [Owner, Administrator, Member];
}

public sealed record TangentPermissionDefinition(string Token, string Name, string Description);

public sealed record TangentRoleSeed(
    string Key,
    string Name,
    string Description,
    string Color,
    IReadOnlySet<string> Permissions)
{
    public string Token => $"role:{Key}";
}
