using System.Text.Json.Serialization;

namespace Tangent.Community;

[JsonConverter(typeof(JsonStringEnumConverter<TopicDenial>))]
public enum TopicDenial { Forbidden, InvalidInput, NotFound, AlreadyExists, MembershipMismatch }
