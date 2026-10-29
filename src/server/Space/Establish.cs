using Koan.Data.Abstractions;
using Koan.Data.Core;
using Tangent.Events;
using Tangent.Participants;
using Tangent.Writer;

namespace Tangent.Space;

/// <summary>First run: writes the one <see cref="TangentSpace"/> row with its name, the base URL the operator
/// configured, the proprietor and the operator, and appends <see cref="EventKind.SpaceChanged"/>. The operator is the
/// signed-in person who sends it, always a person; one whose first sign-in left no participant arrives here, with
/// <see cref="EventKind.StandingChanged"/>. The proprietor is the operator unless <see cref="ProprietorHandle"/> names
/// another participant. A place that already has its row refuses with <see cref="AlreadyEstablished"/> and nothing is
/// written. Membership of the administrator role is Koan's write, made by the sender once this commits.</summary>
public sealed record Establish(
    string OperatorId,
    string OperatorHandle,
    string Name,
    string BaseUrl,
    string? ProprietorHandle = null,
    DateTimeOffset? At = null) : ICommand<TangentSpace>
{
    public const string AlreadyEstablished = "This Tangent Space is already set up.";

    /// <summary>The refusal for a proprietor handle that names no participant.</summary>
    public static string NoParticipant(string handle) => $"No participant here has the handle @{handle}.";

    public async Task<Outcome<TangentSpace>> Run(Sequences sequences, CancellationToken ct)
    {
        if (await TangentSpace.Get(TangentSpace.SingleRow, ct) is not null)
            return new Outcome<TangentSpace>.Refused(AlreadyEstablished);

        var at = At ?? DateTimeOffset.UtcNow;
        var operatorRow = await Participant.Get(OperatorId, ct);
        if (operatorRow is { Kind: not ParticipantKind.Person })
            return new Outcome<TangentSpace>.Refused("The operator answers for the place and is a person; this account is an agent here.");

        var proprietorId = OperatorId;
        if (ProprietorHandle is { Length: > 0 } handle && handle != OperatorHandle)
        {
            var named = await Participant.Query(participant => participant.Handle == handle,
                new QueryDefinition { Page = 1, PageSize = 1 }, ct);
            if (named.Count == 0) return new Outcome<TangentSpace>.Refused(NoParticipant(handle));
            proprietorId = named[0].Id;
        }

        var space = TangentSpace.Establish(Name, BaseUrl, proprietorId, OperatorId);
        if (operatorRow is null)
        {
            operatorRow = Participant.Arrive(OperatorId, OperatorHandle, space.Admission);
            await operatorRow.Save(ct);
            await Event.Next(sequences, EventKind.StandingChanged, at, actorId: OperatorId, targetId: OperatorId).Save(ct);
        }

        await space.Save(ct);
        await Event.Next(sequences, EventKind.SpaceChanged, at, actorId: OperatorId).Save(ct);
        return new Outcome<TangentSpace>.Done(space);
    }
}
