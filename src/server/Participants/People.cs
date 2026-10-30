using System.Security.Cryptography;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core;
using Koan.Data.Core.Sorting;
using Koan.Identity.Roles;
using Koan.Core.Hosting.App;
using Microsoft.AspNetCore.DataProtection;
using Tangent.Access;
using Tangent.Events;
using Tangent.Space;
using Tangent.Writer;

namespace Tangent.Participants;

/// <summary>The place's people, and who may change whose standing and roles. <see cref="Read"/> is one bounded page of
/// the participants admitted to the place — standing admitted, timed out or banned; an applicant stays among the
/// <see cref="Applications"/> — people and agents alike, in handle order through <c>ix_participant_handles</c>,
/// continued from a sealed <see cref="Cursor"/>, and narrowed to the handles that start with what the reader typed, one
/// range of the same index. <see cref="Refusal"/> is the one rule every change of standing or role answers to: the
/// seeded roles stand one above another — administrator, moderator, member — and the operator above every role, and a
/// participant changes the standing or roles only of someone below them, never their own, never the operator's.
/// <see cref="Offered"/> asks the same rule for what a page offers, so a page offers only what the writer would
/// do.</summary>
public static class People
{
    /// <summary>The most participants one page holds.</summary>
    public const int PageSize = 25;

    /// <summary>The purpose a cursor is sealed under, which no other protected value shares.</summary>
    public const string CursorPurpose = "Participant.Cursor";

    /// <summary>The standing verbs, as a page offers them and a row's <c>can</c> lists them.</summary>
    public static class Verb
    {
        public const string TimeOut = "time-out";

        public const string Ban = "ban";

        public const string Lift = "lift";
    }

    /// <summary>The roles given and taken here, by the word a form sends.</summary>
    public const string Moderator = "moderator";

    public const string Administrator = "administrator";

    /// <summary>The answer to a change of someone who never arrived.</summary>
    public const string NoOne = "No one by that id has arrived here.";

    public const string Themselves = "No one changes their own standing or roles.";

    public const string Operator = "The operator answers for this place: no one times them out, bans them or changes their roles.";

    public const string NotBelow = "You change the standing and roles only of someone whose role is below yours.";

    public const string NotHeld = "No one gives or takes a role whose permissions they do not hold.";

    public const string NotARole = "The roles given and taken here are moderator and administrator.";

    public const string NotADuration = "A timeout lasts an hour, a day or a week: send hour, day or week.";

    /// <summary>How long a timeout lasts, by the word a form sends, shortest first.</summary>
    public static readonly IReadOnlyList<(string Word, TimeSpan Length)> Durations =
        [("hour", TimeSpan.FromHours(1)), ("day", TimeSpan.FromDays(1)), ("week", TimeSpan.FromDays(7))];

    /// <summary>The roles given and taken here, by word and key, lowest first. The member role is admission's.</summary>
    public static readonly IReadOnlyList<(string Word, string Key)> Given = [(Moderator, Roles.Moderator), (Administrator, Roles.Administrator)];

    /// <summary>The length a duration's word names; null for any other word.</summary>
    public static TimeSpan? Length(string? word)
    {
        foreach (var (named, length) in Durations)
        {
            if (named == word) return length;
        }

        return null;
    }

    /// <summary>The key of the role a word names among <see cref="Given"/>; null for any other word.</summary>
    public static string? KeyOf(string? word) => Given.FirstOrDefault(role => role.Word == word).Key;

    /// <summary>A standing as a page and JSON write it.</summary>
    public static string Word(Standing standing) => standing switch
    {
        Standing.TimedOut => "timed-out",
        _ => standing.ToString().ToLowerInvariant()
    };

