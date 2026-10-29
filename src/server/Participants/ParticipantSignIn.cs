using Koan.Web.Auth.Connector.Atproto;
using Koan.Web.Auth.Contributors;
using Koan.Web.Auth.Flow;
using Tangent.Writer;

namespace Tangent.Participants;

/// <summary>At every sign-in, after Koan has reconciled the person, a participant takes the handle the connector
/// verified at this sign-in, through the writer, and gets the profile job when their profile is older than its
/// lifetime. Koan discovers it in the sign-in lifecycle. A person who has not arrived has no participant yet; their
/// first arrival takes the handle and submits the job. Neither step fails the sign-in: each is logged and the next
/// sign-in tries again.</summary>
public sealed class ParticipantSignIn(WriteLoop writer, ILogger<ParticipantSignIn> logger) : IKoanAuthFlowHandler
{
    /// <summary>After Koan's reconciliation, which runs at 1000 and settles the person's id.</summary>
    public int Priority => 1100;

    public async Task OnSignIn(AuthSignInContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ctx.UserId) || await Participant.Get(ctx.UserId, ct) is null) return;
        var handle = ctx.Identity.FindFirst(AtprotoClaimTypes.Handle)?.Value ?? "";
        var outcome = await writer.Send(new RecordHandle(ctx.UserId, handle), ct);
        if (outcome is Outcome<Participant>.Done done)
            await ProfileCapture.SubmitWhenStale(done.Result, logger, ct);
        else
            logger.LogWarning("The handle of {Participant} was not recorded at sign-in: {Outcome}", ctx.UserId, outcome);
    }
}
