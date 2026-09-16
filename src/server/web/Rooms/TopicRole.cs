using System.Text.Json.Serialization;

namespace TangentSpace.Rooms;

// Ownership is derived from persisted space/topic state; it cannot be assigned through membership input.
[JsonConverter(typeof(JsonStringEnumConverter<TopicRole>))]
public enum TopicRole { Manager, Member, Reader, Removed }
