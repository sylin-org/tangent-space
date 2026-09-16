using System.Security.Cryptography;
using System.Text.Json;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sorting;
using Microsoft.AspNetCore.DataProtection;
using TangentSpace.Participants;
using TangentSpace.Authorization;

namespace TangentSpace.Conversation;

public sealed partial class ConversationService
{
    private readonly IDataProtector windowCursors = protection.CreateProtector("Tangent.Conversation.WindowCursor.v1");

    /// <summary>A bounded window of one Topic for one participant: the unread posts by default, a page
    /// from a continuation cursor, or the neighborhood of an anchor post.</summary>
    public Task<TopicWindow> ReadWindow(string participantId, string topicKey, string? cursor, string? anchorPostId,
        int limit, CancellationToken ct)
        => governance.WithCurrentPolicy(participantId, topicKey, async (policy, token) =>
        {
            if (!policy.CanRead) throw new UnauthorizedAccessException("This conversation is not available to your account.");
            if (limit is < 1 or > 25) throw new ArgumentException("Choose a window of 1 to 25 posts.");
            if (cursor is not null && anchorPostId is not null)
                throw new ArgumentException("Choose a history cursor or an anchor post, not both.");
            var state = await TopicConversation.Get(topicKey, token) ?? new TopicConversation { Id = topicKey };
            var boundary = state.LastSequence;
            var position = "unread";
            var rows = new List<Post>();
            var anchorIndex = 0;

            if (cursor is not null)
            {
                var selected = DecodeWindowCursor(cursor, participantId, topicKey);
                if (selected.Boundary > boundary || selected.Edge < 1 || selected.Edge > selected.Boundary)
                    throw new ArgumentException("The history cursor no longer matches this conversation. Open a fresh window.");
                boundary = selected.Boundary;
                position = "page";
                var candidates = selected.Older
                    ? await Post.Query(m => m.RoomKey == topicKey && m.Sequence < selected.Edge && m.Sequence <= boundary,
                        SequenceQuery(limit, true), token)
                    : await Post.Query(m => m.RoomKey == topicKey && m.Sequence > selected.Edge && m.Sequence <= boundary,
                        SequenceQuery(limit), token);
                rows = candidates.OrderBy(m => m.Sequence).ToList();
                anchorIndex = selected.Older ? Math.Max(0, rows.Count - 1) : 0;
            }
            else if (anchorPostId is not null)
            {
                var anchor = await Post.Get(anchorPostId, token);
                if (anchor is null || anchor.RoomKey != topicKey || anchor.Sequence > boundary)
                    throw new ArgumentException("Choose an accessible Post in this Topic.");
                var before = await Post.Query(m => m.RoomKey == topicKey && m.Sequence < anchor.Sequence,
                    SequenceQuery(limit, true), token);
                var after = await Post.Query(m => m.RoomKey == topicKey && m.Sequence > anchor.Sequence && m.Sequence <= boundary,
                    SequenceQuery(limit), token);
                rows = before.OrderBy(m => m.Sequence).Append(anchor).Concat(after.OrderBy(m => m.Sequence)).ToList();
                anchorIndex = before.Count;
                position = "around";
            }
            else
            {
                var read = await ReadPosition.Get(ReadPosition.Key(participantId, topicKey), token);
                var after = Math.Min(read?.Sequence ?? 0, boundary);
                rows = (await Post.Query(m => m.RoomKey == topicKey && m.Sequence > after && m.Sequence <= boundary,
                    SequenceQuery(limit), token)).ToList();
                if (rows.Count == 0)
                {
                    rows = (await Post.Query(m => m.RoomKey == topicKey && m.Sequence <= boundary,
                        SequenceQuery(limit, true), token)).OrderBy(m => m.Sequence).ToList();
                    anchorIndex = Math.Max(0, rows.Count - 1);
                    position = "latest";
                }
            }

            var posts = WindowPlanner.Select(rows, anchorIndex, limit);
            foreach (var post in posts) post.Permissions = Permissions.Post(policy, post.AuthorParticipantId, post.Removed);
            string? older = null, newer = null, readCursor = null;
            if (posts.Count > 0)
            {
                var first = posts[0].Sequence;
                var last = posts[^1].Sequence;
                var hasOlder = (await Post.Query(m => m.RoomKey == topicKey && m.Sequence < first,
                    SequenceQuery(1, true), token)).Count > 0;
                var hasNewer = (await Post.Query(m => m.RoomKey == topicKey && m.Sequence > last && m.Sequence <= boundary,
                    SequenceQuery(1), token)).Count > 0;
                var expiry = clock.GetUtcNow().AddDays(7);
                if (hasOlder) older = EncodeWindowCursor(new(participantId, topicKey, boundary, first, true, expiry));
                if (hasNewer) newer = EncodeWindowCursor(new(participantId, topicKey, boundary, last, false, expiry));
                readCursor = Encode(new ConversationCursor(participantId, topicKey, last, null, expiry));
            }
            var handles = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (author, handle) in await directory.LabelsFor(
                     posts.Select(m => m.AuthorParticipantId).Distinct(StringComparer.Ordinal), token))
                if (handle is { Length: > 0 and <= 253 }) handles[author] = handle;
            return new TopicWindow(posts, older, newer, readCursor, position,
                handles, await ResolveParticipants(posts, token));
        }, ct);

    private static QueryDefinition SequenceQuery(int size, bool descending = false)
    {
        var member = typeof(Post).GetProperty(nameof(Post.Sequence))!;
        return new QueryDefinition { Page = 1, PageSize = size,
            Sort = [new SortSpec(new MemberPath(typeof(Post), [member], typeof(long), false, -1), descending)] };
    }

    private string EncodeWindowCursor(WindowCursor value) => windowCursors.Protect(JsonSerializer.Serialize(value));

    private WindowCursor DecodeWindowCursor(string value, string participantId, string topicKey)
    {
        try
        {
            if (value.Length > 4096) throw new ArgumentException("The history cursor is too long.");
            var cursor = JsonSerializer.Deserialize<WindowCursor>(windowCursors.Unprotect(value));
            if (cursor is null || cursor.ParticipantId != participantId || cursor.TopicKey != topicKey || cursor.ExpiresAt <= clock.GetUtcNow())
                throw new ArgumentException("The history cursor expired or belongs to another participant or Topic.");
            return cursor;
        }
        catch (Exception error) when (error is CryptographicException or JsonException)
        { throw new ArgumentException("The history cursor is invalid. Open a fresh window."); }
    }
}
