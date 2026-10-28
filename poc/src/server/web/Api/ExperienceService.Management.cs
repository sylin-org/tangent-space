using System.Security.Claims;
using Koan.Data.Core;
using Tangent.Access;
using Tangent.Community;
using Tangent.Conversation;
using Tangent.Identity;
using Tangent.Infrastructure;
using Tangent.Spaces;
using Tangent.Stewardship;

namespace Tangent.Api;

public sealed partial class ExperienceService
{
    public const int ReadUserPostLimit = 10;

    /// <summary>Apply a participant-management action under current domain authority.</summary>
    public async Task<ResourceResult<ExperienceUserManagementData>> ManageUser(ClaimsPrincipal principal, string user,
        string action, string reason, long? durationSeconds, string? role, string? caseRef, CancellationToken ct)
    {
        var participantId = ParticipationAccess.Require(principal, ParticipationGrants.Manage);
        var outcome = await users.Manage(participantId, user, action, reason, durationSeconds, role, caseRef, ct);
        var data = new ExperienceUserManagementData(outcome.TargetParticipantId, outcome.Action, outcome.Reason,
            outcome.Role, outcome.Until,
            outcome.TangentKey is { Length: > 0 } tangentKey ? refs.Tangent(tangentKey) : null, outcome.CaseRef);
        return ResourceResult<ExperienceUserManagementData>.Success(data);
    }

    /// <summary>Current server management authority, intersected with the active credential.</summary>
    internal async Task<ExperienceManagementAuthority> ManagementAuthorityOf(
        ClaimsPrincipal principal, string participantId, CancellationToken ct)
    {
        var bag = await hub.Roles.Bag(participantId, ct);
        var grant = TransportGrant(principal, ParticipationGrants.Manage);
        return new ExperienceManagementAuthority(
            grant && TangentRoleAccess.CanDo([TangentPermissions.WarnUsers], bag),
            grant && TangentRoleAccess.CanDo([TangentPermissions.TimeoutUsers], bag),
            grant && TangentRoleAccess.CanDo([TangentPermissions.ManageParticipants], bag),
            grant && TangentRoleAccess.CanDo([TangentPermissions.BanUsers], bag),
            grant && TangentRoleAccess.CanDo([TangentPermissions.ManageRoles], bag));
    }

    /// <summary>The participant read a persona uses to contextualize someone before acting:
    /// an identity card and a bounded, read-filtered window of their recent Posts — is today's
    /// outburst a pattern or a one-off?</summary>
    public async Task<ResourceResult<ExperienceReadUserData>> ReadParticipant(ClaimsPrincipal principal, string identifier, CancellationToken ct)
    {
        var viewer = ParticipationAccess.Require(principal, ParticipationGrants.Read);
        (Participant Participant, string MatchedForm)? resolved;
        using (EntityContext.NoCache())
            resolved = await hub.Directory.ByIdentifier(identifier, ct);
        if (resolved is not { } match || match.Participant.IsSuspended && match.Participant.Id != viewer)
            return ResourceResult<ExperienceReadUserData>.Failure(ExperienceProblem.Of(ExperienceProblemCodes.PermissionDenied, "No participant is registered under that identity."));
        var participant = match.Participant;
        var label = await hub.Directory.LabelOf(participant.Id, ct);
        var did = await hub.Directory.AtprotoDidOf(participant.Id, ct);
        var profile = await hub.Profiles.Read(participant.Id, ct);
        var displayName = profile.DisplayName ?? label ?? participant.Id;
        var window = await VisibleRecentPosts(viewer, participant.Id, displayName, ReadUserPostLimit, ct);
        var data = new ExperienceReadUserData(
            new ExperienceReadUserProfile(participant.Id, did, label, displayName,
                participant.Classification.ToString(), profile.Description, profile.Avatar,
                "/u/" + Uri.EscapeDataString(did ?? ParticipantIdentity.InternalValue(participant.Id))),
            window.Posts.Select(view => view.Dto).ToList(), window.More, window.Resolved);
        return ResourceResult<ExperienceReadUserData>.Success(data);
    }
}
