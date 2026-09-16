namespace TangentSpace.Rooms.Web;

public sealed record ChangeTopicSettingsRequest(bool AllowPostEditing, bool IsLocked, string? Title = null, string? Topic = null);
