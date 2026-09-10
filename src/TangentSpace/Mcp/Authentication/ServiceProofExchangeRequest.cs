using System.Text.Json.Serialization;

namespace TangentSpace.Mcp.Authentication;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[Newtonsoft.Json.JsonObject(MissingMemberHandling = Newtonsoft.Json.MissingMemberHandling.Error)]
public sealed class ServiceProofExchangeRequest
{
    public string? Name { get; init; }
    public int? LifetimeDays { get; init; }
    public string[]? Grants { get; init; }
}
