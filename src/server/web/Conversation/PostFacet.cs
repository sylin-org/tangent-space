using System.Text.Json;
using System.Text.Json.Serialization;

namespace TangentSpace.Conversation;

/// <summary>A structural reference inside a Post's verbatim text (ADR 0008): byte-range
/// annotations carrying stable identities, never rewrites of the words. Mentions bind DIDs,
/// groups bind role names expanded at digest time, tags bind search keys, topic references
/// bind topic identities that survive renames. Labels are resolved at read time.</summary>
public sealed record PostFacet
{
    /// <summary>Facet kind: mention, group, tag, or topic.</summary>
    public string Kind { get; init; } = "";

    /// <summary>Inclusive UTF-8 byte offset of the annotated range's start.</summary>
    public int Start { get; init; }

    /// <summary>Exclusive UTF-8 byte offset of the annotated range's end.</summary>
    public int End { get; init; }

    /// <summary>For mentions: the referenced participant DID.</summary>
    public string? Did { get; init; }

    /// <summary>For groups: the role-group name (admins, moderators, members). For tags: the tag value.</summary>
    public string? Value { get; init; }

    /// <summary>For topic references: the server topic identity.</summary>
    public string? Id { get; init; }

    public const string Mention = "mention";
    public const string Group = "group";
    public const string Tag = "tag";
    public const string Topic = "topic";

    /// <summary>The mentionable role groups, resolved dynamically at digest time to current
    /// holders. An exact participant handle always wins over a group of the same spelling.</summary>
    public static readonly IReadOnlyList<string> Groups = ["admins", "moderators", "members"];

    public bool References(string did) => Kind == Mention && string.Equals(Did, did, StringComparison.Ordinal);

    /// <summary>Canonical form for payload comparison in the idempotency conflict check.</summary>
    public string Canonical() => JsonSerializer.Serialize(this,
        new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
}

/// <summary>Validation for client-supplied facets: kinds must be known, each kind carries
/// exactly its own identity field, ranges must be valid ascending UTF-8 byte offsets inside
/// the verbatim text, and ranges must not overlap (renderers replace single spans).</summary>
public static partial class PostFacets
{
    public static IReadOnlyList<PostFacet>? Check(string text, IReadOnlyList<PostFacet>? facets)
    {
        if (facets is null) return null;
        if (facets.Count == 0) return [];
        if (facets.Count > 32) throw new ArgumentException("A post carries at most 32 facets.");
        var bytes = System.Text.Encoding.UTF8.GetByteCount(text);
        var ordered = facets.Select(facet =>
        {
            switch (facet.Kind)
            {
                case PostFacet.Mention:
                    if (string.IsNullOrWhiteSpace(facet.Did) || facet.Value is not null || facet.Id is not null)
                        throw new ArgumentException("A mention facet carries exactly a participant DID.");
                    break;
                case PostFacet.Group:
                    if (!PostFacet.Groups.Contains(facet.Value, StringComparer.Ordinal) || facet.Did is not null || facet.Id is not null)
                        throw new ArgumentException("A group facet carries exactly one of: admins, moderators, members.");
                    break;
                case PostFacet.Tag:
                    if (string.IsNullOrWhiteSpace(facet.Value) || facet.Value.Length > 64 || facet.Did is not null || facet.Id is not null)
                        throw new ArgumentException("A tag facet carries a tag value of 1–64 characters.");
                    break;
                case PostFacet.Topic:
                    if (string.IsNullOrWhiteSpace(facet.Id) || facet.Did is not null || facet.Value is not null)
                        throw new ArgumentException("A topic facet carries exactly a topic identity.");
                    break;
                default:
                    throw new ArgumentException($"Unknown facet kind '{facet.Kind}'.");
            }
            if (facet.Start < 0 || facet.End <= facet.Start || facet.End > bytes)
                throw new ArgumentException("Facet ranges must be ascending UTF-8 byte offsets inside the post text.");
            return facet;
        }).OrderBy(facet => facet.Start).ToList();
        for (var index = 1; index < ordered.Count; index++)
            if (ordered[index].Start < ordered[index - 1].End)
                throw new ArgumentException("Facet ranges must not overlap.");
        return ordered;
    }
}
