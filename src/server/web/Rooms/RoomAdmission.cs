using System.Text.Json.Serialization;

namespace TangentSpace.Rooms;

[JsonConverter(typeof(JsonStringEnumConverter<RoomAdmission>))]
public enum RoomAdmission { SignedIn, InvitationOnly }
