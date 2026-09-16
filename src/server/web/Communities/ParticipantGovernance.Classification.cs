using Koan.Data.Core;
using TangentSpace.Activity;
using TangentSpace.Infrastructure;
using TangentSpace.Participants;
using TangentSpace.Rooms;
using TangentSpace.Site;

namespace TangentSpace.Communities;

public sealed partial class ParticipantGovernance
{
    /// <summary>Prototype owner-reviewed declaration. No label fetch, no inference; undeclared stays its own state.</summary>
    public async Task<ParticipantClassification> DeclareClassification(string actorId, string targetIdentifier, ParticipantClassification classification, CancellationToken ct)
    {
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(TopicConstants.AdministrationTransaction);
            var space = await Space.Get(TangentConstants.SpaceId, ct);
            if (space?.IsOwner(actorId) != true)
                throw new TangentRuleViolation(TangentDenial.Forbidden, "Only the space owner records classification declarations in this prototype.");
            var actor = await Participant.Get(actorId, ct);
            if (!HumanHostAccountability.CanClaim(actor))
                throw new TangentRuleViolation(TangentDenial.Forbidden, "An active human-accountable space owner is required.");
            var participant = (await directory.ByIdentifier(targetIdentifier, ct))?.Participant
                ?? throw new TangentRuleViolation(TangentDenial.NotFound, "The participant must first establish a verified arrival.");
            HumanHostAccountability.RequireDeclaration(participant, classification, space.IsOwner(participant.Id));
            participant.Declare(classification);
            await participant.Save(ct);
            await Journal(ActivityKind.ParticipantChanged, actorId, participant.Id, null, ct);
            await EntityContext.Commit(ct);
            ActivityJournal.SignalAfterCommit();
            return participant.Classification;
        }
        finally { gate.Exit(); }
    }
}
