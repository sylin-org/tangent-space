using Newtonsoft.Json;

namespace Tangent.Api;

[JsonObject(MissingMemberHandling = MissingMemberHandling.Error)]
public sealed record ChangeTopicDescriptionRequest(
    [property: JsonProperty(Required = Required.Always)] string Topic);
