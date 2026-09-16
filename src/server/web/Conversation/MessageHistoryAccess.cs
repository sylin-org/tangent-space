using Koan.Data.Core;
using Koan.Web.Authorization;
using TangentSpace.Participation;
using Microsoft.Extensions.DependencyInjection;
using TangentSpace.Communities;
using TangentSpace.Infrastructure;
using TangentSpace.Participants;
using TangentSpace.Rooms;
using TangentSpace.Site;

namespace TangentSpace.Conversation;

/// <summary>D4b read gate for Message rows on the generic entity surface (the gposingway
/// governed-read pattern on the WEB-0068 rail): a changelog snapshot is visible only to its
/// author (AuthorParticipantId == viewer) or to a viewer holding a moderation-capable role for that
/// snapshot's room. Per-row control lives in the predicate, so a moderation-removed post's
/// snapshots stay author+moderator visible while everyone else gets the framework's honest
/// empty/404. The predicate is ANDed with a structural snapshot-only term (OfMessageId set),
/// so no row other than a changelog snapshot can ever match this surface even if a future
/// partition selection slips; the controller separately pins ?set=changelog. Writes are denied
/// row-wise too: the changelog is write-once history mutated only by domain code.
///
/// A suspended viewer is denied everything on this surface, author read included — the
/// integrator's decision, aligning history with the app-wide suspension posture
/// (RoomGovernance denies suspended participants every governed operation).
///
/// The realization cannot constructor-inject services (the boot registry demands a public
/// parameterless constructor and reads the gate on a throwaway instance), so it resolves the
/// per-request world the framework-supported way: Bind supplies Principal and Services, exactly
/// the seam gposingway's WorkAccess uses. The moderator-room set is computed once per Constrain
/// call — once per request, never per row. (The partition-pinning Where overload is not used:
/// this pinned framework copy's SQLite adapter does not declare SupportsSameIdIn, so a
/// counterpart filter would refuse to execute.)</summary>
public sealed class MessageHistoryAccess : EntityAccess<Message>
{
    public override IAccessFilter<Message> Constrain(IAccessFilter<Message> q, AccessAction action)
    {
        if (action == AccessAction.Read)
        {
            var viewer = Principal.FindFirst(ParticipationConstants.ParticipantClaim)?.Value;
            if (viewer is null || !TangentSpace.Participants.Participant.IsValidId(viewer))
                return q.Where(row => false);
            using (EntityContext.NoCache())
            {
                // Suspension denies the whole surface, not just the moderator tier.
                if (Block<Participant?>().Invoke(Participant.Get(viewer, CancellationToken.None))?.IsSuspended == true)
                    return q.Where(row => false);
            }
            var moderatorRooms = ModeratorRooms(viewer);
            return q.Where(row => row.OfMessageId != null
                && (row.AuthorParticipantId == viewer || moderatorRooms.Contains(row.RoomKey)));
        }
        // No row may ever be updated or removed through the generic surface; a create contributes
        // no Where (an unstamped create constraint is a boot-probed footgun).
        return action is AccessAction.Update or AccessAction.Delete ? q.Where(row => false) : q;
    }

