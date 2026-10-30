using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Tangent.Access;
using Tangent.Events;
using Tangent.Participants;
using Tangent.Tangents;
using Tangent.Writer;

namespace Tangent.Posts;

/// <summary>A mention is a link in a body whose target is <c>at://</c> and a DID — <c>[@ada](at://did:plc:…)</c> — so
/// the identity is written into the body and a handle that changes later changes nothing there; an <c>@</c> and a
/// participant's handle typed in a body becomes one when the Post is written (<see cref="Typed"/>). Wherever a body is
/// shown, a mention resolves to the participant with that DID: in HTML a link to their address reading <c>@</c> and
/// their current handle, in plain text <c>@</c> and that handle; a DID that is no participant's leaves the link's own
/// words. A Post keeps the identities its body mentions (<see cref="Post.MentionedIds"/>), at most <see cref="Limit"/>,
/// so nothing parses a body to find them. The command that writes a Post appends one <see cref="EventKind.Mention"/>
/// per participant its body newly mentions (<see cref="Notify"/>).</summary>
public static partial class Mentions
{
    /// <summary>What a mention's target starts with: atproto's URI scheme, whose authority is the DID.</summary>
    public const string Scheme = "at://";

    /// <summary>The most identities one body mentions.</summary>
    public const int Limit = 32;

    /// <summary>The most characters a mentioned DID holds.</summary>
    public const int IdentityLimit = 576;

    /// <summary>What one resolved mention may weigh in an answer: its DID, handle and address, with room to spare.</summary>
    public const int Weight = 2048;

    /// <summary>The most UTF-8 bytes writing one typed handle as its mention adds to a body: <c>[</c> before it, and
    /// <c>](at://</c>, the DID and <c>)</c> after it.</summary>
    public const int TypedWeight = 9 + IdentityLimit;

    /// <summary>The DID a link target names when it is a mention; null for any other target.</summary>
    public static string? Identity(string target)
    {
        if (!target.StartsWith(Scheme, StringComparison.Ordinal)) return null;
        var did = target[Scheme.Length..];
        return did.Length <= IdentityLimit && Did().IsMatch(did) ? did : null;
    }

