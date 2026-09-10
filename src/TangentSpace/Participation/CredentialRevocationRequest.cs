using System.Text.Json.Serialization;

namespace TangentSpace.Participation;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[Newtonsoft.Json.JsonObject(MissingMemberHandling = Newtonsoft.Json.MissingMemberHandling.Error)]
public sealed record CredentialRevocationRequest;
