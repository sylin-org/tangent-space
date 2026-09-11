using System.Text.Json;
using Koan.Data.Core;
using TangentSpace.Activity;
using TangentSpace.AtProtocol;
using TangentSpace.Authorization;
using TangentSpace.Rooms;

namespace TangentSpace.Conversation;

public sealed partial class ConversationService
{
    public async Task<PostChangeResult> ChangePost(string did, string roomKey, string messageId, string? text,
        bool delete, string operationId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(messageId) || string.IsNullOrWhiteSpace(operationId))
            throw new ArgumentException("A message ID and operation ID are required.");
        if (!delete) { if (text is null) throw new ArgumentException("Text is required for an edit."); MessageContent.CheckText(text); }
        await writes.WaitAsync(ct);
        try
        {
            var change = await governance.WithCurrentPolicy(did, roomKey, async (policy, token) =>
            {
                if (!policy.CanRead) throw new UnauthorizedAccessException("This room's current rules do not allow changing posts.");
                var message = await Message.Get(messageId, token);
                if (message is null || message.RoomKey != roomKey) throw new ArgumentException("Choose a message in this room.");
                var id = PostChange.Key(did, roomKey, messageId, operationId);
                var previous = await PostChange.Get(id, token);
                if (previous is not null)
                {
                    if (previous.Delete != delete || previous.Text != text) throw new ArgumentException("That operation ID was already used with different content.");
                    if (previous.State is "accepted" or "deleted" or "moderated") return previous;
                }
                var own = message.AuthorDid == did;
                if (own)
                {
                    if (!policy.CanWrite || policy.Locked || (!delete && !policy.EditingAllowed))
                        throw new UnauthorizedAccessException("The current room rules do not allow this post change.");
                    if (message.Removed) throw new ArgumentException("That post has already been removed.");
                }
                else if (!policy.CanManage) throw new UnauthorizedAccessException("Only a room manager can remove another participant's post.");
                if (!delete && !own) throw new UnauthorizedAccessException("Moderators may remove posts but cannot rewrite their authors' words.");
                if (previous is not null) return previous;
                var created = new PostChange { Id = id, RoomKey = roomKey, MessageId = messageId, ActorDid = did,
                    OperationId = operationId, Delete = delete, Text = text, UpdatedAt = clock.GetUtcNow() };
                await created.Save(token);
                return created;
            }, ct);
            if (change.State is "accepted" or "moderated" or "deleted") return new(change.State, messageId, change.Detail);

            try
            {
                var message = await Message.Get(messageId, ct) ?? throw new ArgumentException("Choose a message in this room.");
                if (change.Delete && message.AuthorDid != did)
                {
                    await governance.WithCurrentPolicy(did, roomKey, async (currentPolicy, token) =>
                    {
                        if (!currentPolicy.CanManage) throw new UnauthorizedAccessException("The current room rules do not allow moderation.");
                        message.Removed = true; message.Content = new MessageContent("", message.Content.CreatedAt, message.Content.ReplyTo);
                        message.RemovedAt = clock.GetUtcNow(); message.RemovedByDid = did;
                        await message.Save(token);
                        change.State = "moderated"; change.Detail = "post-removed-by-moderator"; change.UpdatedAt = clock.GetUtcNow();
                        var room = await Room.Get(roomKey, token);
                        await ActivityJournal.AppendInTransaction(ActivityKind.MessageDeleted, roomKey, did, message.AuthorDid,
                            room?.TangentKey, message.Sequence, change.UpdatedAt, token);
                        await change.Save(token); return true;
                    }, ct);
                    updates.Pulse(roomKey); ActivityJournal.SignalAfterCommit();
                    return new(change.State, messageId, change.Detail);
                }
                // Local-storage posts change the entity directly: there is no source record
                // to update or delete (ADR 0006). Authorship policy is unchanged.
                if (message.SourceUri.StartsWith("local://", StringComparison.Ordinal))
                {
                    await governance.WithCurrentPolicy(did, roomKey, async (currentPolicy, token) =>
                    {
                        var current = await Message.Get(messageId, token) ?? message;
                        if (current.AuthorDid == did)
                        {
                            if (!currentPolicy.CanWrite || currentPolicy.Locked || (!change.Delete && !currentPolicy.EditingAllowed))
                                throw new UnauthorizedAccessException("The current room rules do not allow this post change.");
                        }
                        else if (!currentPolicy.CanManage) throw new UnauthorizedAccessException("The current room rules do not allow moderation.");
                        if (change.Delete)
                        {
                            current.Removed = true;
                            current.Content = new MessageContent("", current.Content.CreatedAt, current.Content.ReplyTo);
                            current.RemovedAt = clock.GetUtcNow();
                            current.RemovedByDid = did;
                        }
                        else
                        {
                            current.Content = new MessageContent(change.Text!, current.Content.CreatedAt, current.Content.ReplyTo);
                            current.EditedAt = clock.GetUtcNow();
                        }
                        await current.Save(token);
                        change.State = change.Delete ? "deleted" : "accepted";
                        change.Detail = change.Delete ? "post-deleted-local" : "post-edited-local";
                        change.UpdatedAt = clock.GetUtcNow();
                        var room = await Room.Get(roomKey, token);
                        await ActivityJournal.AppendInTransaction(change.Delete ? ActivityKind.MessageDeleted : ActivityKind.MessageEdited,
                            roomKey, did, current.AuthorDid, room?.TangentKey, current.Sequence, change.UpdatedAt, token);
                        await change.Save(token);
                        return true;
                    }, ct);
                    updates.Pulse(roomKey);
                    ActivityJournal.SignalAfterCommit();
                    return new(change.State, messageId, change.Detail);
                }
                var parts = message.SourceUri[5..].Split('/');
                if (parts.Length != 7 || parts[4] != message.AuthorDid || parts[5] != SpacesOptions.Collection)
                    throw new InvalidDataException("The post source URI is invalid.");
                var space = "at://" + string.Join('/', parts[..4]);
                var recordKey = parts[6];
                string? putCid = null;
                if (change.Delete) await spaces.DeleteRecord(did, space, recordKey, ct);
                else
                {
                    var content = new MessageContent(change.Text!, message.Content.CreatedAt, message.Content.ReplyTo);
                    var result = await spaces.PutRecord(did, space, recordKey, content.ToRecord(), ct);
                    if (!result.TryGetProperty("cid", out _)) throw new InvalidDataException("The source did not return a new record CID.");
                    putCid = result.GetProperty("cid").GetString();
                }
                var source = await spaces.ReadRepo(did, space, message.AuthorDid, ct);
                var record = source.Records.SingleOrDefault(r => r.Collection == SpacesOptions.Collection && r.RecordKey == recordKey);
                if (change.Delete ? record is not null : record is null) throw new InvalidDataException("The source did not confirm the requested post change.");
                if (!change.Delete)
                {
                    if (record!.Cid != putCid) throw new InvalidDataException("The source CID does not match the confirmed write.");
                    MessageContent confirmed;
                    try { confirmed = MessageContent.FromCbor(record.DagCbor); }
                    catch (Exception error) when (error is InvalidDataException or ArgumentException or System.Formats.Cbor.CborContentException)
                    { throw new InvalidDataException("The source content could not be verified."); }
                    if (confirmed.Text != change.Text) throw new InvalidDataException("The source content does not match the requested edit.");
                }
                await governance.WithCurrentPolicy(did, roomKey, async (currentPolicy, token) =>
                {
                    var current = await Message.Get(messageId, token) ?? message;
                    if (current.AuthorDid == did)
                    {
                        if (!currentPolicy.CanWrite || currentPolicy.Locked || (!change.Delete && !currentPolicy.EditingAllowed))
                            throw new UnauthorizedAccessException("The current room rules do not allow this post change.");
                    }
                    else if (!currentPolicy.CanManage) throw new UnauthorizedAccessException("The current room rules do not allow moderation.");
                    if (change.Delete) { current.Removed = true; current.Content = new MessageContent("", current.Content.CreatedAt, current.Content.ReplyTo); current.RemovedAt = clock.GetUtcNow(); current.RemovedByDid = did; }
                    else
                    {
                        current.Content = new MessageContent(change.Text!, current.Content.CreatedAt, current.Content.ReplyTo);
                        current.SourceCid = record!.Cid;
                        current.EditedAt = clock.GetUtcNow();
                        current.Facets = null;
                    }
                    await current.Save(token);
                    change.State = change.Delete ? "deleted" : "accepted"; change.Detail = change.Delete ? "post-deleted" : "post-edited"; change.UpdatedAt = clock.GetUtcNow();
                    var room = await Room.Get(roomKey, token);
                    await ActivityJournal.AppendInTransaction(change.Delete ? ActivityKind.MessageDeleted : ActivityKind.MessageEdited,
                        roomKey, did, current.AuthorDid, room?.TangentKey, current.Sequence, change.UpdatedAt, token);
                    await change.Save(token); return true;
                }, ct);
                updates.Pulse(roomKey); ActivityJournal.SignalAfterCommit();
            }
            catch (Exception error) when (error is SpacesUnavailable or HttpRequestException or InvalidDataException or JsonException)
            {
                change.State = "pending"; change.Detail = "source-change-failed-retry-same-operation"; change.UpdatedAt = clock.GetUtcNow(); await change.Save(ct);
            }
            return new(change.State, messageId, change.Detail);
        }
        finally { writes.Release(); }
    }
}
