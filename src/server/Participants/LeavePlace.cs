using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Tangent.Access;
using Tangent.Connections;
using Tangent.Events;
using Tangent.Space;
using Tangent.Tangents;

namespace Tangent.Participants;

/// <summary>A participant's own end in the place, in one hold: their checkpoints, their follows, their Connection, their asks
/// and their row, which carries their cached profile, go, and what it removed comes back. The sender ends the place's grant to write
/// as the account they hold before it, and has Koan erase their person after it, which ends their sessions, links, roles
/// and overwrites. Their Posts stay, each under the account it was written as, and so do the log's rows, their
/// Connection's receipts and the media uses they brought, which name an id no one holds again. Nothing is appended: nothing would read it. Whoever is not a
/// participant has nothing left to go, so a repeat is done with nothing removed; the operator is refused
/// (<see cref="Refusal"/>).</summary>
public sealed record LeavePlace(string PersonId) : ICommand<LeavePlace.Gone>
{
    /// <summary>What leaving removed: how many of their checkpoints, follows and asks went, none where they had already gone,
    /// and whether their Connection did.</summary>
    public sealed record Gone(int Checkpoints, int Follows, int Asks, bool Connection);

    /// <summary>The refusal the operator meets, who answers for the place.</summary>
    public static readonly Reason Operator = Reason.Conflict("operator", "You answer for this place, so you stay with it.");

    /// <summary>The most rows one read of leaving takes.</summary>
    public const int Page = 200;

    /// <summary>Why <paramref name="participant"/> may not leave <paramref name="space"/> at <paramref name="now"/>, or null
    /// where they may: the operator stays with the place, and a banned participant finds it absent, as a ban answers
    /// everywhere. Anyone else leaves, an applicant waiting or declined among them, which ends their application.</summary>
    public static Reason? Refusal(Participant participant, TangentSpace? space, DateTimeOffset now)
    {
        if (space?.OperatorId == participant.Id) return Operator;
        return participant.StandingAt(now) == Standing.Banned ? Capabilities.Absent : null;
    }

    public async Task<Outcome<Gone>> Run(CancellationToken ct)
    {
        if (await Participant.Get(PersonId, ct) is not { } participant) return new Outcome<Gone>.Done(new Gone(0, 0, 0, false));
        if (Refusal(participant, await TangentSpace.Get(TangentSpace.SingleRow, ct), DateTimeOffset.UtcNow) is { } refused)
            return new Outcome<Gone>.Refused(refused);

        var checkpoints = 0;
        for (var read = Page; read == Page;)
        {
            var page = await Checkpoint.Query(checkpoint => checkpoint.ParticipantId == PersonId, new QueryDefinition { Page = 1, PageSize = Page }, ct);
            read = page.Count;
            if (read > 0) checkpoints += await Checkpoint.Remove(page.Select(checkpoint => checkpoint.Id), ct);
        }

        var follows = 0;
        for (var read = Page; read == Page;)
        {
            var page = await Follow.Query(follow => follow.ParticipantId == PersonId, new QueryDefinition { Page = 1, PageSize = Page }, ct);
            read = page.Count;
            if (read > 0) follows += await Follow.Remove(page.Select(follow => follow.Id), ct);
        }

        var asks = 0;
        for (var read = Page; read == Page;)
        {
            var page = await Ask.Query(ask => ask.PersonId == PersonId, new QueryDefinition { Page = 1, PageSize = Page }, ct);
            read = page.Count;
            if (read > 0) asks += await Ask.Remove(page.Select(ask => ask.Id), ct);
        }

        var connection = await Connection.Remove(PersonId, ct);
        await participant.Remove(ct);
        return new Outcome<Gone>.Done(new Gone(checkpoints, follows, asks, connection));
    }
}
