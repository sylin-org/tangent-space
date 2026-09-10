namespace TangentSpace.Rooms.Web;

public sealed record ChangeRoomSettingsRequest(bool AllowPostEditing, bool IsLocked, string? Title = null, string? Topic = null);
