using System.Security.Claims;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sorting;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core.Sorting;
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
    public const int MentionablesLimit = 10;

    /// <summary>The mentionable targets in one Topic's scope for the autocomplete picker
    /// (ADR 0008): participants ranked by prefix relevance, plus the dynamic role groups
    /// with live member counts. Everything is policy-filtered to what this actor may see.</summary>
    public async Task<ResourceResult<ExperienceMentionablesData>> Mentionables(ClaimsPrincipal principal, string topicKey, string? prefix, CancellationToken ct)
    {
        var participantId = ParticipationAccess.Require(principal, ParticipationGrants.Read);
        Topic? stored;
        Community.Tangent? tangentRow;
        using (EntityContext.NoCache())
        {
            stored = await Topic.Get(topicKey, ct);
            tangentRow = stored is null ? null : await Community.Tangent.Get(stored.TangentKey, ct);
        }
        if (stored is null || tangentRow is null)
            return ResourceResult<ExperienceMentionablesData>.Failure(ExperienceProblem.Of(ExperienceProblemCodes.PermissionDenied, "That Topic is not visible to your account."));
        var policy = await conversation.ReadPolicy(participantId, topicKey, ct);
        if (!policy.CanRead) throw new UnauthorizedAccessException();

        var needle = (prefix ?? "").Trim().TrimStart('@').ToLowerInvariant();
        if (needle.Length > 64) needle = needle[..64];

        // Suggestions use bounded recent authors and native identity-prefix queries. A known
        // exact handle remains reachable even when many other handles share its prefix.
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        var badges = new Dictionary<string, string>(StringComparer.Ordinal);
        using (EntityContext.NoCache())
        {
            var authors = await Post.Query(post => post.TopicKey == topicKey && post.OfPostId == null, RecentAuthors(), ct);
            foreach (var post in authors) candidates.Add(post.AuthorParticipantId);
            var memberQuery = new QueryDefinition
            {
                Page = 1, PageSize = 100, Sort = SortSpecParser.ParseStrict<TangentMembership>(nameof(TangentMembership.Id))
            };
            if (needle.Length == 0)
            {
                foreach (var member in await TangentMembership.Query(value => value.TangentKey == tangentRow.Id, memberQuery, ct))
                    candidates.Add(member.ParticipantId);
            }
            else
            {
                var exact = await ParticipantLookup.TryResolveByIdentifier(needle, ct);
                if (exact is { } match) candidates.Add(match.Participant.Id);
                var identityQuery = QueryDefinition.All.Where(Filter.On(FieldPath.Of(nameof(ParticipantIdentity.LabelKey)),
                    FilterOperator.StartsWith, FilterValue.Of(needle))) with
                {
                    Page = 1, PageSize = 100,
                    Sort = SortSpecParser.ParseStrict<ParticipantIdentity>(nameof(ParticipantIdentity.LabelKey) + "," + nameof(ParticipantIdentity.Id))
                };
                foreach (var identity in await ParticipantIdentity.All(identityQuery, ct))
                    if (await TangentMembership.Get(TangentMembership.Key(tangentRow.Id, identity.ParticipantId), ct) is not null)
                        candidates.Add(identity.ParticipantId);
                // The direct identity candidate must belong to this conversation's community or history.
                if (exact is { } direct && !authors.Any(post => post.AuthorParticipantId == direct.Participant.Id)
                    && !tangentRow.IsOwner(direct.Participant.Id)
                    && await TangentMembership.Get(TangentMembership.Key(tangentRow.Id, direct.Participant.Id), ct) is null)
                    candidates.Remove(direct.Participant.Id);
            }
            candidates.Add(tangentRow.OwnerParticipantId);
            foreach (var id in candidates)
                if (await TangentMembership.Get(TangentMembership.Key(tangentRow.Id, id), ct) is { } membership)
                    badges[id] = membership.Role switch { TangentRole.Admin => "admin", TangentRole.Reader => "reader", _ => "member" };
            if ((await Space.Get(TangentConstants.SpaceId, ct)) is { OwnerParticipantId.Length: > 0 } site)
            {
                candidates.Add(site.OwnerParticipantId);
                badges[site.OwnerParticipantId] = "owner";
            }
        }
        candidates.Remove(participantId);

        var entries = new List<ExperienceMentionable>();
        using (EntityContext.NoCache())
        {
            var labels = await hub.Directory.LabelsFor(candidates, ct, resolveMissing: false);
            foreach (var candidate in candidates)
            {
                var participant = await Participant.Get(candidate, ct);
                if (participant is null || participant.IsSuspended) continue;
                var label = labels.GetValueOrDefault(candidate) ?? "";
                var rank = Rank(needle, label, participant.Id);
                if (rank is null) continue;
                // The mention target is the perennial identity value: the DID when held, else the internal DID.
                entries.Add(new ExperienceMentionable("participant", label.Length > 0 ? label : participant.Id,
                    label.Length > 0 ? label : participant.Id, await hub.Directory.PerennialValue(candidate, ct),
                    participant.Classification.ToString(), badges.GetValueOrDefault(candidate), rank.Value));
            }
        }

        // Dynamic role groups with live counts; a participant handle that matches a group
        // spelling exactly outranks (and effectively suppresses) the group entry.
        var exactHandleCollision = entries.Any(entry => entry.Kind == "participant"
            && string.Equals(entry.Label, needle, StringComparison.OrdinalIgnoreCase) && needle.Length > 0);
        if (!exactHandleCollision)
        {
            foreach (var group in Conversation.PostFacet.Groups)
            {
                var rank = Rank(needle, group, group);
                if (rank is null) continue;
                entries.Add(new ExperienceMentionable("group", char.ToUpperInvariant(group[0]) + group[1..], group,
                    null, null, null, rank.Value,
                    await MembersOf(group, tangentRow.Id, ct)));
            }
        }

        var chosen = entries.OrderBy(entry => entry.Rank).ThenBy(entry => entry.Label, StringComparer.Ordinal)
            .Take(MentionablesLimit).ToList();
        return ResourceResult<ExperienceMentionablesData>.Success(new ExperienceMentionablesData(chosen, stored.Title));
    }

    private static int? Rank(string needle, string handle, string participantId)
    {
        if (needle.Length == 0) return 50;
        var lowered = handle.ToLowerInvariant();
        if (lowered.StartsWith(needle, StringComparison.Ordinal)) return lowered.Length == needle.Length ? 0 : 10;
        if (participantId.StartsWith(needle, StringComparison.Ordinal)) return 20;
        if (lowered.Contains(needle, StringComparison.Ordinal)) return 30;
        return null;
    }

    private static async Task<int> MembersOf(string group, string tangentKey, CancellationToken ct)
    {
        var administrators = group is "admins" or "moderators";
        var count = administrators
            ? await TangentMembership.Count.Where(member => member.TangentKey == tangentKey && member.Role == TangentRole.Admin, CountStrategy.Exact, ct)
            : await TangentMembership.Count.Where(member => member.TangentKey == tangentKey, CountStrategy.Exact, ct);
        return (int)Math.Min(int.MaxValue, count + (administrators ? 1 : 0));
    }

    internal static QueryDefinition RecentAuthors()
    {
        var member = typeof(Post).GetProperty(nameof(Post.Sequence))!;
        return new QueryDefinition
        {
            Page = 1, PageSize = 100,
            Sort = [new SortSpec(new MemberPath(typeof(Post), [member], member.PropertyType, false, -1), true)],
        };
    }
}
