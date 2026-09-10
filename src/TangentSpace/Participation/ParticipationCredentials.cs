using System.Security.Claims;
using Koan.Data.Core;
using Koan.Web.Auth.Connector.Atproto;
using TangentSpace.Participants;

namespace TangentSpace.Participation;

public sealed class ParticipationCredentials(TimeProvider clock)
{
    public async Task<CredentialIssued> Enroll(ClaimsPrincipal verifiedCookie, CredentialEnrollmentRequest request, CancellationToken ct)
    {
        var did = ParticipationAccess.EnrollmentDid(verifiedCookie);
        using var fresh = EntityContext.NoCache();
        var participant = await Participant.Get(did, ct);
        if (participant is null || participant.IsSuspended) throw new UnauthorizedAccessException("An active participant arrival is required before enrollment.");
        var (credential, token) = ParticipantCredential.Issue(did, request.Name, request.LifetimeDays,
            request.Grants ?? [ParticipationGrants.Welcome, ParticipationGrants.Read, ParticipationGrants.Post], clock.GetUtcNow());
        await credential.Save(ct);
        return new(CredentialInfo.From(credential), token);
    }

    public async Task<IReadOnlyList<CredentialInfo>> List(ClaimsPrincipal verifiedCookie, CancellationToken ct)
    {
        var did = ParticipationAccess.EnrollmentDid(verifiedCookie);
        using var fresh = EntityContext.NoCache();
        var values = await ParticipantCredential.Query(value => value.ParticipantDid == did, ct);
        return values.OrderByDescending(value => value.CreatedAt).Select(CredentialInfo.From).ToArray();
    }

    public async Task<bool> Revoke(ClaimsPrincipal verifiedCookie, string id, CancellationToken ct)
    {
        var did = ParticipationAccess.EnrollmentDid(verifiedCookie);
        using var fresh = EntityContext.NoCache();
        var credential = await ParticipantCredential.Get(id, ct);
        if (credential is null || credential.ParticipantDid != did) return false;
        credential.Revoke(did, clock.GetUtcNow());
        await credential.Save(ct);
        return true;
    }

    internal async Task<ClaimsPrincipal?> Authenticate(string token, CancellationToken ct)
    {
        var hash = ParticipantCredential.Hash(token);
        if (hash is null) return null;
        using var fresh = EntityContext.NoCache();
        var credential = await ParticipantCredential.Get(hash, ct);
        if (credential is null || !credential.IsActive(clock.GetUtcNow())) return null;
        var participant = await Participant.Get(credential.ParticipantDid, ct);
        if (participant is null || participant.IsSuspended) return null;
        return Principal(credential);
    }

    internal static ClaimsPrincipal Principal(ParticipantCredential credential)
    {
        var identity = new ClaimsIdentity(ParticipationConstants.Scheme);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, credential.ParticipantDid));
        identity.AddClaim(new Claim(AtprotoClaimTypes.Did, credential.ParticipantDid));
        identity.AddClaim(new Claim(ParticipationConstants.CredentialClaim, credential.Id));
        foreach (var grant in credential.Grants.Where(ParticipationGrants.IsKnown)) identity.AddClaim(new Claim(ParticipationConstants.GrantClaim, grant));
        return new ClaimsPrincipal(identity);
    }
}
