namespace TangentSpace.Rooms.Web;

/// <summary>The deliberately small, allowlisted representation safe for anonymous caches and documents.</summary>
public sealed record PublicTopicDescription(string Key, string TangentKey, string Title, string Topic,
    RoomReadAudience ReadAudience, string Path)
{
    internal static PublicTopicDescription From(Room room)
        => new(room.Id, room.TangentKey, room.Title, room.Topic, room.ReadAudience,
            $"/t/{Uri.EscapeDataString(room.TangentKey)}/topics/{Uri.EscapeDataString(room.Id)}");
}
