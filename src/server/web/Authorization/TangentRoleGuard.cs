using Koan.Identity.Roles;
using TangentSpace.Communities;
using TangentSpace.Participants;
using TangentSpace.Rooms;
using TangentSpace.Site;

namespace TangentSpace.Authorization;

/// <summary>Non-delegable application limits intersect every editable role or policy grant.</summary>
public sealed class TangentRoleGuard(TimeProvider clock) : IScopedRoleGuardContributor
{
    public async ValueTask<ScopedRoleGuardResult> Evaluate(ScopedRoleGuardRequest request, CancellationToken ct = default)
    {
        if (request.Target.TenantId != TangentRoleScopes.Tenant) return Deny("tenant.invalid");
        return request.Target.Type switch
        {
            TangentRoleScopes.Host => await Host(request, ct),
            TangentRoleScopes.Tangent => await Tangent(request, ct),
            TangentRoleScopes.Topic => await Topic(request, ct),
            _ => Deny("scope.invalid")
        };
    }

    private static async Task<ScopedRoleGuardResult> Host(ScopedRoleGuardRequest request, CancellationToken ct)
    {
        var site = await TangentSite.Get("site", ct);
        if (site is null) return Deny("host.unavailable");
        if (!request.Subject.IsAuthenticated) return Deny("sign-in.required");
        var participant = await Participant.Get(request.Subject.Subject, ct);
        if (participant is null || participant.IsSuspended) return Deny("participant.unavailable");
        var allowed = request.Capability switch
        {
            TangentRoleCapabilities.HostRead => true,
            TangentRoleCapabilities.HostManage => site.IsOwner(participant.Id),
            TangentRoleCapabilities.TangentCreate => site.CanCreateTangent(participant),
            _ => true
        };
        return allowed ? Permit(site.PolicyRevision) : Deny("host.policy.denied", site.PolicyRevision);
    }

    private async Task<ScopedRoleGuardResult> Tangent(ScopedRoleGuardRequest request, CancellationToken ct)
    {
        if (!request.Subject.IsAuthenticated) return Deny("sign-in.required");
        var site = await TangentSite.Get("site", ct);
        var tangent = await TangentCommunity.Get(request.Target.Id, ct);
        var participant = await Participant.Get(request.Subject.Subject, ct);
        if (site is null || tangent is null || participant is null || participant.IsSuspended) return Deny("participant.unavailable");
        var membership = await TangentMembership.Get(TangentMembership.Key(tangent.Id, participant.Id), ct);
        var owner = site.IsOwner(participant.Id) || tangent.IsOwner(participant.Id);
        var restricted = !owner && await Restrictions.ForTangent(participant.Id, tangent.Id, clock.GetUtcNow(), ct) is not null;
        var rights = tangent.ParticipationRights(participant.Classification);
        var allowed = request.Capability switch
        {
            TangentRoleCapabilities.TangentRead => !restricted && tangent.CanParticipate(participant.Id, membership) && (owner || rights.Read),
            TangentRoleCapabilities.TangentManage => owner || !restricted && membership?.Role == TangentRole.Admin,
            TangentRoleCapabilities.TangentManageParticipants => owner,
            TangentRoleCapabilities.TopicCreate => owner || !restricted && (membership?.Role == TangentRole.Admin
                || tangent.AllowMemberTopics && membership?.Role == TangentRole.Member && rights.Write),
            _ => true
        };
        return allowed ? Permit(tangent.PolicyRevision) : Deny("tangent.policy.denied", tangent.PolicyRevision);
    }

    private async Task<ScopedRoleGuardResult> Topic(ScopedRoleGuardRequest request, CancellationToken ct)
    {
        var room = await Room.Get(request.Target.Id, ct);
        var site = await TangentSite.Get("site", ct);
        var tangent = room is null ? null : await TangentCommunity.Get(room.TangentKey, ct);
        if (room is null || site is null || tangent is null) return Deny("topic.unavailable");
        if (!request.Subject.IsAuthenticated)
            return request.Capability == TangentRoleCapabilities.TopicRead && room.ReadAudience == RoomReadAudience.Public
                && Ready(room) ? Permit(room.PolicyRevision) : Deny("sign-in.required", room.PolicyRevision);
        var participant = await Participant.Get(request.Subject.Subject, ct);
        if (participant is null) return Deny("participant.unavailable", room.PolicyRevision);
        var membership = await RoomMembership.Get(RoomMembership.Key(room.Id, participant.Id), ct);
        var tangentMembership = await TangentMembership.Get(TangentMembership.Key(tangent.Id, participant.Id), ct);
        var restriction = await Restrictions.ForRoom(participant.Id, room.Id, room.TangentKey, clock.GetUtcNow(), ct);
        var policy = room.CurrentPolicy(site, participant.Id, membership, participant.IsSuspended, tangent, tangentMembership,
            participant.Classification, restriction);
        var capability = request.Capability switch
        {
            TangentRoleCapabilities.TopicRead => TopicCapability.Read,
            TangentRoleCapabilities.TopicReply => TopicCapability.Reply,
            TangentRoleCapabilities.TopicManage => TopicCapability.ManageTopic,
            TangentRoleCapabilities.TopicManageParticipants => TopicCapability.ManageParticipants,
            TangentRoleCapabilities.TopicAppointManagers => TopicCapability.AppointManagers,
            TangentRoleCapabilities.PostEditOwn => TopicCapability.EditOwnPost,
            TangentRoleCapabilities.PostDeleteOwn => TopicCapability.DeleteOwnPost,
            TangentRoleCapabilities.PostRemove => TopicCapability.RemovePost,
            TangentRoleCapabilities.PostReport => TopicCapability.ReportPost,
            _ => (TopicCapability?)null
        };
        if (capability is null) return Permit(room.PolicyRevision);
        var decision = TopicPermissionEvaluator.Evaluate(policy, capability.Value);
        return decision.Allowed ? Permit(room.PolicyRevision) : Deny(decision.Reason, room.PolicyRevision);
    }

    private static bool Ready(Room room) => room.SpaceState == RoomSpaceState.Local
        || room.SpaceState == RoomSpaceState.Ready && !string.IsNullOrWhiteSpace(room.SpaceUri);
    private static ScopedRoleGuardResult Permit(long version) => ScopedRoleGuardResult.Permit() with { VersionKey = "tangent-policy", Version = version };
    private static ScopedRoleGuardResult Deny(string code, long? version = null) => ScopedRoleGuardResult.Deny(code, "Tangent policy denied this capability.",
        version is null ? null : "tangent-policy", version);
}
