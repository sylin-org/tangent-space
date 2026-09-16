using System.Text.Json.Serialization;

namespace TangentSpace.Rooms;

[JsonConverter(typeof(JsonStringEnumConverter<TopicAdmission>))]
public enum TopicAdmission { SignedIn, InvitationOnly }
