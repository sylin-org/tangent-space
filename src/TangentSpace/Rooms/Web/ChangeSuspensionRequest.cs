using Newtonsoft.Json;

namespace TangentSpace.Rooms.Web;

[JsonObject(MissingMemberHandling = MissingMemberHandling.Error)]
public sealed record ChangeSuspensionRequest(
    [property: JsonProperty(Required = Required.Always)] bool Suspended);
