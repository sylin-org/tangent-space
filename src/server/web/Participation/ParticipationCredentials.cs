using System.Security.Claims;
using Koan.Data.Core;
using Koan.Web.Auth.Connector.Atproto;
using TangentSpace.Participants;

namespace TangentSpace.Participation;

public sealed class ParticipationCredentials(TimeProvider clock, ParticipantDirectory directory)
{
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
