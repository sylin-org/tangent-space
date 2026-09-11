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
    public const int MentionablesLimit = 10;

    /// <summary>The mentionable targets in one Topic's scope for the autocomplete picker
    /// (ADR 0008): participants ranked by prefix relevance, plus the dynamic role groups
    /// with live member counts. Everything is policy-filtered to what this actor may see.</summary>
    public async Task<ExperienceResponse> Mentionables(ClaimsPrincipal principal, string topicKey, string? prefix, CancellationToken ct)
    {
        var did = ParticipationAccess.Require(principal, ParticipationGrants.Read);
        var credential = CredentialOf(principal);
        var identity = await IdentityOf(did, ct);
        Rooms.Room? stored;
        TangentCommunity? tangentRow;
        using (EntityContext.NoCache())
        {
            stored = await Room.Get(topicKey, ct);
            tangentRow = stored is null ? null : await TangentCommunity.Get(stored.TangentKey, ct);
        }
        if (stored is null || tangentRow is null)
            return Problem("mentionables", identity, ServerPlace(principal, await SiteLabel(ct)),
                ExperienceProblem.Of(ExperienceProblemCodes.PermissionDenied, "That Topic is not visible to your account."),
                did: did, credential: credential, ct: ct);
        var policy = await conversation.ReadPolicy(did, topicKey, ct);
        if (!policy.CanRead) throw new UnauthorizedAccessException();

        var needle = (prefix ?? "").Trim().TrimStart('@').ToLowerInvariant();
        if (needle.Length > 64) needle = needle[..64];

        // Candidate participants: Tangent members, the authors of this Topic's recent
        // history, and the site owner — the people a mention here can plausibly mean.
        var candidateDids = new HashSet<string>(StringComparer.Ordinal);
        IReadOnlyList<TangentMembership> members;
        using (EntityContext.NoCache())
        {
            members = await TangentMembership.Query(value => value.TangentKey == tangentRow.Id, ct);
            foreach (var membership in members) candidateDids.Add(membership.ParticipantDid);
            var authors = await Message.Query(message => message.RoomKey == topicKey, RecentAuthors(), ct);
            foreach (var message in authors) candidateDids.Add(message.AuthorDid);
            var site = await Site.TangentSite.Get(TangentSpace.Infrastructure.TangentConstants.SiteId, ct);
            if (site is { OwnerDid.Length: > 0 }) candidateDids.Add(site.OwnerDid);
        }
        candidateDids.Remove(did);

        var badges = members.ToDictionary(member => member.ParticipantDid,
            member => member.Role switch { TangentRole.Admin => "admin", TangentRole.Reader => "reader", _ => "member" },
            StringComparer.Ordinal);
        using (EntityContext.NoCache())
        {
            if ((await Site.TangentSite.Get(TangentSpace.Infrastructure.TangentConstants.SiteId, ct)) is { } siteRow)
                badges[siteRow.OwnerDid] = "owner";
        }

        var entries = new List<ExperienceMentionable>();
        using (EntityContext.NoCache())
        {
            foreach (var candidate in candidateDids)
            {
                var participant = await Participant.Get(candidate, ct);
                if (participant is null || participant.IsSuspended) continue;
                var handle = participant.Handle ?? "";
                var rank = Rank(needle, handle, participant.Id);
                if (rank is null) continue;
                entries.Add(new ExperienceMentionable("participant", handle.Length > 0 ? handle : participant.Id,
                    LabelOf(participant), participant.Id, participant.Classification.ToString(),
                    badges.GetValueOrDefault(candidate), rank.Value));
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
                    MembersOf(group, members)));
            }
        }

        var chosen = entries.OrderBy(entry => entry.Rank).ThenBy(entry => entry.Label, StringComparer.Ordinal)
            .Take(MentionablesLimit).ToList();
        return await Assemble("mentionables", ExperienceStatus.Ok, identity,
            await TopicPlaceOf(principal, did, tangentRow.Id, topicKey, ct),
            new ExperienceResult(new ExperienceMentionablesData(chosen, stored.Title), null, null),
            (await digest.Page(did, credential, null, tangentRow.Id, topicKey, 3, ct)).Attention,
            Empty(), [], null, null, did, credential, ct);
    }

    private static int? Rank(string needle, string handle, string did)
    {
        if (needle.Length == 0) return 50;
        var lowered = handle.ToLowerInvariant();
        if (lowered.StartsWith(needle, StringComparison.Ordinal)) return lowered.Length == needle.Length ? 0 : 10;
        if (did.StartsWith(needle, StringComparison.Ordinal)) return 20;
        if (lowered.Contains(needle, StringComparison.Ordinal)) return 30;
        return null;
    }

    private static string LabelOf(Participant participant)
        => participant.Handle is { Length: > 0 } handle ? handle : participant.Id;

    private static int MembersOf(string group, IReadOnlyList<TangentMembership> members) => group switch
    {
        "admins" => members.Count(member => member.Role == TangentRole.Admin) + 1,
        "moderators" => members.Count(member => member.Role == TangentRole.Admin) + 1,
        "members" => members.Count,
        _ => 0,
    };

    internal static QueryDefinition RecentAuthors()
    {
        var member = typeof(Message).GetProperty(nameof(Message.Sequence))!;
        return new QueryDefinition
        {
            Page = 1, PageSize = 100,
            Sort = [new SortSpec(new MemberPath(typeof(Message), [member], member.PropertyType, false, -1), true)],
        };
    }
}
