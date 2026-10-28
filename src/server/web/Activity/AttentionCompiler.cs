using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sorting;
using Koan.Data.Core;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Community;
using Tangent.Conversation;
using Tangent.Infrastructure;
using Tangent.Spaces;

namespace Tangent.Activity;

/// <summary>Recomputes one Topic's attention projection for every candidate reader, from current
/// posts under current policy. The compiler is the only writer of <see cref="TopicAttention"/>
/// rows: it derives each reader's numbers exactly as the read paths would, so a compiled row and
/// a fresh computation agree. Runs recompute from truth rather than incrementing, which makes a
/// re-delivered compile harmless.</summary>
internal static class AttentionCompiler
{
    private static readonly QueryDefinition RecentPosts = Window<Post>(nameof(Post.Sequence), ExperienceDigest.MaximumUnread + 1, descending: true);

    /// <summary>Compiles every candidate reader's row for one Topic. Rows for readers who lost
    /// access or muted the Topic are removed: storing nothing for an undeliverable topic is the
    /// honest compiled state.</summary>
    internal static async Task CompileTopic(string topicKey, IServiceProvider services, CancellationToken ct)
    {
        var governance = services.GetRequiredService<TopicGovernance>();
        var clock = services.GetRequiredService<TimeProvider>();
        using var fresh = EntityContext.NoCache();
        var topic = await Topic.Get(topicKey, ct);
        if (topic is null) return;
        var tangent = await Community.Tangent.Get(topic.TangentKey, ct);
        var space = await Space.Get(TangentConstants.SpaceId, ct);
        var state = await TopicConversation.Get(topicKey, ct) ?? new TopicConversation { Id = topicKey };

        // One bounded window serves every reader: each reader's unread slice is the part of
        // this window above their acknowledged sequence.
        var posts = (await Post.Query(post => post.TopicKey == topicKey, RecentPosts, ct))
            .OrderBy(post => post.Sequence).ToList();
        var last = state.LastSequence == 0 ? null : posts.LastOrDefault(post => post.Sequence == state.LastSequence);
        var parents = new Dictionary<string, Post>(StringComparer.Ordinal);
        foreach (var parentId in posts.Select(post => post.Content.ReplyTo)
                     .Where(id => id is not null).Distinct(StringComparer.Ordinal))
        {
            var parent = await Post.Get(parentId!, ct);
            if (parent is not null && parent.TopicKey == topicKey) parents[parentId!] = parent;
        }

        var rows = new List<TopicAttention>();
        var stale = new List<string>();
        foreach (var readerId in await CandidateReaders(space, tangent, topic, ct))
        {
            ct.ThrowIfCancellationRequested();
            var read = await governance.ReadAsync(readerId, topicKey, ct);
            var mode = read.Policy.CanRead
                ? AttentionRules.Effective(
                    await WatchSetting.Get(WatchSetting.Key(readerId, topicKey), ct),
                    await TangentWatchSetting.Get(TangentWatchSetting.Key(readerId, topic.TangentKey), ct))
                : WatchMode.None;
            if (!AttentionRules.DeliversTopic(mode))
            {
                stale.Add(TopicAttention.IdOf(topicKey, readerId));
                continue;
            }
            rows.Add(await CompileReader(readerId, topicKey, state, posts, parents, last, clock.GetUtcNow(), ct));
        }

        // An empty write set commits nothing: topics with no readers or nothing to change
        // skip the transaction entirely (Koan rejects a zero-operation commit).
        if (rows.Count == 0 && stale.Count == 0) return;
        using var transaction = EntityContext.Transaction(TopicConstants.AcceptanceTransaction);
        foreach (var row in rows) await row.Save(ct);
        foreach (var id in stale) await TopicAttention.Remove(id, ct);
        await EntityContext.Commit(ct);
    }