    /// <summary>Null where the reader <paramref name="actorId"/>, who holds <paramref name="actor"/> in the place, may
    /// change <paramref name="target"/>'s standing (<see cref="Bit.ManageStanding"/>) or roles
    /// (<see cref="Bit.ManageRoles"/>), the target being in <paramref name="targetRoles"/>; otherwise why not, the first
    /// of: the verb's gate in the place; the target is the reader; an applicant, decided among the applications; the
    /// operator, whom no one changes; a target whose highest seeded role is not below the reader's, the operator standing
    /// above every role. The target's standing is read as it stands at <paramref name="now"/>.</summary>
    public static string? Refusal(Capabilities actor, string actorId, Participant target, IReadOnlyCollection<string> targetRoles,
        TangentSpace space, Bit verb, DateTimeOffset now)
    {
        if (actor.Gate(verb) is { } gate) return gate;
        if (target.Id == actorId) return Themselves;
        if (target.StandingAt(now) is Standing.Applied or Standing.Declined) return Participant.NotAdmitted;
        if (target.Id == space.OperatorId) return Operator;
        return Height(actorId, actor.RoleIds, space) > Height(target.Id, targetRoles, space) ? null : NotBelow;
    }

    /// <summary>Null where <paramref name="actor"/> holds every permission <paramref name="role"/> grants, in the place,
    /// so they may give or take it; <see cref="NotHeld"/> otherwise. A bit of authorship, which holds only on one's own
    /// Post, counts as held where nothing but authorship withholds it in the place.</summary>
    public static string? Holding(Capabilities actor, Role role)
    {
        var grants = Bits.InTokens(role.Permissions);
        return Bits.Each.Where(bit => grants.HasFlag(bit)).All(bit => actor.Holds(bit) || actor.Gate(bit) == Capabilities.NotYours) ? null : NotHeld;
    }

    /// <summary>What a reader may do to one participant, by <see cref="Refusal"/>: the standing verbs (<see cref="Verb"/>),
    /// in <c>can</c>, and the roles they may give and take, by word.</summary>
    public sealed record Offer(IReadOnlyList<string> Can, IReadOnlyList<string> Give, IReadOnlyList<string> Take)
    {
        public static readonly Offer None = new([], [], []);
    }

    /// <summary>What <paramref name="actorId"/>, holding <paramref name="actor"/> in the place, may do to
    /// <paramref name="target"/> at <paramref name="now"/>: time out and ban someone admitted, lift and ban someone timed
    /// out, lift someone banned; of the roles of <paramref name="given"/> whose permissions they hold, take each from
    /// someone who holds it, and give each to someone not banned whose highest seeded role is below it, so an
    /// administrator is not offered the moderator role. Nothing for a visitor.</summary>
    public static Offer Offered(Capabilities actor, string? actorId, Participant target, IReadOnlyCollection<string> targetRoles,
        TangentSpace space, IReadOnlyDictionary<string, Role> given, DateTimeOffset now)
    {
        if (actorId is null) return Offer.None;
        var can = new List<string>();
        if (Refusal(actor, actorId, target, targetRoles, space, Bit.ManageStanding, now) is null)
        {
            can.AddRange(target.StandingAt(now) switch
            {
                Standing.Admitted => [Verb.TimeOut, Verb.Ban],
                Standing.TimedOut => [Verb.Lift, Verb.Ban],
                Standing.Banned => [Verb.Lift],
                _ => []
            });
        }

        var give = new List<string>();
        var take = new List<string>();
        if (Refusal(actor, actorId, target, targetRoles, space, Bit.ManageRoles, now) is null)
        {
            var height = Height(target.Id, targetRoles, space);
            foreach (var (word, key) in Given)
            {
                if (!given.TryGetValue(key, out var role) || Holding(actor, role) is not null) continue;
                if (targetRoles.Contains(key)) take.Add(word);
                else if (height < RoleHeight(key) && target.StandingAt(now) != Standing.Banned) give.Add(word);
            }
        }

        return new Offer(can, give, take);
    }

