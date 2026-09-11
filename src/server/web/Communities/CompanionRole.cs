using System.Text.Json.Serialization;

namespace TangentSpace.Communities;

/// <summary>Contract-facing role vocabulary for invitations and SetRole; maps onto TangentRole/RoomRole inside the domain.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CompanionRole>))]
public enum CompanionRole { Admin, Member, Reader }
