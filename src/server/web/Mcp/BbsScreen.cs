using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TangentSpace.Mcp;

/// <summary>
/// Deterministic BBS text projection of the structured envelope, a direct port of the contract's
/// renderer. Participant content is escaped: no terminal controls and no bracket-delimiter spoofing.
/// </summary>
public static class BbsScreen
{
    public static string Render(McpEnvelope envelope)
    {
        var lines = new List<string>
        {
            $"TANGENT / {McpVocabulary.Public(envelope.Operation)} / {envelope.Status.ToUpperInvariant()}",
            "",
            "[IDENTITY]"
        };
        lines.Add(envelope.Identity is { } identity
            ? $"{Plain(identity.DisplayName)} ({Plain(identity.ActingAs)}) | {envelope.CompanionId} | {envelope.ContextId ?? "Choose a server"}"
            : "No companion selected.");
        var place = envelope.Place;
        lines.Add("");
        lines.Add("[PLACE]");
        lines.Add($"{Plain(place.Label)} | {place.Readiness}");
        if (place.ServerRef is { } server) lines.Add("serverRef: " + server);
        if (place.TangentRef is { } tangentRef) lines.Add("tangentRef: " + tangentRef);
        if (place.ChannelRef is { } channelRef) lines.Add("topicRef: " + channelRef);
        if (place.Access is {} access) { lines.Add("Role: " + access.Role + " / " + access.Scope); lines.Add("Actions: " + string.Join(", ", access.AllowedActions)); lines.Add("Policies: " + string.Join(", ", access.Restrictions.Select(p => p.Key + "=" + p.Value))); }
        lines.Add("Can: " + (place.Permissions.Count > 0 ? string.Join(", ", place.Permissions) : "no participation actions here"));
        lines.Add("");
        lines.Add("[RESULT]");
        var result = envelope.Result;
        if (result.Data is JsonNode wireData)
        {
            var type = envelope.Operation switch
            {
                "ReadChannel" => typeof(McpReadData), "PostMessage" => typeof(McpPostData),
                "ListTangents" => typeof(McpListTangentsData), "Arrive" => typeof(McpArriveData),
                "JoinTangent" => typeof(McpJoinData), "ListChannels" => typeof(McpListChannelsData),
                "GetUpdates" => typeof(McpUpdatesData), "GetOperation" => typeof(McpOperationData),
                "CreateTangent" => typeof(McpCreateTangentData), _ => null
            };
            if (type is not null) result = result with { Data = wireData.Deserialize(type, McpJson.Options) };
        }
        AppendResult(lines, result);
        if (result.Data is { } details && JsonSerializer.SerializeToNode(details, McpJson.Options) is JsonObject fields)
            foreach (var key in new[] { "olderCursor", "newerCursor", "readCursor", "position", "nextCursor", "checkpoint", "incomplete" })
                if (fields[key] is { } value) lines.Add(key + ": " + value.ToJsonString(McpJson.Options));
        var activity = envelope.Activity;
        lines.Add("");
        lines.Add($"[AROUND YOU] {activity.Coverage} | {activity.AsOf}");
        foreach (var notice in activity.Notices) lines.Add(NoticeText(notice));
        if (activity.Notices.Count == 0)
        {
            lines.Add(activity.Coverage switch
            {
                "not_connected" => "No connected places yet.",
                "unavailable" => "Activity is unavailable; this does not mean nothing happened.",
                _ => "No relevant updates in this snapshot."
            });
        }
        if (activity.More) lines.Add("More activity is available with GetUpdates.");
        lines.Add("");
        lines.Add("[NEXT]");
        lines.Add("Available: " + (envelope.Next.Available.Count > 0 ? string.Join(", ", envelope.Next.Available.Select(McpVocabulary.Public)) : "none in this profile"));
        foreach (var call in envelope.Next.Calls)
            lines.Add($"{Plain(call.Label)}: {McpVocabulary.Public(call.Tool)}({McpVocabulary.Outbound(JsonNode.Parse(call.ArgumentsJson)!).ToJsonString(McpJson.Options)})");
        return string.Join("\n", lines);
    }

    private static void AppendResult(List<string> lines, McpResult result)
    {
        if (result.Data is not null) AppendData(lines, result.Data);
        if (result.Receipt is { } receipt)
            lines.Add($"Receipt: {receipt.RequestId} / {receipt.State} / {receipt.OperationRef}");
        if (result.Problem is { } problem)
            lines.Add($"{problem.Code}: {Plain(problem.Message)}");
    }