    /// <summary>The rooms where the viewer durably holds moderation authority, mirroring
    /// Room.CurrentPolicy's owner/manager clauses: space or Tangent ownership, room manager,
    /// delegated Tangent administrator, or channel creator — subject to room admission: a viewer
    /// durably Removed from the parent Tangent loses the tier, and an active durable
    /// Banned restriction removes it for non-owners. Time-scoped timeouts are enforced on
    /// management actions in the domain layer and remain deliberately ignored by this read
    /// predicate. Constrain is synchronous on the endpoint read path (no SynchronizationContext)
    /// and runs before the main query, so the bounded role lookups block once per request rather
    /// than deadlocking.</summary>
    private static HashSet<string> ModeratorRooms(string participantId)
    {
        var rooms = new HashSet<string>(StringComparer.Ordinal);
        using (EntityContext.NoCache())
        {
            var space = Block<Space?>().Invoke(Space.Get(TangentConstants.SpaceId, CancellationToken.None));
            var siteOwner = space?.IsOwner(participantId) == true;
            var roles = new Dictionary<string, RoomRole>(StringComparer.Ordinal);
            foreach (var membership in Block<IReadOnlyList<RoomMembership>>()
                .Invoke(RoomMembership.Query(membership => membership.ParticipantId == participantId, CancellationToken.None)))
                roles[membership.RoomKey] = membership.Role;
            var tangentMemberships = new Dictionary<string, TangentMembership>(StringComparer.Ordinal);
            var admin = new HashSet<string>(StringComparer.Ordinal);
            foreach (var membership in Block<IReadOnlyList<TangentMembership>>()
                .Invoke(TangentMembership.Query(membership => membership.ParticipantId == participantId, CancellationToken.None)))
            {
                tangentMemberships[membership.TangentKey] = membership;
                if (membership.Role == TangentRole.Admin) admin.Add(membership.TangentKey);
            }
            var owned = new HashSet<string>(StringComparer.Ordinal);
            foreach (var tangent in Block<IReadOnlyList<Tangent>>()
                .Invoke(Tangent.Query(tangent => tangent.OwnerParticipantId == participantId, CancellationToken.None)))
                owned.Add(tangent.Id);

            var candidates = new Dictionary<string, Room>(StringComparer.Ordinal);
            void Admit(Room room) => candidates[room.Id] = room;
            if (siteOwner)
            {
                // The space owner manages every room; the room universe is this space's bounded directory.
                foreach (var room in Block<IReadOnlyList<Room>>().Invoke(Room.All(CancellationToken.None))) Admit(room);
            }
            else
            {
                foreach (var tangentKey in owned.Concat(admin))
                    foreach (var room in Block<IReadOnlyList<Room>>()
                        .Invoke(Room.Query(room => room.TangentKey == tangentKey, CancellationToken.None))) Admit(room);
                foreach (var room in Block<IReadOnlyList<Room>>()
                    .Invoke(Room.Query(room => room.CreatorParticipantId == participantId, CancellationToken.None))) Admit(room);
                foreach (var (roomKey, role) in roles)
                    if (role == RoomRole.Manager && Block<Room?>().Invoke(Room.Get(roomKey, CancellationToken.None)) is { } room)
                        Admit(room);
            }
            var now = DateTimeOffset.UtcNow;
            foreach (var room in candidates.Values)
            {
                // Room admission parity with CurrentPolicy: a Topic whose Tangent is missing admits no one;
                // owners are always admitted; otherwise the Tangent's own admission rule decides with the
                // viewer's membership, where a durable removal overrides.
                var tangent = Block<Tangent?>().Invoke(Tangent.Get(room.TangentKey, CancellationToken.None));
                if (tangent is null) continue;
                var ownerHere = siteOwner || owned.Contains(room.TangentKey);
                var admitted = ownerHere
                    || tangent.CanParticipate(participantId, tangentMemberships.GetValueOrDefault(room.TangentKey));
                // A durable ban removes the manager tier for non-owners (owners are never
                // restriction targets); time-scoped timeouts stay ignored for reads.
                var banned = !ownerHere && Block<EffectiveRestriction?>()
                    .Invoke(Restrictions.ForRoom(participantId, room.Id, room.TangentKey, now, CancellationToken.None)) is { Banned: true };
                RoomRole? role = roles.TryGetValue(room.Id, out var assigned) ? assigned : null;
                var managerHere = admitted
                    && (role == RoomRole.Manager
                        || role is null or not RoomRole.Removed && (admin.Contains(room.TangentKey) || room.CreatorParticipantId == participantId));
                if (ownerHere || managerHere && !banned) rooms.Add(room.Id);
            }
        }
        return rooms;
    }

    private static Func<Task<T>, T> Block<T>() => task => task.GetAwaiter().GetResult();
}
