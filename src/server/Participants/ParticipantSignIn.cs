using Koan.Web.Auth.Connector.Atproto;
using Koan.Web.Auth.Contributors;
using Koan.Web.Auth.Flow;
using Tangent.Writer;

namespace Tangent.Participants;

/// <summary>At every sign-in, after Koan has reconciled the person: a person's first sign-in is their first arrival,
/// which creates their participant through the writer; a returning participant takes the handle the connector
/// verified at this sign-in. Either way the profile job is submitted when the profile is empty or older than its
/// lifetime. This is where Tangent learns of a first arrival. Koan discovers it in the sign-in lifecycle. No step
/// fails the sign-in: each is logged and the next sign-in tries again.</summary>
public sealed class ParticipantSignIn(WriteLoop writer, ILogger<ParticipantSignIn> logger) : IKoanAuthFlowHandler
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
            await ProfileCapture.SubmitWhenStale(done.Result, logger, ct);
        else
            logger.LogWarning("{Command} for {Person} did not complete at sign-in: {Outcome}", command.GetType().Name, ctx.UserId, outcome);
    }
}
