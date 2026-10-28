using Newtonsoft.Json;
using Tangent.Community;
using Tangent.Stewardship;

namespace Tangent.Api;

[JsonObject(MissingMemberHandling = MissingMemberHandling.Error)]
public sealed record CreateTangentRequest(
    [property: JsonProperty(Required = Required.Always)] string Key,
    [property: JsonProperty(Required = Required.Always)] string Name,
    [property: JsonProperty(Required = Required.Always)] string Description,
    [property: JsonProperty(Required = Required.Always)] string Motto,
    [property: JsonProperty(Required = Required.Always)] string Accent,
    [property: JsonProperty(Required = Required.Always)] string Artwork,
    [property: JsonProperty] string? Kind = null, [property: JsonProperty] string? ReadAudience = null);

// PATCH deliberately permits any subset while rejecting unknown fields. Null preserves
// the current card field; callers send an empty string to clear an optional field.
[JsonObject(MissingMemberHandling = MissingMemberHandling.Error)]
public sealed record ChangeTangentRequest(
    [property: JsonProperty] string? Name,
    [property: JsonProperty] string? Description,
    [property: JsonProperty] string? Motto,
    [property: JsonProperty] string? Accent,
    [property: JsonProperty] string? Artwork,
    [property: JsonProperty] string? Kind = null, [property: JsonProperty] string? ReadAudience = null);

[JsonObject(MissingMemberHandling = MissingMemberHandling.Error)]
public sealed record CreateTangentTopicRequest(
    [property: JsonProperty(Required = Required.Always)] string Key,
    [property: JsonProperty(Required = Required.Always)] string Title,
    [property: JsonProperty] string? Topic);

[JsonObject(MissingMemberHandling = MissingMemberHandling.Error)]
public sealed record ChangeTangentMembershipRequest(
    [property: JsonProperty(Required = Required.Always)] TangentRole Role);

[JsonObject(MissingMemberHandling = MissingMemberHandling.Error)]
public sealed record ChangeParticipantRoleRequest(
    [property: JsonProperty(Required = Required.Always)] ParticipantRole Role);

[JsonObject(MissingMemberHandling = MissingMemberHandling.Error)]
public sealed record ChangeTangentAdmissionRequest(
    [property: JsonProperty(Required = Required.Always)] TangentAdmission Admission);
