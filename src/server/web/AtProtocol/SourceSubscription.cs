using Koan.Data.Core.Model;

namespace TangentSpace.AtProtocol;

public sealed class SourceSubscription : Entity<SourceSubscription>
{
    public string Space { get; set; } = "";
    public string Service { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public string Status { get; set; } = "pending";
}
