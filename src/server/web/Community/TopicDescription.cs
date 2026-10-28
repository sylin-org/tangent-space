using Tangent.Access;
namespace Tangent.Community;

public sealed record TopicDescription(string Key, string Title, string Topic, string CreatorParticipantId,
    long PolicyRevision, long SpacePolicyRevision,
    bool CanRead, bool CanWrite, bool CanManage, string AccessState, string TangentKey,
    bool AllowPostEditing = true, bool IsLocked = false,
    PermissionView? Permissions = null,
    TopicReadAudience ReadAudience = TopicReadAudience.Restricted,
    bool PubliclyReadable = false,
    DateTimeOffset? CreatedAt = null, TopicDirectorySummary? Summary = null,
    string Presentation = TopicPresentation.Conversation)
{
    internal static TopicDescription From(Topic topic, TopicPolicy policy, Tangent? tangent)
        => new(topic.Id, topic.Title, topic.Description, topic.CreatorParticipantId,
            policy.SelectedPolicyRevision, policy.SpacePolicyRevision, policy.CanRead, policy.CanWrite,
            policy.CanManage, policy.Reason, topic.TangentKey,
            topic.AllowPostEditing, topic.IsLocked, Access.Permissions.Topic(policy), topic.ReadAudience,
            topic.IsPubliclyReadable(tangent), topic.CreatedAt, Presentation: TangentKind.TopicPresentation(tangent?.Kind ?? TangentKind.Discussions));
}
