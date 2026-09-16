using Newtonsoft.Json;
using Tangent.Community;

namespace Tangent.Api;

[JsonObject(MissingMemberHandling = MissingMemberHandling.Error)]
public sealed record ChangeTopicMembershipRequest(
    [property: JsonProperty(Required = Required.Always)] TopicRole Role);
