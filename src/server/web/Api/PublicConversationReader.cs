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
using Tangent.Access;

namespace Tangent.Api;

/// <summary>The single bounded read model shared by anonymous JSON and HTML surfaces.</summary>
public sealed class PublicConversationReader(ParticipantDirectory directory)
{
    internal const int MaximumPosts = 25;
    private const int ResultBudget = 1024 * 1024;
    private const int EnvelopeReserve = 1024;

    public async Task<PublicPostWindow?> ReadTopic(string tangentId, string topicId, string? before, string? after,
        int limit, CancellationToken ct)
    {
        ValidateWindow(before, after, around: null, limit);
        var topic = await ResolveTopic(tangentId, topicId, ct);
        return topic is null ? null : await ReadPosts(topic, before, after, around: null, limit, ct);
    }

    public async Task<PublicPostDocument?> ReadPost(string tangentId, string postId, int limit, CancellationToken ct)
    {
        ValidateWindow(before: null, after: null, around: postId, limit);
        using var fresh = EntityContext.NoCache();
        var anchor = await Post.Get(postId, ct);
        if (anchor is null || anchor.OfPostId is not null || anchor.Sequence <= 0) return null;
        var topic = await ResolveTopic(tangentId, anchor.TopicKey, ct);
        if (topic is null) return null;
        var window = await ReadPosts(topic, before: null, after: null, postId, limit, ct, anchor);
        return window is not null && window.Posts.Any(post => post.Id == postId) ? new(postId, window) : null;
    }

    internal async Task<PublicPostWindow?> ReadPosts(PublicTopicDescription topic, string? before, string? after,
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
            var beforeSequence = await EdgeSequence(topic.Key, before, ct);
            var afterSequence = await EdgeSequence(topic.Key, after, ct);
            var descending = before is not null || after is null;
            var query = MessageQuery(limit + 1, descending);
            candidates = beforeSequence is { } older
                ? await Post.Query(post => post.TopicKey == topic.Key && post.OfPostId == null && post.Sequence < older, query, ct)
                : afterSequence is { } newer
                    ? await Post.Query(post => post.TopicKey == topic.Key && post.OfPostId == null && post.Sequence > newer, query, ct)
                    : await Post.Query(post => post.TopicKey == topic.Key && post.OfPostId == null, query, ct);
            candidates = candidates.Take(limit).ToArray();
        }

