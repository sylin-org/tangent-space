using Koan.Core.Hosting.App;
using Koan.Data.Core.Commands;
using Koan.Identity.Access;
using Koan.Identity.Roles;
using Koan.Web.Controllers;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Space;
using Tangent.Tangents;

namespace Tangent.Access;

/// <summary>What one participant holds on one resource, and the one reason for each verb they do not. Discord's model
/// on Koan's roles and overwrites, resolved by <see cref="For"/> in this order: standing; <see cref="Bit.View"/> on the
/// Tangent, from the place's audience, the bag and the overwrites, without which the resource is absent to the reader and
/// nothing else is said; the Topic's state and the Post's, a Topic whose root is removed taking no reply and one its
/// moderators closed none from whoever does not hold <see cref="Bit.Moderate"/> there; the bag and the overwrites for every
/// other bit; authorship; and what a bit implies, <see cref="Bit.ManageSpace"/> every Tangent's <see cref="Bit.ManageTangent"/> and <see cref="Bit.Moderate"/>
/// <see cref="Bit.EditOwn"/> and <see cref="Bit.RemoveOwn"/> on any Post, so every action asks one bit. Koan resolves the
/// bag and the overwrites (<see cref="RoleBag.On"/>) and names the fact that decided each missing bit; the first step
/// that drops a verb names its gate, which puts the words in their precedence: <see cref="SignIn"/>, <see cref="Apply"/>,
/// <see cref="Declined"/>, <see cref="TimedOut"/>, <see cref="Absent"/>, <see cref="RootRemoved"/> or <see cref="Closed"/>, <see cref="Removed"/>. Standing words
/// <see cref="Bit.View"/> for reading (<see cref="SignInToRead"/>, <see cref="ApplyToRead"/>, <see cref="DeclinedToRead"/>).
/// Computed per request from a few small rows; nothing stores it.</summary>
public sealed class Capabilities
{
    public static readonly Reason SignIn = Reason.Gated("sign-in", "Sign in to take part here.");

    /// <summary>Why a visitor may not read a private place.</summary>
    public static readonly Reason SignInToRead = Reason.Gated(SignIn.Word, "Sign in to read here.");

    public static readonly Reason Apply = Reason.Gated("apply", "Your application is waiting: you can take part here once someone admits you.");

    /// <summary>Why an applicant may not read a private place.</summary>
    public static readonly Reason ApplyToRead = Reason.Gated(Apply.Word, "Your application is waiting: you can read here once someone admits you.");

    /// <summary>The participant's application was declined: they read what a visitor reads, and nothing more.</summary>
    public static readonly Reason Declined = Reason.Gated("declined", "Your application was declined, so you read here and don’t take part.");

    /// <summary>Why a declined applicant may not read a private place.</summary>
    public static readonly Reason DeclinedToRead = Reason.Gated(Declined.Word, "Your application was declined, so this place isn’t open to you.");
    public static readonly Reason TimedOut = Reason.Gated("timed-out", "You’re timed out: you can read and report here, and post again once it ends.");

    /// <summary>Why a timed-out participant reads and reports and posts nothing, saying until when, as their door does.</summary>
    public static Reason TimedOutUntil(DateTimeOffset until)
        => Reason.Gated(TimedOut.Word, $"You’re timed out until {Render.Wording.Until(until)}: you can read and report here, and post again then.");

    /// <summary>The resource does not exist for this reader: banned, or without <see cref="Bit.View"/> where they
    /// stand. Every verb carries this one reason, and a command answers what it answers for a missing resource, which
    /// says <see cref="Answers.Nothing"/>.</summary>
    public static readonly Reason Absent = Reason.Absent("absent");
    public static readonly Reason Removed = Reason.Gated("removed", "This post is removed, so nothing changes it now.");

    /// <summary>Why a Topic its moderators closed takes no reply from whoever does not hold <see cref="Bit.Moderate"/> there:
    /// every word stays, and its reply box says who closed it and when.</summary>
    public static readonly Reason Closed = Reason.Gated("closed", "This topic is closed: every word stays, and it takes no new replies.");

    /// <summary>Why a Topic whose root is removed takes no reply, from anyone: its replies stay, and it takes no more. It says
    /// <see cref="Closed"/>'s word in a sentence of its own.</summary>
    public static readonly Reason RootRemoved = Reason.Gated(Closed.Word, "This topic is closed: its opening post was removed, so it takes no more replies.");

    /// <summary>The reason <see cref="Bit.EditOwn"/> and <see cref="Bit.RemoveOwn"/> are missing on a Post that is
    /// not the participant's own, for anyone who does not hold <see cref="Bit.Moderate"/> there.</summary>
    public static readonly Reason NotYours = Reason.Gated("not yours", "Only its author, or a moderator, can change this post.");

