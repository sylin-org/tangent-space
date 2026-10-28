using System.Security.Claims;
using System.Text.Json;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sorting;
using Koan.Data.Core;
using Tangent.Community;
using Tangent.Conversation;
using Tangent.Activity;
using Tangent.Identity;
using Tangent.Stewardship;
using Tangent.Infrastructure;
using Tangent.Spaces;

namespace Tangent.Api;
public sealed partial class ExperienceService
{
    public const int ProfilePostLimit = 25;

    /// <summary>The internal participant profile (ADR 0008): identity card, roles across
    /// Tangents, a bounded policy-filtered window of their posts, and the viewer's permitted
    /// operational actions. Every post is scoped to Topics the viewer may read.</summary>
    public async Task<ResourceResult<ExperienceProfileData>> ParticipantProfile(ClaimsPrincipal principal, string identifier, CancellationToken ct)
    {
        var viewer = ParticipationAccess.Require(principal, ParticipationGrants.Read);
        // Identifier resolution precedes policy: an unknown, stale, or ambiguous identifier is the
        // same honest miss regardless of form; the profile data stays keyed by the participant id.
        (Participant Participant, string MatchedForm)? resolved;
        using (EntityContext.NoCache())
            resolved = await hub.Directory.ByIdentifier(identifier, ct);
        if (resolved is not { } match || match.Participant.IsSuspended && match.Participant.Id != viewer)
            return ResourceResult<ExperienceProfileData>.Failure(ExperienceProblem.Of(ExperienceProblemCodes.PermissionDenied, "No participant is registered under that identity."));

        var participant = match.Participant;
        var participantId = participant.Id;
        var self = string.Equals(participantId, viewer, StringComparison.Ordinal);
        var roles = new List<ExperienceProfileRole>();
        var label = await hub.Directory.LabelOf(participantId, ct);
        var did = await hub.Directory.AtprotoDidOf(participantId, ct);
        var profile = await hub.Profiles.Read(participantId, ct);
        using (EntityContext.NoCache())
        {
            var space = await Space.Get(TangentConstants.SpaceId, ct);
            if (space?.IsOwner(participantId) == true) roles.Add(new("server", "", "This server", "owner"));
            var bag = await hub.Roles.Bag(participantId, ct);
            foreach (var token in bag.Where(token => token.StartsWith("role:", StringComparison.Ordinal)))
            {
                var roleKey = token["role:".Length..];
                if (roles.Any(r => r.Scope == "server" && r.Role == roleKey)) continue;
                roles.Add(new("server", "", "This server", roleKey));
            }
            foreach (var membership in await TangentMembership.Query(value => value.ParticipantId == participantId, ct))
            {
                var tangent = await tangents.Describe(viewer, membership.TangentKey, ct);
                if (tangent is null) continue;
                roles.Add(new("tangent", membership.TangentKey,
                    tangent?.Name is { Length: > 0 } name ? name : membership.TangentKey,
                    membership.Role.ToString().ToLowerInvariant()));
            }
        }

        var window = await VisibleRecentPosts(viewer, participantId,
            profile.DisplayName ?? label ?? "Participant", ProfilePostLimit, ct);

        var roleManagement = new List<ExperienceProfileManagementLink>();
        using (EntityContext.NoCache())
        {
            foreach (var tangent in roles.Where(role => role.Scope == "tangent").Take(5))
            {
                var canAppoint = await participants.CanAdminister(viewer, tangent.Key, null, ct);
                var context = canAppoint ? await tangents.Describe(viewer, tangent.Key, ct) : null;
                var protectedTarget = context?.OwnerParticipantId == participantId || roles.Any(role => role.Scope == "server" && role.Role == "owner");
                if (canAppoint && !self && !protectedTarget && (tangent.Role != "admin" || context?.IsOwner == true))
                    roleManagement.Add(new(tangent.Key, $"Set {profile.DisplayName ?? label ?? "this participant"}'s role in {tangent.Label}"));
            }
        }

        return ResourceResult<ExperienceProfileData>.Success(new ExperienceProfileData(
                participantId, did, label, participant.Classification.ToString(),
                Format(participant.JoinedAt), self, participant.IsSuspended, roles,
                window.Posts.Select(view => view.Dto).ToList(), window.More, profile.DisplayName, profile.Description, profile.Avatar,
                "/u/" + Uri.EscapeDataString(did ?? ParticipantIdentity.InternalValue(participantId)),
                window.Resolved, roleManagement.Take(3).ToList()));
    }

    /// <summary>The bounded recent-posts window two readers share (the profile page and the
    /// persona's participant read): newest first, only through Topics the viewer may read,
    /// with the mention facets resolved for fresh labels.</summary>
    internal async Task<RecentPostWindow> VisibleRecentPosts(string viewer, string participantId,
        string authorName, int limit, CancellationToken ct)
    {
        var views = new List<RecentPostView>(limit);
        var bytes = 32 * 1024;
        var saturated = false;
        IReadOnlyList<Post> scanned;
        using (EntityContext.NoCache())
            scanned = await Post.Query(post => post.AuthorParticipantId == participantId, RecentPosts(), ct);
        var visible = new List<Post>(limit);
        foreach (var post in scanned)
        {
            if (views.Count >= limit) { saturated = true; break; }
            var description = await topics.Describe(viewer, post.TopicKey, ct);
            if (description is null || !description.CanRead) continue;
            var dto = new ExperiencePostDto(
                refs.Post(description.TangentKey, post.TopicKey, post.Id), participantId,
                authorName, post.Removed ? "" : post.Content.Text, null,
                refs.Origin + "/t/" + Uri.EscapeDataString(description.TangentKey) + "/" + Uri.EscapeDataString(post.Id),
                Format(post.AcceptedAt), post.EditedAt is { } edited ? Format(edited) : null, post.Removed,
                post.Facets, new ExperiencePostSource("tangent",
                    refs.Origin + "/tangents/" + description.TangentKey + "/posts/" + post.Id + "/", post.Id),
                post.Content.Format, post.Removed ? null : PostMarkup.Html(post.Content));
            var size = JsonSerializer.SerializeToUtf8Bytes(dto).Length + 1024;
            if (bytes + size > WindowPlanner.ResultBudget) { saturated = true; break; }
            bytes += size;
            visible.Add(post);
            views.Add(new(dto, description.TangentKey, post.TopicKey));
        }
        var resolved = await conversation.ResolveParticipants(visible, ct);
        return new(views, saturated || views.Count >= limit, resolved);
    }

    internal sealed record RecentPostWindow(IReadOnlyList<RecentPostView> Posts, bool More,
        IReadOnlyDictionary<string, ParticipantResolution>? Resolved);

    internal sealed record RecentPostView(ExperiencePostDto Dto, string TangentKey, string TopicKey);

    internal static QueryDefinition RecentPosts()
    {
        var member = typeof(Post).GetProperty(nameof(Post.AcceptedAt))!;
        return new QueryDefinition
        {
            Page = 1, PageSize = 200,
            Sort = [new SortSpec(new MemberPath(typeof(Post), [member], member.PropertyType, false, -1), true)],
        };
    }
}
