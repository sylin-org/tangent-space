using System.Text.Json.Serialization;

namespace TangentSpace.Rooms;

[JsonConverter(typeof(JsonStringEnumConverter<RoomAdministration>))]
public enum RoomAdministration { Create, SetMembership, SetTopic, SetAdmission, MapSpace, SetSuspension, Provision, SetRestriction, SetSettings, SetReadAudience }
