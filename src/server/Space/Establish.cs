using Koan.Core.Hosting.App;
using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Koan.Identity.Roles;
using Tangent.Access;
using Tangent.Events;
using Tangent.Participants;

namespace Tangent.Space;

/// <summary>First run: writes the one <see cref="TangentSpace"/> row with its name, and whoever sends it as both its
/// proprietor and its operator, in the administrator role, and appends <see cref="EventKind.SpaceChanged"/>. One whose
/// first sign-in left no participant arrives here, with <see cref="EventKind.StandingChanged"/>, taking their handle from
/// any other participant who still keeps it, and begins reading the door at that arrival. The moderators' Tangent is created with the row and recorded on it
/// (<see cref="ModeratorsTangent"/>), and so is the first Tangent, public, where the place is talked in from its first
/// minute (<see cref="FirstTangent"/>). A place that already has its row refuses with <see cref="AlreadyEstablished"/> and
/// nothing is written. Who may send it is the sender's question.</summary>
public sealed record Establish(
    string OperatorId,
    string OperatorHandle,
    string Name,
    DateTimeOffset? At = null) : ICommand<TangentSpace>
{
    public const string AlreadyEstablished = "This Tangent Space is already set up.";

    public async Task<Outcome<TangentSpace>> Run(CancellationToken ct)
    {
        if (await TangentSpace.Get(TangentSpace.SingleRow, ct) is not null)
            return new Outcome<TangentSpace>.Refused(AlreadyEstablished);

        var at = At ?? DateTimeOffset.UtcNow;
        var moderators = await ModeratorsTangent.FreeSlug(ct) ?? throw new Refusal(ModeratorsTangent.NoSlug);
        var first = await FirstTangent.FreeSlug(ct);
        var space = TangentSpace.Establish(Name, OperatorId, OperatorId, moderators);
        if (await Participant.Get(OperatorId, ct) is null)
        {
            var arrived = await Participant.Arrive(OperatorId, OperatorHandle, space.Admission).Save(ct);
            await Handles.Release(OperatorHandle, OperatorId, ct);
            await MemberRole.Join(arrived, ct);
            var arrival = await Event.Append(EventKind.StandingChanged, at, ct, actorId: OperatorId, targetId: OperatorId,
                standing: arrived.Standing);
            await Checkpoint.At(OperatorId, Scope.Place, arrival.Seq).Save(ct);
        }

        if (first is not null) await FirstTangent.Create(first, OperatorId, at, ct);
        await ModeratorsTangent.Create(moderators, OperatorId, at, ct);
        await space.Save(ct);
        await AppHost.GetRequiredService<RoleCollection>(nameof(Establish)).Add(Roles.Administrator, OperatorId, ct);
        await Event.Append(EventKind.SpaceChanged, at, ct, actorId: OperatorId);
        return new Outcome<TangentSpace>.Done(space);
    }
}
