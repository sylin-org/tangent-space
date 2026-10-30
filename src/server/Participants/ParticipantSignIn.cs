using Koan.Identity.Roles;
using Koan.Web.Auth.Connector.Atproto;
using Koan.Web.Auth.Contributors;
using Koan.Web.Auth.Flow;
using Tangent.Writer;

namespace Tangent.Participants;

/// <summary>At every sign-in, after Koan has reconciled the person: a person's first sign-in is their first arrival,
/// which creates their participant through the writer; a returning participant takes the handle the connector
/// verified at this sign-in. Either way, once that commits, a participant who stands admitted now, a timeout that has
/// ended among them, joins the member role where they lack it (<see cref="MemberRole"/>); the row keeps the standing it
/// holds, which reads as admitted everywhere once a timeout ends. The profile job is submitted when the profile is empty or older than
/// its lifetime. This is where Tangent learns of a first arrival. Koan discovers it in the sign-in lifecycle. No step
/// fails the sign-in: each is logged and the next sign-in tries again.</summary>
public sealed class ParticipantSignIn(WriteLoop writer, RoleCollection roles, ILogger<ParticipantSignIn> logger) : IKoanAuthFlowHandler
{
    /// <summary>After Koan's reconciliation, which runs at 1000 and settles the person's id.</summary>
    public int Priority => 1100;

    public async Task OnSignIn(AuthSignInContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ctx.UserId)) return;
        var handle = ctx.Identity.FindFirst(AtprotoClaimTypes.Handle)?.Value ?? "";
        ICommand<Participant> command = await Participant.Get(ctx.UserId, ct) is null
            ? new FirstArrival(ctx.UserId, handle)
            : new RecordHandle(ctx.UserId, handle);
        var outcome = await writer.Send(command, ct);
        if (outcome is Outcome<Participant>.Done done)
        {
            await JoinMemberRole(done.Result, ct);
            await ProfileCapture.SubmitWhenStale(done.Result, logger, ct);
        }
        else
        {
            logger.LogWarning("{Command} for {Person} did not complete at sign-in: {Outcome}", command.GetType().Name, ctx.UserId, outcome);
        }
    }

    private async Task JoinMemberRole(Participant participant, CancellationToken ct)
    {
        try
        {
            if (await MemberRole.Join(roles, participant, ct))
                logger.LogInformation("{Person} joined the member role at sign-in", participant.Id);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            logger.LogWarning(failure, "{Person} could not join the member role at sign-in", participant.Id);
        }
    }
}
