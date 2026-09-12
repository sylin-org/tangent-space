using Koan.Data.Core.Model;
using CarpaNet.Identity;

namespace TangentSpace.Participants;

/// <summary>Keyed by an internal GUIDv7 minted at creation; no external identifier is ever the
/// key. External identifiers (atproto DIDs, handles, future kinds) live on the identity
/// collection and resolve point-in-time to their current holder.</summary>
public sealed class Participant : Entity<Participant>
{
    public DateTimeOffset JoinedAt { get; set; }
    public DateTimeOffset LastArrivedAt { get; set; }
    public bool IsSuspended { get; set; }
    // A declaration, never an inference: absent labels prove nothing and undeclared stays distinct from human.
    public ParticipantClassification Classification { get; set; }
    public bool WasDeclaredAgent { get; set; }

    public static string NewIdentifier() => Guid.CreateVersion7().ToString("N");

    /// <summary>GUIDv7 participant ids are 32 lowercase hex characters.</summary>
    public static bool IsValidId(string? value)
        => value is { Length: 32 } && value.All(char.IsAsciiHexDigitLower);

    /// <summary>First verified atproto arrival: mint the GUIDv7 spine, the derived internal
    /// identity and the atproto identity entry carrying the verified handle as its label.
    /// The caller persists the participant and the returned rows in one transaction.</summary>
    public static (Participant Participant, IReadOnlyList<ParticipantIdentity> Identities) Enroll(
        string verifiedDid, string? verifiedHandle, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verifiedDid);
        if (!IdentityResolver.IsValidDid(verifiedDid))
            throw new ArgumentException("A participant needs a verified AT DID.", nameof(verifiedDid));
        var participant = new Participant { Id = NewIdentifier(), JoinedAt = now, LastArrivedAt = now };
        return (participant,
        [
            ParticipantIdentity.Internal(participant.Id, now),
            ParticipantIdentity.Atproto(participant.Id, verifiedDid, verifiedHandle, now)
        ]);
    }

    /// <summary>Refreshes the arrival stamp. The caller has already matched the atproto identity
    /// row to this participant and refreshes its label alongside.</summary>
    public void Return(ParticipantIdentity atproto, DateTimeOffset now)
    {
        if (atproto.Kind != ParticipantIdentity.AtprotoKind || atproto.ParticipantId != Id)
            throw new InvalidOperationException("A returning account must arrive with its own atproto identity.");
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
