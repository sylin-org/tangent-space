using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Identity.Roles;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Space;
using Tangent.Tangents;

namespace Tangent.Access;

/// <summary>What one participant holds on one resource, and the one reason for each verb they do not. Discord's model
/// on Koan's roles, resolved in Discord's order by <see cref="For"/>: standing first, then the Post's state, then the
/// base from the bag and the Tangent's overwrites, with <see cref="Bit.View"/> the prerequisite of every other bit,
/// then authorship. The first step that drops a verb names its gate, which puts the five words in their precedence:
/// <see cref="SignIn"/>, <see cref="Apply"/>, <see cref="TimedOut"/>, <see cref="Locked"/>, <see cref="Removed"/>.
/// Computed per request from a few small rows; nothing stores it.</summary>
public sealed class Capabilities
{
    public const string SignIn = "sign-in";
    public const string Apply = "apply";
    public const string TimedOut = "timed-out";
    public const string Locked = "locked";
    public const string Removed = "removed";

    /// <summary>A banned participant's reason. The resource reads as absent, so no door shows this word.</summary>
    public const string Banned = "banned";

    /// <summary>The reason <see cref="Bit.EditOwn"/> and <see cref="Bit.RemoveOwn"/> are missing on a Post that is
    /// not the participant's own, or where no Post is in question.</summary>
    public const string NotYours = "not yours";

    private readonly Dictionary<Bit, string> _missing;

    private Capabilities(Bit held, bool absent, Dictionary<Bit, string> missing)
    {
        Held = held;
        Absent = absent;
        _missing = missing;
    }

    /// <summary>The bits the participant holds on this resource.</summary>
    public Bit Held { get; }

    /// <summary>Whether the resource reads as absent to this participant: a banned one sees nothing.</summary>
    public bool Absent { get; }

    /// <summary>The verbs the participant holds here, by name: the value every resource carries.</summary>
    public IReadOnlyList<string> Can => Bits.Names(Held);

    public bool Holds(Bit verb) => (Held & verb) == verb;

    /// <summary>Null when the verb is held; otherwise the one reason it is not: <see cref="SignIn"/>,
    /// <see cref="Apply"/>, <see cref="TimedOut"/>, <see cref="Locked"/> or <see cref="Removed"/> where standing or
    /// state decided, or the deciding overwrite or the absence of a role where the bag decided.</summary>
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
    /// permissions beside it. <paramref name="overwrites"/> are the Tangent's; the Tangent Space itself has none.
    /// <paramref name="post"/> is the Topic's root for a question about the Topic, the Post itself for a question
    /// about that Post, and null for a Tangent or the place.</summary>
    public static Capabilities For(Participant? participant, IEnumerable<string> tokens, TangentSpace space,
        IReadOnlyList<Overwrite> overwrites, Post? post = null)
    {
        var missing = new Dictionary<Bit, string>();
        var held = Bits.All;

        // Standing precedes everything: a cap on what any later step may leave.
        var viewIfPublic = space.Audience == Audience.Public ? Bit.View : Bit.None;
        var absent = false;
        switch (participant?.Standing)
        {
            case null:
                Drop(Bits.All & ~viewIfPublic, SignIn);
                break;
            case Standing.Banned:
                Drop(Bits.All, Banned);
                absent = true;
                break;
            case Standing.Applied:
                Drop(Bits.All & ~viewIfPublic, Apply);
                break;
            case Standing.TimedOut:
                Drop(Bits.All & ~Bit.View, TimedOut);
                break;
        }

        if (post is { Locked: true }) Drop(Bit.Post, Locked);
        if (post is { Removed: true }) Drop(Bits.All, Removed);

        var (granted, denied) = Resolve(tokens, overwrites, participant?.Id);
        foreach (var bit in Bits.Each)
        {
            if (!granted.HasFlag(bit)) Drop(bit, denied.GetValueOrDefault(bit) ?? NoRoleGrants(bit));
        }

        // Without View nothing else applies here: the reason View is missing is the reason for every verb.
        if (!held.HasFlag(Bit.View)) Drop(Bits.All, missing[Bit.View]);

        if (post is null || participant is null || post.AuthorId != participant.Id) Drop(Bit.EditOwn | Bit.RemoveOwn, NotYours);

        return new Capabilities(held, absent, missing);

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

    /// <summary>The base and the overwrites in Discord's order: the union of the bag's bits; then the
    /// <see cref="Overwrite.Everyone"/> overwrite's deny and allow; then the denies and allows of the overwrites for
    /// the roles in the bag; then the participant's own. <see cref="Bit.Administrator"/> in the base is every bit and
    /// no overwrite touches it. For each bit absent at the end, the deny that decided it, where one did.</summary>
    private static (Bit Granted, Dictionary<Bit, string> Denied) Resolve(IEnumerable<string> tokens,
        IReadOnlyList<Overwrite> overwrites, string? participantId)
    {
        var bag = tokens as IReadOnlyCollection<string> ?? tokens.ToList();
        var granted = Bits.InTokens(bag);
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
    /// for the subjects the bag names. A participant without a row is a visitor.</summary>
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

        return For(participant, tokens, space, overwrites, post);
    }
}
