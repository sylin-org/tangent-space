using Newtonsoft.Json.Linq;
using TangentSpace.Conversation;

namespace TangentSpace.Mcp;

/// <summary>
/// Strict typed parsing of tool arguments. Unknown members, wrong types and bound violations are
/// rejected with field-targeted messages before any domain call. Mirrors the catalog's input schemas.
/// </summary>
public static class McpArguments
{
    public sealed record SelectCompanionArgs(string Moniker);
    public sealed record ArriveArgs(string CompanionId, string ServerUrl);
    public sealed record ListTangentsArgs(string ContextId, string? ServerRef, string? Cursor, int Limit);
    public sealed record JoinTangentArgs(string ContextId, string? TangentRef, string? InviteRef, string RequestId);
    public sealed record ListChannelsArgs(string ContextId, string? TangentRef, string? Cursor, int Limit);
    public sealed record ReadChannelArgs(string ContextId, string? ChannelRef, string? Cursor, string? AroundMessageRef, int Limit);
    public sealed record PostMessageArgs(string ContextId, string? ChannelRef, string Text, string? ReplyTo, string RequestId);
    public sealed record GetUpdatesArgs(string ContextId, string? ScopeRef, string? Cursor, int Limit);
    public sealed record MarkReadArgs(string ContextId, string? ChannelRef, string ReadCursor, string RequestId);
    public sealed record LeaveTangentArgs(string ContextId, string? TangentRef, string RequestId);
    public sealed record SetWatchArgs(string ContextId, string ScopeRef, string Mode, string RequestId);
    public sealed record GetOperationArgs(string ContextId, string RequestId);
    public sealed record CreateTangentArgs(string ContextId, string? ServerRef, string Name, string? Description,
        string Visibility, string? FirstChannelName, string RequestId);
    public sealed record CreateChannelArgs(string ContextId, string? TangentRef, string Name, string? Topic,
        string Visibility, string RequestId);
    public sealed record InviteParticipantArgs(string ContextId, string? TangentRef, string ParticipantDid, string Role, string RequestId);
    public sealed record SetRoleArgs(string ContextId, string ScopeRef, string ParticipantDid, string Role, string RequestId);
    public sealed record SetPolicyArgs(string ContextId, string? TangentRef, string Admission, string Preset,
        string Undeclared, string RequestId);
    public sealed record SetRestrictionArgs(string ContextId, string? ScopeRef, string ParticipantDid, string Restriction,
        string? Until, string Reason, string RequestId);

    public static SelectCompanionArgs SelectCompanion(JObject args)
    {
        var reader = Reader(args, ["moniker"]);
        return new(reader.String("moniker", required: true, 1, 253));
    }

    public static ArriveArgs Arrive(JObject args)
    {
        var reader = Reader(args, ["companionId", "serverUrl"]);
        var companionId = reader.String("companionId", true, 1, 96);
        if (!companionId.StartsWith(McpSelection.IdPrefix, StringComparison.Ordinal)
            || companionId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw reader.Invalid("companionId", "Copy the companionId returned by SelectCompanion.");
        var serverUrl = reader.String("serverUrl", true, 1, 1024);
        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
            || uri.UserInfo.Length > 0 || uri.Fragment.Length > 0)
            throw reader.Invalid("serverUrl", "Supply the server's canonical HTTPS URL copied from discovery.");
        return new(companionId, serverUrl);
    }

    public static ListTangentsArgs ListTangents(JObject args)
    {
        var reader = Reader(args, ["contextId", "serverRef", "cursor", "limit"]);
        return new(reader.String("contextId", true, 1, 96), reader.String("serverRef", true, 1, 512),
            reader.String("cursor", false, 1, 4096), reader.Limit());
    }

    public static JoinTangentArgs JoinTangent(JObject args)
    {
        var reader = Reader(args, ["contextId", "tangentRef", "inviteRef", "requestId"]);
        return new(reader.String("contextId", true, 1, 96), reader.String("tangentRef", true, 1, 512),
            reader.String("inviteRef", false, 1, 512), reader.RequestId());
    }

    public static ListChannelsArgs ListChannels(JObject args)
    {
        var reader = Reader(args, ["contextId", "tangentRef", "cursor", "limit"]);
        return new(reader.String("contextId", true, 1, 96), reader.String("tangentRef", true, 1, 512),
            reader.String("cursor", false, 1, 4096), reader.Limit());
    }

