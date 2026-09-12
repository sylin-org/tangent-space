using System.Text.RegularExpressions;

namespace TangentSpace.Experience;

/// <summary>Deterministic mention candidate extraction. A mention candidate is an @handle or
/// DID token outside fenced code blocks, at a token boundary, with trailing punctuation
/// trimmed. Quoted prose that contains no such token yields no candidate; content inside
/// ``` fences never counts. There is no display-name matching and no inference; resolution to
/// participants happens against a bounded, unambiguous set at digest assembly.</summary>
public static partial class ExperienceMentions
{
    [GeneratedRegex(@"(?<![\w@])@(?<handle>[A-Za-z0-9][A-Za-z0-9.-]{1,252})", RegexOptions.CultureInvariant)]
    private static partial Regex HandleToken();

    [GeneratedRegex(@"(?<![\w:])did:(?<method>[a-z]+):(?<identifier>[A-Za-z0-9._:%-]{1,512})", RegexOptions.CultureInvariant)]
    private static partial Regex DidToken();

    /// <summary>Candidate identifiers a message text addresses: handles without the leading @
/// (lowercased for later case-insensitive exact resolution) and DIDs verbatim.</summary>
    public static IReadOnlyList<string> Candidates(string text)
    {
        if (string.IsNullOrEmpty(text)) return [];
        var found = new List<string>();
        var inFence = false;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }
            if (inFence) continue;
            foreach (Match match in HandleToken().Matches(line))
            {
                var handle = match.Groups["handle"].Value.TrimEnd('.', ',', '!', '?', ';', ':', ')', ']', '"', '\'');
                if (handle.Length >= 2) found.Add(handle.ToLowerInvariant());
            }
            foreach (Match match in DidToken().Matches(line))
            {
                var did = match.Value.TrimEnd('.', ',', '!', '?', ';', ':', ')', ']', '"', '\'');
                if (did.Length is > 8 and <= 576) found.Add(did);
            }
        }
        return found.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>Group tokens typed into the text (@admins/@moderators/@members). A group is
    /// only offered when no stored participant handle matches the same spelling exactly —
    /// the collision rule keeps a real handle authoritative over a group name.</summary>
    public static IReadOnlyList<string> GroupCandidates(string text, IReadOnlyCollection<string> knownHandles)
    {
        if (string.IsNullOrEmpty(text)) return [];
        var found = new List<string>();
        var inFence = false;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal)) { inFence = !inFence; continue; }
            if (inFence) continue;
            foreach (var group in Conversation.PostFacet.Groups)
            {
                var token = "@" + group;
                if (knownHandles.Contains(group, StringComparer.OrdinalIgnoreCase)) continue;
                var index = line.IndexOf(token, StringComparison.OrdinalIgnoreCase);
                while (index >= 0)
                {
                    var after = index + token.Length;
                    var boundaryBefore = index == 0 || char.IsWhiteSpace(line[index - 1]);
                    var boundaryAfter = after >= line.Length || char.IsPunctuation(line[after]) || char.IsWhiteSpace(line[after]);
                    if (boundaryBefore && boundaryAfter) { found.Add(group); break; }
                    index = line.IndexOf(token, index + 1, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
        return found.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>Whether a candidate list addresses the recipient directly by label or perennial
    /// identity value. An internal-only participant matches by internal DID, never by null.</summary>
    public static bool Addresses(IReadOnlyList<string> candidates, string? identityValue, string? handle)
        => identityValue is not null && candidates.Contains(identityValue, StringComparer.Ordinal)
           || (handle is { Length: > 1 } && candidates.Contains(handle, StringComparer.OrdinalIgnoreCase));
}
