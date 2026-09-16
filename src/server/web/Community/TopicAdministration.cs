using System.Text.Json.Serialization;

namespace Tangent.Community;

[JsonConverter(typeof(JsonStringEnumConverter<TopicAdministration>))]
public enum TopicAdministration { Create, SetMembership, SetTopic, SetAdmission, SetSuspension, SetRestriction, SetSettings, SetReadAudience, SetAccess }
