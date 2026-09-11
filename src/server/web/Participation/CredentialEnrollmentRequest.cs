using System.Text.Json.Serialization;

namespace TangentSpace.Participation;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[Newtonsoft.Json.JsonObject(MissingMemberHandling = Newtonsoft.Json.MissingMemberHandling.Error)]
public sealed class CredentialEnrollmentRequest
{
    public string Name { get; init; } = "";
    public int LifetimeDays { get; init; } = 7;
    public string[]? Grants { get; init; }
}
