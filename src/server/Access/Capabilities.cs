using Koan.Core.Hosting.App;
using Koan.Identity.Access;
using Koan.Identity.Roles;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Space;
using Tangent.Tangents;

namespace Tangent.Access;

/// <summary>What one participant holds on one resource, and the one reason for each verb they do not. Discord's model
/// on Koan's roles and overwrites, resolved by <see cref="For"/> in this order: standing; <see cref="Bit.View"/> on the
/// Tangent, from the place's audience, the bag and the overwrites, without which the resource is absent to the reader and
/// nothing else is said; the Post's state; the bag and the overwrites for every other bit; authorship. Koan resolves the
/// bag and the overwrites (<see cref="RoleBag.On"/>) and names the fact that decided each missing bit; the first step
/// that drops a verb names its gate, which puts the words in their precedence: <see cref="SignIn"/>, <see cref="Apply"/>,
/// <see cref="Declined"/>, <see cref="TimedOut"/>, <see cref="Absent"/>, <see cref="Locked"/>, <see cref="Removed"/>. Computed per request from a
/// few small rows; nothing stores it.</summary>
public sealed class Capabilities
{
    public const string SignIn = "sign-in";
    public const string Apply = "apply";

    /// <summary>The participant's application was declined: they read what a visitor reads, and nothing more.</summary>
    public const string Declined = "declined";
    public const string TimedOut = "timed-out";

    /// <summary>The resource does not exist for this reader: banned, or without <see cref="Bit.View"/> where they
    /// stand. Every verb carries this one reason, and a command answers what it answers for a missing resource.</summary>
    public const string Absent = "absent";
    public const string Locked = "locked";
    public const string Removed = "removed";

    /// <summary>The reason <see cref="Bit.EditOwn"/> and <see cref="Bit.RemoveOwn"/> are missing on a Post that is
    /// not the participant's own, or where no Post is in question.</summary>
    public const string NotYours = "not yours";

    /// <summary>The resource resolution names where no Tangent is in question: the place, which has no overwrites. The
    /// bag alone decides there.</summary>
    private const string Place = "place";

    private readonly Dictionary<Bit, string> _missing;

    private Capabilities(Bit held, bool exists, Dictionary<Bit, string> missing, IReadOnlyList<string> roleIds)
    {
        Held = held;
        Exists = exists;
        _missing = missing;
        RoleIds = roleIds;
    }

    /// <summary>The roles in the participant's bag, by id; empty for a visitor.</summary>
    public IReadOnlyList<string> RoleIds { get; }

    /// <summary>The bits the participant holds on this resource.</summary>
    public Bit Held { get; }

    /// <summary>Whether the resource exists for this reader: false where they lack <see cref="Bit.View"/> on the
    /// Tangent or the place, when a command answers as for a missing resource. A removed Post exists, as a tombstone.</summary>
    public bool Exists { get; }

    /// <summary>The verbs the participant holds here, by name: the value every resource carries.</summary>
    public IReadOnlyList<string> Can => Bits.Names(Held);

    public bool Holds(Bit verb) => (Held & verb) == verb;

    /// <summary>Null when the verb is held; otherwise the one reason it is not: <see cref="SignIn"/>,
    /// <see cref="Apply"/>, <see cref="Declined"/>, <see cref="TimedOut"/>, <see cref="Absent"/>, <see cref="Locked"/> or <see cref="Removed"/>
    /// where standing, audience or state decided, or the deciding overwrite or the absence of a role where the bag and
    /// the overwrites decided.</summary>
    public string? Gate(Bit verb)
    {
        if (!Bits.Each.Contains(verb)) throw new ArgumentException("A gate is asked for one bit.", nameof(verb));
        return _missing.GetValueOrDefault(verb);
    }

    /// <summary>The reason an overwrite's deny decides a verb.</summary>
    public static string Denied(string subject, Bit verb) => $"{subject} denies {verb}";

