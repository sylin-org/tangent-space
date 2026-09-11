using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TangentSpace.Mcp;

/// <summary>Wire JSON helpers. System.Text.Json end to end; Newtonsoft JObject values are read as
/// primitives and never passed through to System.Text.Json serialization.</summary>
public static class McpJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Envelope wire object with the contract's fixed key order.</summary>
    public static JsonObject ToWire(McpEnvelope envelope)
    {
        var segments = new JsonObject
        {
            ["identity"] = envelope.Identity is null ? null : new JsonObject
            {
                ["did"] = envelope.Identity.Did,
                ["actingAs"] = envelope.Identity.ActingAs,
                ["displayName"] = envelope.Identity.DisplayName,
                ["expiresAt"] = envelope.Identity.ExpiresAt
            },
            ["place"] = new JsonObject
            {
                ["kind"] = envelope.Place.Kind,
                ["label"] = envelope.Place.Label,
                ["serverRef"] = envelope.Place.ServerRef,
                ["tangentRef"] = envelope.Place.TangentRef,
                ["channelRef"] = envelope.Place.ChannelRef,
                ["permissions"] = StringArray(envelope.Place.Permissions),
                ["access"] = JsonSerializer.SerializeToNode(envelope.Place.Access, Options),
                ["readiness"] = envelope.Place.Readiness
            },
            ["result"] = new JsonObject
            {
                ["data"] = envelope.Result.Data is null ? null : JsonNode.Parse(JsonSerializer.Serialize(envelope.Result.Data, Options)),
                ["receipt"] = Receipt(envelope.Result.Receipt),
                ["problem"] = envelope.Result.Problem is null ? null : new JsonObject
                {
                    ["code"] = envelope.Result.Problem.Code,
                    ["message"] = envelope.Result.Problem.Message,
                    ["field"] = envelope.Result.Problem.Field,
                    ["retryable"] = envelope.Result.Problem.Retryable
                }
            },
            ["activity"] = new JsonObject
            {
                ["asOf"] = envelope.Activity.AsOf,
                ["coverage"] = envelope.Activity.Coverage,
                ["notices"] = Notices(envelope.Activity.Notices),
                ["more"] = envelope.Activity.More
            },
            ["next"] = new JsonObject
            {
                ["available"] = StringArray(envelope.Next.Available),
                ["calls"] = new JsonArray(envelope.Next.Calls.Select(call => (JsonNode?)new JsonObject
                {
                    ["tool"] = call.Tool,
                    ["arguments"] = JsonNode.Parse(call.ArgumentsJson),
                    ["label"] = call.Label
                }).ToArray())
            }
        };
        return new JsonObject
        {
            ["contractVersion"] = McpEnvelope.ContractVersion,
            ["operation"] = envelope.Operation,
            ["status"] = envelope.Status,
            ["companionId"] = envelope.CompanionId,
            ["contextId"] = envelope.ContextId,
            ["segments"] = segments
        };
    }

    public static JsonObject? Receipt(McpReceipt? receipt) => receipt is null ? null : new JsonObject
    {
        ["requestId"] = receipt.RequestId,
        ["operationRef"] = receipt.OperationRef,
        ["state"] = receipt.State,
        ["resultRef"] = receipt.ResultRef,
        ["retryAfterSeconds"] = receipt.RetryAfterSeconds
    };

    public static JsonArray Notices(IEnumerable<McpNotice> notices) => new(notices.Select(Notice).ToArray());

    public static JsonNode Notice(McpNotice notice) => new JsonObject
    {
        ["channelRef"] = notice.ChannelRef,
        ["label"] = notice.Label,
        ["unread"] = Count(notice.Unread),
        ["repliesToYou"] = Count(notice.RepliesToYou),
        ["mentions"] = Count(notice.Mentions),
        ["revision"] = notice.Revision
    };

    private static JsonNode Count(McpCount count) => new JsonObject
    {
        ["value"] = count.Value,
        ["atLeast"] = count.AtLeast
    };

    public static JsonArray StringArray(IEnumerable<string> values)
        => new(values.Select(value => (JsonNode?)value).ToArray());

    public static string Serialize(JsonNode node) => node.ToJsonString(Options);

    /// <summary>Compact argument JSON for next.calls, preserving insertion order.</summary>
    public static string Arguments(IReadOnlyDictionary<string, string?> arguments)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = Options.Encoder }))
        {
            writer.WriteStartObject();
            foreach (var (key, value) in arguments)
            {
                if (value is null) writer.WriteNull(key);
                else writer.WriteString(key, value);
            }
            writer.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
}
