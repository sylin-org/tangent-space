using Koan.Data.Abstractions;
using Koan.Data.Core;
using Tangent.Events;
using Tangent.Participants;
using Tangent.Writer;

namespace Tangent.Space;

/// <summary>First run: writes the one <see cref="TangentSpace"/> row with its name, the proprietor and the operator,
/// and appends <see cref="EventKind.SpaceChanged"/>. The operator is the signed-in person who sends it, and sending it
/// is their declaration that they are the person who answers for the place: their kind becomes person by their own
/// choice, which no profile label changes; one who chose agent themselves is refused. One whose first sign-in left no
/// participant arrives here, with <see cref="EventKind.StandingChanged"/>. The proprietor is the operator unless
/// <see cref="ProprietorHandle"/> names another participant: exactly one, and not banned; a person or an agent. The
/// moderators' Tangent is created with the row and recorded on it (<see cref="ModeratorsTangent"/>), its access written
/// before the transaction opens. A place
/// that already has its row refuses with <see cref="AlreadyEstablished"/> and nothing is written. Who may send it is
/// the sender's question; membership of the administrator role is Koan's write, made by the sender once this
/// commits.</summary>
public sealed record Establish(
    string OperatorId,
    string OperatorHandle,
    string Name,
    string? ProprietorHandle = null,
    DateTimeOffset? At = null) : ICommand<TangentSpace>
{
    public const string AlreadyEstablished = "This Tangent Space is already set up.";

    /// <summary>The refusal for a proprietor handle that names no participant.</summary>
    public static string NoParticipant(string handle) => $"No participant here has the handle @{handle}.";

    /// <summary>The refusal for a proprietor handle two or more participants hold.</summary>
    public static string SharedHandle(string handle) => $"The handle @{handle} belongs to more than one participant here.";

    /// <summary>The refusal for a proprietor who is banned.</summary>
    public static string Banned(string handle) => $"@{handle} is banned here, so they cannot be its proprietor.";

    /// <summary>Writes the moderators' Tangent's access, outside the transaction, only where <see cref="Run"/> goes on to
    /// establish the place: it has no row yet and the name is a place's. A command refused after this leaves the access on
    /// a resource no Tangent has, which answers as a missing Tangent, and the next first run writes it anew.</summary>
    public async Task Prepare(CancellationToken ct)
    {
        if (await TangentSpace.Get(TangentSpace.SingleRow, ct) is not null) return;
        try { TangentSpace.Establish(Name, OperatorId, OperatorId, ModeratorsTangent.Slug); }
        catch (Refusal) { return; }
        if (await ModeratorsTangent.FreeSlug(ct) is { } slug) await ModeratorsTangent.WriteAccess(slug, ct);
    }

    public async Task<Outcome<TangentSpace>> Run(Sequences sequences, CancellationToken ct)
    {
        if (await TangentSpace.Get(TangentSpace.SingleRow, ct) is not null)
            return new Outcome<TangentSpace>.Refused(AlreadyEstablished);

        var at = At ?? DateTimeOffset.UtcNow;
        var operatorRow = await Participant.Get(OperatorId, ct);
        var proprietorId = OperatorId;
        if (ProprietorHandle is { Length: > 0 } handle && handle != OperatorHandle)
        {
            var named = await Participant.Query(participant => participant.Handle == handle,
                new QueryDefinition { Page = 1, PageSize = 2 }, ct);
            if (named.Count == 0) return new Outcome<TangentSpace>.Refused(NoParticipant(handle));
            if (named.Count > 1) return new Outcome<TangentSpace>.Refused(SharedHandle(handle));
            if (named[0].Standing == Standing.Banned) return new Outcome<TangentSpace>.Refused(Banned(handle));
            proprietorId = named[0].Id;
        }

        var moderators = await ModeratorsTangent.FreeSlug(ct) ?? throw new Refusal(ModeratorsTangent.NoSlug);
        var space = TangentSpace.Establish(Name, proprietorId, OperatorId, moderators);
        var arriving = operatorRow is null;
        operatorRow ??= Participant.Arrive(OperatorId, OperatorHandle, space.Admission);
        operatorRow.DeclareAccountable();
        await operatorRow.Save(ct);
        if (arriving) await Event.Next(sequences, EventKind.StandingChanged, at, actorId: OperatorId, targetId: OperatorId).Save(ct);

        await ModeratorsTangent.Create(sequences, moderators, OperatorId, at, ct);
        await space.Save(ct);
        await Event.Next(sequences, EventKind.SpaceChanged, at, actorId: OperatorId).Save(ct);
        return new Outcome<TangentSpace>.Done(space);
    }
}
