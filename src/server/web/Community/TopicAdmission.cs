using System.Text.Json.Serialization;

namespace Tangent.Community;

[JsonConverter(typeof(JsonStringEnumConverter<TopicAdmission>))]
public enum TopicAdmission { SignedIn, InvitationOnly }
