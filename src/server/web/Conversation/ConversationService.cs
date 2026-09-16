using Koan.Data.Core;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sorting;
using Microsoft.AspNetCore.DataProtection;
using TangentSpace.Participants;
using TangentSpace.Rooms;

namespace TangentSpace.Conversation;

public sealed partial class ConversationService(TopicGovernance governance, TimeProvider clock, IDataProtectionProvider protection,
    TangentSpace.Participants.ParticipantDirectory directory, ParticipantProfiles profiles) : IDisposable
{
    private readonly SemaphoreSlim writes = new(1, 1);
    private readonly IDataProtector cursors = protection.CreateProtector("Tangent.Conversation.Cursor.v1");

    public Task<TopicPolicy> ReadPolicy(string did, string topic, CancellationToken ct)
        => governance.WithCurrentPolicy(did, topic, (policy, _) =>
        {
            if (!policy.CanRead) throw new UnauthorizedAccessException("This topic's current rules do not allow reading.");
            return Task.FromResult(policy);
        }, ct);

    public void Dispose() => writes.Dispose();

    /// <summary>Read-time resolution for every author and facet-referenced participant in a
    /// page (ADR 0008): fresh labels for stable identity values, bounded to distinct targets.
    /// Authors resolve by participant id, facet targets by their perennial value; both live in
    /// one map because the two key spaces never collide.</summary>
    internal async Task<IReadOnlyDictionary<string, ParticipantResolution>?> ResolveParticipants(
        IReadOnlyList<Post> posts, CancellationToken ct)
    {
        var authors = posts.Select(post => post.AuthorParticipantId)
            .Distinct(StringComparer.Ordinal).Take(32).ToList();
        var targets = posts.Where(post => post.Facets is not null).SelectMany(post => post.Facets!)
            .Where(facet => facet.Kind == PostFacet.Mention && facet.Did is not null).Select(facet => facet.Did!)
            .Distinct(StringComparer.Ordinal).Take(32).ToList();
        if (authors.Count == 0 && targets.Count == 0) return null;
        var resolved = new Dictionary<string, ParticipantResolution>(StringComparer.Ordinal);
        var labelKeys = new List<string>(authors);
        var holders = new Dictionary<string, Participant>(StringComparer.Ordinal);
        foreach (var target in targets)
            if (await ResolveHolder(target, ct) is { } holder && holders.TryAdd(target, holder))
                labelKeys.Add(holder.Id);
        var presentations = (await Task.WhenAll(labelKeys.Distinct(StringComparer.Ordinal).Select(async id =>
            (Id: id, Profile: await profiles.Read(id, ct)))))
            .ToDictionary(value => value.Id, value => value.Profile, StringComparer.Ordinal);
        foreach (var author in authors)
        {
            var participant = await Participant.Get(author, ct);
            if (participant is null) continue;
            var profile = presentations[author];
            resolved[author] = Present(profile, participant.Classification.ToString(), profile.Did);
        }
        foreach (var target in targets)
        {
            if (resolved.ContainsKey(target) || !holders.TryGetValue(target, out var holder)) continue;
            resolved[target] = Present(presentations[holder.Id], holder.Classification.ToString(), target);
        }
        return resolved;
    }

    private static ParticipantResolution Present(ParticipantProfile profile, string classification, string value)
        => new(profile.Handle is { Length: > 0 and <= 253 } handle ? handle : null,
            profile.DisplayName, classification, value, profile.Avatar, "/u/" + Uri.EscapeDataString(value));

    /// <summary>One facet target's current holder: an atproto DID through the identity row, a
    /// tangent:local value through the participant it derives from. Misses are honest.</summary>
    private async Task<Participant?> ResolveHolder(string target, CancellationToken ct)
    {
        if (target.StartsWith("did:", StringComparison.Ordinal)) return await directory.ByDid(target, ct);
        if (target.StartsWith(Participants.ParticipantLookup.LocalPrefix, StringComparison.Ordinal))
        {
            var participant = await Participant.Get(target[Participants.ParticipantLookup.LocalPrefix.Length..], ct);
            return participant is null ? null : await directory.ByInternal(participant.Id, ct);
        }
        return null;
    }


    private static QueryDefinition Window<T>(string property, int page, int size)
    {
        var member = typeof(T).GetProperty(property)!;
        return new QueryDefinition { Page = page, PageSize = size,
            Sort = [new SortSpec(new MemberPath(typeof(T), [member], member.PropertyType, false, -1), false)] };
    }
}
