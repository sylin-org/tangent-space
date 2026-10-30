using Koan.Identity.Roles;
using Koan.Web.Auth.Connector.Atproto;
using Koan.Web.Auth.Contributors;
using Koan.Web.Auth.Flow;
using Microsoft.Extensions.Options;
using Tangent.Access;
using Tangent.Writer;

namespace Tangent.Space;

/// <summary>First run, where a sign-in completes: signing in to a place that is not established, as the account
/// <c>Tangent:Operator</c> names by handle or DID or, with none named, as anyone, sends <see cref="Establish"/> under the
/// name the verified handle gives (<see cref="TangentSpace.NameFor"/>), and once it commits joins the signer to the
/// administrator role. Anyone else's sign-in sets nothing up. Of two sign-ins at once the writer establishes the first,
/// and refuses the second, which lands where first run sends it. No step fails the sign-in: each is logged and the
/// next sign-in tries again.</summary>
public sealed class FirstRunSignIn(WriteLoop writer, RoleCollection roles, IOptions<TangentSpaceOptions> options,
    ILogger<FirstRunSignIn> logger) : IKoanAuthFlowHandler
{
    /// <summary>After the participant's own sign-in step, which runs at 1100 and records their arrival.</summary>
    public int Priority => 1200;

    public async Task OnSignIn(AuthSignInContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ctx.UserId)) return;
        try
        {
            if (await TangentSpace.Get(TangentSpace.SingleRow, ct) is not null) return;
            var handle = ctx.Identity.FindFirst(AtprotoClaimTypes.Handle)?.Value ?? "";
            if (!options.Value.MaySetUp(ctx.UserId, handle)) return;

            switch (await writer.Send(new Establish(ctx.UserId, handle, TangentSpace.NameFor(handle)), ct))
            {
                case Outcome<TangentSpace>.Done { Result: var space }:
                    // Koan's role write is not part of the writer's transaction: it follows the commit, and is
                    // idempotent; a start repairs it if this sign-in never gets that far.
                    await roles.Add(Roles.Administrator, ctx.UserId, ct);
                    logger.LogInformation("{Person} set up this Tangent Space as {Name} at sign-in", ctx.UserId, space.Name);
                    break;
                case Outcome<TangentSpace>.Refused { Reason: Establish.AlreadyEstablished }:
                    break;
                case var other:
                    logger.LogWarning("First run at the sign-in of {Person} did not complete: {Outcome}", ctx.UserId, other);
                    break;
            }
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            logger.LogWarning(failure, "First run at the sign-in of {Person} failed", ctx.UserId);
        }
    }
}
