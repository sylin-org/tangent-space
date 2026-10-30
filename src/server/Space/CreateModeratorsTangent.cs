using Koan.Data.Core;
using Tangent.Events;
using Tangent.Writer;

namespace Tangent.Space;

/// <summary>Creates the moderators' Tangent of a place established without one (<see cref="ModeratorsTangent"/>) and
/// records it on the settings row, with <see cref="EventKind.TangentChanged"/> and <see cref="EventKind.SpaceChanged"/>,
/// both the operator's, who answers for the place. A start sends it where the row records none (<see cref="FirstRunStart"/>).
/// Idempotent: a place that records one answers with it and nothing is written; one with no row is refused with
/// <see cref="NoPlace"/>.</summary>
public sealed record CreateModeratorsTangent(DateTimeOffset? At = null) : ICommand<Tangents.Tangent>
{
    public const string NoPlace = "This Tangent Space is not set up yet.";

    /// <summary>The answer for a row that records a moderators' Tangent no row holds.</summary>
    public const string Missing = "The moderators' Tangent this Tangent Space records is missing.";

    /// <summary>Writes the access outside the transaction, only where <see cref="Run"/> goes on to create the
    /// Tangent: the place is established and records none.</summary>
    public async Task Prepare(CancellationToken ct)
    {
        if (await TangentSpace.Get(TangentSpace.SingleRow, ct) is not { ModeratorsTangentId: null }) return;
        if (await ModeratorsTangent.FreeSlug(ct) is { } slug) await ModeratorsTangent.WriteAccess(slug, ct);
    }

    public async Task<Outcome<Tangents.Tangent>> Run(Sequences sequences, CancellationToken ct)
    {
        if (await TangentSpace.Get(TangentSpace.SingleRow, ct) is not { } space) return new Outcome<Tangents.Tangent>.Refused(NoPlace);
        if (space.ModeratorsTangentId is { } recorded)
            return await Tangents.Tangent.Get(recorded, ct) is { } kept ? new Outcome<Tangents.Tangent>.Done(kept) : new Outcome<Tangents.Tangent>.Refused(Missing);

        var slug = await ModeratorsTangent.FreeSlug(ct) ?? throw new Refusal(ModeratorsTangent.NoSlug);
        var at = At ?? DateTimeOffset.UtcNow;
        var tangent = await ModeratorsTangent.Create(sequences, slug, space.OperatorId, at, ct);
        space.RecordModerators(tangent.Id);
        await space.Save(ct);
        await Event.Next(sequences, EventKind.SpaceChanged, at, actorId: space.OperatorId).Save(ct);
        return new Outcome<Tangents.Tangent>.Done(tangent);
    }
}