    /// <summary>The participants these Posts mention, by DID, as a page shows them, in one keyed read of at most
    /// <see cref="Limit"/> identities a Post.</summary>
    public static async Task<IReadOnlyDictionary<string, TopicPage.Mention>> Resolve(IEnumerable<Post> posts, string baseUrl,
        CancellationToken ct)
    {
        var ids = posts.SelectMany(post => post.MentionedIds).Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0) return new Dictionary<string, TopicPage.Mention>();
        return (await Participant.Get(ids, ct)).OfType<Participant>().ToDictionary(participant => participant.Id, participant =>
        {
            var handle = participant.Handle.Length > 0 ? participant.Handle : participant.Id;
            return new TopicPage.Mention(participant.Id, handle, ProfilePage.AddressOf(baseUrl, handle));
        }, StringComparer.Ordinal);
    }

    /// <summary>The participants among <paramref name="resolved"/> that <paramref name="post"/> mentions, in the order of
    /// their first mention.</summary>
    public static IReadOnlyList<TopicPage.Mention> Of(Post post, IReadOnlyDictionary<string, TopicPage.Mention> resolved)
        => post.MentionedIds.Select(id => resolved.GetValueOrDefault(id)).OfType<TopicPage.Mention>().ToList();

    /// <summary>A Post's body at a version as safe HTML, each mention of one of <paramref name="mentioned"/> a link to
    /// their address reading <c>@</c> and their current handle, and every other mention its own words.</summary>
    public static string Html(string postId, long version, string body, IReadOnlyList<TopicPage.Mention> mentioned)
        => RenderedBodies.For(postId, version, body).Html(target => Identity(target) is { } did
            && mentioned.FirstOrDefault(mention => mention.Id == did) is { } mention
                ? new MarkdownHtml.Link(mention.Address, "@" + mention.Handle)
                : null);

    /// <summary>A Post's body as plain text on one line, each mention of a participant among <paramref name="people"/>
    /// <c>@</c> and their current handle, and every other mention its own words.</summary>
    public static string PlainText(Post post, IReadOnlyDictionary<string, TangentPage.Author> people)
        => RenderedBodies.For(post).PlainText(Words(people));

    /// <summary>A stretch of a body cut anywhere as plain text on one line, read as <see cref="PlainText"/> reads a whole
    /// one. Parsed each time, a stretch being small; one too intricate to parse stays as it is.</summary>
    public static string PlainFragment(string markdown, IReadOnlyDictionary<string, TangentPage.Author> people)
        => MarkdownHtml.Tractable(markdown) ? RenderedBodies.Render(markdown).PlainText(Words(people)) : markdown;

    private static Func<string, string?> Words(IReadOnlyDictionary<string, TangentPage.Author> people)
        => target => Identity(target) is { } did && people.TryGetValue(did, out var person) ? "@" + person.Handle : null;

    /// <summary>Appends, inside the running command, one <see cref="EventKind.Mention"/> per participant
    /// <paramref name="post"/> mentions that <paramref name="before"/> — the identities it mentioned before an edit,
    /// empty for a new Post — did not, targeted at them: not the actor, not a DID that is no participant's, and not a
    /// participant for whom the Post's Tangent is absent. One keyed read of the participants, then capability's rows for
    /// each; nothing parses.</summary>
    public static async Task Notify(Sequences sequences, Post post, string actorId, IReadOnlyList<string> before, DateTimeOffset at,
        CancellationToken ct)
    {
        var earlier = before.ToHashSet(StringComparer.Ordinal);
        var added = post.MentionedIds.Where(id => id != actorId && !earlier.Contains(id)).ToList();
        if (added.Count == 0) return;
        var participants = (await Participant.Get(added, ct)).OfType<Participant>().Select(participant => participant.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in added.Where(participants.Contains))
        {
            if (!(await Capabilities.Load(id, post.TangentId, null, ct)).Exists) continue;
            await Event.Next(sequences, EventKind.Mention, at,
                tangentId: post.TangentId, topicId: post.TopicId, postId: post.Id, actorId: actorId, targetId: id, version: post.Version).Save(ct);
        }
    }

    /// <summary><paramref name="body"/> with each handle typed in its prose that names a participant here written as
    /// their mention, <c>[@handle](at://did)</c> with the words as typed, so the body keeps their identity and a handle
    /// that changes later changes nothing in it. A typed handle is atproto's handle syntax after an <c>@</c> that begins
    /// a word (<see cref="BeginsAWord"/>) and is never inside a URL, so an address (<c>ines@bsky.social</c>) and a path
    /// (<c>/@ines.bsky.social</c>) stay as typed; only prose is read (<see cref="MarkdownHtml.Prose"/>), so code, a link's
    /// words and an image's description stay as typed too. The first <see cref="Limit"/> handles typed are read in one
    /// keyed read of the participants, each as typed or in lower case, as atproto writes handles; a handle no participant
    /// holds stays the words typed. At most <see cref="Limit"/> are written, the first typed, each adding at most
    /// <see cref="TypedWeight"/>, so the body grows within <see cref="Post.KeptBodyLimit"/>. A body without an <c>@</c>
    /// is returned unparsed; ask <see cref="MarkdownHtml.Tractable"/> first.</summary>
    public static async Task<string> Typed(string body, CancellationToken ct)
    {
        var typed = TypedHandles(body);
        if (typed.Count == 0) return body;
        var handles = typed.Select(handle => handle.Handle).Distinct(StringComparer.OrdinalIgnoreCase).Take(Limit).ToList();
        var asked = handles.Concat(handles.Select(handle => handle.ToLowerInvariant())).Distinct(StringComparer.Ordinal).ToList();
        var holders = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var participant in await Participant.Query(participant => asked.Contains(participant.Handle),
                     new QueryDefinition { Page = 1, PageSize = asked.Count }, ct))
            if (Identity(Scheme + participant.Id) is not null) holders.TryAdd(participant.Handle, participant.Id);
        if (holders.Count == 0) return body;

        var written = new StringBuilder(body.Length);
        int at = 0, count = 0;
        foreach (var (start, length, handle) in typed)
        {
            if ((holders.GetValueOrDefault(handle) ?? holders.GetValueOrDefault(handle.ToLowerInvariant())) is not { } did) continue;
            written.Append(body, at, start - at).Append('[').Append(body, start, length).Append("](").Append(Scheme).Append(did).Append(')');
            at = start + length;
            if (++count == Limit) break;
        }

        return written.Append(body, at, body.Length - at).ToString();
    }

    /// <summary>Each handle typed in the prose of <paramref name="body"/>, as <see cref="Typed"/> reads one: where its
    /// <c>@</c> stands, how long it is with it, and the handle, in the order of the source. One parse, and one walk of
    /// the source for the words and URLs the <c>@</c>s stand in.</summary>
    private static List<(int Start, int Length, string Handle)> TypedHandles(string body)
    {
        var found = new List<(int Start, int Length, string Handle)>();
        if (!body.Contains('@')) return found;
        IReadOnlyList<Range> prose;
        try
        {
            prose = MarkdownHtml.Prose(body);
        }
        catch (ArgumentException)
        {
            return found;
        }

        // The walk: where the word holding the next character began, and where the last "://" began.
        int walked = 0, word = 0, scheme = -1;
        foreach (var run in prose.OrderBy(run => run.Start.Value))
        {
            var (from, end) = (run.Start.Value, run.End.Value);
            for (var sign = body.IndexOf('@', from, end - from); sign >= 0; sign = sign + 1 < end ? body.IndexOf('@', sign + 1, end - sign - 1) : -1)
            {
                for (; walked < sign; walked++)
                {
                    if (char.IsWhiteSpace(body[walked])) word = walked + 1;
                    else if (string.CompareOrdinal(body, walked, "://", 0, 3) == 0) scheme = walked;
                }

                if (!BeginsAWord(body, sign) || scheme >= word) continue;
                var last = sign + 1;
                while (last < end && (char.IsAsciiLetterOrDigit(body[last]) || body[last] is '.' or '-')) last++;
                while (last > sign + 1 && body[last - 1] is '.' or '-') last--;
                var handle = body[(sign + 1)..last];
                if (handle.Length <= SignIn.HandleLimit && HandleSyntax().IsMatch(handle)) found.Add((sign, last - sign, handle));
            }
        }

        return found;
    }

    /// <summary>Whether the <c>@</c> at <paramref name="sign"/> begins a word: unless the character before it, past any
    /// emphasis marks (<c>*</c> and <c>_</c>) right before it, belongs to a word — a letter, a digit or a mark that joins
    /// one — or to an address or a path: <c>.</c>, <c>+</c>, <c>-</c>, <c>/</c>, <c>\</c>, <c>=</c>, <c>%</c>,
    /// <c>&amp;</c>, <c>~</c>, <c>#</c> or <c>@</c>. So the start of a line, whitespace, a quotation mark of any kind, a
    /// dash other than the hyphen, a colon, a bracket and other punctuation begin one, <c>_@</c> opening emphasis does,
    /// and <c>snake_@</c> does not.</summary>
    private static bool BeginsAWord(string body, int sign)
    {
        var before = sign;
        while (before > 0 && body[before - 1] is '*' or '_') before--;
        if (before == 0) return true;
        if (body[before - 1] is '.' or '+' or '-' or '/' or '\\' or '=' or '%' or '&' or '~' or '#' or '@') return false;
        Rune.DecodeLastFromUtf16(body.AsSpan(0, before), out var rune, out _);
        return !Rune.IsLetterOrDigit(rune) && Rune.GetUnicodeCategory(rune) is not
            (UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark);
    }

    [GeneratedRegex(@"^did:[a-z]+:[A-Za-z0-9._:%-]+$")]
    private static partial Regex Did();

    /// <summary>atproto's handle syntax: dot-separated labels of letters, digits and inner hyphens, at least two, the
    /// last starting with a letter.</summary>
    [GeneratedRegex(@"^([a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?$")]
    private static partial Regex HandleSyntax();
}
