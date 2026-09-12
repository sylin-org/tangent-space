using System.Text.Json.Nodes;
using Koan.Data.Core;
using Newtonsoft.Json.Linq;
using TangentSpace.Rooms;

namespace TangentSpace.Mcp;

public sealed partial class McpOperationDispatcher
{
    private async Task ValidateReferences(JObject arguments, CancellationToken ct)
    {
        using var fresh = EntityContext.NoCache();
        foreach (var field in new[] { "channelRef", "messageRef", "scopeRef", "aroundMessageRef", "replyTo" })
        {
            var value = arguments[field]?.Type == JTokenType.String ? (string?)arguments[field] : null;
            var channel = refs.ParseChannel(value);
            if (refs.ParseMessage(value) is { } message) channel = (message.TangentKey, message.RoomKey);
            if (channel is not { } target) continue;
            var room = await Room.Get(target.RoomKey, ct);
            if (room is null || room.TangentKey != target.TangentKey) throw new UnauthorizedAccessException();
        }
    }

    private static void ClampCapabilities(JsonNode? node, bool read, bool post, bool manage)
    {
        if (node is JsonObject obj)
        {
            if (obj["allowedActions"] is JsonArray actions)
                obj["allowedActions"] = new JsonArray(actions.Where(a => ActionGranted(a?.GetValue<string>() ?? "", read, post, manage)).Select(a => a?.DeepClone()).ToArray());
            if (!read && obj.ContainsKey("canRead")) obj["canRead"] = false;
            if (!post && obj.ContainsKey("canPost")) obj["canPost"] = false;
            foreach (var child in obj.ToArray()) ClampCapabilities(child.Value, read, post, manage);
        }
        else if (node is JsonArray array)
            foreach (var child in array) ClampCapabilities(child, read, post, manage);
    }

    private async Task AuthorizeReplay(McpContext context, McpRequestRecord record, CancellationToken ct)
    {
        await activity.EnsureParticipantActive(context.ParticipantId, context.CredentialId, ct);
        if (record.Operation is "DeclareParticipant" or "ClaimServer") return;
        if (record.Operation == "ConfigureServer") { if (!(await server.Read(context.ParticipantId, ct)).CanManage) throw new UnauthorizedAccessException(); return; }
        if (record.Operation == "ConfigureTangent") { var key = refs.ParseTangent(record.TargetKey); if (key is null || (await FindTangent(context, key, ct))?.CanManage != true) throw new UnauthorizedAccessException(); return; }
        if (record.Operation == "ConfigureTopic") { var key = refs.ParseChannel(record.TargetKey); if (key is null || !(await conversation.ReadPolicy(context.ParticipantId, key.Value.RoomKey, ct)).CanManage) throw new UnauthorizedAccessException(); return; }
        if (record.Operation is "LeaveTangent") return; // Only the caller's departure receipt, no private data.
        using var fresh = EntityContext.NoCache();
        var room = await Room.Get(record.TargetKey, ct);
        if (record.Operation is "SetRole" or "SetRestriction" or "InviteParticipant" or "SetParticipationPolicy" or "CreateChannel")
        {
            if (!await companions.CanAdminister(context.ParticipantId,
                    room?.TangentKey ?? record.TargetKey, room?.Id, ct)) throw new UnauthorizedAccessException();
        }
        else if (room is not null)
            await conversation.ReadPolicy(context.ParticipantId, room.Id, ct);
        else if (record.Operation != "CreateTangent" && !await tangents.CanAccess(context.ParticipantId, record.TargetKey, ct))
            throw new UnauthorizedAccessException();
        if (record.ResultRef is { } reference && !await RefReadable(context.ParticipantId, reference, ct))
            throw new UnauthorizedAccessException();
    }

    private async Task RefreshReplayData(string did, JsonNode? node, CancellationToken ct)
    {
        if (node is JsonObject obj)
        {
            if (obj["channelRef"] is JsonValue value && value.TryGetValue<string>(out var reference)
                && refs.ParseChannel(reference) is { } target)
            {
                var room = await rooms.Describe(did, target.RoomKey, ct);
                if (room is null || !room.CanRead || room.TangentKey != target.TangentKey) throw new UnauthorizedAccessException();
                if (obj.ContainsKey("canRead")) obj["canRead"] = room.CanRead;
                if (obj.ContainsKey("canPost")) obj["canPost"] = room.CanWrite;
            }
            foreach (var child in obj.ToArray()) await RefreshReplayData(did, child.Value, ct);
        }
        else if (node is JsonArray array)
            foreach (var child in array) await RefreshReplayData(did, child, ct);
    }
}
