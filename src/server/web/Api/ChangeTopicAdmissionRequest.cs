using Newtonsoft.Json;
using Tangent.Community;

namespace Tangent.Api;

[JsonObject(MissingMemberHandling = MissingMemberHandling.Error)]
public sealed record ChangeTopicAdmissionRequest(
    [property: JsonProperty(Required = Required.Always)] TopicAdmission Admission);
