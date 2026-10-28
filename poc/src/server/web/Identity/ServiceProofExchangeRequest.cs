using System.Text.Json.Serialization;

namespace Tangent.Identity;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[Newtonsoft.Json.JsonObject(MissingMemberHandling = Newtonsoft.Json.MissingMemberHandling.Error)]
public sealed class ServiceProofExchangeRequest
{
    public string? Name { get; init; }
    public int? LifetimeDays { get; init; }
    public string[]? Grants { get; init; }
}
