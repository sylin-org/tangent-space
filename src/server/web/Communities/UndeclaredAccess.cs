using System.Text.Json.Serialization;

namespace TangentSpace.Communities;

/// <summary>
/// Access granted to an undeclared participant. The persisted zero value is Write so records migrated
/// before this field existed keep every existing participant writing; every preset (including Everyone)
/// honors an explicitly stored value.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<UndeclaredAccess>))]
public enum UndeclaredAccess { Write, Deny, Read }
