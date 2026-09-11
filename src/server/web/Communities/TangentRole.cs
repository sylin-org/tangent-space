using System.Text.Json.Serialization;

namespace TangentSpace.Communities;

[JsonConverter(typeof(JsonStringEnumConverter<TangentRole>))]
// Persisted values Member=0 and Removed=1 are stable; Admin/Reader/Left were appended for companion participation.
public enum TangentRole { Member, Removed, Admin, Reader, Left }
