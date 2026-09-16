using System.Text.Json.Serialization;

namespace TangentSpace.Rooms;

[JsonConverter(typeof(JsonStringEnumConverter<TopicAdministration>))]
public enum TopicAdministration { Create, SetMembership, SetTopic, SetAdmission, SetSuspension, SetRestriction, SetSettings, SetReadAudience, SetAccess }
