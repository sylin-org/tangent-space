using Koan.Identity;
using Koan.Identity.Roles;
using TangentSpace.Participation;

namespace TangentSpace.Authorization;

/// <summary>Binds Koan's stable role subject to Tangent's persistent participant spine.</summary>
public sealed class TangentRoleContext(IHttpContextAccessor http) : IIdentityActorAccessor, IScopedRoleSubjectAccessor
{
    private static readonly AsyncLocal<Frame?> ambient = new();
    public string? CurrentActorSubject => ambient.Value?.Actor ?? CurrentParticipant();
    public string? CurrentSubject => ambient.Value?.Subject ?? CurrentParticipant();

    public IDisposable Bind(string subject, string? actor = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        var previous = ambient.Value;
        ambient.Value = new(actor ?? subject, subject);
        return new Restore(previous);
    }

    private string? CurrentParticipant() => http.HttpContext?.User.FindFirst(ParticipationConstants.ParticipantClaim)?.Value;
    private sealed record Frame(string Actor, string Subject);
    private sealed class Restore(Frame? previous) : IDisposable
    {
        private bool disposed;
        public void Dispose() { if (!disposed) { ambient.Value = previous; disposed = true; } }
    }
}
