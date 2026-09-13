using System.Text.Json.Serialization;

namespace TangentSpace.Rooms;

[JsonConverter(typeof(JsonStringEnumConverter<RoomReadAudience>))]
public enum RoomReadAudience
{
    Restricted,
    Public
}
