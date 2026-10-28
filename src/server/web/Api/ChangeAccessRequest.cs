using Newtonsoft.Json;
using Tangent.Access;

namespace Tangent.Api;

[JsonObject(MissingMemberHandling = MissingMemberHandling.Error)]
public sealed record ChangeAccessRequest(
    [property: JsonProperty(Required = Required.Always)] AccessMap Access,
    [property: JsonProperty(Required = Required.Always)] bool PublishExistingHistory);
