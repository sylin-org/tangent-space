using System.Text.Json.Serialization;

namespace Tangent.Stewardship;

[JsonConverter(typeof(JsonStringEnumConverter<RestrictionScope>))]
public enum RestrictionScope { Tangent, Topic }