        var traversal = candidates.Take(limit).ToArray();
        var opening = topic.Presentation == TopicPresentation.Article
            ? (await Post.Query(post => post.TopicKey == topic.Key && post.OfPostId == null && post.Sequence == 1,
                MessageQuery(1, descending: false), ct)).FirstOrDefault()
            : null;
        if (opening?.Content.ReplyTo is not null) opening = null;
        var authors = traversal.Concat(opening is null ? [] : [opening])
            .Select(post => post.AuthorParticipantId).Distinct(StringComparer.Ordinal).ToArray();
        var labels = new Dictionary<string, string>(await directory.LabelsFor(authors, ct, resolveMissing: false), StringComparer.Ordinal);
        var refs = new Dictionary<string, string>(StringComparer.Ordinal);
        var classifications = new Dictionary<string, string>(StringComparer.Ordinal);
        var avatars = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var author in authors)
        {
            refs[author] = await directory.PerennialValue(author, ct);
            classifications[author] = (await Participant.Get(author,ct))?.Classification.ToString() ?? "Undeclared";
            var profile = await ParticipantProfileSnapshot.Get(refs[author], ct);
            avatars[author] = profile?.Present().Avatar;
            if (profile?.DisplayName is { Length: > 0 } displayName) labels[author] = displayName;
        }

        var selected = new List<(long Sequence, PublicPostDescription Post)>(traversal.Length);
        var bytes = EnvelopeReserve + JsonSerializer.SerializeToUtf8Bytes(topic).Length;
        PublicPostDescription Project(Post post)
        {
            var authorRef = refs[post.AuthorParticipantId];
            var label = labels.GetValueOrDefault(post.AuthorParticipantId, authorRef);
            return new PublicPostDescription(post.Id, new(authorRef, label, classifications[post.AuthorParticipantId],
                avatars.GetValueOrDefault(post.AuthorParticipantId), "/u/" + Uri.EscapeDataString(authorRef)),
                post.Removed ? null : post.Content.Text, post.Content.CreatedAt, post.AcceptedAt,
                post.EditedAt, post.Removed,
                $"/t/{Uri.EscapeDataString(topic.TangentKey)}/{Uri.EscapeDataString(post.Id)}",
                post.Content.Format, post.Removed ? null : PostMarkup.Html(post.Content), post.Removed ? null : post.SourceUri);
        }
        bool Include(Post post)
        {
            var rendered = Project(post);
            var size = JsonSerializer.SerializeToUtf8Bytes(rendered).Length;
            if (bytes + size > ResultBudget) return false;
            bytes += size;
            selected.Add((post.Sequence, rendered));
            return true;
        }
        if (around is null)
        {
            foreach (var post in traversal)
                if (!Include(post)) break;
        }
        else
        {
            var anchorIndex = Array.FindIndex(traversal, post => post.Id == around);
            if (anchorIndex < 0 || !Include(traversal[anchorIndex]))
                throw new ArgumentException("The requested Post cannot fit in a public reading window.");
            var left = anchorIndex - 1;
            var right = anchorIndex + 1;
            while (left >= 0 || right < traversal.Length)
            {
                // Keep context contiguous: a neighbor that does not fit ends that side.
                if (left >= 0) left = Include(traversal[left]) ? left - 1 : -1;
                if (right < traversal.Length) right = Include(traversal[right]) ? right + 1 : traversal.Length;
            }
        }
        selected.Sort((left, right) => left.Sequence.CompareTo(right.Sequence));

        string? olderBefore = null;
        string? newerAfter = null;
        if (selected.Count > 0)
        {
            var first = selected[0].Sequence;
            var last = selected[^1].Sequence;
            if ((await Post.Query(post => post.TopicKey == topic.Key && post.OfPostId == null && post.Sequence < first,
                    MessageQuery(1, descending: true), ct)).Count > 0)
                olderBefore = selected[0].Post.Id;
            if ((await Post.Query(post => post.TopicKey == topic.Key && post.OfPostId == null && post.Sequence > last,
                    MessageQuery(1, descending: false), ct)).Count > 0)
                newerAfter = selected[^1].Post.Id;
        }
        // A concurrent policy change must not publish a window fetched under an earlier audience.
        // Reads never hold the writer gate across database or profile work.
        if (await ResolveTopic(topic.TangentKey, topic.Key, ct) is null) return null;
        var window = new PublicPostWindow(topic, selected.Select(item => item.Post).ToArray(), olderBefore, newerAfter,
            opening is null ? null : Project(opening));
        if (opening is not null) WindowPlanner.CheckArticleResponse(window);
        return window;
    }

    private static void ValidateWindow(string? before, string? after, string? around, int limit)
    {
        if (new[] { before is not null, after is not null, around is not null }.Count(value => value) > 1)
            throw new ArgumentException("Choose before, after, or around, not more than one.");
        if (before == "" || after == "" || limit is < 1 or > MaximumPosts)
            throw new ArgumentException("Use a Post id for before or after, and a limit from 1 to 25.");
    }

    private static async Task<long?> EdgeSequence(string topicKey, string? postId, CancellationToken ct)
    {
        if (postId is null) return null;
        if (postId.Length > 128) throw new ArgumentException("Choose a Post in this Topic.");
        var post = await Post.Get(postId, ct);
        if (post is null || post.TopicKey != topicKey || post.OfPostId is not null || post.Sequence <= 0)
            throw new ArgumentException("Choose a Post in this Topic.");
        return post.Sequence;
    }

    private static async Task<PublicTopicDescription?> ResolveTopic(string tangentId, string topicId, CancellationToken ct)
    {
        using var fresh = EntityContext.NoCache();
        var topic = await Topic.Get(topicId, ct);
        if (topic is null || topic.TangentKey != tangentId) return null;
        var tangent = await Community.Tangent.Get(tangentId, ct);
        return PublicTopicAccess.IsPublic(topic, tangent) ? PublicTopicDescription.From(topic, tangent!, await Tangent.Spaces.Space.Get(TangentConstants.SpaceId,ct)) : null;
    }

    private static QueryDefinition MessageQuery(int size, bool descending)
    {
        var sequence = typeof(Post).GetProperty(nameof(Post.Sequence))!;
        return new QueryDefinition { Page = 1, PageSize = size,
            Sort = [new SortSpec(new MemberPath(typeof(Post), [sequence], typeof(long), false, -1), descending)] };
    }
}

public sealed record PublicPostDocument(string AnchorId, PublicPostWindow Window);
