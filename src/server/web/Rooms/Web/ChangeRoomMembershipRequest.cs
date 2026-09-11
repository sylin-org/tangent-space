using Newtonsoft.Json;

namespace TangentSpace.Rooms.Web;

[JsonObject(MissingMemberHandling = MissingMemberHandling.Error)]
public sealed record ChangeRoomMembershipRequest(
    [property: JsonProperty(Required = Required.Always)] RoomRole Role);