    /// <summary>What a gate says that a role or an overwrite decided.</summary>
    private const string NotOpen = "This isn’t open to you here.";

    /// <summary>What a person reads above a gate that a role or an overwrite decided, or that this table does not name.</summary>
    private const string NotOpenHeading = "Not open to you";

    /// <summary>What a person reads of each gate standing, audience or state decides, by its word: its sentence, and the
    /// heading above it where it is the whole page. <see cref="Closed"/> and <see cref="RootRemoved"/> share their word, whose
    /// sentence is the removed root's: wherever a Topic its moderators closed is drawn, it says who closed it and when.</summary>
    private static readonly IReadOnlyDictionary<string, (string Sentence, string Heading)> Words = new (Reason Gate, string Heading)[]
    {
        (SignIn, "Sign in first"),
        (Apply, "Waiting to be admitted"),
        (Declined, "Not admitted"),
        (TimedOut, "Timed out"),
        (Removed, "Removed"),
        (RootRemoved, "Closed"),
        (NotYours, "Not yours")
    }.ToDictionary(worded => worded.Gate.Word, worded => (worded.Gate.Text, worded.Heading), StringComparer.Ordinal);

    /// <summary>The resource resolution names where no Tangent is in question: the place, which has no overwrites. The
    /// bag alone decides there.</summary>
    private const string Place = "place";

    private readonly Dictionary<Bit, Reason> _missing;

    private Capabilities(Bit held, bool exists, Dictionary<Bit, Reason> missing, IReadOnlyList<string> roleIds, RoleBag bag)
    {
        Held = held;
        Exists = exists;
        _missing = missing;
        RoleIds = roleIds;
        Bag = bag;
    }

    /// <summary>The roles in the participant's bag, by id; empty for a visitor.</summary>
    public IReadOnlyList<string> RoleIds { get; }

    /// <summary>Koan's bag for the participant, the anonymous bag for a visitor: the roles they are in and where the
    /// highest of them stands (<see cref="RoleBag.Highest"/>), which decides whose standing and roles they change and
    /// which roles they manage (<see cref="RoleBag.Manages"/>).</summary>
    public RoleBag Bag { get; }

    /// <summary>The bits the participant holds on this resource.</summary>
    public Bit Held { get; }

    /// <summary>Whether the resource exists for this reader: false where they lack <see cref="Bit.View"/> on the
    /// Tangent or the place, when a command answers as for a missing resource. A removed Post exists, as a tombstone.</summary>
    public bool Exists { get; }

    /// <summary>The verbs the participant holds here, by name: the value every resource carries.</summary>
    public IReadOnlyList<string> Can => Bits.Names(Held);

    /// <summary>Where the Tangent's own rows of the log begin, from its row (<see cref="Tangents.Tangent.LogSeq"/>), as
    /// <see cref="LoadEach"/> answers: a row of its slug before it, that no move took into it, belongs to a Tangent deleted
    /// before this one took the slug. Past every row for a Tangent with no row of its own; zero for the place and wherever <see cref="LoadEach"/>
    /// did not answer.</summary>
    public long Since { get; private init; }

    /// <summary>Whether <paramref name="row"/>, a row of the log that names this Tangent, is one of its own: written since
    /// the Tangent began (<see cref="Since"/>), or taken into it by a move of its Topic (<see cref="Events.Event.Into"/>).</summary>
    public bool Owns(Events.Event row) => row.Seq >= Since || row.Into == Since;

    public bool Holds(Bit verb) => (Held & verb) == verb;

    /// <summary>Null when the verb is held; otherwise the one reason it is not: <see cref="SignIn"/>,
    /// <see cref="Apply"/>, <see cref="Declined"/>, <see cref="TimedOut"/>, <see cref="Absent"/>, <see cref="RootRemoved"/>,
    /// <see cref="Closed"/> or <see cref="Removed"/> where standing, audience or state decided, or the deciding overwrite or the absence of a role where the bag and
    /// the overwrites decided.</summary>
    public Reason? Gate(Bit verb)
    {
        if (!Bits.Each.Contains(verb)) throw new ArgumentException("A gate is asked for one bit.", nameof(verb));
        return _missing.GetValueOrDefault(verb);
    }

    /// <summary>The reason an overwrite's deny decides a verb.</summary>
    public static Reason Denied(string subject, Bit verb) => Reason.Gated($"{subject} denies {verb}", NotOpen);

    /// <summary>The reason the bag decides a verb: no role of the participant's grants it.</summary>
    public static Reason NoRoleGrants(Bit verb) => Reason.Gated($"no role grants {verb}", NotOpen);

