using System.Security.Claims;
using Koan.Data.Core;
using Koan.Web.Auth.Connector.Atproto;
using TangentSpace.Participants;

namespace TangentSpace.Participation;

public sealed class ParticipationCredentials(TimeProvider clock, ParticipantDirectory directory)
{
    public async Task<CredentialIssued> Enroll(ClaimsPrincipal verifiedCookie, CredentialEnrollmentRequest request, CancellationToken ct)
    {
        var participantId = ParticipationAccess.EnrollmentParticipant(verifiedCookie);
        using var fresh = EntityContext.NoCache();
        var participant = await Participant.Get(participantId, ct);
        if (participant is null || participant.IsSuspended) throw new UnauthorizedAccessException("An active participant arrival is required before enrollment.");
        var (credential, token) = ParticipantCredential.Issue(participantId, request.Name, request.LifetimeDays,
            request.Grants ?? [ParticipationGrants.Welcome, ParticipationGrants.Read, ParticipationGrants.Post], clock.GetUtcNow());
        await credential.Save(ct);
        return new(CredentialInfo.From(credential), token);
    }

    public async Task<IReadOnlyList<CredentialInfo>> List(ClaimsPrincipal verifiedCookie, CancellationToken ct)
    {
        var participantId = ParticipationAccess.EnrollmentParticipant(verifiedCookie);
        using var fresh = EntityContext.NoCache();
        var values = await ParticipantCredential.Query(value => value.ParticipantId == participantId, ct);
        return values.OrderByDescending(value => value.CreatedAt).Select(CredentialInfo.From).ToArray();
    }

    public async Task<bool> Revoke(ClaimsPrincipal verifiedCookie, string id, CancellationToken ct)
    {
        var participantId = ParticipationAccess.EnrollmentParticipant(verifiedCookie);
        using var fresh = EntityContext.NoCache();
        var credential = await ParticipantCredential.Get(id, ct);
        if (credential is null || credential.ParticipantId != participantId) return false;
        credential.Revoke(participantId, clock.GetUtcNow());
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
        var participant = await Participant.Get(credential.ParticipantId, ct);
        if (participant is null || participant.IsSuspended) return null;
        return Principal(credential, await directory.AtprotoDidOf(credential.ParticipantId, ct));
    }

    /// <summary>Bearer principal minting: tangent:participant (the GUID) always, and the atproto
    /// DID claim only when the participant holds an atproto identity.</summary>
    internal static ClaimsPrincipal Principal(ParticipantCredential credential, string? atprotoDid = null)
    {
        var identity = new ClaimsIdentity(ParticipationConstants.Scheme);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, credential.ParticipantId));
        identity.AddClaim(new Claim(ParticipationConstants.ParticipantClaim, credential.ParticipantId));
        if (atprotoDid is not null) identity.AddClaim(new Claim(AtprotoClaimTypes.Did, atprotoDid));
        identity.AddClaim(new Claim(ParticipationConstants.CredentialClaim, credential.Id));
        foreach (var grant in credential.Grants.Where(ParticipationGrants.IsKnown)) identity.AddClaim(new Claim(ParticipationConstants.GrantClaim, grant));
        return new ClaimsPrincipal(identity);
    }
}
