namespace Tangent.Api;

public sealed record ChangeTopicSettingsRequest(bool AllowPostEditing, bool IsLocked, string? Title = null, string? Topic = null);