    /// <summary>The one sentence a person reads for a gate's word, where a page carries the word.</summary>
    public static string Sentence(string gate) => Words.TryGetValue(gate, out var words) ? words.Sentence : NotOpen;

    /// <summary>The heading a person reads above a refusal of a gate's word, where the refusal is the whole page.</summary>
    public static string Heading(string gate) => Words.TryGetValue(gate, out var words) ? words.Heading : NotOpenHeading;

    /// <summary>Whether a gate's word is one standing, audience or state decides, which has a heading of its own.</summary>
    public static bool Worded(string gate) => Words.ContainsKey(gate);

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
    /// Topic's root for a question about a reply: a removed root drops <see cref="Bit.Post"/> on every reply of
    /// its Topic, and a closed one drops it for whoever does not hold <see cref="Bit.Moderate"/> there, while the reply's
    /// own removal governs everything else.</summary>
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
        var missing = new Dictionary<Bit, Reason>();
        var held = Bits.All;
        var viewIfPublic = space.Audience == Audience.Public ? Bit.View : Bit.None;

        // Standing precedes everything: a cap on what any later step may leave, worded for reading where it takes View. A
        // timeout that has ended is over.
        switch (participant?.StandingAt(now))
        {
            case null:
                Drop(Bit.View & ~viewIfPublic, SignInToRead);
                Drop(Bits.All & ~Bit.View, SignIn);
                break;
            case Standing.Banned:
                Drop(Bits.All, Absent);
                break;
            case Standing.Applied:
                Drop(Bit.View & ~viewIfPublic, ApplyToRead);
                Drop(Bits.All & ~Bit.View, Apply);
                break;
            case Standing.Declined:
                Drop(Bit.View & ~viewIfPublic, DeclinedToRead);
                Drop(Bits.All & ~Bit.View, Declined);
                break;
            case Standing.TimedOut:
                Drop(Bits.All & ~Bit.View, participant.StandingUntil is { } until ? TimedOutUntil(until) : TimedOut);
                break;
        }

        // View on the Tangent: without it the resource is absent to the reader, and every verb says only that.
        if (!resolved.Granted.HasFlag(Bit.View)) Drop(Bit.View, Absent);
        if (!held.HasFlag(Bit.View))
        {
            var reason = missing[Bit.View];
            foreach (var bit in Bits.Each) missing[bit] = reason;
            return new Capabilities(Bit.None, false, missing, resolved.RoleIds, resolved.Bag);
        }

        // Replying is judged by the Topic, whose state lives on its root: a Topic whose root is removed is closed to every
        // reply, and one its moderators closed to whoever does not moderate there; everything else by the Post itself.
        // Nothing applies to a tombstone but removing it again, which changes nothing and is judged as the Post stood.
        var topic = root ?? post;
        var tombstone = post is { Removed: true };
        if (topic is { Removed: true }) Drop(Bit.Post, RootRemoved);
        if (topic is { Closed: true } && !resolved.Granted.HasFlag(Bit.Moderate)) Drop(Bit.Post, Closed);
        if (tombstone) Drop(Bits.All & ~(Bit.RemoveOwn | Bit.Moderate), Removed);

        // Whoever may not remove a tombstone again is told it is removed.
        foreach (var (bit, reason) in resolved.Withheld) Drop(bit, tombstone && bit == Bit.RemoveOwn ? Removed : reason);

        // Authorship is asked of a Post; of a Tangent or the place, a bit of it says what the reader may do to their own.
        if (post is not null && post.AuthorId != participant?.Id) Drop(Bit.EditOwn | Bit.RemoveOwn, tombstone ? Removed : NotYours);

        if (held.HasFlag(Bit.ManageSpace)) Imply(Bit.ManageTangent);
        if (held.HasFlag(Bit.Moderate)) Imply(tombstone ? Bit.RemoveOwn : Bit.EditOwn | Bit.RemoveOwn);
        if (tombstone) Drop(Bit.Moderate, Removed);

        return new Capabilities(held, true, missing, resolved.RoleIds, resolved.Bag);

        void Imply(Bit bits)
        {
            held |= bits;
            foreach (var bit in Bits.Each)
                if (bits.HasFlag(bit)) missing.Remove(bit);
        }

        void Drop(Bit bits, Reason reason)
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
    /// each bit withheld, the roles in the bag, and the bag.</summary>
    internal sealed record Resolved(Bit Granted, IReadOnlyList<(Bit Bit, Reason Why)> Withheld, IReadOnlyList<string> RoleIds, RoleBag Bag);

