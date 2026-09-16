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
        var participantId = ParticipationAccess.Require(principal, ParticipationGrants.Read);
        var credential = CredentialOf(principal);
        var identity = await IdentityOf(participantId, ct);
        Rooms.Room? stored;
        Tangent? tangentRow;
        using (EntityContext.NoCache())
        {
            stored = await Room.Get(topicKey, ct);
            tangentRow = stored is null ? null : await Tangent.Get(stored.TangentKey, ct);
        }
        if (stored is null || tangentRow is null)
            return Problem("mentionables", identity, ServerPlace(principal, await SiteLabel(ct)),
                ExperienceProblem.Of(ExperienceProblemCodes.PermissionDenied, "That Topic is not visible to your account."),
                participantId: participantId, credential: credential, ct: ct);
        var policy = await conversation.ReadPolicy(participantId, topicKey, ct);
        if (!policy.CanRead) throw new UnauthorizedAccessException();

        var needle = (prefix ?? "").Trim().TrimStart('@').ToLowerInvariant();
        if (needle.Length > 64) needle = needle[..64];

        // Candidate participants: Tangent members, the authors of this Topic's recent
        // history, and the space owner — the people a mention here can plausibly mean.
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        IReadOnlyList<TangentMembership> members;
        using (EntityContext.NoCache())
        {
            members = await TangentMembership.Query(value => value.TangentKey == tangentRow.Id, ct);
            foreach (var membership in members) candidates.Add(membership.ParticipantId);
            var authors = await Message.Query(message => message.RoomKey == topicKey, RecentAuthors(), ct);
            foreach (var message in authors) candidates.Add(message.AuthorParticipantId);
            var space = await Site.Space.Get(TangentSpace.Infrastructure.TangentConstants.SpaceId, ct);
            if (space is { OwnerParticipantId.Length: > 0 }) candidates.Add(space.OwnerParticipantId);
        }
        candidates.Remove(participantId);

        var badges = members.ToDictionary(member => member.ParticipantId,
            member => member.Role switch { TangentRole.Admin => "admin", TangentRole.Reader => "reader", _ => "member" },
            StringComparer.Ordinal);
        using (EntityContext.NoCache())
        {
            if ((await Site.Space.Get(TangentSpace.Infrastructure.TangentConstants.SpaceId, ct)) is { } siteRow)
                badges[siteRow.OwnerParticipantId] = "owner";
        }

        var entries = new List<ExperienceMentionable>();
        using (EntityContext.NoCache())
        {
            var labels = await hub.Directory.LabelsFor(candidates, ct);
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
                    MembersOf(group, members)));
            }
        }

        var chosen = entries.OrderBy(entry => entry.Rank).ThenBy(entry => entry.Label, StringComparer.Ordinal)
            .Take(MentionablesLimit).ToList();
        return await Assemble("mentionables", ExperienceStatus.Ok, identity,
            await TopicPlaceOf(principal, participantId, tangentRow.Id, topicKey, ct),
            new ExperienceResult(new ExperienceMentionablesData(chosen, stored.Title), null, null),
            (await digest.Page(participantId, credential, null, tangentRow.Id, topicKey, 3, ct)).Attention,
            Empty(), [], null, null, participantId, credential, ct);
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
