using System.Text.Json.Serialization;

namespace TangentSpace.Communities;

/// <summary>Contract-facing role vocabulary for invitations and SetRole; maps onto TangentRole/TopicRole inside the domain.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ParticipantRole>))]
public enum ParticipantRole { Admin, Member, Reader }
