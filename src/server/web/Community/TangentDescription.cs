using Tangent.Stewardship;
using Tangent.Access;

namespace Tangent.Community;

public sealed record TangentDescription(string Key, string Name, string Description, string Motto, string Accent, string Artwork,
    string OwnerParticipantId, bool IsOwner, bool CanManage, IReadOnlyList<TopicDescription> Topics, bool TopicsTruncated = false,
    int? NextTopicsPage = null, bool TopicsIncomplete = false, bool IsMember = false, TangentRole? MembershipRole = null,
    TangentAdmission Admission = TangentAdmission.Invite, bool MembershipPending = false,
    bool CanCreateTopic = false, PermissionView? Permissions = null);

public sealed record TangentsResponse(IReadOnlyList<TangentDescription> Tangents, bool CanCreate,
    int Page = 1, int? NextPage = null, bool DirectoryIncomplete = false);

public sealed record TangentTopicCreation(TopicAdministrationResult Result, TopicDescription? Topic);

public sealed record TangentMembershipResult(string TangentKey, string ParticipantId, TangentRole Role);

/// <summary>Bound, actor-filtered source used by the activity overview and topic continuation.</summary>
public sealed record TangentTopicDirectory(IReadOnlyList<TopicDescription> Topics, int Page, int? NextPage,
    bool ScanLimited = false);
