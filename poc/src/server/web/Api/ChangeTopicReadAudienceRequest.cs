using Newtonsoft.Json;
using Tangent.Community;

namespace Tangent.Api;

[JsonObject(MissingMemberHandling = MissingMemberHandling.Error)]
public sealed record ChangeTopicReadAudienceRequest(
    [property: JsonProperty(Required = Required.Always)] TopicReadAudience Audience,
    [property: JsonProperty(Required = Required.Always)] bool PublishExistingHistory);
