using System.Text.Json.Serialization;

namespace TangentSpace.Communities;

[JsonConverter(typeof(JsonStringEnumConverter<TangentRole>))]
// Member=0 and Removed=1 are fixed by what is already stored; later roles are appended, never inserted.
public enum TangentRole { Member, Removed, Admin, Reader, Left }
