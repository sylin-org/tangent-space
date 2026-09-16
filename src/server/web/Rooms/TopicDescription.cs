namespace TangentSpace.Rooms;

public sealed record TopicDescription(string Key, string Title, string Topic, string CreatorParticipantId,
    TopicAdmission Admission, long PolicyRevision, long SpacePolicyRevision,
    bool CanRead, bool CanWrite, bool CanManage, bool CanAppointManagers, string AccessState, string TangentKey,
    bool MembersOnly = false, bool AllowPostEditing = false, bool IsLocked = false,
    TangentSpace.Authorization.PermissionView? Permissions = null,
    TopicReadAudience ReadAudience = TopicReadAudience.Restricted)
{
    internal static TopicDescription From(Topic topic, TopicPolicy policy)
        => new(topic.Id, topic.Title, topic.Description, topic.CreatorParticipantId, topic.Admission,
            policy.SelectedPolicyRevision, policy.SpacePolicyRevision, policy.CanRead, policy.CanWrite,
            policy.CanManage, policy.CanAppointManagers, policy.Reason, topic.TangentKey, topic.MembersOnly,
            topic.AllowPostEditing, topic.IsLocked, TangentSpace.Authorization.Permissions.Topic(policy), topic.ReadAudience);
}
