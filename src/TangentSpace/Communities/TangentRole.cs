using System.Text.Json.Serialization;

namespace TangentSpace.Communities;

[JsonConverter(typeof(JsonStringEnumConverter<TangentRole>))]
public enum TangentRole { Member, Removed }
