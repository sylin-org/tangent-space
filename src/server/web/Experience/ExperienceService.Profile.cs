using System.Security.Claims;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sorting;
using Koan.Data.Core;
using TangentSpace.Communities;
using TangentSpace.Conversation;
using TangentSpace.Participants;
using TangentSpace.Participation;
using TangentSpace.Rooms;

namespace TangentSpace.Experience;
public sealed partial class ExperienceService
{
    public const int ProfilePostLimit = 25;

    /// <summary>The internal participant profile (ADR 0008): identity card, roles across
    /// Tangents, a bounded policy-filtered window of their posts, and the viewer's permitted
    /// operational actions. Every post is scoped to Topics the viewer may read.</summary>
    public async Task<ExperienceResponse> ParticipantProfile(ClaimsPrincipal principal, string identifier, CancellationToken ct)
    {
        var viewer = ParticipationAccess.Require(principal, ParticipationGrants.Read);
        var credential = CredentialOf(principal);
        var identity = await IdentityOf(viewer, ct);
        // Identifier resolution precedes policy: an unknown, stale, or ambiguous identifier is the
        // same honest miss regardless of form; the profile data stays keyed by the DID.
        (Participant Participant, string MatchedForm)? resolved;
        using (EntityContext.NoCache())
            resolved = await ParticipantLookup.TryResolveByIdentifier(identifier, ct);
        if (resolved is not { } match || match.Participant.IsSuspended && match.Participant.Id != viewer)
            return Problem("participant_profile", identity, ServerPlace(principal, await SiteLabel(ct)),
                ExperienceProblem.Of(ExperienceProblemCodes.PermissionDenied, "No participant is registered under that identity."),
                did: viewer, credential: credential, ct: ct);

        var participant = match.Participant;
        var did = participant.Id;
        var self = string.Equals(did, viewer, StringComparison.Ordinal);
        var roles = new List<ExperienceProfileRole>();
        using (EntityContext.NoCache())
        {
            var site = await Site.TangentSite.Get(TangentSpace.Infrastructure.TangentConstants.SiteId, ct);
            if (site?.IsOwner(did) == true) roles.Add(new("server", "", "This server", "owner"));
            foreach (var membership in await TangentMembership.Query(value => value.ParticipantDid == did, ct))
            {
                var tangent = await TangentCommunity.Get(membership.TangentKey, ct);
                roles.Add(new("tangent", membership.TangentKey,
                    tangent?.Name is { Length: > 0 } name ? name : membership.TangentKey,
                    membership.Role.ToString().ToLowerInvariant()));
            }
        }

        var posts = new List<ExperiencePostDto>();
        IReadOnlyList<Message> scanned;
        using (EntityContext.NoCache())
            scanned = await Message.Query(message => message.AuthorDid == did, RecentPosts(), ct);
        foreach (var message in scanned)
        {
            if (posts.Count >= ProfilePostLimit) break;
            var description = await rooms.Describe(viewer, message.RoomKey, ct);
            if (description is null || !description.CanRead) continue;
            string? replyTo = null;
            posts.Add(new ExperiencePostDto(
                refs.Message(description.TangentKey, message.RoomKey, message.Id), did,
                participant.Handle ?? did, message.Removed ? "" : message.Content.Text, replyTo,
                refs.Origin + "/tangents/" + description.TangentKey + "/posts/" + message.Id + "/",
                Format(message.AcceptedAt), message.EditedAt is { } edited ? Format(edited) : null, message.Removed,
                message.Facets));
        }

        // Permitted operational actions for this viewer, computed from real authority.
        var actions = new List<ExperienceAction>();
        if (self)
            actions.Add(new("declare_self", refs.ServerRef, null, "Declare your human/agent classification"));
        using (EntityContext.NoCache())
        {
            foreach (var tangent in roles.Where(role => role.Scope == "tangent").Take(5))
            {
                var canAppoint = await companions.CanAdminister(viewer, tangent.Key, null, ct);
                if (canAppoint && !self)
                    actions.Add(new("set_role", refs.Tangent(tangent.Key), null, $"Set {participant.Handle ?? did}'s role in {tangent.Label}"));
            }
        }

        return await Assemble("participant_profile", ExperienceStatus.Ok, identity,
            ServerPlace(principal, await SiteLabel(ct)),
            new ExperienceResult(new ExperienceProfileData(
                did, participant.Handle, participant.Classification.ToString(),
                Format(participant.JoinedAt), self, participant.IsSuspended, roles, posts,
                posts.Count >= ProfilePostLimit), null, null),
            (await digest.Page(viewer, credential, null, null, null, 3, ct)).Attention,
            Empty(), actions.Take(3).ToList(), null, null, viewer, credential, ct);
    }

    internal static QueryDefinition RecentPosts()
    {
        var member = typeof(Message).GetProperty(nameof(Message.AcceptedAt))!;
        return new QueryDefinition
        {
            Page = 1, PageSize = 200,
            Sort = [new SortSpec(new MemberPath(typeof(Message), [member], member.PropertyType, false, -1), true)],
        };
    }
}
