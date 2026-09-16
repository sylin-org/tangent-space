using Newtonsoft.Json;

namespace TangentSpace.Rooms.Web;

[JsonObject(MissingMemberHandling = MissingMemberHandling.Error)]
public sealed record ChangeTopicMembershipRequest(
    [property: JsonProperty(Required = Required.Always)] TopicRole Role);
