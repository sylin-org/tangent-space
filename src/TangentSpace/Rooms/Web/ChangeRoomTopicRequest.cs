using Newtonsoft.Json;

namespace TangentSpace.Rooms.Web;

[JsonObject(MissingMemberHandling = MissingMemberHandling.Error)]
public sealed record ChangeRoomTopicRequest(
    [property: JsonProperty(Required = Required.Always)] string Topic);
