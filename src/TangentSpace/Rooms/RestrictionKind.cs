using System.Text.Json.Serialization;

namespace TangentSpace.Rooms;

[JsonConverter(typeof(JsonStringEnumConverter<RestrictionKind>))]
public enum RestrictionKind { None, Timeout, Ban }
