using System.Text.Json.Serialization;

namespace Tangent.Stewardship;

[JsonConverter(typeof(JsonStringEnumConverter<RestrictionKind>))]
public enum RestrictionKind { None, Timeout, Ban }
