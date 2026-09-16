using Koan.Data.Core;
using Koan.Web.Authorization;
using Tangent.Identity;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Community;
using Tangent.Infrastructure;
using Tangent.Application;
using Tangent.Identity;
using Tangent.Community;
using Tangent.Stewardship;
using Tangent.Spaces;

namespace Tangent.Conversation;

/// <summary>D4b read gate for Post rows on the generic entity surface (the gposingway
/// governed-read pattern on the WEB-0068 rail): a changelog snapshot is visible only to its
/// author (AuthorParticipantId == viewer) or to a viewer holding a moderation-capable role for that
/// snapshot's topic. Per-row control lives in the predicate, so a moderation-removed post's
/// snapshots stay author+moderator visible while everyone else gets the framework's honest
/// empty/404. The predicate is ANDed with a structural snapshot-only term (OfPostId set),
/// so no row other than a changelog snapshot can ever match this surface even if a future
/// partition selection slips; the controller separately pins ?set=changelog. Writes are denied
/// row-wise too: the changelog is write-once history mutated only by domain code.
///
/// A suspended viewer is denied everything on this surface, author read included — the
/// integrator's decision, aligning history with the app-wide suspension posture
/// (TopicGovernance denies suspended participants every governed operation).
///
/// The realization cannot constructor-inject services (the boot registry demands a public
/// parameterless constructor and reads the gate on a throwaway instance), so it resolves the
/// per-request world the framework-supported way: Bind supplies Principal and Services, exactly
/// the seam gposingway's WorkAccess uses. The moderator-topic set is computed once per Constrain
/// call — once per request, never per row. (The partition-pinning Where overload is not used:
/// this pinned framework copy's SQLite adapter does not declare SupportsSameIdIn, so a
/// counterpart filter would refuse to execute.)</summary>
public sealed class PostHistoryAccess : EntityAccess<Post>
{
    public override IAccessFilter<Post> Constrain(IAccessFilter<Post> q, AccessAction action)
    {
        if (action == AccessAction.Read)
        {
            var viewer = Principal.FindFirst(ParticipationConstants.ParticipantClaim)?.Value;
            if (viewer is null || !Participant.IsValidId(viewer))
                return q.Where(row => false);
            using (EntityContext.NoCache())
            {
                // Suspension denies the whole surface, not just the moderator tier.
                if (Block<Participant?>().Invoke(Participant.Get(viewer, CancellationToken.None))?.IsSuspended == true)
                    return q.Where(row => false);
            }
            var moderatorTopics = ModeratorTopics(viewer);
            return q.Where(row => row.OfPostId != null
                && (row.AuthorParticipantId == viewer || moderatorTopics.Contains(row.TopicKey)));
        }
        // No row may ever be updated or removed through the generic surface; a create contributes
        // no Where (an unstamped create constraint is a boot-probed footgun).
        return action is AccessAction.Update or AccessAction.Delete ? q.Where(row => false) : q;
    }