    public static ReadChannelArgs ReadChannel(JObject args)
    {
        var reader = Reader(args, ["contextId", "channelRef", "cursor", "aroundMessageRef", "limit"]);
        var cursor = reader.String("cursor", false, 1, 4096);
        var around = reader.String("aroundMessageRef", false, 1, 512);
        if (cursor is not null && around is not null)
            throw reader.Invalid("cursor", "Choose a history cursor or an anchor message, not both.");
        return new(reader.String("contextId", true, 1, 96), reader.String("channelRef", true, 1, 512),
            cursor, around, reader.Limit());
    }

    public static PostMessageArgs PostMessage(JObject args)
    {
        var reader = Reader(args, ["contextId", "channelRef", "text", "replyTo", "requestId"]);
        var text = reader.String("text", true, 1, 4096);
        return new(reader.String("contextId", true, 1, 96), reader.String("channelRef", true, 1, 512),
            text, reader.String("replyTo", false, 1, 512), reader.RequestId());
    }

    public static GetUpdatesArgs GetUpdates(JObject args)
    {
        var reader = Reader(args, ["contextId", "scopeRef", "cursor", "limit"]);
        return new(reader.String("contextId", true, 1, 96), reader.String("scopeRef", false, 1, 512),
            reader.String("cursor", false, 1, 4096), reader.Limit());
    }

    public static MarkReadArgs MarkRead(JObject args)
    {
        var reader = Reader(args, ["contextId", "channelRef", "readCursor", "requestId"]);
        return new(reader.String("contextId", true, 1, 96), reader.String("channelRef", true, 1, 512),
            reader.String("readCursor", true, 1, 4096), reader.RequestId());
    }

    public static LeaveTangentArgs LeaveTangent(JObject args)
    {
        var reader = Reader(args, ["contextId", "tangentRef", "requestId"]);
        return new(reader.String("contextId", true, 1, 96), reader.String("tangentRef", true, 1, 512), reader.RequestId());
    }

    public static SetWatchArgs SetWatch(JObject args)
    {
        var reader = Reader(args, ["contextId", "scopeRef", "mode", "requestId"]);
        var mode = reader.String("mode", true, 3, 7);
        if (mode is not ("all" or "replies" or "none"))
            throw reader.Invalid("mode", "Choose watch mode all, replies, or none.");
        return new(reader.String("contextId", true, 1, 96), reader.String("scopeRef", true, 1, 512), mode, reader.RequestId());
    }

    public static GetOperationArgs GetOperation(JObject args)
    {
        var reader = Reader(args, ["contextId", "requestId"]);
        return new(reader.String("contextId", true, 1, 96), reader.RequestId());
    }

    public static CreateTangentArgs CreateTangent(JObject args)
    {
        var reader = Reader(args, ["contextId", "serverRef", "name", "description", "visibility", "firstChannelName", "requestId"]);
        var visibility = reader.Enum("visibility", "public", "members");
        return new(reader.String("contextId", true, 1, 96), reader.String("serverRef", true, 1, 512),
            reader.String("name", true, 1, 120), reader.String("description", false, 0, 1000),
            visibility, reader.String("firstChannelName", false, 1, 120), reader.RequestId());
    }

    public static CreateChannelArgs CreateChannel(JObject args)
    {
        var reader = Reader(args, ["contextId", "tangentRef", "name", "topic", "visibility", "requestId"]);
        var visibility = reader.Enum("visibility", "public", "members");
        return new(reader.String("contextId", true, 1, 96), reader.String("tangentRef", true, 1, 512),
            reader.String("name", true, 1, 120), reader.String("topic", false, 0, 2000), visibility, reader.RequestId());
    }

    public static InviteParticipantArgs InviteParticipant(JObject args)
    {
        var reader = Reader(args, ["contextId", "tangentRef", "participantDid", "role", "requestId"]);
        return new(reader.String("contextId", true, 1, 96), reader.String("tangentRef", true, 1, 512),
            reader.ParticipantDid(), reader.Enum("role", "admin", "member", "reader"), reader.RequestId());
    }

    public static SetRoleArgs SetRole(JObject args)
    {
        var reader = Reader(args, ["contextId", "scopeRef", "participantDid", "role", "requestId"]);
        return new(reader.String("contextId", true, 1, 96), reader.String("scopeRef", true, 1, 512),
            reader.ParticipantDid(), reader.Enum("role", "admin", "member", "reader"), reader.RequestId());
    }

