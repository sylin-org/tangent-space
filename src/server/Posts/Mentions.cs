using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Koan.Identity.Roles;
using Tangent.Access;
using Tangent.Events;
using Tangent.Participants;
using Tangent.Tangents;

namespace Tangent.Posts;

/// <summary>A mention is a link in a body whose target is the participant's page by their id, which is the place's own —
/// <c>[@ada](/p/0193…)</c> — so the participant is written into the body, and a handle, a name or a Bluesky account that
/// changes later changes nothing there; a writer's pick writes it whole (<see cref="Target"/>), and an <c>@</c> and a
/// participant's handle typed in a body becomes one when the Post is written (<see cref="Typed"/>). Wherever a body is
/// shown, a mention resolves live to the participant with that id: in HTML a link to their address reading <c>@</c> and
/// the name the place shows them by (<see cref="Participant.ShownName"/>), with <c>@</c> and their handle as its title, so
/// two people of one name are told apart; in plain text <c>@</c> and that name; an id that is no participant's leaves the
/// link's own words. A Post keeps the participants its body mentions (<see cref="Post.MentionedIds"/>), at most
/// <see cref="Limit"/>, so nothing parses a body to find them. A role is mentioned the same way, by a link whose target is
/// its key — <c>[@moderators](role:moderator)</c>, written from an <c>@</c> and its word typed — which the Post keeps among
/// its roles (<see cref="Post.MentionedRoles"/>) and a page shows as its words, marked, while the role exists; and a
/// Tangent's slug typed after a <c>#</c> is written as a link to it. The command that writes a Post appends one
/// <see cref="EventKind.Mention"/> per participant its body newly mentions, and one per role, however many hold it
/// (<see cref="Notify"/>).</summary>
public static partial class Mentions
{
    /// <summary>What a mention's target starts with: the path of the participants' pages, as a Tangent's link is
    /// <c>/t/</c> and its slug.</summary>
    public const string Prefix = "/p/";

    /// <summary>The most identities one body mentions.</summary>
    public const int Limit = 32;

    /// <summary>The most characters a DID holds, where a profile's address names one.</summary>
    public const int IdentityLimit = 576;

    /// <summary>What one resolved mention may weigh in an answer: its id, handle and address, with room to spare, and its
    /// name, each of its characters escaped at worst.</summary>
    public const int Weight = 2048 + 6 * ProfileReader.DisplayNameLimit;

    /// <summary>A participant's mention target, <c>/p/</c> and their id: the one place it is written.</summary>
    public static string Target(string participantId) => Prefix + participantId;

    /// <summary>A Tangent's link target in a body, by its slug.</summary>
    public static string TangentTarget(string slug) => "/t/" + slug;

    /// <summary>The most UTF-8 bytes writing one typed handle as its mention adds to a body: <c>[</c> before it, and
    /// <c>](/p/</c>, the id and <c>)</c> after it.</summary>
    public const int TypedWeight = 7 + 36;

    /// <summary>The most UTF-8 bytes writing one typed Tangent as its link adds to a body: <c>[</c> before it, and
    /// <c>](/t/</c>, its slug and <c>)</c> after it.</summary>
    public const int LinkedWeight = 7 + 4 * Slug.Limit;

    /// <summary>The most UTF-8 bytes writing one typed role as its mention adds to a body: <c>[</c> before it, and
    /// <c>](role:</c>, its word and <c>)</c> after it.</summary>
    public const int RoleWeight = 9 + Slug.Limit;

    /// <summary>The participant's id a link target names when it is a mention; null for any other target.</summary>
    public static string? Identity(string target)
        => target.StartsWith(Prefix, StringComparison.Ordinal) && Participant.IsId(target[Prefix.Length..]) ? target[Prefix.Length..] : null;

    /// <summary>The key of the role a link target names when it is a role's mention: <c>role:</c> and the word of a role
    /// the place gives (<see cref="Access.Roles.Given"/>); null for any other target.</summary>
    public static string? RoleOf(string target)
        => target.StartsWith(RoleTokens.RolePrefix, StringComparison.Ordinal) && Access.Roles.KeyOf(target[RoleTokens.RolePrefix.Length..]) == target
           && Access.Roles.Given(target) ? target : null;

