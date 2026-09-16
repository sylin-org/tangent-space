using System.Text.Json.Serialization;

namespace TangentSpace.Rooms;

[JsonConverter(typeof(JsonStringEnumConverter<TopicReadAudience>))]
public enum TopicReadAudience
{
    Restricted,
    Public
}
