using Koan.Data.Core.Model;
using CarpaNet.Identity;

namespace TangentSpace.Participants;

public sealed class Participant : Entity<Participant>
{
    public string? Handle { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
    public DateTimeOffset LastArrivedAt { get; set; }
    public bool IsSuspended { get; set; }
    // A declaration, never an inference: absent labels prove nothing and undeclared stays distinct from human.
    public ParticipantClassification Classification { get; set; }
    public bool WasDeclaredAgent { get; set; }

    public static Participant FirstArrival(string verifiedDid, string? verifiedHandle, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verifiedDid);
        if (!IdentityResolver.IsValidDid(verifiedDid))
            throw new ArgumentException("A participant needs a verified AT DID.", nameof(verifiedDid));
        return new Participant { Id = verifiedDid, Handle = verifiedHandle, JoinedAt = now, LastArrivedAt = now };
    }

    public void Return(string verifiedDid, string? verifiedHandle, DateTimeOffset now)
    {
        if (!string.Equals(Id, verifiedDid, StringComparison.Ordinal))
            throw new InvalidOperationException("A returning account must have the same verified DID.");
        Handle = verifiedHandle;
        LastArrivedAt = now;
    }

    /// <summary>Owner-reviewed prototype declaration; validated claim sources arrive later.</summary>
    public void Declare(ParticipantClassification classification)
    {
        if (!Enum.IsDefined(classification)) throw new InvalidOperationException("Choose undeclared, human, or agent.");
        if (WasDeclaredAgent && classification == ParticipantClassification.Human)
            throw new InvalidOperationException("A known agent cannot declare itself human.");
        Classification = classification;
        if (classification == ParticipantClassification.Agent) WasDeclaredAgent = true;
    }
}