    /// <summary>The reason the bag decides a verb: no role of the participant's grants it.</summary>
    public static string NoRoleGrants(Bit verb) => $"no role grants {verb}";

    /// <summary>One pure function over its inputs. <paramref name="participant"/> is null for a visitor.
    /// <paramref name="tokens"/> are Koan's bag for the participant (<see cref="RoleBag.Tokens"/>), the anonymous bag for
    /// a visitor: their roles and the permissions those roles grant, the <see cref="Roles.Everyone"/> role's among them.
    /// <paramref name="space"/> states the place's audience, which is where everyone's <see cref="Bit.View"/> on a public
    /// place comes from. <paramref name="tangentId"/> is the Tangent asked about, null for the place; its resource
    /// (<see cref="Overwrites.Resource"/>) is the one the overwrites are resolved on. <paramref name="overwrites"/> are
    /// Koan's overwrites there, those the bag meets (<see cref="RoleCollection.OverwritesFor"/>) or any superset of them;
    /// the Tangent Space itself has none. <paramref name="now"/> decides whether a
    /// timeout or an overwrite has ended. <paramref name="post"/> is the Topic's root for a question about the Topic, the
    /// Post itself for a question about that Post, and null for a Tangent or the place. <paramref name="root"/> is the
    /// Topic's root for a question about a reply: a locked or removed root drops <see cref="Bit.Post"/> on every reply of
    /// its Topic with that gate, while the reply's own removal governs everything else.</summary>
    public static Capabilities For(Participant? participant, IEnumerable<string> tokens, TangentSpace space, string? tangentId,
        IReadOnlyList<PermissionOverwrite> overwrites, DateTimeOffset now, Post? post = null, Post? root = null)
    {
        var held = tokens as IReadOnlyCollection<string> ?? tokens.ToList();
        var bag = new RoleBag(participant?.Id, participant is not null, held.Where(token => !Granted(token)), held.Where(Granted));
        return For(participant, space, Resolve(participant, bag, space, tangentId, overwrites, now), now, post, root);

        static bool Granted(string token) => token.StartsWith(RoleTokens.GlobalPrefix, StringComparison.Ordinal);
    }

    private static Capabilities For(Participant? participant, TangentSpace space, Resolved resolved, DateTimeOffset now,
        Post? post, Post? root)
    {
        var missing = new Dictionary<Bit, string>();
        var held = Bits.All;
        var viewIfPublic = space.Audience == Audience.Public ? Bit.View : Bit.None;

        // Standing precedes everything: a cap on what any later step may leave. A timeout that has ended is over.
        switch (participant?.StandingAt(now))
        {
            case null:
                Drop(Bits.All & ~viewIfPublic, SignIn);
                break;
            case Standing.Banned:
                Drop(Bits.All, Absent);
                break;
            case Standing.Applied:
                Drop(Bits.All & ~viewIfPublic, Apply);
                break;
            case Standing.Declined:
                Drop(Bits.All & ~viewIfPublic, Declined);
                break;
            case Standing.TimedOut:
                Drop(Bits.All & ~Bit.View, TimedOut);
                break;
        }

        // View on the Tangent: without it the resource is absent to the reader, and every verb says only that.
        if (!resolved.Granted.HasFlag(Bit.View)) Drop(Bit.View, Absent);
        if (!held.HasFlag(Bit.View))
        {
            var reason = missing[Bit.View];
            foreach (var bit in Bits.Each) missing[bit] = reason;
            return new Capabilities(Bit.None, false, missing, resolved.RoleIds);
        }

        // Replying is judged by the Topic, whose state lives on its root; everything else by the Post itself.
        var topic = root ?? post;
        if (topic is { Locked: true }) Drop(Bit.Post, Locked);
        if (topic is { Removed: true }) Drop(Bit.Post, Removed);
        if (post is { Removed: true }) Drop(Bits.All, Removed);

        foreach (var (bit, reason) in resolved.Withheld) Drop(bit, reason);

        if (post is null || participant is null || post.AuthorId != participant.Id) Drop(Bit.EditOwn | Bit.RemoveOwn, NotYours);

        return new Capabilities(held, true, missing, resolved.RoleIds);

        void Drop(Bit bits, string reason)
        {
            foreach (var bit in Bits.Each)
            {
                if (bits.HasFlag(bit) && held.HasFlag(bit))
                {
                    held &= ~bit;
                    missing[bit] = reason;
                }
            }
        }
    }

