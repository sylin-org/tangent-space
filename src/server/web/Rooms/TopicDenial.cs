using System.Text.Json.Serialization;

namespace TangentSpace.Rooms;

[JsonConverter(typeof(JsonStringEnumConverter<TopicDenial>))]
public enum TopicDenial { Forbidden, InvalidInput, NotFound, AlreadyExists, MembershipMismatch }
