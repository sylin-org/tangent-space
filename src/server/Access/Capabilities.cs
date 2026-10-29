using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Identity.Roles;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Space;
using Tangent.Tangents;

namespace Tangent.Access;

/// <summary>What one participant holds on one resource, and the one reason for each verb they do not. Discord's model
/// on Koan's roles, resolved by <see cref="For"/> in this order: standing; <see cref="Bit.View"/> on the Tangent, from
/// the place's audience, the base and the overwrites, without which the resource is absent to the reader and nothing
/// else is said; the Post's state; the base and the overwrites for every other bit; authorship. The first step that
/// drops a verb names its gate, which puts the words in their precedence: <see cref="SignIn"/>, <see cref="Apply"/>,
/// <see cref="TimedOut"/>, <see cref="Absent"/>, <see cref="Locked"/>, <see cref="Removed"/>. Computed per request
/// from a few small rows; nothing stores it.</summary>
public sealed class Capabilities
{
    public const string SignIn = "sign-in";
    public const string Apply = "apply";
    public const string TimedOut = "timed-out";

    /// <summary>The resource does not exist for this reader: banned, or without <see cref="Bit.View"/> where they
    /// stand. Every verb carries this one reason, and a command answers what it answers for a missing resource.</summary>
    public const string Absent = "absent";
    public const string Locked = "locked";
    public const string Removed = "removed";

    /// <summary>The reason <see cref="Bit.EditOwn"/> and <see cref="Bit.RemoveOwn"/> are missing on a Post that is
    /// not the participant's own, or where no Post is in question.</summary>
    public const string NotYours = "not yours";

    private readonly Dictionary<Bit, string> _missing;

    private Capabilities(Bit held, bool exists, Dictionary<Bit, string> missing)
    {
        Held = held;
        Exists = exists;
        _missing = missing;
    }

    /// <summary>The bits the participant holds on this resource.</summary>
    public Bit Held { get; }

    /// <summary>Whether the resource exists for this reader: false where they lack <see cref="Bit.View"/> on the
    /// Tangent or the place, when a command answers as for a missing resource. A removed Post exists, as a tombstone.</summary>
    public bool Exists { get; }

    /// <summary>The verbs the participant holds here, by name: the value every resource carries.</summary>
    public IReadOnlyList<string> Can => Bits.Names(Held);

    public bool Holds(Bit verb) => (Held & verb) == verb;

    /// <summary>Null when the verb is held; otherwise the one reason it is not: <see cref="SignIn"/>,
    /// <see cref="Apply"/>, <see cref="TimedOut"/>, <see cref="Absent"/>, <see cref="Locked"/> or <see cref="Removed"/>
    /// where standing, audience or state decided, or the deciding overwrite or the absence of a role where the bag
    /// decided.</summary>
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
    /// <paramref name="tokens"/> is Koan's bag for the participant with the <see cref="Roles.Everyone"/> role's own
    /// permissions beside it. <paramref name="space"/> states the place's audience, which is where everyone's
    /// <see cref="Bit.View"/> on a public place comes from. <paramref name="overwrites"/> are the Tangent's; the
    /// Tangent Space itself has none. <paramref name="now"/> decides whether a timeout has ended. <paramref name="post"/>
    /// is the Topic's root for a question about the Topic, the Post itself for a question about that Post, and null
    /// for a Tangent or the place.</summary>
    public static Capabilities For(Participant? participant, IEnumerable<string> tokens, TangentSpace space,
        IReadOnlyList<Overwrite> overwrites, DateTimeOffset now, Post? post = null)
    {
        var missing = new Dictionary<Bit, string>();
        var held = Bits.All;
        var viewIfPublic = space.Audience == Audience.Public ? Bit.View : Bit.None;

        // Standing precedes everything: a cap on what any later step may leave. A timeout that has ended is over.
        var standing = participant?.Standing;
        if (standing == Standing.TimedOut && participant!.StandingUntil is { } until && until <= now) standing = Standing.Admitted;
        switch (standing)
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
            case Standing.TimedOut:
                Drop(Bits.All & ~Bit.View, TimedOut);
                break;
        }

        var (granted, denied) = Resolve(tokens, viewIfPublic, overwrites, participant?.Id);

        // View on the Tangent: without it the resource is absent to the reader, and every verb says only that.
        if (!granted.HasFlag(Bit.View)) Drop(Bit.View, Absent);
        if (!held.HasFlag(Bit.View))
        {
            var reason = missing[Bit.View];
            foreach (var bit in Bits.Each) missing[bit] = reason;
            return new Capabilities(Bit.None, false, missing);
        }

