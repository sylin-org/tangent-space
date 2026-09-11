using System.Text.Json.Serialization;

namespace TangentSpace.Rooms;

// Ownership is derived from persisted site/room state; it cannot be assigned through membership input.
[JsonConverter(typeof(JsonStringEnumConverter<RoomRole>))]
public enum RoomRole { Manager, Member, Reader, Removed }
