using System.Text.Json;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sorting;
using Koan.Data.Core;
using Tangent.Community;
using Tangent.Conversation;
using Tangent.Activity;
using Tangent.Infrastructure;
using Tangent.Application;
using Tangent.Identity;

namespace Tangent.Api;

/// <summary>The single bounded read model shared by anonymous JSON and HTML surfaces.</summary>
public sealed class PublicConversationReader(ParticipantDirectory directory, PolicyGate policyGate)
{
    internal const int MaximumPosts = 25;
    private const int ResultBudget = 64 * 1024;
    private const int EnvelopeReserve = 1024;

    public async Task<PublicPostWindow?> ReadTopic(string tangentId, string topicId, long? before, long? after,
        int limit, CancellationToken ct)
    {
        ValidateWindow(before, after, around: null, limit);
        await policyGate.Enter(ct);
        try
        {
            var topic = await ResolveTopicUnderGate(tangentId, topicId, ct);
            return topic is null ? null : await ReadPostsUnderGate(topic, before, after, around: null, limit, ct);
        }
        finally { policyGate.Exit(); }
    }

    public async Task<PublicPostDocument?> ReadPost(string tangentId, string postId, int limit, CancellationToken ct)
    {
        ValidateWindow(before: null, after: null, around: postId, limit);
        await policyGate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            var anchor = await Post.Get(postId, ct);
            if (anchor is null || anchor.OfPostId is not null || anchor.Sequence <= 0) return null;
            var topic = await ResolveTopicUnderGate(tangentId, anchor.TopicKey, ct);
            if (topic is null) return null;
            var window = await ReadPostsUnderGate(topic, before: null, after: null, postId, limit, ct, anchor);
            return window is not null && window.Posts.Any(post => post.Id == postId) ? new(postId, window) : null;
        }
        finally { policyGate.Exit(); }
    }

    internal async Task<PublicPostWindow?> ReadPostsUnderGate(PublicTopicDescription topic, long? before, long? after,
        string? around, int limit, CancellationToken ct, Post? knownAnchor = null)
    {
        ValidateWindow(before, after, around, limit);
        using var fresh = EntityContext.NoCache();
        IReadOnlyList<Post> candidates;
        if (around is not null)
        {
            var anchor = knownAnchor ?? await Post.Get(around, ct);
            if (anchor is null || anchor.TopicKey != topic.Key || anchor.OfPostId is not null || anchor.Sequence <= 0)
                return null;
            var beforeCount = limit / 2;
            var afterCount = limit - beforeCount - 1;
            var preceding = beforeCount == 0 ? [] : await Post.Query(
                post => post.TopicKey == topic.Key && post.OfPostId == null && post.Sequence < anchor.Sequence,
                MessageQuery(beforeCount, descending: true), ct);
            var following = afterCount == 0 ? [] : await Post.Query(
                post => post.TopicKey == topic.Key && post.OfPostId == null && post.Sequence > anchor.Sequence,
                MessageQuery(afterCount, descending: false), ct);
            candidates = preceding.OrderBy(post => post.Sequence).Append(anchor)
                .Concat(following.OrderBy(post => post.Sequence)).ToArray();
        }
        else
        {
            var descending = before is not null || after is null;
            var query = MessageQuery(limit + 1, descending);
            candidates = before is { } older
                ? await Post.Query(post => post.TopicKey == topic.Key && post.OfPostId == null && post.Sequence < older, query, ct)
                : after is { } newer
                    ? await Post.Query(post => post.TopicKey == topic.Key && post.OfPostId == null && post.Sequence > newer, query, ct)
                    : await Post.Query(post => post.TopicKey == topic.Key && post.OfPostId == null, query, ct);
            candidates = candidates.Take(limit).ToArray();
        }

        var traversal = candidates.Take(limit).ToArray();
        var authors = traversal.Select(post => post.AuthorParticipantId).Distinct(StringComparer.Ordinal).ToArray();
        var labels = await directory.LabelsFor(authors, ct, resolveMissing: false);
        var refs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var author in authors) refs[author] = await directory.PerennialValue(author, ct);

        var selected = new List<(long Sequence, PublicPostDescription Post)>(traversal.Length);
        var bytes = EnvelopeReserve + JsonSerializer.SerializeToUtf8Bytes(topic).Length;
        foreach (var post in traversal)
        {
            var authorRef = refs[post.AuthorParticipantId];
            var label = labels.GetValueOrDefault(post.AuthorParticipantId, authorRef);
            var rendered = new PublicPostDescription(post.Id, new(authorRef, label),
                post.Removed ? null : post.Content.Text, post.Content.CreatedAt, post.AcceptedAt,
                post.EditedAt, post.Removed,
                $"/t/{Uri.EscapeDataString(topic.TangentKey)}/{Uri.EscapeDataString(post.Id)}");
            var size = JsonSerializer.SerializeToUtf8Bytes(rendered).Length;
            if (bytes + size > ResultBudget) break;
            bytes += size;
            selected.Add((post.Sequence, rendered));
        }
        selected.Sort((left, right) => left.Sequence.CompareTo(right.Sequence));

        long? olderBefore = null;
        long? newerAfter = null;
        if (selected.Count > 0)
        {
            var first = selected[0].Sequence;
            var last = selected[^1].Sequence;
            if ((await Post.Query(post => post.TopicKey == topic.Key && post.OfPostId == null && post.Sequence < first,
                    MessageQuery(1, descending: true), ct)).Count > 0)
                olderBefore = first;
            if ((await Post.Query(post => post.TopicKey == topic.Key && post.OfPostId == null && post.Sequence > last,
                    MessageQuery(1, descending: false), ct)).Count > 0)
                newerAfter = last;
        }
        return new PublicPostWindow(topic, selected.Select(item => item.Post).ToArray(), olderBefore, newerAfter);
    }

    private static void ValidateWindow(long? before, long? after, string? around, int limit)
    {
        if (new[] { before is not null, after is not null, around is not null }.Count(value => value) > 1)
            throw new ArgumentException("Choose before, after, or around, not more than one.");
        if (before is <= 0 || after is < 0 || limit is < 1 or > MaximumPosts)
            throw new ArgumentException("Use before > 0, after >= 0, and a limit from 1 to 25.");
    }

    private static async Task<PublicTopicDescription?> ResolveTopicUnderGate(string tangentId, string topicId, CancellationToken ct)
    {
        using var fresh = EntityContext.NoCache();
        var topic = await Topic.Get(topicId, ct);
        if (topic is null || topic.TangentKey != tangentId || !PublicTopicAccess.IsPublic(topic)) return null;
        return await Community.Tangent.Get(tangentId, ct) is null ? null : PublicTopicDescription.From(topic);
    }

    private static QueryDefinition MessageQuery(int size, bool descending)
    {
        var sequence = typeof(Post).GetProperty(nameof(Post.Sequence))!;
        return new QueryDefinition { Page = 1, PageSize = size,
            Sort = [new SortSpec(new MemberPath(typeof(Post), [sequence], typeof(long), false, -1), descending)] };
    }
}

public sealed record PublicPostDocument(string AnchorId, PublicPostWindow Window);