    /// <summary>What a change of standing or role judges by, read inside the command: the place's row, what the actor
    /// holds in it, the participant changed and Koan's bag for them; and why the change is refused, null where it is
    /// not.</summary>
    public sealed record Judged(TangentSpace? Space, Capabilities? Actor, Participant? Target, RoleBag? Bag, string? Refusal);

    /// <summary>Judges a change of <paramref name="participantId"/>'s standing (<see cref="Bit.ManageStanding"/>) or roles
    /// (<see cref="Bit.ManageRoles"/>) by <paramref name="actorId"/>: the place exists for the actor, who holds
    /// <see cref="Bit.View"/> on it, or the change answers that no place is here; the actor holds the verb, or is refused
    /// with its gate; the participant has arrived, or the change answers <see cref="NoOne"/>; then
    /// <see cref="Refusal"/>, with their standing as it stands at <paramref name="now"/>.</summary>
    public static async Task<Judged> Judge(string actorId, string participantId, Bit verb, DateTimeOffset now, CancellationToken ct)
    {
        if (await TangentSpace.Get(TangentSpace.SingleRow, ct) is not { } space) return new Judged(null, null, null, null, Applications.NoPlace);
        var actor = await Capabilities.Load(actorId, null, null, ct);
        if (!actor.Exists) return new Judged(space, actor, null, null, Applications.NoPlace);
        if (actor.Gate(verb) is { } gate) return new Judged(space, actor, null, null, gate);
        if (string.IsNullOrWhiteSpace(participantId) || await Participant.Get(participantId, ct) is not { } target)
            return new Judged(space, actor, null, null, NoOne);
        var bag = await AppHost.GetRequiredService<RoleCollection>(nameof(People)).Bag(target.Id, authenticated: true, ct);
        return new Judged(space, actor, target, bag, Refusal(actor, actorId, target, bag.Roles, space, verb, now));
    }

    /// <summary>One change of standing, by <paramref name="actorId"/>, judged by <see cref="Judge"/>: where
    /// <paramref name="change"/> changes the participant's standing at <paramref name="at"/>, or now, it appends one
    /// <see cref="EventKind.StandingChanged"/> with the actor, the participant as its target, the standing it left them
    /// with and the one they held before, so their door tells them; a change that changes nothing writes nothing, and a
    /// <see cref="Writer.Refusal"/> the participant throws refuses it.</summary>
    public static async Task<Outcome<Participant>> ChangeStanding(Sequences sequences, string actorId, string participantId,
        Func<Participant, DateTimeOffset, bool> change, DateTimeOffset? at, CancellationToken ct)
    {
        var now = at ?? DateTimeOffset.UtcNow;
        var judged = await Judge(actorId, participantId, Bit.ManageStanding, now, ct);
        if (judged.Refusal is { } refusal) return new Outcome<Participant>.Refused(refusal);
        var target = judged.Target!;
        var before = target.StandingAt(now);
        if (!change(target, now)) return new Outcome<Participant>.Done(target);
        await target.Save(ct);
        await Event.Next(sequences, EventKind.StandingChanged, now, actorId: actorId, targetId: target.Id, standing: target.Standing,
            before: before).Save(ct);
        return new Outcome<Participant>.Done(target);
    }

    /// <summary>Where a participant stands among the roles, lowest first: no seeded role, member, moderator,
    /// administrator; the operator above every role.</summary>
    private static int Height(string id, IEnumerable<string> roleIds, TangentSpace space)
        => id == space.OperatorId ? RoleHeight(Roles.Administrator) + 1 : roleIds.Select(RoleHeight).DefaultIfEmpty(0).Max();

    /// <summary>Where a seeded role stands among them, lowest first; every other role stands with none.</summary>
    private static int RoleHeight(string key) => key switch
    {
        Roles.Administrator => 3,
        Roles.Moderator => 2,
        Roles.Member => 1,
        _ => 0
    };

