using System.Text.Json.Serialization;

namespace TangentSpace.Communities;

[JsonConverter(typeof(JsonStringEnumConverter<TangentDenial>))]
public enum TangentDenial { Forbidden, InvalidInput, NotFound, AlreadyExists, MembershipMismatch }
