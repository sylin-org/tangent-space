using System.Text.Json.Serialization;

namespace TangentSpace.Rooms;

[JsonConverter(typeof(JsonStringEnumConverter<RoomSpaceState>))]
public enum RoomSpaceState { Pending, Ready }
