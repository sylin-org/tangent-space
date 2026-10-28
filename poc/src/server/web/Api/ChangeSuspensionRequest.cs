using Newtonsoft.Json;

namespace Tangent.Api;

[JsonObject(MissingMemberHandling = MissingMemberHandling.Error)]
public sealed record ChangeSuspensionRequest(
    [property: JsonProperty(Required = Required.Always)] bool Suspended);