    /// <summary>The topics where the viewer durably holds moderation authority, mirroring
    /// Topic.CurrentPolicy's owner/manager clauses: space or Tangent ownership, topic manager,
    /// delegated Tangent administrator, or topic creator — subject to topic admission: a viewer
    /// durably Removed from the parent Tangent loses the tier, and an active durable
    /// Banned restriction removes it for non-owners. Time-scoped timeouts are enforced on
    /// management actions in the domain layer and remain deliberately ignored by this read
    /// predicate. Constrain is synchronous on the endpoint read path (no SynchronizationContext)
    /// and runs before the main query, so the bounded role lookups block once per request rather
    /// than deadlocking.</summary>
    private static HashSet<string> ModeratorTopics(string participantId)
    {
        var topics = new HashSet<string>(StringComparer.Ordinal);
        using (EntityContext.NoCache())
        {
            var space = Block<Space?>().Invoke(Space.Get(TangentConstants.SpaceId, CancellationToken.None));
            var siteOwner = space?.IsOwner(participantId) == true;
            var roles = new Dictionary<string, TopicRole>(StringComparer.Ordinal);
            foreach (var membership in Block<IReadOnlyList<TopicMembership>>()
                .Invoke(TopicMembership.Query(membership => membership.ParticipantId == participantId, CancellationToken.None)))
                roles[membership.TopicKey] = membership.Role;
            var tangentMemberships = new Dictionary<string, TangentMembership>(StringComparer.Ordinal);
            var admin = new HashSet<string>(StringComparer.Ordinal);
            foreach (var membership in Block<IReadOnlyList<TangentMembership>>()
                .Invoke(TangentMembership.Query(membership => membership.ParticipantId == participantId, CancellationToken.None)))
            {
                tangentMemberships[membership.TangentKey] = membership;
                if (membership.Role == TangentRole.Admin) admin.Add(membership.TangentKey);
            }
            var owned = new HashSet<string>(StringComparer.Ordinal);
            foreach (var tangent in Block<IReadOnlyList<Community.Tangent>>()
                .Invoke(Community.Tangent.Query(tangent => tangent.OwnerParticipantId == participantId, CancellationToken.None)))
                owned.Add(tangent.Id);

            var candidates = new Dictionary<string, Topic>(StringComparer.Ordinal);
            void Admit(Topic topic) => candidates[topic.Id] = topic;
            if (siteOwner)
            {
                // The space owner manages every topic; the topic universe is this space's bounded directory.
                foreach (var topic in Block<IReadOnlyList<Topic>>().Invoke(Topic.All(CancellationToken.None))) Admit(topic);
            }
            else
            {
                foreach (var tangentKey in owned.Concat(admin))
                    foreach (var topic in Block<IReadOnlyList<Topic>>()
                        .Invoke(Topic.Query(topic => topic.TangentKey == tangentKey, CancellationToken.None))) Admit(topic);
                foreach (var topic in Block<IReadOnlyList<Topic>>()
                    .Invoke(Topic.Query(topic => topic.CreatorParticipantId == participantId, CancellationToken.None))) Admit(topic);
                foreach (var (roomKey, role) in roles)
                    if (role == TopicRole.Manager && Block<Topic?>().Invoke(Topic.Get(roomKey, CancellationToken.None)) is { } topic)
                        Admit(topic);
            }
            var now = DateTimeOffset.UtcNow;
            foreach (var topic in candidates.Values)
            {
                // Topic admission parity with CurrentPolicy: a Topic whose Tangent is missing admits no one;
                // owners are always admitted; otherwise the Tangent's own admission rule decides with the
                // viewer's membership, where a durable removal overrides.
                var tangent = Block<Community.Tangent?>().Invoke(Community.Tangent.Get(topic.TangentKey, CancellationToken.None));
                if (tangent is null) continue;
                var ownerHere = siteOwner || owned.Contains(topic.TangentKey);
                var admitted = ownerHere
                    || tangent.CanParticipate(participantId, tangentMemberships.GetValueOrDefault(topic.TangentKey));
                // A durable ban removes the manager tier for non-owners (owners are never
                // restriction targets); time-scoped timeouts stay ignored for reads.
                var banned = !ownerHere && Block<EffectiveRestriction?>()
                    .Invoke(Restrictions.ForTopic(participantId, topic.Id, topic.TangentKey, now, CancellationToken.None)) is { Banned: true };
                TopicRole? role = roles.TryGetValue(topic.Id, out var assigned) ? assigned : null;
                var managerHere = admitted
                    && (role == TopicRole.Manager
                        || role is null or not TopicRole.Removed && (admin.Contains(topic.TangentKey) || topic.CreatorParticipantId == participantId));
                if (ownerHere || managerHere && !banned) topics.Add(topic.Id);
            }
        }
        return topics;
    }

    private static Func<Task<T>, T> Block<T>() => task => task.GetAwaiter().GetResult();
}
