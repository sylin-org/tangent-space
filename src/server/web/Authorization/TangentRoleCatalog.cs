using Koan.Identity.Roles;

namespace TangentSpace.Authorization;

public static class TangentRoleCapabilities
{
    public const string HostRead = "host.read";
    public const string HostManage = "host.manage";
    public const string TangentCreate = "tangent.create";
    public const string TangentRead = "tangent.read";
    public const string TangentManage = "tangent.manage";
    public const string TangentManageParticipants = "tangent.participants.manage";
    public const string TopicCreate = "topic.create";
    public const string TopicRead = "topic.read";
    public const string TopicReply = "topic.reply";
    public const string TopicManage = "topic.manage";
    public const string TopicManageParticipants = "topic.participants.manage";
    public const string TopicAppointManagers = "topic.managers.appoint";
    public const string PostEditOwn = "post.edit-own";
    public const string PostDeleteOwn = "post.delete-own";
    public const string PostRemove = "post.remove";
    public const string PostReport = "post.report";

    public static readonly string[] All = [HostRead, HostManage, TangentCreate, TangentRead, TangentManage,
        TangentManageParticipants, TopicCreate, TopicRead, TopicReply, TopicManage, TopicManageParticipants,
        TopicAppointManagers, PostEditOwn, PostDeleteOwn, PostRemove, PostReport];
}

public static class TangentRoleScopes
{
    public const string Tenant = "tangent-space";
    public const string Host = "host";
    public const string Tangent = "tangent";
    public const string Topic = "topic";
    public static ScopedRoleScopeRef HostScope => new(Tenant, Host, "site");
    public static ScopedRoleScopeRef TangentScope(string key) => new(Tenant, Tangent, key);
    public static ScopedRoleScopeRef TopicScope(string key) => new(Tenant, Topic, key);
}

/// <summary>The application vocabulary exposed by Koan's headless descriptor and management API.</summary>
public sealed class TangentRoleCatalog : IScopedRoleCatalogContributor
{
    private static readonly string[] scopeTypes = [TangentRoleScopes.Host, TangentRoleScopes.Tangent, TangentRoleScopes.Topic];

    public void Describe(ScopedRoleCatalogBuilder catalog)
    {
        catalog.Scope(TangentRoleScopes.Host)
            .Scope(TangentRoleScopes.Tangent, TangentRoleScopes.Host)
            .Scope(TangentRoleScopes.Topic, TangentRoleScopes.Tangent);
        Add(TangentRoleCapabilities.HostRead, "Enter host", "See this Tangent Space host.");
        Add(TangentRoleCapabilities.HostManage, "Manage host", "Change host settings and policy.");
        Add(TangentRoleCapabilities.TangentCreate, "Create Tangents", "Create a community on this host.");
        Add(TangentRoleCapabilities.TangentRead, "Enter Tangent", "See and enter a community.");
        Add(TangentRoleCapabilities.TangentManage, "Manage Tangent", "Change a community and its policy.");
        Add(TangentRoleCapabilities.TangentManageParticipants, "Manage Tangent people", "Invite or change community participants.");
        Add(TangentRoleCapabilities.TopicCreate, "Create Topics", "Create a conversation in a community.");
        Add(TangentRoleCapabilities.TopicRead, "Read Topic", "Read a conversation and its history.", true);
        Add(TangentRoleCapabilities.TopicReply, "Reply", "Add a Post to a conversation.");
        Add(TangentRoleCapabilities.TopicManage, "Manage Topic", "Change conversation settings and moderate it.");
        Add(TangentRoleCapabilities.TopicManageParticipants, "Manage Topic people", "Invite or change conversation participants.");
        Add(TangentRoleCapabilities.TopicAppointManagers, "Appoint Topic managers", "Delegate Topic management.");
        Add(TangentRoleCapabilities.PostEditOwn, "Edit own Posts", "Edit a Post authored by the participant.");
        Add(TangentRoleCapabilities.PostDeleteOwn, "Delete own Posts", "Delete a Post authored by the participant.");
        Add(TangentRoleCapabilities.PostRemove, "Remove Posts", "Moderate another participant's Post.");
        Add(TangentRoleCapabilities.PostReport, "Report Posts", "Report another participant's Post.");
        return;

        void Add(string key, string label, string description, bool anonymous = false)
            => catalog.Capability(key, scopeTypes, label, description, anonymous);
    }
}
