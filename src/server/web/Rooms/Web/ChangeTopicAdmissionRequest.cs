using Newtonsoft.Json;

namespace TangentSpace.Rooms.Web;

[JsonObject(MissingMemberHandling = MissingMemberHandling.Error)]
public sealed record ChangeTopicAdmissionRequest(
    [property: JsonProperty(Required = Required.Always)] TopicAdmission Admission);
