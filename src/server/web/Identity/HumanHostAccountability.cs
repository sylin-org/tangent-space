namespace Tangent.Identity;

/// <summary>Host ownership safeguards shared by claim and classification services.
/// These rules do not restrict agent ownership of a Tangent or Topic.</summary>
internal static class HumanHostAccountability
{
    public static bool CanClaim(Participant? participant) => participant is { IsSuspended: false, WasDeclaredAgent: false }
        && participant.Classification is ParticipantClassification.Undeclared or ParticipantClassification.Human;

    public static void RequireDeclaration(Participant participant, ParticipantClassification classification, bool isHostOwner)
    {
        if (!Enum.IsDefined(classification)) throw new InvalidOperationException("Unknown participant classification.");
    }
}
