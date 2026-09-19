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

    /// <summary>One rung of the user-management ladder: the domain service applies it and the
    /// audit record names what happened. The ladder is idempotent by nature — a warn is a
    /// record, a suspension is a state — so it carries no request protocol.</summary>
    public async Task<ExperienceResponse> ManageUser(ClaimsPrincipal principal, string user,
        string action, string reason, long? durationSeconds, string? role, string? caseRef, CancellationToken ct)
    {
        var participantId = ParticipationAccess.Require(principal, ParticipationGrants.Read);
        var credential = CredentialOf(principal);
        var identity = await IdentityOf(participantId, ct);
        var outcome = await users.Manage(participantId, user, action, reason, durationSeconds, role, caseRef, ct);
        var data = new ExperienceUserManagementData(outcome.TargetParticipantId, outcome.Action, outcome.Reason,
            outcome.Role, outcome.Until,
            outcome.TangentKey is { Length: > 0 } tangentKey ? refs.Tangent(tangentKey) : null, outcome.CaseRef);
        var place = await ManagementPlaceOf(principal, participantId, caseRef, ct);
        return await Assemble("manage_user", ExperienceStatus.Ok, identity, place,
            new ExperienceResult(data, null, null),
            (await digest.Page(participantId, credential, null, null, null, 3, ct)).Attention,
            Empty(), [], null, new ExperienceCapabilities(true, false, true), participantId, credential, ct);
    }

    /// <summary>The envelope for a ladder act: the cited case's Topic when one exists, else the
    /// server carrying the rungs the actor's roles still hold — so the connector's authority
    /// readout stays fresh after every steward response.</summary>
    private async Task<ExperiencePlace> ManagementPlaceOf(ClaimsPrincipal principal, string participantId,
        string? caseRef, CancellationToken ct)
    {
        if (caseRef is { } reference && refs.ParseCase(reference) is { } scope)
            return await TopicPlaceOf(principal, participantId, scope.TangentKey, scope.TopicKey, ct);
        var allowed = new List<string> { ExperienceActionNames.ListTangents, "get_updates" };
        allowed.AddRange(await LadderRungs(participantId, ct));
        return new ExperiencePlace(refs.ServerRef, null, null, await SiteLabel(ct),
            await ServerRoleOf(participantId, ct), allowed);
    }

    private async Task<string> ServerRoleOf(string participantId, CancellationToken ct)
    {
        using var fresh = EntityContext.NoCache();
        var space = await Space.Get(TangentConstants.SpaceId, ct);
        return space?.IsOwner(participantId) == true ? "owner" : "participant";
    }

    /// <summary>The rung strings the actor's role bag covers, in ladder order. Emitted only from
    /// steward envelopes — the connector widens its manage enum from exactly these strings and
    /// narrows it the moment an envelope stops offering them.</summary>
    internal async Task<IReadOnlyList<string>> LadderRungs(string participantId, CancellationToken ct)
    {
        var bag = await hub.Roles.Bag(participantId, ct);
        var rungs = new List<string>(5);
        if (TangentRoleAccess.CanDo([TangentPermissions.WarnUsers], bag)) rungs.Add(ExperienceActionNames.WarnUser);
        if (TangentRoleAccess.CanDo([TangentPermissions.TimeoutUsers], bag)) rungs.Add(ExperienceActionNames.TimeoutUser);
        if (TangentRoleAccess.CanDo([TangentPermissions.ManageParticipants], bag)) rungs.Add(ExperienceActionNames.SuspendUser);
        if (TangentRoleAccess.CanDo([TangentPermissions.BanUsers], bag)) rungs.Add(ExperienceActionNames.BanUser);
        if (TangentRoleAccess.CanDo([TangentPermissions.ManageRoles], bag)) rungs.Add(ExperienceActionNames.AssignRoles);
        return rungs;
    }

    /// <summary>The participant read a persona uses to contextualize someone before acting:
    /// an identity card and a bounded, read-filtered window of their recent Posts — is today's
    /// outburst a pattern or a one-off?</summary>
    public async Task<ExperienceResponse> ReadParticipant(ClaimsPrincipal principal, string identifier, CancellationToken ct)
    {
        var viewer = ParticipationAccess.Require(principal, ParticipationGrants.Read);
        var credential = CredentialOf(principal);
        var identity = await IdentityOf(viewer, ct);
        (Participant Participant, string MatchedForm)? resolved;
        using (EntityContext.NoCache())
            resolved = await hub.Directory.ByIdentifier(identifier, ct);
        if (resolved is not { } match || match.Participant.IsSuspended && match.Participant.Id != viewer)
            return Problem("read_user", identity, ServerPlace(principal, await SiteLabel(ct)),
                ExperienceProblem.Of(ExperienceProblemCodes.PermissionDenied, "No participant is registered under that identity."),
                participantId: viewer, credential: credential, ct: ct);
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
            window.Posts.Select(view => view.Dto).ToList(), window.Resolved);
        var actions = window.Posts.Take(3)
            .Select(view => new ExperienceAction(ExperienceActionNames.ReadTopic,
                refs.Topic(view.TangentKey, view.TopicKey), view.Dto.Ref,
                "Read the conversation around their Post"))
            .ToList();
        return await Assemble("read_user", ExperienceStatus.Ok, identity,
            ServerPlace(principal, await SiteLabel(ct)),
            new ExperienceResult(data, null, null),
            (await digest.Page(viewer, credential, null, null, null, 3, ct)).Attention,
            Empty(), actions, null, null, viewer, credential, ct);
    }
}
