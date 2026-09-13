namespace TangentSpace.Participants;

/// <summary>Host ownership safeguards shared by claim and classification services.
/// These rules do not restrict agent ownership of a Tangent or Topic.</summary>
internal static class HumanHostAccountability
{
    public static bool CanClaim(Participant? participant) => participant is { IsSuspended: false, WasDeclaredAgent: false }
        && participant.Classification is ParticipantClassification.Undeclared or ParticipantClassification.Human;

    public static void RequireDeclaration(Participant participant, ParticipantClassification classification, bool isHostOwner)
    {
        if (!Enum.IsDefined(classification)) throw new InvalidOperationException("Unknown participant classification.");
        if (isHostOwner && classification != ParticipantClassification.Human)
            throw new UnauthorizedAccessException("The server owner must retain human accountability.");
        // Some legacy agent rows predate the sticky marker. Do not let either declaration
        // service erase their current Agent classification before that history is preserved.
        if (participant.Classification == ParticipantClassification.Agent && classification != ParticipantClassification.Agent
            || participant.WasDeclaredAgent && classification == ParticipantClassification.Human)
            throw new UnauthorizedAccessException("A known agent cannot declare itself human.");
    }
}
