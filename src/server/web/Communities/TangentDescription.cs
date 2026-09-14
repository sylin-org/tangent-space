using TangentSpace.Rooms;
using TangentSpace.Authorization;

namespace TangentSpace.Communities;

public sealed record TangentDescription(string Key, string Name, string Description, string Motto, string Accent, string Artwork,
    string OwnerParticipantId, bool IsOwner, bool CanManage, IReadOnlyList<RoomDescription> Channels, bool ChannelsTruncated = false,
    int? NextChannelsPage = null, bool ChannelsIncomplete = false, bool IsMember = false, TangentRole? MembershipRole = null,
    TangentAdmission Admission = TangentAdmission.Invite, bool MembershipPending = false,
    bool CanCreateTopic = false, PermissionView? Permissions = null);

public sealed record TangentsResponse(IReadOnlyList<TangentDescription> Tangents, bool CanCreate, bool SetupRequired,
    int Page = 1, int? NextPage = null, bool DirectoryIncomplete = false);

public sealed record TangentChannelCreation(RoomAdministrationResult Result, RoomDescription? Channel);

public sealed record TangentMembershipResult(string TangentKey, string ParticipantId, TangentRole Role);

/// <summary>Bound, actor-filtered source used by the activity overview and channel continuation.</summary>
public sealed record TangentChannelDirectory(IReadOnlyList<RoomDescription> Channels, int Page, int? NextPage,
    bool ScanLimited = false);
