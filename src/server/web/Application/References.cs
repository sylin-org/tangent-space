using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using TangentSpace.Communities;
using TangentSpace.Rooms;
using TangentSpace.Site;

namespace TangentSpace.Application;

/// <summary>
/// Qualified, opaque references to this server's Tangents, Topics, Posts, invitations and moderation
/// cases, bound to the configured public origin. A reference minted by another server is rejected
/// before any data is disclosed; nothing here routes over the network.
/// </summary>
public sealed class References(IOptions<SpaceOptions> options, IDataProtectionProvider protection)
{
    public const string InvitePrefix = "i_";
    public const string CasePrefix = "case_";

    private readonly string origin = ConfiguredOrigin(options.Value);
    private readonly IDataProtector listCursors = protection.CreateProtector("Tangent.References.ListCursor.v1");

    public static string ConfiguredOrigin(SpaceOptions options)
        => SpaceOptions.IsCanonicalOrigin(options.PublicOrigin, out var origin)
            ? origin
            : throw new InvalidOperationException("Set Tangent:Space:PublicOrigin to a canonical absolute origin like https://tangent.example.");

    public string Origin => origin;
    public string ServerRef => origin;
    public string Tangent(string tangentKey) => origin + "::" + tangentKey;
    public string Topic(string tangentKey, string topicKey) => Tangent(tangentKey) + "::" + topicKey;
    public string Post(string tangentKey, string topicKey, string postId) => Topic(tangentKey, topicKey) + "::" + postId;
    public string Invite(string tangentKey, string invitationId) => Tangent(tangentKey) + "::" + InvitePrefix + invitationId;
    public string Case(string tangentKey, string topicKey, string caseId) => Topic(tangentKey, topicKey) + "::" + CasePrefix + caseId;

    /// <summary>Parses origin::tangentKey.</summary>
    public string? ParseTangent(string? reference)
        => Parse(reference, 2) is { } parts && IsTangentKey(parts[1]) ? parts[1] : null;

    /// <summary>Parses origin::tangentKey::topicKey.</summary>
    public (string TangentKey, string TopicKey)? ParseTopic(string? reference)
        => Parse(reference, 3) is { } parts && IsTangentKey(parts[1]) && IsTopicKey(parts[2])
            ? (parts[1], parts[2])
            : null;

    /// <summary>Parses origin::tangentKey::topicKey::postId.</summary>
    public (string TangentKey, string TopicKey, string PostId)? ParsePost(string? reference)
        => Parse(reference, 4) is { } parts && IsTangentKey(parts[1]) && IsTopicKey(parts[2]) && IsOpaque(parts[3])
            ? (parts[1], parts[2], parts[3])
            : null;

    /// <summary>Parses origin::tangentKey::i_invitationId.</summary>
    public (string TangentKey, string InvitationId)? ParseInvite(string? reference)
    {
        if (Parse(reference, 3) is not { } parts || !IsTangentKey(parts[1])
            || !parts[2].StartsWith(InvitePrefix, StringComparison.Ordinal)) return null;
        var id = parts[2][InvitePrefix.Length..];
        return IsOpaque(id) ? (parts[1], id) : null;
    }

    /// <summary>Parses origin::tangentKey::topicKey::case_id, where the id is 64 lowercase hexadecimal digits.</summary>
    public (string TangentKey, string TopicKey, string CaseId)? ParseCase(string? reference)
    {
        if (Parse(reference, 4) is not { } parts || !IsTangentKey(parts[1]) || !IsTopicKey(parts[2])
            || !parts[3].StartsWith(CasePrefix, StringComparison.Ordinal)) return null;
        var id = parts[3][CasePrefix.Length..];
        return id.Length == 64 && id.All(character => char.IsAsciiDigit(character) || character is >= 'a' and <= 'f')
            ? (parts[1], parts[2], id)
            : null;
    }

    public string EncodeListCursor(string scope, string participantId, int page, string? inner)
        => listCursors.Protect(JsonSerializer.Serialize(
            new ListCursor(scope, participantId, page, inner, DateTimeOffset.UtcNow.AddDays(7))));

    public ListCursor? DecodeListCursor(string? value, string scope, string participantId)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.Length > 4096) throw new ArgumentException("The directory cursor is too long.");
        try
        {
            var cursor = JsonSerializer.Deserialize<ListCursor>(listCursors.Unprotect(value));
            if (cursor is null || cursor.Scope != scope || cursor.ParticipantId != participantId
                || cursor.Page is < 1 or > 10000 || cursor.ExpiresAt <= DateTimeOffset.UtcNow)
                throw new ArgumentException("This directory cursor belongs to another scope or participant.");
            return cursor;
        }
        catch (Exception error) when (error is CryptographicException or JsonException)
        {
            throw new ArgumentException("This directory cursor is invalid. Open the directory again.");
        }
    }

    /// <summary>Splits a reference that carries this server's exact origin and the expected number of
    /// segments; anything a response from this server could not have produced is null.</summary>
    private string[]? Parse(string? reference, int segments)
    {
        if (string.IsNullOrWhiteSpace(reference) || reference.Length > 512
            || !reference.StartsWith(origin + "::", StringComparison.Ordinal))
            return null;
        var parts = reference.Split("::");
        return parts.Length == segments ? parts : null;
    }

    private static bool IsOpaque(string value)
        => value.Length is >= 1 and <= 64
            && value.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');

    private static bool IsTangentKey(string key)
    {
        try { TangentCommunity.CheckKey(key); return true; }
        catch (TangentRuleViolation) { return false; }
    }

    private static bool IsTopicKey(string key)
    {
        try { Room.CheckKey(key); return true; }
        catch (RoomRuleViolation) { return false; }
    }
}

/// <summary>A protected directory continuation bound to one scope and one participant.</summary>
public sealed record ListCursor(string Scope, string ParticipantId, int Page, string? Inner, DateTimeOffset ExpiresAt);