    /// <summary>A page of participants and where the next one starts; <see cref="Next"/> is null on the last page.</summary>
    public sealed record Page(IReadOnlyList<Participant> Participants, Cursor? Next);

    /// <summary>The place of the last participant a page held: their handle, and their id, which orders participants
    /// who show no handle. It leaves the server only sealed.</summary>
    public sealed record Cursor(string Handle, string Id)
    {
        public static Cursor After(Participant participant) => new(participant.Handle, participant.Id);

        /// <summary>The cursor as an address carries it: protected under <see cref="CursorPurpose"/>, so it says nothing
        /// of the handle it holds and is never accepted altered.</summary>
        public string Seal(IDataProtectionProvider protection) => protection.CreateProtector(CursorPurpose).Protect(Handle + "\n" + Id);

        /// <summary>The cursor a sealed text holds, or null when the text is not one this server sealed.</summary>
        public static Cursor? Open(string? text, IDataProtectionProvider protection)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string plain;
            try { plain = protection.CreateProtector(CursorPurpose).Unprotect(text); }
            catch (CryptographicException) { return null; }
            var at = plain.IndexOf('\n');
            if (at < 0) return null;
            var id = plain[(at + 1)..];
            return id.StartsWith("did:", StringComparison.Ordinal) ? new Cursor(plain[..at], id) : null;
        }
    }

    /// <summary>The start of a handle as a search holds it: what was typed, trimmed, without a leading <c>@</c>, in lower
    /// case as atproto writes handles, at most a handle long; null where nothing was typed.</summary>
    public static string? Start(string? typed)
    {
        var text = typed?.Trim().TrimStart('@').ToLowerInvariant();
        return string.IsNullOrEmpty(text) ? null : text[..Math.Min(text.Length, SignIn.HandleLimit)];
    }

    /// <summary>The page of participants after <paramref name="after"/>, or the first page, whose handles start with
    /// <paramref name="start"/> where it is given: one read through the handle index, limited to the page and one more,
    /// which says whether a next page exists. A start holding a character no handle holds finds no one, and reads
    /// nothing.</summary>
    public static async Task<Page> Read(string? start, Cursor? after, CancellationToken ct)
    {
        if (start is not null && !start.All(HandleCharacter)) return new Page([], null);
        var handle = FieldPath.Of(nameof(Participant.Handle));
        var filters = new List<Filter>();
        if (start is not null)
        {
            // Every handle that starts with the text sorts at or after it and before the text with its last character
            // raised by one: one range of the index.
            filters.Add(Filter.On(handle, FilterOperator.Gte, FilterValue.Of(start)));
            filters.Add(Filter.On(handle, FilterOperator.Lt, FilterValue.Of(start[..^1] + (char)(start[^1] + 1))));
        }

        if (after is not null)
        {
            filters.Add(Filter.On(handle, FilterOperator.Gte, FilterValue.Of(after.Handle)));
            filters.Add(Filter.Any(
                Filter.On(handle, FilterOperator.Gt, FilterValue.Of(after.Handle)),
                Filter.On(FieldPath.Of(nameof(Participant.Id)), FilterOperator.Gt, FilterValue.Of(after.Id))));
        }

        var query = new QueryDefinition { Filter = filters.Count == 0 ? null : Filter.All([.. filters]), Page = 1, PageSize = PageSize + 1 }
            .WithSort<Participant>(sort => sort.OrderBy(participant => participant.Handle).ThenBy(participant => participant.Id));
        var rows = (await Participant.Query(participant => participant.Standing == Standing.Admitted
            || participant.Standing == Standing.TimedOut || participant.Standing == Standing.Banned, query, ct)).ToList();
        return rows.Count > PageSize
            ? new Page(rows.GetRange(0, PageSize), Cursor.After(rows[PageSize - 1]))
            : new Page(rows, null);
    }

    /// <summary>A character an atproto handle holds, in lower case.</summary>
    private static bool HandleCharacter(char character) => character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '-';
}
