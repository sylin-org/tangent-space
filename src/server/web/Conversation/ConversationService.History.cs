using System.Security.Cryptography;
using System.Text.Json;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sorting;
using Koan.Data.Core;
using Microsoft.AspNetCore.DataProtection;
using Tangent.Activity;
using Tangent.Access;
using Tangent.Identity;
using Tangent.Community;
using Tangent.Stewardship;
using Tangent.Application;

namespace Tangent.Conversation;

public sealed partial class ConversationService
{
    private static readonly QueryDefinition HistoryWindow = new()
    {
        Page = 1, PageSize = 21,
        Sort = [new SortSpec(new MemberPath(typeof(Post), [typeof(Post).GetProperty(nameof(Post.Sequence))!], typeof(long), false, -1), false)]
    };

    public Task<PostPage> History(string participantId, string topic, string? cursor, CancellationToken ct, bool fromStart = false)
        => governance.WithCurrentPolicy(participantId, topic, async (policy, token) =>
        {
            if (!policy.CanRead) throw new UnauthorizedAccessException("This topic's current rules do not allow reading.");
            var state = await TopicConversation.Get(topic, token) ?? new TopicConversation { Id = topic };
            var position = await ReadPosition.Get(ReadPosition.Key(participantId, topic), token);
            if (fromStart && cursor is not null) throw new ArgumentException("Choose a continuation or read from the beginning, not both.");
            var selected = cursor is null ? new ConversationCursor(participantId, topic, fromStart ? 0 : position?.Sequence ?? 0, null, clock.GetUtcNow().AddDays(7)) : Decode(cursor, participantId, topic);
            var boundary = selected.Boundary ?? state.LastSequence;
            if (selected.After > boundary || boundary > state.LastSequence) throw new ArgumentException("The continuation no longer matches this conversation; restart the read.");
            var candidates = await Post.Query(m => m.TopicKey == topic && m.Sequence > selected.After && m.Sequence <= boundary, HistoryWindow, token);
            var posts = new List<Post>();
            var bytes = 4096; // Envelope and protected continuations stay inside the overall 128 KiB budget.
            foreach (var post in candidates.Take(20))
            {
                post.Permissions = Permissions.Post(policy, post.AuthorParticipantId, post.Removed);
                var size = JsonSerializer.SerializeToUtf8Bytes(post).Length;
                if (bytes + size > 128 * 1024) break;
                posts.Add(post); bytes += size;
            }
            var after = posts.Count == 0 ? selected.After : posts[^1].Sequence;
            var more = candidates.Count > posts.Count;
            var expiry = clock.GetUtcNow().AddDays(7);
            var next = more ? Encode(new(participantId, topic, after, boundary, expiry)) : null;
            var resume = Encode(new(participantId, topic, more ? after : boundary, null, expiry));
            var handles = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (author, label) in (await directory.LabelsFor(
                     posts.Select(post => post.AuthorParticipantId).Distinct(StringComparer.Ordinal).Take(20), token))
                     .Where(pair => pair.Value is { Length: > 0 and <= 253 }))
            {
                var handleBytes = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string> { [author] = label }).Length;
                if (bytes + handleBytes > 128 * 1024) continue;
                handles[author] = label;
                bytes += handleBytes;
            }
            return new PostPage(posts, next, resume, boundary, handles,
                await ResolveParticipants(posts, token));
        }, ct);

    public async Task<long> Acknowledge(string participantId, string topic, string cursor, CancellationToken ct)
    {
        var result = await governance.WithCurrentPolicy(participantId, topic, async (policy, token) =>
        {
            if (!policy.CanRead) throw new UnauthorizedAccessException("This topic's current rules do not allow reading.");
            var selected = Decode(cursor, participantId, topic);
            if (selected.Boundary is not null) throw new ArgumentException("Acknowledge a resume cursor returned with a page.");
            var position = await ReadPosition.Get(ReadPosition.Key(participantId, topic), token)
                ?? new ReadPosition { Id = ReadPosition.Key(participantId, topic), ParticipantId = participantId, TopicKey = topic };
            var previous = position.Sequence;
            position.Sequence = Math.Max(previous, selected.After);
            position.AcknowledgedAt = clock.GetUtcNow();
            await position.Save(token);
            if (position.Sequence > previous)
            {
                var currentTopic = await Topic.Get(topic, token);
                await ActivityJournal.AppendInTransaction(ActivityKind.ReadAcknowledged, topic, participantId, null,
                    currentTopic?.TangentKey, position.Sequence, position.AcknowledgedAt, token);
            }
            // The MCP boundary can persist a read receipt in this same transaction.
            await CommandCommit.Report(position.Sequence, token);
            return (position.Sequence, Changed: position.Sequence > previous);
        }, ct);
        if (result.Changed) ActivityJournal.SignalAfterCommit();
        return result.Sequence;
    }

    private string Encode(ConversationCursor value) => cursors.Protect(JsonSerializer.Serialize(value));
    private ConversationCursor Decode(string value, string participantId, string topic)
    {
        try
        {
            if (value.Length > 4096) throw new ArgumentException("Continuation exceeds the supported length.");
            var cursor = JsonSerializer.Deserialize<ConversationCursor>(cursors.Unprotect(value))!;
            if (cursor.ParticipantId != participantId || cursor.Topic != topic || cursor.ExpiresAt <= clock.GetUtcNow() || cursor.After < 0)
                throw new ArgumentException("This continuation is expired or belongs to another participant or topic.");
            return cursor;
        }
        catch (Exception e) when (e is CryptographicException or JsonException or NullReferenceException)
        { throw new ArgumentException("Invalid conversation continuation."); }
    }
}
