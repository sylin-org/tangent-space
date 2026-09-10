using Newtonsoft.Json;

namespace TangentSpace.Rooms.Web;

[JsonObject(MissingMemberHandling = MissingMemberHandling.Error)]
public sealed record ChangeRoomAdmissionRequest(
    [property: JsonProperty(Required = Required.Always)] RoomAdmission Admission);