    /// <summary>Koan's resolution in Discord's order (<see cref="RoleBag.On"/>) over the bag, with everyone's
    /// <see cref="Bit.View"/> on a public place in its base, and the overwrites on the resource of
    /// <paramref name="tangentId"/>, or on the place where it names no Tangent, as a public Tangent meets them
    /// (<see cref="AsPublic"/>); an administrator holds every bit and no overwrite touches it. For each bit withheld,
    /// Koan's deciding fact in Tangent's words: the overwrite whose deny decided it, by its subject, or that no role
    /// grants it.</summary>
    private static Resolved Resolve(Participant? participant, RoleBag bag, TangentSpace space, string? tangentId,
        IReadOnlyList<PermissionOverwrite> overwrites, DateTimeOffset now)
    {
        var view = Bits.Token(Bit.View);
        if (space.Audience == Audience.Public && !bag.Permissions.Contains(view, StringComparer.Ordinal))
            bag = new RoleBag(bag.PersonId, bag.IsAuthenticated, bag.Roles, [.. bag.Permissions, view], bag.Highest);
        var resource = ResourceOf(tangentId) ?? Place;
        var here = bag.On(resource, AsPublic(resource, overwrites, now), now);
        var granted = here.IsAdministrator ? Bits.All : Bits.InTokens(here.Permissions);
        var withheld = Bits.Each.Where(bit => !granted.HasFlag(bit))
            .Select(bit => (bit, Deciding(here.Explain(Bits.Token(bit)), bit))).ToList();
        IReadOnlyList<string> roleIds = participant is null
            ? []
            : bag.Roles.Where(role => role is not (RoleTokens.Everyone or RoleTokens.Authenticated)).ToList();
        return new Resolved(granted, withheld, roleIds, bag);
    }

    /// <summary>The overwrites on <paramref name="resource"/> as its audience reads them: where its <c>everyone</c>
    /// overwrite allows <see cref="Bit.View"/>, a public Tangent, everyone who reads the place reads it, so a person's own
    /// deny of <see cref="Bit.View"/> is left out and every other bit of theirs stands; the row stays stored and keeps
    /// them out again once the Tangent is private. A filter over the rows already loaded.</summary>
    private static IReadOnlyList<PermissionOverwrite> AsPublic(string resource, IReadOnlyList<PermissionOverwrite> overwrites, DateTimeOffset now)
    {
        var view = Bits.Token(Bit.View);
        if (!overwrites.Any(row => row.ResourceId == resource && row.SubjectKind == OverwriteSubjectKind.Role
                && row.SubjectId == RoleTokens.Everyone && row.AppliesAt(now) && row.Allow.Contains(view)))
            return overwrites;
        return overwrites.Select(row => row.SubjectKind == OverwriteSubjectKind.Person && row.Deny.Contains(view)
            ? new PermissionOverwrite
            {
                Id = row.Id, ResourceId = row.ResourceId, SubjectKind = row.SubjectKind, SubjectId = row.SubjectId,
                Allow = [.. row.Allow], Deny = [.. row.Deny.Where(token => token != view)], ExpiresAt = row.ExpiresAt
            }
            : row).ToList();
    }

    /// <summary>Koan's deciding fact for a missing bit, in Tangent's words.</summary>
    private static Reason Deciding(AccessDecision decision, Bit verb) =>
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

    /// <summary>What one participant holds on each of several Tangents, by Tangent id: one keyed read of their rows, the
    /// reads <see cref="LoadRows"/> makes once, and the overwrites of each from Koan's cache, each answer saying where its
    /// Tangent's own rows of the log begin (<see cref="Since"/>). Every caller names Tangents that other rows name — rows
    /// of the log, asks, origins — so a Tangent with no row of its own exists for no one, administrators among them: it
    /// holds nothing, and no row of the log is its own.</summary>
    public static async Task<IReadOnlyDictionary<string, Capabilities>> LoadEach(string? participantId, IReadOnlyList<string> tangentIds,
        CancellationToken ct)
    {
        var ids = tangentIds.Distinct(StringComparer.Ordinal).ToList();
        var each = new Dictionary<string, Capabilities>(StringComparer.Ordinal);
        if (ids.Count == 0) return each;
        var rows = await Tangents.Tangent.Get(ids, ct);
        var reader = await Reader(participantId, ct);
        var now = DateTimeOffset.UtcNow;
        for (var index = 0; index < ids.Count; index++)
        {
            var id = ids[index];
            if (rows[index] is not { } row)
            {
                each[id] = new Capabilities(Bit.None, false, Bits.Each.ToDictionary(bit => bit, _ => Absent), [], reader.Bag) { Since = long.MaxValue };
                continue;
            }

            var overwrites = await OverwritesOf(reader, id, ct);
            var held = For(reader.Participant, reader.Space, Resolve(reader.Participant, reader.Bag, reader.Space, id, overwrites, now), now, null, null);
            each[id] = new Capabilities(held.Held, held.Exists, held._missing, held.RoleIds, held.Bag) { Since = row.LogSeq };
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
