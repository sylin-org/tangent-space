using Tangent.Participants;
using Tangent.Tangents;

namespace Tangent.Posts;

/// <summary>A Post quoted for context on a page that acts on it — taken on a tangent, or reported: its address, who wrote
/// it and when, its words as plain text cut short — none on a tombstone, which is <see cref="Removed"/> — the Topic and
/// Tangent it is in, and who other than its author last edited it, where someone did.</summary>
public sealed record QuotedPost(
    string Id,
    string Address,
    Render.Person Author,
    DateTimeOffset CreatedAt,
    string? Text,
    bool Removed,
    string Topic,
    string TopicAddress,
    string Tangent,
    string TangentAddress,
    TangentPage.Edited? Edited = null) : Render.IPart
{
    /// <summary>The most characters of a Post's first words that a title offered from them carries.</summary>
    public const int TitleWords = 60;

    /// <summary><paramref name="post"/> of the Topic <paramref name="root"/> in <paramref name="tangent"/>, quoted under
    /// <paramref name="baseAddress"/>, with its author and whoever it mentions read in one keyed read.</summary>
    public static async Task<QuotedPost> Of(Post post, Post root, Tangents.Tangent tangent, string baseAddress, CancellationToken ct)
    {
        var people = await People(post, ct);
        var author = people.TryGetValue(post.AuthorId, out var person) ? person : Render.Person.Unknown(post.AuthorId);
        var topicAddress = PostWrites.TopicAddress(baseAddress, tangent.Id, root.Slug ?? root.Id);
        return new QuotedPost(post.Id, topicAddress + "/" + Uri.EscapeDataString(post.Id), author, post.CreatedAt,
            post.Removed ? null : TangentPage.Opening(post, people), post.Removed, root.Title ?? "", topicAddress,
            tangent.Name, PostWrites.TangentAddress(baseAddress, tangent.Id), TangentPage.EditedOf(post, people));
    }

    /// <summary>A title offered from the quoted Post's first words: on one line, cut at a word within
    /// <see cref="TitleWords"/> characters; null for a tombstone or a Post with no words.</summary>
    public string? OfferedTitle()
    {
        if (Text is not { } text) return null;
        var line = string.Join(' ', text.TrimEnd('…').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (line.Length == 0) return null;
        if (line.Length <= TitleWords) return line;
        var cut = line.LastIndexOf(' ', TitleWords);
        return line[..(cut > 0 ? cut : TitleWords)].TrimEnd(',', ';', ':', '.', ' ');
    }

    /// <summary>The Post's author, its last editor and the participants its body mentions, in one keyed read, by
    /// participant Id.</summary>
    public static async Task<IReadOnlyDictionary<string, Render.Person>> People(Post post, CancellationToken ct)
    {
        var ids = post.MentionedIds.Prepend(post.AuthorId).Concat(post.EditedBy is { } editor ? [editor] : []).Distinct(StringComparer.Ordinal).ToList();
        return (await Participant.Get(ids, ct)).OfType<Participant>().ToDictionary(row => row.Id, row => Render.Person.Of(row), StringComparer.Ordinal);
    }
}