    private static void AppendData(List<string> lines, object data)
    {
        switch (data)
        {
            case McpArriveData arrival:
                lines.Add(Plain(arrival.Welcome));
                AppendData(lines, new McpListTangentsData(arrival.Tangents, arrival.NextCursor, arrival.Incomplete));
                break;
            case McpReadData read:
                foreach (var message in read.Messages)
                {
                    lines.Add($"{message.MessageRef} | {Plain(message.Author)} | {message.CreatedAt}");
                    lines.Add("  " + (message.Removed ? "[Removed]" : Plain(message.Text)));
                    if (message.ReplyTo is { } reply) lines.Add("  Reply to: " + reply);
                }
                if (read.Messages.Count == 0) lines.Add("No retained messages in this window.");
                break;
            case McpListTangentsData list when list.Tangents is { Count: > 0 }:
                foreach (var tangent in list.Tangents) AppendRow(lines, tangent.Name, tangent.TangentRef, tangent.Membership, tangent.CanPost);
                break;
            case McpListTangentsData:
                lines.Add("No visible tangents on this page.");
                break;
            case McpJoinData join:
                lines.Add("welcome: " + Plain(join.Welcome));
                foreach (var channel in join.Channels) AppendRow(lines, channel.Name, channel.ChannelRef, null, channel.CanPost);
                if (join.Channels.Count == 0) lines.Add("No visible topics on this page.");
                break;
            case McpListChannelsData channels:
                foreach (var channel in channels.Channels) AppendRow(lines, channel.Name, channel.ChannelRef, null, channel.CanPost);
                if (channels.Channels.Count == 0) lines.Add("No visible topics on this page.");
                break;
            case McpPostData post:
                AppendData(lines, new McpReadData([post.Message], null, null, null, "unread"));
                break;
            case McpUpdatesData updates:
                foreach (var notice in updates.Notices) lines.Add(NoticeText(notice));
                if (updates.Notices.Count == 0) lines.Add("You are caught up in the observed places.");
                break;
            case McpOperationData operation:
                lines.Add("operation: " + McpVocabulary.Public(operation.Operation));
                if (operation.Receipt is { } receipt)
                    lines.Add($"Receipt: {receipt.RequestId} / {receipt.State} / {receipt.OperationRef}");
                break;
            case McpCreateTangentData created:
                AppendRow(lines, created.Tangent.Name, created.Tangent.TangentRef, created.Tangent.Membership, created.Tangent.CanPost);
                foreach (var channel in created.Channels) AppendRow(lines, channel.Name, channel.ChannelRef, null, channel.CanPost);
                break;
            default:
                foreach (var line in DescribeScalar(data)) lines.Add(line);
                break;
        }
    }

    private static IEnumerable<string> DescribeScalar(object data)
    {
        var node = JsonSerializer.SerializeToNode(data, McpJson.Options);
        if (node is JsonObject obj)
        {
            foreach (var property in obj)
            {
                if (property.Value is JsonValue value && value.TryGetValue<string>(out var scalar))
                    yield return $"{property.Key}: {Plain(scalar)}";
                else
                    yield return $"{property.Key}: {property.Value?.ToJsonString(McpJson.Options)}";
            }
        }
        else if (node is not null) yield return node.ToJsonString(McpJson.Options);
    }

    private static void AppendRow(List<string> lines, string label, string address, string? membership, bool? canPost)
    {
        var flags = new List<string>();
        if (membership is not null) flags.Add(membership);
        if (canPost is { } post) flags.Add(post ? "can reply" : "cannot reply");
        lines.Add($"- {Plain(label)} | {address}" + (flags.Count > 0 ? " | " + string.Join(", ", flags) : ""));
    }

    internal static string NoticeText(McpNotice notice)
        => $"- {Plain(notice.Label)}: {Count(notice.Unread)} unread, {Count(notice.RepliesToYou)} replies to you, " +
           $"{Count(notice.Mentions)} mentions | {notice.ChannelRef}";

    private static string Count(McpCount count) => count.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
        + (count.AtLeast ? "+" : "");

    /// <summary>Participant content cannot impersonate screen sections or terminal controls.</summary>
    internal static string Plain(string value)
    {
        var escaped = new StringBuilder(value.Length + 8);
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': escaped.Append("\\\""); break;
                case '\\': escaped.Append("\\\\"); break;
                case '\n': escaped.Append("\\n"); break;
                case '\r': escaped.Append("\\r"); break;
                case '\t': escaped.Append("\\t"); break;
                case '\b': escaped.Append("\\b"); break;
                case '\f': escaped.Append("\\f"); break;
                case < ' ': escaped.Append("\\u").Append(((int)c).ToString("x4")); break;
                default: escaped.Append(c); break;
            }
        }
        return escaped.Replace("[", "\\u005b").Replace("]", "\\u005d").ToString();
    }
}
