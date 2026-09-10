using System.Text.Json.Nodes;
using Newtonsoft.Json.Linq;

namespace TangentSpace.Mcp;

// Public vocabulary changes without migrating stored references, receipts or native records.
public static class McpVocabulary
{
    private static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>
    {
        ["ListChannels"] = "ListTopics", ["ReadChannel"] = "ReadTopic",
        ["CreateChannel"] = "CreateTopic", ["PostMessage"] = "CreatePost",
        ["channelRef"] = "topicRef", ["messageRef"] = "postRef",
        ["aroundMessageRef"] = "aroundPostRef", ["throughMessageRef"] = "throughPostRef",
        ["firstChannelName"] = "firstTopicName", ["channels"] = "topics", ["messages"] = "posts",
        ["channel"] = "topic", ["message"] = "post"
    };
    public static string Public(string value) => Names.GetValueOrDefault(value, value);
    public static string Internal(string value) => Names.FirstOrDefault(x => x.Value == value).Key ?? value;
    public static JObject Arguments(JObject value) => new(value.Properties().Select(p => new JProperty(p.Name is "topic" or "post" ? p.Name : Internal(p.Name), p.Value.DeepClone())));

    public static JsonNode Outbound(JsonNode node, string? field = null)
    {
        if (node is JsonObject obj)
            return new JsonObject(obj.Select(p => KeyValuePair.Create(p.Key is "message" or "channel" && p.Value is not JsonObject ? p.Key : Public(p.Key), p.Value is null ? null : Outbound(p.Value, p.Key))));
        if (node is JsonArray array)
            return new JsonArray(array.Select(v => v is null ? null : Outbound(v, field)).ToArray());
        if (node is JsonValue value && value.TryGetValue<string>(out var text)
            && field is "operation" or "tool" or "available" or "field" or "kind") return JsonValue.Create(Public(text))!;
        return node.DeepClone();
    }
}
