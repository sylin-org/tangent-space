using Koan.Identity;
using TangentSpace.Participation;

namespace TangentSpace.Authorization;

/// <summary>Maps Koan identity operations onto Tangent's persistent participant identity.</summary>
public sealed class TangentIdentityActorAccessor(IHttpContextAccessor http) : IIdentityActorAccessor
{
    public string? CurrentActorSubject
        => http.HttpContext?.User.FindFirst(ParticipationConstants.ParticipantClaim)?.Value;
}
