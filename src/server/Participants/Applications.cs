using System.Globalization;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core;
using Koan.Data.Core.Sorting;
using Tangent.Access;
using Tangent.Events;
using Tangent.Space;
using Tangent.Writer;

namespace Tangent.Participants;

/// <summary>The applications of a place whose admission says apply: the participants who applied, waiting or declined.
/// <see cref="Decide"/> is what <see cref="Admit"/> and <see cref="Decline"/> share; <see cref="Read"/> is one bounded
/// page of them, newest application first, through <c>ix_participant_applications</c>
/// (<see cref="Participant.AppliedAt"/>), continued from a <see cref="Cursor"/>. Nothing is counted.</summary>
public static class Applications
{
    /// <summary>The most applications one page holds.</summary>
    public const int PageSize = 25;

    /// <summary>The answer to a decider for whom the place does not exist.</summary>
    public const string NoPlace = "No Tangent Space is here.";

    /// <summary>The answer to a decision about someone who never arrived.</summary>
    public const string NoApplicant = "No one by that id has arrived here.";

    /// <summary>One decision: the place exists for the actor, who holds <see cref="Bit.View"/> on it, or the command
    /// answers that no place is here; the actor holds <see cref="Bit.ManageStanding"/>, or is refused with the gate word;
    /// the participant has arrived, or the command answers that no one did; <paramref name="decide"/> changes their
    /// standing, or refuses with <see cref="Participant.NotAnApplication"/>. A decision that changes something appends
    /// one <see cref="EventKind.StandingChanged"/> with the actor, the applicant as its target, the standing it left
    /// them with and the one they held before; one that changes nothing writes nothing.</summary>
    public static async Task<Outcome<Participant>> Decide(Sequences sequences, string actorId, string participantId,
        Func<Participant, DateTimeOffset, bool> decide, DateTimeOffset? at, CancellationToken ct)
    {
        if (await TangentSpace.Get(TangentSpace.SingleRow, ct) is null) return new Outcome<Participant>.Refused(NoPlace);
        var capabilities = await Capabilities.Load(actorId, null, null, ct);
        if (!capabilities.Exists) return new Outcome<Participant>.Refused(NoPlace);
        if (capabilities.Gate(Bit.ManageStanding) is { } gate) return new Outcome<Participant>.Refused(gate);
        if (string.IsNullOrWhiteSpace(participantId) || await Participant.Get(participantId, ct) is not { } participant)
            return new Outcome<Participant>.Refused(NoApplicant);

        var now = at ?? DateTimeOffset.UtcNow;
        var before = participant.StandingAt(now);
        if (!decide(participant, now)) return new Outcome<Participant>.Done(participant);
        await participant.Save(ct);
        await Event.Next(sequences, EventKind.StandingChanged, now, actorId: actorId, targetId: participant.Id,
            standing: participant.Standing, before: before).Save(ct);
        return new Outcome<Participant>.Done(participant);
    }

    /// <summary>A page of applications and where the next one starts; <see cref="Next"/> is null on the last page.</summary>
    public sealed record Page(IReadOnlyList<Participant> Applicants, Cursor? Next);

    /// <summary>The place of the last application a page held: when it was made, and the applicant's id, which orders
    /// applications made at the same instant.</summary>
    public sealed record Cursor(DateTimeOffset At, string Id)
    {
        /// <summary>The longest DID a cursor carries, the bound atproto sets.</summary>
        private const int DidLimit = 2048;

        public static Cursor After(Participant applicant) => new(applicant.AppliedAt ?? DateTimeOffset.MinValue, applicant.Id);

        /// <summary>The cursor a query carries, or null when the text is not one.</summary>
        public static Cursor? Parse(string? text)
        {
            var parts = text?.Split('.', 2) ?? [];
            if (parts.Length != 2 || !parts[1].StartsWith("did:", StringComparison.Ordinal) || parts[1].Length > DidLimit) return null;
            if (!long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                || ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks) return null;
            return new Cursor(new DateTimeOffset(ticks, TimeSpan.Zero), parts[1]);
        }

        public override string ToString() => At.UtcTicks.ToString(CultureInfo.InvariantCulture) + "." + Id;
    }

    /// <summary>The page of applications after <paramref name="after"/>, or the first page: one read, limited to the page
    /// and one more, which says whether a next page exists.</summary>
    public static async Task<Page> Read(Cursor? after, CancellationToken ct)
    {
        var query = new QueryDefinition { Filter = after is null ? null : Seek(after), Page = 1, PageSize = PageSize + 1 }
            .WithSort<Participant>(sort => sort.OrderByDescending(participant => participant.AppliedAt).ThenByDescending(participant => participant.Id));
        var applicants = (await Participant.Query(participant => participant.AppliedAt != null
            && (participant.Standing == Standing.Applied || participant.Standing == Standing.Declined), query, ct)).ToList();
        return applicants.Count > PageSize
            ? new Page(applicants.GetRange(0, PageSize), Cursor.After(applicants[PageSize - 1]))
            : new Page(applicants, null);
    }

    /// <summary>The applications after the cursor, newest first: made no later than the cursor's, which the index answers
    /// as a range, and, at the cursor's own instant, an id before the cursor's.</summary>
    private static Filter Seek(Cursor after)
    {
        var at = FieldPath.Of(nameof(Participant.AppliedAt));
        var id = FieldPath.Of(nameof(Participant.Id));
        return Filter.All(
            Filter.On(at, FilterOperator.Lte, FilterValue.Of(after.At)),
            Filter.Any(
                Filter.On(at, FilterOperator.Lt, FilterValue.Of(after.At)),
                Filter.On(id, FilterOperator.Lt, FilterValue.Of(after.Id))));
    }
}
