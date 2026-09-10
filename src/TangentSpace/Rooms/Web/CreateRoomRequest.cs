using Newtonsoft.Json;

namespace TangentSpace.Rooms.Web;

[JsonObject(MissingMemberHandling = MissingMemberHandling.Error)]
public sealed record CreateRoomRequest(
    [property: JsonProperty(Required = Required.Always)] string Key,
    [property: JsonProperty(Required = Required.Always)] string Title,
    [property: JsonProperty(Required = Required.Always)] RoomAdmission Admission);
