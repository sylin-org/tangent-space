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
    private readonly IDataProtector mcpHistory = protection.CreateProtector("Tangent.Mcp.History.v1");

    public Task<McpMessageWindow> McpWindow(string participantId, string room, string? cursor, string? aroundMessageId,
        int limit, CancellationToken ct)
        => governance.WithCurrentPolicy(participantId, room, async (policy, token) =>
        {
            if (!policy.CanRead) throw new UnauthorizedAccessException("This conversation is not available to your account.");
            if (limit is < 1 or > 25) throw new ArgumentException("Choose a window of 1 to 25 messages.");
            if (cursor is not null && aroundMessageId is not null)
                throw new ArgumentException("Choose a history cursor or an anchor message, not both.");
            var state = await RoomConversation.Get(room, token) ?? new RoomConversation { Id = room };
            var boundary = state.LastSequence;
            var position = "unread";
            var rows = new List<Message>();
            var anchorIndex = 0;

            if (cursor is not null)
            {
                var selected = DecodeMcpHistory(cursor, participantId, room);
                if (selected.Boundary > boundary || selected.Edge < 1 || selected.Edge > selected.Boundary)
                    throw new ArgumentException("The history cursor no longer matches this conversation. Open a fresh window.");
                boundary = selected.Boundary;
                position = "page";
                var candidates = selected.Older
                    ? await Message.Query(m => m.RoomKey == room && m.Sequence < selected.Edge && m.Sequence <= boundary,
                        McpQuery(limit, true), token)
                    : await Message.Query(m => m.RoomKey == room && m.Sequence > selected.Edge && m.Sequence <= boundary,
                        McpQuery(limit), token);
                rows = candidates.OrderBy(m => m.Sequence).ToList();
                anchorIndex = selected.Older ? Math.Max(0, rows.Count - 1) : 0;
            }
            else if (aroundMessageId is not null)
            {
                var anchor = await Message.Get(aroundMessageId, token);
                if (anchor is null || anchor.RoomKey != room || anchor.Sequence > boundary)
                    throw new ArgumentException("Choose an accessible message in this Channel.");
                var before = await Message.Query(m => m.RoomKey == room && m.Sequence < anchor.Sequence,
                    McpQuery(limit, true), token);
                var after = await Message.Query(m => m.RoomKey == room && m.Sequence > anchor.Sequence && m.Sequence <= boundary,
                    McpQuery(limit), token);
                rows = before.OrderBy(m => m.Sequence).Append(anchor).Concat(after.OrderBy(m => m.Sequence)).ToList();
                anchorIndex = before.Count;
                position = "around";
            }
            else
            {
                var read = await ReadPosition.Get(ReadPosition.Key(participantId, room), token);
                var after = Math.Min(read?.Sequence ?? 0, boundary);
                rows = (await Message.Query(m => m.RoomKey == room && m.Sequence > after && m.Sequence <= boundary,
                    McpQuery(limit), token)).ToList();
                if (rows.Count == 0)
                {
                    rows = (await Message.Query(m => m.RoomKey == room && m.Sequence <= boundary,
                        McpQuery(limit, true), token)).OrderBy(m => m.Sequence).ToList();
                    anchorIndex = Math.Max(0, rows.Count - 1);
                    position = "latest";
                }
            }

            var messages = McpWindowPlanner.Select(rows, anchorIndex, limit);
            foreach (var message in messages) message.Permissions = Permissions.Post(policy, message.AuthorParticipantId, message.Removed);
            string? older = null, newer = null, readCursor = null;
            if (messages.Count > 0)
            {
                var first = messages[0].Sequence;
                var last = messages[^1].Sequence;
                var hasOlder = (await Message.Query(m => m.RoomKey == room && m.Sequence < first,
                    McpQuery(1, true), token)).Count > 0;
                var hasNewer = (await Message.Query(m => m.RoomKey == room && m.Sequence > last && m.Sequence <= boundary,
                    McpQuery(1), token)).Count > 0;
                var expiry = clock.GetUtcNow().AddDays(7);
                if (hasOlder) older = EncodeMcpHistory(new(participantId, room, boundary, first, true, expiry));
                if (hasNewer) newer = EncodeMcpHistory(new(participantId, room, boundary, last, false, expiry));
                readCursor = Encode(new ConversationCursor(participantId, room, last, null, expiry));
            }
            var handles = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (author, handle) in await directory.LabelsFor(
                     messages.Select(m => m.AuthorParticipantId).Distinct(StringComparer.Ordinal), token))
                if (handle is { Length: > 0 and <= 253 }) handles[author] = handle;
            return new McpMessageWindow(messages, older, newer, readCursor, position,
                state.Freshness, state.LastCompleteAt, handles, await ResolveParticipants(messages, token));
        }, ct);

    private static QueryDefinition McpQuery(int size, bool descending = false)
    {
        var member = typeof(Message).GetProperty(nameof(Message.Sequence))!;
        return new QueryDefinition { Page = 1, PageSize = size,
            Sort = [new SortSpec(new MemberPath(typeof(Message), [member], typeof(long), false, -1), descending)] };
    }

    private string EncodeMcpHistory(McpHistoryCursor value) => mcpHistory.Protect(JsonSerializer.Serialize(value));

    private McpHistoryCursor DecodeMcpHistory(string value, string participantId, string room)
    {
        try
        {
            if (value.Length > 4096) throw new ArgumentException("The history cursor is too long.");
            var cursor = JsonSerializer.Deserialize<McpHistoryCursor>(mcpHistory.Unprotect(value));
            if (cursor is null || cursor.ParticipantId != participantId || cursor.Room != room || cursor.ExpiresAt <= clock.GetUtcNow())
                throw new ArgumentException("The history cursor expired or belongs to another participant or Channel.");
            return cursor;
        }
        catch (Exception error) when (error is CryptographicException or JsonException)
        { throw new ArgumentException("The history cursor is invalid. Open a fresh window."); }
    }
}
