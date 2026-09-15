using System.Text.Json;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sorting;
using Koan.Data.Core;
using TangentSpace.Communities;
using TangentSpace.Conversation;
using TangentSpace.Infrastructure;
using TangentSpace.Participants;

namespace TangentSpace.Rooms.Web;

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
            var anchor = await Message.Get(postId, ct);
            if (anchor is null || anchor.OfMessageId is not null || anchor.Sequence <= 0) return null;
            var topic = await ResolveTopicUnderGate(tangentId, anchor.RoomKey, ct);
            if (topic is null) return null;
            var window = await ReadPostsUnderGate(topic, before: null, after: null, postId, limit, ct, anchor);
            return window is not null && window.Posts.Any(post => post.Id == postId) ? new(postId, window) : null;
        }
        finally { policyGate.Exit(); }
    }

    internal async Task<PublicPostWindow?> ReadPostsUnderGate(PublicTopicDescription topic, long? before, long? after,
        string? around, int limit, CancellationToken ct, Message? knownAnchor = null)
    {
        ValidateWindow(before, after, around, limit);
        using var fresh = EntityContext.NoCache();
        IReadOnlyList<Message> candidates;
        if (around is not null)
        {
            var anchor = knownAnchor ?? await Message.Get(around, ct);
            if (anchor is null || anchor.RoomKey != topic.Key || anchor.OfMessageId is not null || anchor.Sequence <= 0)
                return null;
            var beforeCount = limit / 2;
            var afterCount = limit - beforeCount - 1;
            var preceding = beforeCount == 0 ? [] : await Message.Query(
                message => message.RoomKey == topic.Key && message.OfMessageId == null && message.Sequence < anchor.Sequence,
                MessageQuery(beforeCount, descending: true), ct);
            var following = afterCount == 0 ? [] : await Message.Query(
                message => message.RoomKey == topic.Key && message.OfMessageId == null && message.Sequence > anchor.Sequence,
                MessageQuery(afterCount, descending: false), ct);
            candidates = preceding.OrderBy(message => message.Sequence).Append(anchor)
                .Concat(following.OrderBy(message => message.Sequence)).ToArray();
        }
        else
        {
            var descending = before is not null || after is null;
            var query = MessageQuery(limit + 1, descending);
            candidates = before is { } older
                ? await Message.Query(message => message.RoomKey == topic.Key && message.OfMessageId == null && message.Sequence < older, query, ct)
                : after is { } newer
                    ? await Message.Query(message => message.RoomKey == topic.Key && message.OfMessageId == null && message.Sequence > newer, query, ct)
                    : await Message.Query(message => message.RoomKey == topic.Key && message.OfMessageId == null, query, ct);
            candidates = candidates.Take(limit).ToArray();
        }

        var traversal = candidates.Take(limit).ToArray();
        var authors = traversal.Select(message => message.AuthorParticipantId).Distinct(StringComparer.Ordinal).ToArray();
        var labels = await directory.LabelsFor(authors, ct, resolveMissing: false);
        var refs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var author in authors) refs[author] = await directory.PerennialValue(author, ct);

        var selected = new List<(long Sequence, PublicPostDescription Post)>(traversal.Length);
        var bytes = EnvelopeReserve + JsonSerializer.SerializeToUtf8Bytes(topic).Length;
        foreach (var message in traversal)
        {
            var authorRef = refs[message.AuthorParticipantId];
            var label = labels.GetValueOrDefault(message.AuthorParticipantId, authorRef);
            var post = new PublicPostDescription(message.Id, new(authorRef, label),
                message.Removed ? null : message.Content.Text, message.Content.CreatedAt, message.AcceptedAt,
                message.EditedAt, message.Removed,
                $"/t/{Uri.EscapeDataString(topic.TangentKey)}/{Uri.EscapeDataString(message.Id)}");
            var size = JsonSerializer.SerializeToUtf8Bytes(post).Length;
            if (bytes + size > ResultBudget) break;
            bytes += size;
            selected.Add((message.Sequence, post));
        }
        selected.Sort((left, right) => left.Sequence.CompareTo(right.Sequence));

        long? olderBefore = null;
        long? newerAfter = null;
        if (selected.Count > 0)
        {
            var first = selected[0].Sequence;
            var last = selected[^1].Sequence;
            if ((await Message.Query(message => message.RoomKey == topic.Key && message.OfMessageId == null && message.Sequence < first,
                    MessageQuery(1, descending: true), ct)).Count > 0)
                olderBefore = first;
            if ((await Message.Query(message => message.RoomKey == topic.Key && message.OfMessageId == null && message.Sequence > last,
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
        var room = await Room.Get(topicId, ct);
        if (room is null || room.TangentKey != tangentId || !PublicTopicAccess.IsPublic(room)) return null;
        return await TangentCommunity.Get(tangentId, ct) is null ? null : PublicTopicDescription.From(room);
    }

    private static QueryDefinition MessageQuery(int size, bool descending)
    {
        var sequence = typeof(Message).GetProperty(nameof(Message.Sequence))!;
        return new QueryDefinition { Page = 1, PageSize = size,
            Sort = [new SortSpec(new MemberPath(typeof(Message), [sequence], typeof(long), false, -1), descending)] };
    }
}

public sealed record PublicPostDocument(string AnchorId, PublicPostWindow Window);