    /// <summary>What the bag and the overwrites leave for one reader on one resource: the bits granted, the reason for
    /// each bit withheld, and the roles in the bag.</summary>
    internal sealed record Resolved(Bit Granted, IReadOnlyList<(Bit Bit, string Reason)> Withheld, IReadOnlyList<string> RoleIds);

    /// <summary>Koan's resolution in Discord's order (<see cref="RoleBag.On"/>) over the bag, with everyone's
    /// <see cref="Bit.View"/> on a public place in its base, and the overwrites on the resource of
    /// <paramref name="tangentId"/>, or on the place where it names no Tangent; an administrator holds every bit and no
    /// overwrite touches it. For each bit withheld, Koan's deciding fact in
    /// Tangent's words: the overwrite whose deny decided it, by its subject, or that no role grants it.</summary>
    private static Resolved Resolve(Participant? participant, RoleBag bag, TangentSpace space, string? tangentId,
        IReadOnlyList<PermissionOverwrite> overwrites, DateTimeOffset now)
    {
        var view = Bits.Token(Bit.View);
        if (space.Audience == Audience.Public && !bag.Permissions.Contains(view, StringComparer.Ordinal))
            bag = new RoleBag(bag.PersonId, bag.IsAuthenticated, bag.Roles, [.. bag.Permissions, view], bag.Highest);
        var here = bag.On(ResourceOf(tangentId) ?? Place, overwrites, now);
        var granted = here.IsAdministrator ? Bits.All : Bits.InTokens(here.Permissions);
        var withheld = Bits.Each.Where(bit => !granted.HasFlag(bit))
            .Select(bit => (bit, Reason(here.Explain(Bits.Token(bit)), bit))).ToList();
        IReadOnlyList<string> roleIds = participant is null
            ? []
            : bag.Roles.Where(role => role is not (RoleTokens.Everyone or RoleTokens.Authenticated)).ToList();
        return new Resolved(granted, withheld, roleIds);
    }

    /// <summary>Koan's deciding fact for a missing bit, in Tangent's words.</summary>
    private static string Reason(AccessDecision decision, Bit verb) =>
        decision.ContributingFacts is [{ Kind: AccessFactKind.Deny, Subject: { } subject }] ? Denied(subject, verb) : NoRoleGrants(verb);

    /// <summary>What a command asks before it answers anything else, so that a reader without <see cref="Bit.View"/> is
    /// answered as for a missing resource first: the rows <see cref="LoadRows"/> reads, asked about
    /// <paramref name="post"/>, or about the Tangent or the place where it is null.</summary>
    public static async Task<Capabilities> Load(string? participantId, string? tangentId, Post? post, CancellationToken ct)
        => (await LoadRows(participantId, tangentId, ct)).For(post);

    /// <summary>The rows what one participant holds in one Tangent, or in the place where <paramref name="tangentId"/> is
    /// null, is computed from, each read bounded and without a lock: the participant's row, Koan's cached bag for them,
    /// the settings row, and the Tangent's overwrites the bag meets, from Koan's cache (<see cref="OverwritesOf"/>). A
    /// participant without a row is a visitor. The bag and the overwrites are resolved once, so <see cref="Rows.For"/>
    /// answers for the Tangent and each of its Posts without another read. A page that answers for a Tangent reads the
    /// Tangent's row before these: a Tangent it finds is one whose audience is already among the overwrites read
    /// here.</summary>
    public static async Task<Rows> LoadRows(string? participantId, string? tangentId, CancellationToken ct)
    {
        var reader = await Reader(participantId, ct);
        var overwrites = await OverwritesOf(reader, tangentId, ct);
        var now = DateTimeOffset.UtcNow;
        return new Rows(reader.Participant, reader.Space, Resolve(reader.Participant, reader.Bag, reader.Space, tangentId, overwrites, now), now);
    }

