namespace Tangent.Api;

public sealed record ChangeTopicSettingsRequest(bool? AllowPostEditing = null, bool? IsLocked = null,
    string? Title = null, string? Topic = null);
