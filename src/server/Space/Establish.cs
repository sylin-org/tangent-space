using Koan.Data.Core;
using Tangent.Events;
using Tangent.Participants;
using Tangent.Writer;

namespace Tangent.Space;

/// <summary>First run: writes the one <see cref="TangentSpace"/> row with its name, and whoever sends it as both its
/// proprietor and its operator, and appends <see cref="EventKind.SpaceChanged"/>. One whose first sign-in left
/// no participant arrives here, with <see cref="EventKind.StandingChanged"/>. The moderators' Tangent is created with
/// the row and recorded on it (<see cref="ModeratorsTangent"/>), and so is the first Tangent, public, where the place is
/// talked in from its first minute (<see cref="FirstTangent"/>), each in this transaction with its access written before
/// the transaction opens. A place that already has its row refuses with <see cref="AlreadyEstablished"/> and nothing is
/// written. Who may send it is the sender's question; membership of the administrator role is Koan's write, made by the
/// sender once this commits.</summary>
public sealed record Establish(
    string OperatorId,
    string OperatorHandle,
    string Name,
    DateTimeOffset? At = null) : ICommand<TangentSpace>
{
    public const string AlreadyEstablished = "This Tangent Space is already set up.";

    /// <summary>Writes the moderators' Tangent's access and the first Tangent's audience, outside the transaction, only
    /// where <see cref="Run"/> goes on to establish the place: it has no row yet and the name is a place's. A command
    /// refused after this leaves the access on resources no Tangent has, which answer as missing Tangents, and the next
    /// first run writes it anew.</summary>
    public async Task Prepare(CancellationToken ct)
    {
        if (await TangentSpace.Get(TangentSpace.SingleRow, ct) is not null) return;
        try { TangentSpace.Establish(Name, OperatorId, OperatorId, ModeratorsTangent.Slug); }
        catch (Refusal) { return; }
        if (await ModeratorsTangent.FreeSlug(ct) is { } slug) await ModeratorsTangent.WriteAccess(slug, ct);
        if (await FirstTangent.FreeSlug(ct) is { } first) await FirstTangent.WriteAudience(first, ct);
    }

    public async Task<Outcome<TangentSpace>> Run(Sequences sequences, CancellationToken ct)
    {
        if (await TangentSpace.Get(TangentSpace.SingleRow, ct) is not null)
            return new Outcome<TangentSpace>.Refused(AlreadyEstablished);

        var at = At ?? DateTimeOffset.UtcNow;
        var moderators = await ModeratorsTangent.FreeSlug(ct) ?? throw new Refusal(ModeratorsTangent.NoSlug);
        var first = await FirstTangent.FreeSlug(ct);
        var space = TangentSpace.Establish(Name, OperatorId, OperatorId, moderators);
        if (await Participant.Get(OperatorId, ct) is null)
        {
            await Participant.Arrive(OperatorId, OperatorHandle, space.Admission).Save(ct);
            await Event.Next(sequences, EventKind.StandingChanged, at, actorId: OperatorId, targetId: OperatorId).Save(ct);
        }

        if (first is not null) await FirstTangent.Create(sequences, first, OperatorId, at, ct);
        await ModeratorsTangent.Create(sequences, moderators, OperatorId, at, ct);
        await space.Save(ct);
        await Event.Next(sequences, EventKind.SpaceChanged, at, actorId: OperatorId).Save(ct);
        return new Outcome<TangentSpace>.Done(space);
    }
}
