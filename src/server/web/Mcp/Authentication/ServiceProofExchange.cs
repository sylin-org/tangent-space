using Koan.Data.Core;
using Microsoft.Extensions.Options;
using TangentSpace.Infrastructure;
using TangentSpace.Participants;
using TangentSpace.Participation;
using TangentSpace.Site;

namespace TangentSpace.Mcp.Authentication;

/// <summary>Exchanges a verified inbound AT service-auth proof for a Tangent-local participant credential.
/// Proof verification precedes participant arrival; replay consumption, expiry/suspension rechecks and issuance
/// share one gated transaction so async gaps cannot issue from an expired proof or a suspended actor.</summary>
public sealed class ServiceProofExchange(ServiceProofAuthentication proofs, Arrival arrival, PolicyGate gate,
    TimeProvider clock, TangentSpace.Participants.ParticipantDirectory directory, ILogger<ServiceProofExchange> logger)
{
    private static readonly string[] DefaultGrants = [ParticipationGrants.Welcome, ParticipationGrants.Read, ParticipationGrants.Post];

    public async Task<ServiceProofExchangeResult> Exchange(string authorization, ServiceProofExchangeRequest? request, CancellationToken ct)
    {
        var proof = await proofs.Verify(authorization, ct);
        if (proof.Status == ServiceProofStatus.Unconfigured) return new(ServiceProofExchangeStatus.UnconfiguredAudience);
        if (proof.Status == ServiceProofStatus.Unreachable) return new(ServiceProofExchangeStatus.IdentityUnreachable);
        if (proof.Status != ServiceProofStatus.Valid || proof.Proof is null) return new(ServiceProofExchangeStatus.InvalidProof);
        return await Complete(proof.Proof, request, ct);
    }

    /// <summary>Continues from an already verified proof: arrival, then the gated issuance transaction.
    /// Internal so the in-transaction expiry and suspension rechecks are directly regression-testable
    /// after the async verification/arrival gaps.</summary>
    internal async Task<ServiceProofExchangeResult> Complete(ServiceProof verified, ServiceProofExchangeRequest? request, CancellationToken ct)
    {
        var name = request?.Name ?? "mcp";
        var lifetime = request?.LifetimeDays ?? 1;
        var grants = request?.Grants ?? DefaultGrants;
        // A manage grant is an explicitly requested coarse credential grant only; it never appoints anyone.
        // Every domain mutation independently verifies current authority against live policy.
        var managementPermitted = grants.Contains(ParticipationGrants.Manage);
        try
        {
            ct.ThrowIfCancellationRequested();
            // Arrival resolves the verified account's document label and refreshes it on the
            // identity row. A missing public profile never changes proof authority.
            try { await arrival.Enter(verified.IssuerDid, null, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception error) when (error is InvalidOperationException or UnauthorizedAccessException)
            {
                logger.LogDebug("MCP exchange arrival rejected for {Issuer}: {FailureType}", verified.IssuerDid, error.GetType().Name);
                return new(ServiceProofExchangeStatus.SiteUnavailable);
            }
            await gate.Enter(ct);
            try
            {
                using var fresh = EntityContext.NoCache();
                using var transaction = EntityContext.Transaction("mcp-proof-exchange");
                var now = clock.GetUtcNow();
                if (verified.ExpiresAt <= now) return new(ServiceProofExchangeStatus.InvalidProof);
                var participant = await directory.ByDid(verified.IssuerDid, ct);
                if (participant is null || participant.IsSuspended) return new(ServiceProofExchangeStatus.Suspended);
                var recordId = ServiceProofReplayRecord.Key(verified.IssuerDid, verified.Jti);
                var consumed = await ServiceProofReplayRecord.Get(recordId, ct);
                if (consumed is not null && consumed.ExpiresAt > now) return new(ServiceProofExchangeStatus.ReplayedProof);
                var (credential, token) = ParticipantCredential.Issue(participant!.Id, name, lifetime, grants, now, managementPermitted);
                await credential.Save(ct);
                await new ServiceProofReplayRecord
                {
                    Id = recordId, IssuerDid = verified.IssuerDid, Jti = verified.Jti, CredentialId = credential.Id,
                    ConsumedAt = now, ExpiresAt = verified.ExpiresAt.AddMinutes(5)
                }.Save(ct);
                await EntityContext.Commit(ct);
                logger.LogInformation("MCP proof exchange issued credential {CredentialId} for participant {Issuer}.", credential.Id, verified.IssuerDid);
                return new(ServiceProofExchangeStatus.Issued, new CredentialIssued(CredentialInfo.From(credential), token));
            }
            finally { gate.Exit(); }
        }
        catch (Exception error) when (error is ArgumentException)
        {
            return new(ServiceProofExchangeStatus.BadRequest, Reason: error.Message);
        }
    }
}
