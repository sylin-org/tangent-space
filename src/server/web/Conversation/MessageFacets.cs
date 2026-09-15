using System.Text;
using System.Text.RegularExpressions;
using TangentSpace.Participants;

namespace TangentSpace.Conversation;

/// <summary>One facet derivation shared by Message persistence and replay comparison. It reads
/// only stored identities; no provider calls or writes.</summary>
public static partial class MessageFacets
{
    public static async Task<IReadOnlyList<PostFacet>> Effective(string text, IReadOnlyList<PostFacet>? provided, CancellationToken ct)
    {
        if (provided is not null) return PostFacets.Check(text, provided)!;
        // Empty is a materialized decision too: a later save must not reinterpret an era.
        if (!text.Contains('@') && !text.Contains("did:", StringComparison.Ordinal)) return [];
        return Detect(text, await ParticipantIdentity.Query(_ => true, ct));
    }

    // A mention candidate is an @handle or DID token outside ``` fences, at a token boundary, with
    // trailing punctuation trimmed. There is no display-name matching and no inference.
    [GeneratedRegex(@"(?<![\w@])@(?<handle>[A-Za-z0-9][A-Za-z0-9.-]{1,252})", RegexOptions.CultureInvariant)]
    private static partial Regex HandleToken();

    [GeneratedRegex(@"(?<![\w:])did:(?<method>[a-z]+):(?<identifier>[A-Za-z0-9._:%-]{1,512})", RegexOptions.CultureInvariant)]
    private static partial Regex DidToken();

    /// <summary>Server-side facet re-detection: mention facets from @handle and DID tokens that
    /// resolve to exactly one stored participant, group facets from @admins/@moderators/@members
    /// when no stored handle claims the spelling. One facet per distinct target at its first
    /// occurrence; the create-path bound of 32 applies; anything unresolved is left as plain text.
    /// Ranges are absolute whole-text UTF-8 byte offsets: each pass tracks the byte offset of the
    /// current line's start (raw segment bytes plus one byte per '\n'), matching what the composer
    /// mints and the renderer expects.</summary>
    internal static IReadOnlyList<PostFacet> Detect(string text, IReadOnlyList<ParticipantIdentity> identities)
    {
        var facets = new List<PostFacet>();
        var targets = new HashSet<string>(StringComparer.Ordinal);
        var inFence = false;
        var lineStart = 0;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal)) inFence = !inFence;
            else if (!inFence)
            {
                foreach (Match match in HandleToken().Matches(line))
                {
                    var handle = match.Groups["handle"].Value.TrimEnd('.', ',', '!', '?', ';', ':', ')', ']', '"', '\'');
                    if (handle.Length < 2) continue;
                    var holders = identities.Where(entry => string.Equals(handle, entry.Label?.Trim(), StringComparison.OrdinalIgnoreCase))
                        .Select(entry => entry.ParticipantId).Distinct(StringComparer.Ordinal).Take(2).ToArray();
                    var resolved = holders.Length != 1 ? null : identities.Where(entry => entry.ParticipantId == holders[0] && entry.Kind == ParticipantIdentity.AtprotoKind)
                        .OrderBy(entry => entry.Value, StringComparer.Ordinal).FirstOrDefault()?.Value ?? (holders.Length == 1 ? ParticipantIdentity.InternalValue(holders[0]) : null);
                    if (resolved is null || !targets.Add(resolved)) continue;
                    facets.Add(At(lineStart, line, match.Index, "@" + handle, PostFacet.Mention, did: resolved));
                }
                foreach (Match match in DidToken().Matches(line))
                {
                    var did = match.Value.TrimEnd('.', ',', '!', '?', ';', ':', ')', ']', '"', '\'');
                    if (did.Length is < 9 or > 576 || !targets.Add(did) || !identities.Any(entry => entry.Value == did)) continue;
                    facets.Add(At(lineStart, line, match.Index, did, PostFacet.Mention, did: did));
                }
            }
            // The raw segment (including any '\r') plus one byte for the '\n' separator.
            lineStart += Encoding.UTF8.GetByteCount(rawLine) + 1;
        }
        var claims = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var storedLabels = identities.Where(entry => entry.Label != null)
            .Select(entry => entry.Label!.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var group in PostFacet.Groups)
            if (storedLabels.Contains(group))
                claims.Add(group);
        inFence = false;
        lineStart = 0;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal)) inFence = !inFence;
            else if (!inFence)
            {
                foreach (var group in PostFacet.Groups)
                {
                    if (claims.Contains(group) || targets.Contains(group)) continue;
                    var spelled = "@" + group;
                    var index = line.IndexOf(spelled, StringComparison.OrdinalIgnoreCase);
                    while (index >= 0)
                    {
                        var after = index + spelled.Length;
                        var boundaryBefore = index == 0 || char.IsWhiteSpace(line[index - 1]);
                        var boundaryAfter = after >= line.Length || char.IsPunctuation(line[after]) || char.IsWhiteSpace(line[after]);
                        if (boundaryBefore && boundaryAfter)
                        {
                            targets.Add(group);
                            facets.Add(At(lineStart, line, index, spelled, PostFacet.Group, value: group));
                            break;
                        }
                        index = line.IndexOf(spelled, index + 1, StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
            // The raw segment (including any '\r') plus one byte for the '\n' separator.
            lineStart += Encoding.UTF8.GetByteCount(rawLine) + 1;
        }
        return PostFacets.Check(text, facets.OrderBy(facet => facet.Start).Take(32).ToList())!;
    }

    /// <summary>A facet range in whole-text absolute UTF-8 byte offsets: the line's start offset
    /// plus the line-relative prefix bytes, then the token's own byte length.</summary>
    private static PostFacet At(int lineStart, string line, int charIndex, string token, string kind, string? did = null, string? value = null)
    {
        var start = lineStart + Encoding.UTF8.GetByteCount(line[..charIndex]);
        return new PostFacet { Kind = kind, Start = start, End = start + Encoding.UTF8.GetByteCount(token), Did = did, Value = value };
    }
}
