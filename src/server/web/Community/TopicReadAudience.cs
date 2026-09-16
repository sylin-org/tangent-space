using System.Text.Json.Serialization;

namespace Tangent.Community;

[JsonConverter(typeof(JsonStringEnumConverter<TopicReadAudience>))]
public enum TopicReadAudience
{
    Restricted,
    Public
}