    /// <summary>What one participant holds on each of several Tangents, from the reads <see cref="LoadRows"/> makes once
    /// and the overwrites of each from Koan's cache, by Tangent id.</summary>
    public static async Task<IReadOnlyDictionary<string, Capabilities>> LoadEach(string? participantId, IReadOnlyList<string> tangentIds,
        CancellationToken ct)
    {
        var reader = await Reader(participantId, ct);
        var now = DateTimeOffset.UtcNow;
        var each = new Dictionary<string, Capabilities>(StringComparer.Ordinal);
        foreach (var id in tangentIds.Distinct(StringComparer.Ordinal))
        {
            var overwrites = await OverwritesOf(reader, id, ct);
            each[id] = For(reader.Participant, reader.Space, Resolve(reader.Participant, reader.Bag, reader.Space, id, overwrites, now), now, null, null);
        }

        return each;
    }

    /// <summary>The overwrites on a Tangent's resource that the reader's bag meets, from Koan's cache: those of the bag's
    /// roles, <c>everyone</c> among them, and the reader's own (<see cref="RoleCollection.OverwritesFor"/>), however many
    /// the resource holds for others. An id that names no resource (<see cref="ResourceOf"/>) reads none, and is
    /// answered as a missing Tangent is.</summary>
    private static async Task<IReadOnlyList<PermissionOverwrite>> OverwritesOf(ReaderRows reader, string? tangentId, CancellationToken ct)
        => ResourceOf(tangentId) is { } resource ? await reader.Roles.OverwritesFor(reader.Bag, resource, ct) : [];

    /// <summary>The resource a Tangent's overwrites are written on; null for the place's null and for an id that cannot
    /// be a Tangent's slug, which names no Tangent.</summary>
    private static string? ResourceOf(string? tangentId)
        => tangentId is null || !Slug.IsValid(tangentId) ? null : Overwrites.Resource(tangentId);

    /// <summary>The rows one reader's capabilities in one Tangent are computed from, read once, with the bag and the
    /// overwrites resolved.</summary>
    public sealed class Rows
    {
        private readonly Resolved _resolved;
        private readonly DateTimeOffset _now;

        internal Rows(Participant? participant, TangentSpace space, Resolved resolved, DateTimeOffset now)
        {
            Participant = participant;
            Space = space;
            _resolved = resolved;
            _now = now;
        }

        /// <summary>The reader's row; null for a visitor.</summary>
        public Participant? Participant { get; }

        /// <summary>The settings row.</summary>
        public TangentSpace Space { get; }

        /// <summary>What the reader holds on <paramref name="post"/>, or on the Tangent where it is null;
        /// <paramref name="root"/> is the Topic's root for a question about a reply.</summary>
        public Capabilities For(Post? post, Post? root = null) => Capabilities.For(Participant, Space, _resolved, _now, post, root);
    }

    /// <summary>The rows every answer starts from: the participant, Koan's bag for them, the settings row, and the
    /// collection the overwrites are read from.</summary>
    private sealed record ReaderRows(Participant? Participant, RoleBag Bag, TangentSpace Space, RoleCollection Roles);

    private static async Task<ReaderRows> Reader(string? participantId, CancellationToken ct)
    {
        var participant = string.IsNullOrWhiteSpace(participantId) ? null : await Participant.Get(participantId, ct);
        var roles = AppHost.GetRequiredService<RoleCollection>(nameof(Capabilities));
        var bag = await roles.Bag(participant?.Id, authenticated: participant is not null, ct);
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? new TangentSpace();
        return new ReaderRows(participant, bag, space, roles);
    }
}
