using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using TangentSpace.Communities;
using TangentSpace.Rooms;

namespace TangentSpace.Mcp;

/// <summary>
/// Qualified, opaque destination references for the configured canonical origin. No network routing
/// happens here: a reference from another server is rejected before any data disclosure.
/// </summary>
public sealed partial class McpRefs(IOptions<McpOptions> options, IDataProtectionProvider protection)
{
    public const string InvitePrefix = "i_";

    private readonly string origin = ConfiguredOrigin(options.Value);
    private readonly IDataProtector protector = protection.CreateProtector("Tangent.Mcp.ListCursor.v1");

    public static string ConfiguredOrigin(McpOptions options)
    {
        if (!McpOptionsValidation.IsCanonicalOrigin(options.PublicBaseUrl, out var origin))
            throw new InvalidOperationException("Tangent:Mcp:PublicBaseUrl must be a canonical absolute origin like https://tangent.example.");
        return origin;
    }

    public string Origin => origin;
    public string ServerRef => origin;
    public string Tangent(string key) => origin + "::" + key;
    public string Channel(string tangentKey, string roomKey) => origin + "::" + tangentKey + "::" + roomKey;
    public string Message(string tangentKey, string roomKey, string messageId) => Channel(tangentKey, roomKey) + "::" + messageId;
    public string Invite(string tangentKey, string invitationId) => Channel(tangentKey, InvitePrefix + invitationId);
    public string Audit(string tangentKey, string roomKey, string auditId) => Channel(tangentKey, roomKey) + "::" + AuditPrefix + auditId;
    public const string AuditPrefix = "audit_";

    /// <summary>Parses a tangent reference: exactly origin::tangentKey with a valid key.</summary>
    public string? ParseTangent(string? reference)
    {
        if (Parse(reference, 2) is not { } parts) return null;
        return Key.Check(parts[1]) ? parts[1] : null;
    }

    /// <summary>Parses a channel reference: exactly origin::tangentKey::roomKey with valid keys.</summary>
    public (string TangentKey, string RoomKey)? ParseChannel(string? reference)
    {
        if (Parse(reference, 3) is not { } parts) return null;
        return Key.Check(parts[1]) && RoomKey.Check(parts[2]) ? (parts[1], parts[2]) : null;
    }

    /// <summary>Parses a message reference: exactly origin::tangentKey::roomKey::messageId.</summary>
    public (string TangentKey, string RoomKey, string MessageId)? ParseMessage(string? reference)
    {
        if (Parse(reference, 4) is not { } parts) return null;
        return Key.Check(parts[1]) && RoomKey.Check(parts[2]) && Opaque(parts[3]) ? (parts[1], parts[2], parts[3]) : null;
    }

    /// <summary>Parses an invitation reference: origin::tangentKey::i_invitationId.</summary>
    public (string TangentKey, string InvitationId)? ParseInvite(string? reference)
    {
        if (Parse(reference, 3) is not { } parts) return null;
        if (!Key.Check(parts[1]) || !parts[2].StartsWith(InvitePrefix, StringComparison.Ordinal)) return null;
        var id = parts[2][InvitePrefix.Length..];
        return Opaque(id) ? (parts[1], id) : null;
    }

    /// <summary>Parses an audit reference: origin::tangentKey::roomKey::audit_id.</summary>
    public (string TangentKey, string RoomKey, string AuditId)? ParseAudit(string? reference)
    {
        if (Parse(reference, 4) is not { } parts) return null;
        if (!Key.Check(parts[1]) || !RoomKey.Check(parts[2]) || !parts[3].StartsWith(AuditPrefix, StringComparison.Ordinal)) return null;
        var id = parts[3][AuditPrefix.Length..];
        return Opaque(id) ? (parts[1], parts[2], id) : null;
    }

    private static bool Opaque(string value)
        => value.Length is >= 1 and <= 64 && value.All(char.IsAsciiLetterOrDigit);

    /// <summary>Splits a strict qualified reference: exact canonical origin prefix and segment count.
    /// Returns null for anything a prior response from this server could not have produced.</summary>
    private string[]? Parse(string? reference, int exactParts)
    {
        if (string.IsNullOrWhiteSpace(reference) || reference.Length > 512
            || !reference.StartsWith(origin + "::", StringComparison.Ordinal))
            return null;
        var parts = reference.Split("::");
        return parts.Length == exactParts ? parts : null;
    }

    private static class Key
    {
        public static bool Check(string key)
        {
            try { TangentCommunity.CheckKey(key); return true; }
            catch (TangentRuleViolation) { return false; }
        }
    }

    private static class RoomKey
    {
        public static bool Check(string key)
        {
            try { Room.CheckKey(key); return true; }
            catch (RoomRuleViolation) { return false; }
        }
    }

    public string EncodeListCursor(string scope, string did, int page, string? inner)
        => protector.Protect(JsonSerializer.Serialize(new McpListCursor(scope, did, page, inner, DateTimeOffset.UtcNow.AddDays(7))));

    public McpListCursor? DecodeListCursor(string? value, string scope, string did)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.Length > 4096) throw new ArgumentException("The directory cursor is too long.");
        try
        {
            var cursor = JsonSerializer.Deserialize<McpListCursor>(protector.Unprotect(value));
            if (cursor is null || cursor.Scope != scope || cursor.Did != did || cursor.Page is < 1 or > 10000 || cursor.ExpiresAt <= DateTimeOffset.UtcNow)
                throw new ArgumentException("This directory cursor belongs to another scope or participant.");
            return cursor;
        }
        catch (Exception error) when (error is CryptographicException or JsonException)
        {
            throw new ArgumentException("This directory cursor is invalid. Open the directory again.");
        }
    }
}

public sealed record McpListCursor(string Scope, string Did, int Page, string? Inner, DateTimeOffset ExpiresAt);
