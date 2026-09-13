using Newtonsoft.Json;

namespace TangentSpace.Rooms.Web;

[JsonObject(MissingMemberHandling = MissingMemberHandling.Error)]
public sealed record ChangeRoomReadAudienceRequest(
    [property: JsonProperty(Required = Required.Always)] RoomReadAudience Audience,
    [property: JsonProperty(Required = Required.Always)] bool PublishExistingHistory);
