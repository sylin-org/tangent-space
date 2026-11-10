using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Tangent.Access;
using Tangent.Space;

namespace Tangent.Participants;

/// <summary>Invites the Bluesky account <see cref="Did"/>, as its public profile answered for <see cref="Handle"/>, where
/// admission says apply: the place exists for the actor, who holds <see cref="Bit.ManageStanding"/>, or is refused with the
/// gate word; admission says apply, or the command is refused, since whoever signs in is admitted already
/// (<see cref="Joins"/>). Someone bound to that account who applied, waiting or declined, is admitted at once, as
/// <see cref="SetStanding"/> admits them; anyone else bound to it arrived already and is refused (<see cref="Arrived"/>). An
/// invitation already waiting for the account is answered as it stands, so a repeat writes nothing.</summary>
public sealed record Invite(string ActorId, string Did, string Handle, string? Name, string? Avatar, DateTimeOffset? At = null) : ICommand<Invite.Result>
{
    /// <summary>What inviting did: the invitation waiting for the account, or the applicant it admitted.</summary>
    public sealed record Result(Invitation? Invited, Participant? Admitted);

    /// <summary>The refusal of an invitation where admission says join.</summary>
    public static readonly Reason Joins = Reason.Conflict("joins", "Admission says join, so whoever signs in is admitted already.");

    /// <summary>The refusal of an invitation of an account that arrived here already.</summary>
    public static Reason Arrived(string handle) => Reason.Conflict("here", "@" + handle + " has arrived here already; their standing is among the people.");

    public async Task<Outcome<Result>> Run(CancellationToken ct)
    {
        if (await TangentSpace.Get(TangentSpace.SingleRow, ct) is not { } space) return new Outcome<Result>.Refused(Applications.NoPlace);
        var capabilities = await Capabilities.Load(ActorId, null, null, ct);
        if (!capabilities.Exists) return new Outcome<Result>.Refused(Applications.NoPlace);
        if (capabilities.Gate(Bit.ManageStanding) is { } gate) return new Outcome<Result>.Refused(gate);
        if (space.Admission != Admission.Apply) return new Outcome<Result>.Refused(Joins);

        var bound = (await Participant.Query(participant => participant.Did == Did, new QueryDefinition { Page = 1, PageSize = 1 }, ct)).FirstOrDefault();
        if (bound is not null)
        {
            var now = At ?? DateTimeOffset.UtcNow;
            if (bound.StandingAt(now) is not (Standing.Applied or Standing.Declined)) return new Outcome<Result>.Refused(Arrived(Handle));
            return await new SetStanding(ActorId, bound.Id, Standing.Admitted, At: now).Run(ct) switch
            {
                Outcome<Participant>.Done { Result: var admitted } => new Outcome<Result>.Done(new Result(null, admitted)),
                Outcome<Participant>.Refused { Reason: var reason } => new Outcome<Result>.Refused(reason),
                var other => throw new InvalidOperationException($"Admitting an applicant answered {other}.")
            };
        }

        if (await Invitation.Get(Did, ct) is { } waiting) return new Outcome<Result>.Done(new Result(waiting, null));
        var invitation = new Invitation { Id = Did, Handle = Handle, Name = Name, Avatar = Avatar, InvitedBy = ActorId, InvitedAt = At ?? DateTimeOffset.UtcNow };
        await invitation.Save(ct);
        return new Outcome<Result>.Done(new Result(invitation, null));
    }
}

/// <summary>Withdraws the invitation of the account <see cref="Did"/>, for whoever holds <see cref="Bit.ManageStanding"/>:
/// whoever it named applies as anyone does. One already gone is withdrawn, so a repeat writes nothing.</summary>
public sealed record WithdrawInvitation(string ActorId, string Did) : ICommand<string>
{
    public async Task<Outcome<string>> Run(CancellationToken ct)
    {
        var capabilities = await Capabilities.Load(ActorId, null, null, ct);
        if (!capabilities.Exists) return new Outcome<string>.Refused(Applications.NoPlace);
        if (capabilities.Gate(Bit.ManageStanding) is { } gate) return new Outcome<string>.Refused(gate);
        if (await Invitation.Get(Did, ct) is { } invitation) await invitation.Remove(ct);
        return new Outcome<string>.Done(Did);
    }
}
