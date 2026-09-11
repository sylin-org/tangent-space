using System.Text.Json.Serialization;

namespace TangentSpace.Rooms;

[JsonConverter(typeof(JsonStringEnumConverter<RoomDenial>))]
public enum RoomDenial { Forbidden, InvalidInput, NotFound, AlreadyExists, PolicyChanged, SpaceAlreadyMapped, MembershipMismatch }
