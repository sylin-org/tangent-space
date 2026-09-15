using TangentSpace.Participation;

namespace TangentSpace.Hosting;

/// <summary>Selects the participant credential handler whenever an Authorization header is present,
/// even for an invalid token: a failed bearer never falls back to a browser cookie.</summary>
public sealed class BearerRegistration : IRegistration
{
    public string Name => "bearer";
    public string AuthenticationScheme => ParticipationConstants.Scheme;
    public bool Accepts(HttpRequest request) => request.Headers.ContainsKey("Authorization");
}