    /// <summary>The participants these Posts mention, by id, and the roles they mention that still exist, by key, as a page
    /// shows them, in one keyed read of at most <see cref="Limit"/> identities a Post and one of their roles.</summary>
    public static async Task<IReadOnlyDictionary<string, TopicPage.Mention>> Resolve(IEnumerable<Post> posts, string baseUrl,
        CancellationToken ct)
    {
        var shown = posts.ToList();
        var resolved = new Dictionary<string, TopicPage.Mention>(StringComparer.Ordinal);
        var ids = shown.SelectMany(post => post.MentionedIds).Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count > 0)
            foreach (var participant in (await Participant.Get(ids, ct)).OfType<Participant>())
                resolved[participant.Id] = new TopicPage.Mention(participant.Id, participant.ShownHandle(), participant.ShownName(),
                    ProfilePage.AddressOf(baseUrl, participant.ShownHandle()));

        var keys = shown.SelectMany(post => post.MentionedRoles).Distinct(StringComparer.Ordinal).ToList();
        if (keys.Count > 0)
            foreach (var role in (await Role.Get(keys.Select(Role.KeyFor).ToList(), ct)).OfType<Role>())
                resolved[role.Key] = new TopicPage.Mention(role.Key, Access.Roles.Word(role.Key), role.Name, null);
        return resolved;
    }

    /// <summary>The participants among <paramref name="resolved"/> that <paramref name="post"/> mentions, in the order of
    /// their first mention, then the roles it mentions.</summary>
    public static IReadOnlyList<TopicPage.Mention> Of(Post post, IReadOnlyDictionary<string, TopicPage.Mention> resolved)
        => post.MentionedIds.Concat(post.MentionedRoles).Select(id => resolved.GetValueOrDefault(id)).OfType<TopicPage.Mention>().ToList();

    /// <summary>A Post's body at a version as safe HTML, each mention of a participant among <paramref name="mentioned"/> a
    /// link to their address reading <c>@</c> and their name now, titled <c>@</c> and their handle, each mention of a role
    /// among them its own words marked, and every other mention its own words.</summary>
    public static string Html(string postId, long version, string body, IReadOnlyList<TopicPage.Mention> mentioned)
        => RenderedBodies.For(postId, version, body).Html(target => (Identity(target) ?? RoleOf(target)) is { } id
            && mentioned.FirstOrDefault(mention => mention.Id == id) is { } mention
                ? new MarkdownHtml.Link(mention.Address, "@" + mention.Name, mention.Address is null ? null : "@" + mention.Handle)
                : null);

    /// <summary>A Post's body as plain text on one line, each mention of a participant among <paramref name="people"/>
    /// <c>@</c> and their name now, and every other mention its own words.</summary>
    public static string PlainText(Post post, IReadOnlyDictionary<string, Render.Person> people)
        => RenderedBodies.For(post).PlainText(Words(people));

    /// <summary>A stretch of a body cut anywhere as plain text on one line, read as <see cref="PlainText"/> reads a whole
    /// one. Parsed each time, a stretch being small; one too intricate to parse stays as it is.</summary>
    public static string PlainFragment(string markdown, IReadOnlyDictionary<string, Render.Person> people)
        => MarkdownHtml.Tractable(markdown) ? RenderedBodies.Render(markdown).PlainText(Words(people)) : markdown;

    private static Func<string, string?> Words(IReadOnlyDictionary<string, Render.Person> people)
        => target => Identity(target) is { } id && people.TryGetValue(id, out var person) ? "@" + person.Name : null;

    /// <summary>A Post's body at a version as plain text on one line with its mentions of participants left out, so it
    /// names no one, and no punctuation stranded where one stood.</summary>
    public static string Unnamed(string postId, long version, string body)
        => Stranded().Replace(RenderedBodies.For(postId, version, body).PlainText(target => Identity(target) is null ? null : ""), "");

    /// <summary>Appends, inside the running command, one <see cref="EventKind.Mention"/> per participant
    /// <paramref name="post"/> mentions that <paramref name="before"/> — the identities and roles it mentioned before an
    /// edit, empty for a new Post — did not, targeted at them: not the actor, not an id that is no participant's, and not a
    /// participant for whom the Post's Tangent is absent; and one per role it newly mentions, naming the role and
    /// targeting no one, which reaches each holder who may view the Tangent (<see cref="Unread"/>). One keyed read of the
    /// participants, then capability's rows for each; nothing parses.</summary>
    public static async Task Notify(Post post, string actorId, IReadOnlyList<string> before, DateTimeOffset at,
        CancellationToken ct)
    {
        var earlier = before.ToHashSet(StringComparer.Ordinal);
        foreach (var role in post.MentionedRoles.Where(role => !earlier.Contains(role)))
            await Event.Append(EventKind.Mention, at, ct,
                tangentId: post.TangentId, topicId: post.TopicId, postId: post.Id, actorId: actorId, role: role, version: post.Version);
        var added = post.MentionedIds.Where(id => id != actorId && !earlier.Contains(id)).ToList();
        if (added.Count == 0) return;
        var participants = (await Participant.Get(added, ct)).OfType<Participant>().Select(participant => participant.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in added.Where(participants.Contains))
        {
            if (!(await Capabilities.Load(id, post.TangentId, null, ct)).Exists) continue;
            await Event.Append(EventKind.Mention, at, ct,
                tangentId: post.TangentId, topicId: post.TopicId, postId: post.Id, actorId: actorId, targetId: id, version: post.Version);
        }
    }

    /// <summary><paramref name="body"/> as its writer typed it, written: each handle typed in its prose that names a
    /// participant here written as their mention, <c>[@handle](/p/id)</c> with the words as typed, so the body keeps
    /// their identity and a handle that changes later changes nothing in it; each <c>#</c> and slug of a Tangent written as
    /// a link to it, <c>[#slug](/t/slug)</c>; and each <c>@</c> and word of a role the place gives
    /// (<see cref="Access.Roles.Given"/>) — its word, or its word and an <c>s</c>, as <c>@moderators</c> — written as its
    /// mention, <c>[@moderators](role:moderator)</c> with the words as typed. A typed handle is atproto's handle syntax
    /// after an <c>@</c> that begins a word (<see cref="BeginsAWord"/>), a role a slug there, and a Tangent a slug after a
    /// <c>#</c> that begins one; none is ever inside a URL, so an address (<c>ines@bsky.social</c>), a path
    /// (<c>/@ines.bsky.social</c>) and a fragment stay as typed; only prose is read (<see cref="MarkdownHtml.Prose"/>), so
    /// code, a link's words and an image's description stay as typed too. The first <see cref="Limit"/> of each are read
    /// in one keyed read each, a handle as typed or in lower case, as atproto writes handles; what nothing here holds stays
    /// the words typed. At most <see cref="Limit"/> handles, as many Tangents and as many roles are written, the first
    /// typed, each adding at most <see cref="TypedWeight"/>, <see cref="LinkedWeight"/> or <see cref="RoleWeight"/>, so the
    /// body grows within <see cref="Post.KeptBodyLimit"/>. A body without an <c>@</c> or a <c>#</c> is returned unparsed;
    /// ask <see cref="MarkdownHtml.Tractable"/> first.</summary>
    public static async Task<string> Typed(string body, CancellationToken ct)
    {
        var typed = TypedNames(body);
        if (typed.Count == 0) return body;
        var handles = typed.Where(name => name.Sign == '@' && name.Text.Contains('.')).Select(name => name.Text)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(Limit).ToList();
        var words = typed.Where(name => name.Sign == '@' && !name.Text.Contains('.')).Select(name => name.Text.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal).Take(Limit).ToList();
        var slugs = typed.Where(name => name.Sign == '#').Select(name => name.Text.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal).Take(Limit).ToList();

        var holders = new Dictionary<string, string>(StringComparer.Ordinal);
        if (handles.Count > 0)
        {
            var asked = handles.Concat(handles.Select(handle => handle.ToLowerInvariant())).Distinct(StringComparer.Ordinal).ToList();
            foreach (var participant in await Participant.Query(participant => asked.Contains(participant.Handle),
                         new QueryDefinition { Page = 1, PageSize = asked.Count }, ct))
                if (Identity(Target(participant.Id)) is not null) holders.TryAdd(participant.Handle, participant.Id);
        }

        var tangents = slugs.Count == 0 ? new HashSet<string>(StringComparer.Ordinal)
            : (await Tangents.Tangent.Get(slugs, ct)).OfType<Tangents.Tangent>().Select(tangent => tangent.Id).ToHashSet(StringComparer.Ordinal);
        var roles = await RolesNamed(words, ct);
        if (holders.Count == 0 && tangents.Count == 0 && roles.Count == 0) return body;

        var written = new StringBuilder(body.Length);
        int at = 0, people = 0, places = 0, named = 0;
        foreach (var (sign, start, length, text) in typed)
        {
            string target;
            if (sign == '#')
            {
                var slug = text.ToLowerInvariant();
                if (places == Limit || !tangents.Contains(slug)) continue;
                target = TangentTarget(slug);
                places++;
            }
            else if (!text.Contains('.'))
            {
                if (named == Limit || roles.GetValueOrDefault(text.ToLowerInvariant()) is not { } role) continue;
                target = role;
                named++;
            }
            else
            {
                if (people == Limit || (holders.GetValueOrDefault(text) ?? holders.GetValueOrDefault(text.ToLowerInvariant())) is not { } id) continue;
                target = Target(id);
                people++;
            }

            written.Append(body, at, start - at).Append('[').Append(body, start, length).Append("](").Append(target).Append(')');
            at = start + length;
        }

        return written.Append(body, at, body.Length - at).ToString();
    }

    /// <summary>The roles the place gives that these typed words name, by word: the role keyed by the word, or else by the
    /// word without its last <c>s</c>; one keyed read.</summary>
    private static async Task<IReadOnlyDictionary<string, string>> RolesNamed(IReadOnlyList<string> words, CancellationToken ct)
    {
        var named = new Dictionary<string, string>(StringComparer.Ordinal);
        static IEnumerable<string> Keys(string word)
        {
            if (Access.Roles.KeyOf(word) is { } key && Access.Roles.Given(key)) yield return key;
            if (word.Length > 1 && word.EndsWith('s') && Access.Roles.KeyOf(word[..^1]) is { } singular && Access.Roles.Given(singular)) yield return singular;
        }

        var keys = words.SelectMany(Keys).Distinct(StringComparer.Ordinal).ToList();
        if (keys.Count == 0) return named;
        var found = (await Role.Get(keys.Select(Role.KeyFor).ToList(), ct)).OfType<Role>().Select(role => role.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var word in words)
            if (Keys(word).FirstOrDefault(found.Contains) is { } key) named[word] = key;
        return named;
    }

    /// <summary>Each name typed in the prose of <paramref name="body"/>, as <see cref="Typed"/> reads one: its sign, where
    /// the sign stands, how long it is with it, and the name, in the order of the source. One parse, and one walk of the
    /// source for the words and URLs the signs stand in.</summary>
    private static List<(char Sign, int Start, int Length, string Text)> TypedNames(string body)
    {
        var found = new List<(char Sign, int Start, int Length, string Text)>();
        if (body.IndexOfAny(Signs) < 0) return found;
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
            for (var sign = body.IndexOfAny(Signs, from, end - from); sign >= 0; sign = sign + 1 < end ? body.IndexOfAny(Signs, sign + 1, end - sign - 1) : -1)
            {
                for (; walked < sign; walked++)
                {
                    if (char.IsWhiteSpace(body[walked])) word = walked + 1;
                    else if (string.CompareOrdinal(body, walked, "://", 0, 3) == 0) scheme = walked;
                }

                if (!BeginsAWord(body, sign) || scheme >= word) continue;
                var last = sign + 1;
                if (body[sign] == '@')
                {
                    while (last < end && (char.IsAsciiLetterOrDigit(body[last]) || body[last] is '.' or '-')) last++;
                    while (last > sign + 1 && body[last - 1] is '.' or '-') last--;
                    var name = body[(sign + 1)..last];
                    if (name.Contains('.') ? name.Length <= SignIn.HandleLimit && HandleSyntax().IsMatch(name) : Slug.IsValid(name.ToLowerInvariant()))
                        found.Add(('@', sign, last - sign, name));
                }
                else
                {
                    while (last < end && (char.IsLetterOrDigit(body[last]) || body[last] == '-')) last++;
                    while (last > sign + 1 && body[last - 1] == '-') last--;
                    var name = body[(sign + 1)..last];
                    if (Slug.IsValid(name.ToLowerInvariant())) found.Add(('#', sign, last - sign, name));
                }
            }
        }

        return found;
    }

    /// <summary>The signs a name is typed after: <c>@</c> for a person or a role, <c>#</c> for a Tangent.</summary>
    private static readonly char[] Signs = ['@', '#'];

    /// <summary>Whether the sign at <paramref name="sign"/> begins a word: unless the character before it, past any
    /// emphasis marks (<c>*</c> and <c>_</c>) right before it, belongs to a word — a letter, a digit or a mark that joins
    /// one — or to an address or a path: <c>.</c>, <c>+</c>, <c>-</c>, <c>/</c>, <c>\</c>, <c>=</c>, <c>%</c>,
    /// <c>&amp;</c>, <c>~</c>, <c>#</c> or <c>@</c>. So the start of a line, whitespace, a quotation mark of any kind, a
    /// dash other than the hyphen, a colon, a bracket and other punctuation begin one, <c>_@</c> opening emphasis does,
    /// and <c>snake_@</c> does not; so <c>##</c> and <c>&amp;#</c> never begin one.</summary>
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

    [GeneratedRegex(@"^[\s,.;:!?]+|\s+(?=[,.;:!?])")]
    private static partial Regex Stranded();

    /// <summary>atproto's handle syntax: dot-separated labels of letters, digits and inner hyphens, at least two, the
    /// last starting with a letter.</summary>
    [GeneratedRegex(@"^([a-zA-Z0-9]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]([a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?$")]
    private static partial Regex HandleSyntax();
}