    /// <summary>One reader's attention numbers, the same computation the read paths make:
    /// the viewer's own writes never count as unread, and only direct replies to the viewer's
    /// accepted posts count as replies.</summary>
    private static async Task<TopicAttention> CompileReader(string readerId, string topicKey,
        TopicConversation state, List<Post> posts, Dictionary<string, Post> parents, Post? last,
        DateTimeOffset now, CancellationToken ct)
    {
        var position = await ReadPosition.Get(ReadPosition.Key(readerId, topicKey), ct);
        var readSequence = Math.Min(position?.Sequence ?? 0, state.LastSequence);
        var unread = posts.Where(post => post.Sequence > readSequence).ToList();
        return new TopicAttention
        {
            Id = TopicAttention.IdOf(topicKey, readerId),
            TopicKey = topicKey,
            ParticipantId = readerId,
            UnreadCount = Math.Min(unread.Count(post => AttentionRules.CountsForAttention(post, readerId)), ExperienceDigest.MaximumUnread),
            DirectReplies = Math.Min(DirectReplies(readerId, unread, parents), ExperienceDigest.MaximumUnread),
            LastSequence = state.LastSequence,
            ReadSequence = readSequence,
            LastPostAt = last?.AcceptedAt,
            CompiledAt = now,
        };
    }

    private static int DirectReplies(string readerId, List<Post> unread, Dictionary<string, Post> parents)
    {
        var replies = 0;
        foreach (var post in unread.Take(ExperienceDigest.MaximumUnread))
        {
            if (post.Content.ReplyTo is not { } parentId) continue;
            if (parents.TryGetValue(parentId, out var parent)
                && AttentionRules.IsDirectReply(post, parent, readerId)) replies++;
        }
        return replies;
    }

    /// <summary>Everyone whose own policy could deliver this Topic: tangent members, topic
    /// members, explicit followers, the tangent owner, the space owner and the topic creator.</summary>
    private static async Task<IReadOnlyList<string>> CandidateReaders(Space? space, Community.Tangent? tangent,
        Topic topic, CancellationToken ct)
    {
        var readers = new HashSet<string>(StringComparer.Ordinal);
        if (tangent is not null)
        {
            foreach (var membership in await TangentMembership.Query(m => m.TangentKey == tangent.Id, ct))
                readers.Add(membership.ParticipantId);
            if (!string.IsNullOrEmpty(tangent.OwnerParticipantId)) readers.Add(tangent.OwnerParticipantId);
        }
        foreach (var membership in await TopicMembership.Query(m => m.TopicKey == topic.Id, ct))
            readers.Add(membership.ParticipantId);
        for (var page = 1; ; page++)
        {
            var watches = await WatchSetting.Query(watch => watch.TopicKey == topic.Id,
                Window<WatchSetting>(nameof(WatchSetting.Id), 100).WithPagination(page, 100), ct);
            foreach (var watch in watches) readers.Add(watch.ParticipantId);
            if (watches.Count < 100) break;
        }
        for (var page = 1; ; page++)
        {
            var watches = await TangentWatchSetting.Query(watch => watch.TangentKey == topic.TangentKey,
                Window<TangentWatchSetting>(nameof(TangentWatchSetting.Id), 100).WithPagination(page, 100), ct);
            foreach (var watch in watches) readers.Add(watch.ParticipantId);
            if (watches.Count < 100) break;
        }
        if (space?.OwnerParticipantId is { } owner && owner.Length > 0) readers.Add(owner);
        if (!string.IsNullOrEmpty(topic.CreatorParticipantId)) readers.Add(topic.CreatorParticipantId);
        return [.. readers];
    }

    private static QueryDefinition Window<T>(string property, int size, bool descending = false)
    {
        var member = typeof(T).GetProperty(property)!;
        return new QueryDefinition
        {
            Page = 1, PageSize = size,
            Sort = [new SortSpec(new MemberPath(typeof(T), [member], member.PropertyType, false, -1), descending)]
        };
    }
}
