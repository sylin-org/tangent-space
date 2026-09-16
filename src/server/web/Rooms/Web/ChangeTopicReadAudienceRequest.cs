using Newtonsoft.Json;

namespace TangentSpace.Rooms.Web;

[JsonObject(MissingMemberHandling = MissingMemberHandling.Error)]
public sealed record ChangeTopicReadAudienceRequest(
    [property: JsonProperty(Required = Required.Always)] TopicReadAudience Audience,
    [property: JsonProperty(Required = Required.Always)] bool PublishExistingHistory);