        if (post is { Locked: true }) Drop(Bit.Post, Locked);
        if (post is { Removed: true }) Drop(Bits.All, Removed);

        foreach (var bit in Bits.Each)
        {
            if (!granted.HasFlag(bit)) Drop(bit, denied.GetValueOrDefault(bit) ?? NoRoleGrants(bit));
        }

        if (post is null || participant is null || post.AuthorId != participant.Id) Drop(Bit.EditOwn | Bit.RemoveOwn, NotYours);

        return new Capabilities(held, true, missing);

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

    /// <summary>The base and the overwrites in Discord's order: the union of the bag's bits and everyone's
    /// <see cref="Bit.View"/> on a public place; then the <see cref="Overwrite.Everyone"/> overwrite's deny and allow;
    /// then the denies and allows of the overwrites for the roles in the bag; then the participant's own.
    /// <see cref="Bit.Administrator"/> in the base is every bit and no overwrite touches it. For each bit absent at the
    /// end, the deny that decided it, where one did.</summary>
    private static (Bit Granted, Dictionary<Bit, string> Denied) Resolve(IEnumerable<string> tokens, Bit everyoneHolds,
        IReadOnlyList<Overwrite> overwrites, string? participantId)
    {
        var bag = tokens as IReadOnlyCollection<string> ?? tokens.ToList();
        var granted = Bits.InTokens(bag) | everyoneHolds;
        var denied = new Dictionary<Bit, string>();
        if (granted.HasFlag(Bit.Administrator)) return (Bits.All, denied);

        var roleIds = bag.Where(Bits.IsRoleId).ToHashSet(StringComparer.Ordinal);
        Step(overwrites.Where(overwrite => overwrite.SubjectId == Overwrite.Everyone));
        Step(overwrites.Where(overwrite => roleIds.Contains(overwrite.SubjectId)));
        if (participantId is not null) Step(overwrites.Where(overwrite => overwrite.SubjectId == participantId));
        return (granted, denied);

        void Step(IEnumerable<Overwrite> step)
        {
            var rows = step.ToList();
            foreach (var row in rows)
            {
                var deny = Bits.Named(row.Deny) & ~Bit.Administrator;
                granted &= ~deny;
                foreach (var bit in Bits.Each)
                {
                    if (deny.HasFlag(bit)) denied[bit] = Denied(row.SubjectId, bit);
                }
            }

            foreach (var row in rows)
            {
                var allow = Bits.Named(row.Allow) & ~Bit.Administrator;
                granted |= allow;
                foreach (var bit in Bits.Each)
                {
                    if (allow.HasFlag(bit)) denied.Remove(bit);
                }
            }
        }
    }

    /// <summary>The reads a command makes before it asks <see cref="For"/>, each bounded: the participant's row, Koan's
    /// cached bag for them, the <see cref="Roles.Everyone"/> role, the settings row, and the overwrites of the Tangent
    /// for the subjects the bag names. A participant without a row is a visitor. A command asks this first, so that a
    /// reader without <see cref="Bit.View"/> is answered as for a missing resource before anything else is said.</summary>
    public static async Task<Capabilities> Load(string? participantId, string? tangentId, Post? post, CancellationToken ct)
    {
        var participant = string.IsNullOrWhiteSpace(participantId) ? null : await Participant.Get(participantId, ct);
        var roles = AppHost.GetRequiredService<RoleCollection>(nameof(Capabilities));
        var bag = await roles.Bag(participant?.Id, authenticated: participant is not null, ct);
        var everyone = await roles.Get(Roles.Everyone, ct);
        var tokens = everyone is null ? bag.Tokens : [.. bag.Tokens, .. everyone.Permissions];
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? new TangentSpace();

        IReadOnlyList<Overwrite> overwrites = [];
        if (tangentId is not null)
        {
            var subjects = bag.Tokens.Where(Bits.IsRoleId).Append(Overwrite.Everyone).ToList();
            if (participant is not null) subjects.Add(participant.Id);
            overwrites = await Overwrite.Query(
                overwrite => overwrite.TangentId == tangentId && subjects.Contains(overwrite.SubjectId),
                new QueryDefinition { Page = 1, PageSize = subjects.Count }, ct);
        }

        return For(participant, tokens, space, overwrites, DateTimeOffset.UtcNow, post);
    }
}
