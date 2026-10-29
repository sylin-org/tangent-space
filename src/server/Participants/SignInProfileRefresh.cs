using Koan.Web.Auth.Contributors;
using Koan.Web.Auth.Flow;

namespace Tangent.Participants;

/// <summary>At every sign-in, after Koan has reconciled the person, a participant whose profile is older than its
/// lifetime gets the profile job. Koan discovers it in the sign-in lifecycle. A person who has not arrived has no
/// participant yet; their first arrival submits the job.</summary>
public sealed class SignInProfileRefresh(ILogger<SignInProfileRefresh> logger) : IKoanAuthFlowHandler
{
    /// <summary>After Koan's reconciliation, which runs at 1000 and settles the person's id.</summary>
    public int Priority => 1100;

    public async Task OnSignIn(AuthSignInContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ctx.UserId)) return;
        if (await Participant.Get(ctx.UserId, ct) is { } participant)
            await ProfileCapture.SubmitWhenStale(participant, logger, ct);
    }
}
