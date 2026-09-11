using System.Text.Json.Serialization;

namespace TangentSpace.Rooms;

[JsonConverter(typeof(JsonStringEnumConverter<RestrictionScope>))]
public enum RestrictionScope { Tangent, Room }
