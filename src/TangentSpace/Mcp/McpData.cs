namespace TangentSpace.Mcp;

// Typed result.data payloads for the wire envelope. Field names and shapes follow tools.json exactly.

public sealed record McpMessageDto(string MessageRef, string AuthorDid, string Author, string Text,
    string CreatedAt, string? ReplyTo, bool Removed, TangentSpace.Authorization.PermissionView? Permissions = null, string? EditedAt = null);

public sealed record McpTangentDto(string TangentRef, string Name, string Description, string Membership,
    string Admission, bool CanJoin, bool CanRead, bool CanPost, TangentSpace.Authorization.PermissionView? Permissions = null);

public sealed record McpChannelDto(string ChannelRef, string Name, string Topic, bool CanRead, bool CanPost, TangentSpace.Authorization.PermissionView? Permissions = null);

public sealed record McpReadData(IReadOnlyList<McpMessageDto> Messages, string? OlderCursor, string? NewerCursor,
    string? ReadCursor, string Position);

public sealed record McpListTangentsData(IReadOnlyList<McpTangentDto> Tangents, string? NextCursor, bool Incomplete = false);

public sealed record McpJoinData(string Membership, string Welcome, IReadOnlyList<McpChannelDto> Channels, string? NextCursor, bool Incomplete = false);

public sealed record McpListChannelsData(IReadOnlyList<McpChannelDto> Channels, string? NextCursor, bool Incomplete = false);

public sealed record McpPostData(McpMessageDto Message);

public sealed record McpUpdatesData(IReadOnlyList<McpNotice> Notices, string? NextCursor, string Checkpoint, bool Incomplete);

public sealed record McpMarkReadData(string? ThroughMessageRef, string AcknowledgementScope);

public sealed record McpSelectData(string CompanionId);

public sealed record McpArriveData(string Welcome, IReadOnlyList<McpTangentDto> Tangents, string? NextCursor, bool Incomplete = false);

public sealed record McpOperationData(string Operation, McpReceipt? Receipt);

public sealed record McpCreateTangentData(McpTangentDto Tangent, IReadOnlyList<McpChannelDto> Channels);

public sealed record McpCreateChannelData(McpChannelDto Channel);

public sealed record McpSetRoleData(string ScopeRef, string ParticipantDid, string Role);

public sealed record McpInviteData(string InviteRef, string InviteUrl, string Delivery);

public sealed record McpSetWatchData(string ScopeRef, string Mode);

public sealed record McpLeaveData(string Membership, bool HistoryRetained);

public sealed record McpPolicyData(string Admission, string Preset, string Undeclared);

public sealed record McpRestrictionData(string Restriction, string? Until, string AuditRef);
