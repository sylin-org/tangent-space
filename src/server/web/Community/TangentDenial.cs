using System.Text.Json.Serialization;

namespace Tangent.Community;

[JsonConverter(typeof(JsonStringEnumConverter<TangentDenial>))]
public enum TangentDenial { Forbidden, InvalidInput, NotFound, AlreadyExists, MembershipMismatch }
