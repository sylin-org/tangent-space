using System.Formats.Cbor;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CarpaNet;
using CarpaNet.Identity;
using TangentSpace.AtProtocol;

namespace TangentSpace.Conversation;

public sealed record MessageContent(string Text, DateTimeOffset CreatedAt, SourceReference? ReplyTo)
{
    // The strict AT datetime form is an RFC3339/ISO8601 intersection: explicit
    // uppercase T and zone, no unknown -00:00 offset, at most 64 characters.
    private static readonly Regex Datetime = new(@"\A(?<whole>[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2})(?:\.(?<fraction>[0-9]+))?(?<zone>Z|[+-](?:[01][0-9]|2[0-3]):[0-5][0-9])\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static void CheckText(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || Encoding.UTF8.GetByteCount(text) > 4096 || text.Contains('\0'))
            throw new ArgumentException("A message must contain 1–4096 UTF-8 bytes and no null characters.");
    }

    public JsonElement ToRecord()
    {
        CheckText(Text);
        var record = new Dictionary<string, object> { ["$type"] = SpacesOptions.Collection, ["text"] = Text,
            ["createdAt"] = CreatedAt.ToUniversalTime().ToString("O") };
        if (ReplyTo is not null) record["replyTo"] = new { uri = ReplyTo.Uri, cid = ReplyTo.Cid };
        return JsonSerializer.SerializeToElement(record);
    }

    public static MessageContent FromCbor(byte[] bytes)
    {
        var reader = new CborReader(bytes, CborConformanceMode.Canonical);
        var count = reader.ReadStartMap();
        if (count is not (3 or 4)) throw new InvalidDataException("Unsupported message fields.");
        string? type = null, text = null, created = null;
        SourceReference? reply = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            var name = reader.ReadTextString();
            if (!seen.Add(name)) throw new InvalidDataException("Duplicate message field.");
            switch (name)
            {
                case "$type": type = reader.ReadTextString(); break;
                case "text": text = reader.ReadTextString(); break;
                case "createdAt": created = reader.ReadTextString(); break;
                case "replyTo":
                    if (reader.ReadStartMap() != 2) throw new InvalidDataException("Reply must include URI and CID.");
                    var fields = new Dictionary<string, string>(StringComparer.Ordinal);
                    for (var j = 0; j < 2; j++)
                    {
                        var field = reader.ReadTextString();
                        if (field is not ("uri" or "cid") || !fields.TryAdd(field, reader.ReadTextString()))
                            throw new InvalidDataException("Reply must contain exactly URI and CID.");
                    }
                    reader.ReadEndMap();
                    reply = new SourceReference(fields["uri"], fields["cid"]);
                    CheckReply(reply);
                    break;
                default: throw new InvalidDataException("Unsupported message field.");
            }
        }
        reader.ReadEndMap();
        if (reader.BytesRemaining != 0 || type != SpacesOptions.Collection || text is null
            || !TryCreatedAt(created, out var date))
            throw new InvalidDataException("Invalid message record.");
        CheckText(text);
        return new(text, date, reply);
    }

    private static void CheckReply(SourceReference reply)
    {
        if (reply.Uri.Length is 0 or > 2048 || reply.Cid.Length is 0 or > 128 || !reply.Uri.StartsWith("at://", StringComparison.Ordinal))
            throw new InvalidDataException("Invalid reply reference.");
        // A reply can target only a full message source in a Space. Reject
        // malformed same-room strings before the dependency-deferral stage.
        var parts = reply.Uri[5..].Split('/');
        if (parts.Length != 7 || !IdentityResolver.IsValidDid(parts[0]) || parts[1] != "space"
            || parts[2] != SpacesOptions.SpaceType || !IsRecordKey(parts[3]) || !IdentityResolver.IsValidDid(parts[4])
            || parts[5] != SpacesOptions.Collection || !IsRecordKey(parts[6]))
            throw new InvalidDataException("Reply URI must identify a message source in a Space.");
        // All accepted sources use canonical base32 CIDv1 DAG-CBOR/SHA-256.
        // Bound the codec prefix before parsing: the pinned SDK also accepts
        // arbitrary multihash lengths, which a message reference never needs.
        if (reply.Cid.Length != 59 || !reply.Cid.StartsWith("bafyrei", StringComparison.Ordinal)
            || reply.Cid.AsSpan(1).ContainsAnyExcept("abcdefghijklmnopqrstuvwxyz234567"))
            throw new InvalidDataException("Reply CID must be a canonical message-record CID.");
        // Re-encoding also rejects alternate spellings and trailing bytes that
        // this pinned SDK's permissive parser would otherwise accept.
        bool canonical;
        try
        {
            var cid = new ATCid(reply.Cid);
            canonical = cid.IsValid && cid.IsAtProtoBlessedFormat && reply.Cid == ATCid.FromSha256Hash(cid.Hash!).Value;
        }
        catch (Exception failure)
        {
            // A bounded untrusted CID must become an invalid-record decision,
            // regardless of the SDK parser's exception type.
            throw new InvalidDataException("Invalid reply CID.", failure);
        }
        if (!canonical)
            throw new InvalidDataException("Reply CID must be a canonical message-record CID.");
    }

    private static bool IsRecordKey(string value) => value.Length is >= 1 and <= 512 && value is not ("." or "..")
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '~' or '.' or ':' or '-');

    private static bool TryCreatedAt(string? value, out DateTimeOffset date)
    {
        date = default;
        if (value is null || value.Length > 64 || value.EndsWith("-00:00", StringComparison.Ordinal)) return false;
        var match = Datetime.Match(value);
        if (!match.Success) return false;
        var fraction = match.Groups["fraction"].Value;
        // Source text remains in the verified record; this projection has .NET
        // tick precision. Preserve all supported source round-trip digits.
        var ticks = fraction.Length > 7 ? fraction[..7] : fraction.PadRight(7, '0');
        if (!DateTime.TryParseExact(match.Groups["whole"].Value + "." + ticks, "yyyy-MM-dd'T'HH:mm:ss.fffffff",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)) return false;
        var zone = match.Groups["zone"].Value;
        var offset = zone == "Z" ? TimeSpan.Zero : new TimeSpan(int.Parse(zone.AsSpan(1, 2), CultureInfo.InvariantCulture),
            int.Parse(zone.AsSpan(4, 2), CultureInfo.InvariantCulture), 0) * (zone[0] == '-' ? -1 : 1);
        try
        {
            // Normalize even legal offsets beyond DateTimeOffset's ±14-hour
            // constructor bound. Instants outside .NET years 1–9999 are refused.
            date = new DateTimeOffset(new DateTime(checked(local.Ticks - offset.Ticks), DateTimeKind.Utc));
            return true;
        }
        catch (ArgumentOutOfRangeException) { return false; }
    }
}