    public static SetPolicyArgs SetPolicy(JObject args)
    {
        var reader = Reader(args, ["contextId", "tangentRef", "admission", "preset", "undeclared", "requestId"]);
        return new(reader.String("contextId", true, 1, 96), reader.String("tangentRef", true, 1, 512),
            reader.Enum("admission", "open", "approval", "invite"),
            reader.Enum("preset", "everyone", "humans_only", "agents_only", "humans_write_agents_read", "humans_read_agents_write"),
            reader.Enum("undeclared", "deny", "read", "write"), reader.RequestId());
    }

    public static SetRestrictionArgs SetRestriction(JObject args)
    {
        var reader = Reader(args, ["contextId", "scopeRef", "participantDid", "restriction", "until", "reason", "requestId"]);
        var until = reader.String("until", false, 1, 40);
        if (until is not null && (!DateTimeOffset.TryParse(until, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
            || parsed <= DateTimeOffset.MinValue))
            throw reader.Invalid("until", "Supply an absolute expiry timestamp for the timeout.");
        return new(reader.String("contextId", true, 1, 96), reader.String("scopeRef", true, 1, 512),
            reader.ParticipantDid(), reader.Enum("restriction", "timeout", "ban", "none"),
            until, reader.String("reason", true, 1, 280), reader.RequestId());
    }

    private static StrictReader Reader(JObject args, string[] allowed)
    {
        var reader = new StrictReader(args ?? [], allowed);
        // Unknown members are rejected up front per contract schemas (additionalProperties: false).
        reader.CheckUnknown();
        return reader;
    }

    public static void CheckText(string text)
    {
        try { MessageContent.CheckText(text); }
        catch (ArgumentException) { throw new McpInvalidArgumentsException("text", "A message must contain 1–4096 UTF-8 bytes and no null characters."); }
    }

    private sealed class StrictReader(JObject args, string[] allowed) : StrictFields(args, allowed)
    {
        public int Limit()
        {
            var limit = OptionalInt("limit");
            if (limit is null) return 10;
            if (limit is < 1 or > 25) throw Invalid("limit", "Choose a limit between 1 and 25.");
            return limit.Value;
        }

        public string RequestId()
        {
            var value = String("requestId", true, 1, 64);
            try { McpRequestRecord.CheckRequestId(value); }
            catch (ArgumentException) { throw Invalid("requestId", "Use a requestId of 1–64 letters, digits, hyphens or underscores."); }
            return value;
        }

        public string ParticipantDid()
        {
            var value = String("participantDid", true, 1, 256);
            if (!CarpaNet.Identity.IdentityResolver.IsValidDid(value))
                throw Invalid("participantDid", "Choose a participant by their AT DID.");
            return value;
        }

        public string Enum(string field, params string[] values)
        {
            var value = String(field, true, 1, 40);
            if (!values.Contains(value, StringComparer.Ordinal)) throw Invalid(field, "Choose " + field + " of: " + string.Join(", ", values) + ".");
            return value;
        }
    }
}

/// <summary>Base strict reader: unknown members, wrong types and missing required members are field errors.</summary>
public abstract class StrictFields(JObject args, string[] allowed)
{
    public McpInvalidArgumentsException Invalid(string field, string message) => new(field, message);

    protected int? OptionalInt(string field)
    {
        if (!args.TryGetValue(field, out var token)) return null;
        if (token.Type != JTokenType.Integer) throw Invalid(field, "Supply an integer value.");
        var value = token.Value<long>();
        if (value is < int.MinValue or > int.MaxValue) throw Invalid(field, "The value is out of range.");
        return (int)value;
    }

    public string String(string field, bool required, int minLength, int maxLength)
    {
        if (!args.TryGetValue(field, out var token))
        {
            if (required) throw Invalid(field, "This argument is required.");
            return null!;
        }
        if (token.Type != JTokenType.String) throw Invalid(field, "Supply a text value.");
        var value = token.Value<string>()!;
        if (value.Length < minLength || value.Length > maxLength) throw Invalid(field, $"Supply text of {minLength}–{maxLength} characters.");
        return value;
    }

    protected JObject Args => args;

    public void CheckUnknown()
    {
        foreach (var property in args.Properties())
        {
            if (!allowed.Contains(property.Name, StringComparer.Ordinal))
                throw Invalid(property.Name, "This argument is not part of the operation.");
        }
    }
}

public sealed class McpInvalidArgumentsException(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}
