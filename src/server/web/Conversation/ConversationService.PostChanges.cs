using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using TangentSpace.Activity;
using TangentSpace.AtProtocol;
using TangentSpace.Authorization;
using TangentSpace.Participants;
using TangentSpace.Rooms;

namespace TangentSpace.Conversation;

public sealed partial class ConversationService
{
    public async Task<PostChangeResult> ChangePost(string did, string roomKey, string messageId, string? text,
        bool delete, string operationId, CancellationToken ct, IReadOnlyList<PostFacet>? facets = null)
    {
        if (string.IsNullOrWhiteSpace(messageId) || string.IsNullOrWhiteSpace(operationId))
            throw new ArgumentException("A message ID and operation ID are required.");
        if (!delete)
        {
            if (text is null) throw new ArgumentException("Text is required for an edit.");
            MessageContent.CheckText(text);
            PostFacets.Check(text, facets);
        }
        else if (facets is { Count: > 0 }) throw new ArgumentException("A removal cannot carry facets.");
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
                    // Facet conflict parity with create: the ledger remembers the client-sent facet
                    // payload, so the same package replays its receipt regardless of later edits or
                    // pending-retry timing. Re-detection is server-side derivation computed at
                    // application time; world state is not part of the delivered package (ADR 0007).
                    if (Canonical(previous.Facets) != Canonical(facets)) throw new WriteConflict();
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
                    OperationId = operationId, Delete = delete, Text = text, Facets = facets, UpdatedAt = clock.GetUtcNow() };
                await created.Save(token);
                return created;
            }, ct);
            // Completed operations return before any mutation, so an idempotent re-delivery can never
            // mint a second snapshot; pending Spaces operations retry below with the same guard.
            if (change.State is "accepted" or "moderated" or "deleted") return new(change.State, messageId, change.Detail);

            var embedder = ChangeClassification.ResolveEmbedder();
            try
            {
                var message = await Message.Get(messageId, ct) ?? throw new ArgumentException("Choose a message in this room.");
                if (change.Delete && message.AuthorDid != did)
                {
                    await governance.WithCurrentPolicy(did, roomKey, async (currentPolicy, token) =>
                    {
                        if (!currentPolicy.CanManage) throw new UnauthorizedAccessException("The current room rules do not allow moderation.");
                        await SnapshotChange(message, "", null, embedder, token);
                        message.Removed = true; message.Content = new MessageContent("", message.Content.CreatedAt, message.Content.ReplyTo);
                        message.RemovedAt = clock.GetUtcNow(); message.RemovedByDid = did;
                        // The words are gone; their byte ranges are meaningless on a removed row.
                        message.Facets = null;
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
                            await SnapshotChange(current, "", null, embedder, token);
                            current.Removed = true;
                            current.Content = new MessageContent("", current.Content.CreatedAt, current.Content.ReplyTo);
                            current.RemovedAt = clock.GetUtcNow();
                            current.RemovedByDid = did;
                            current.Facets = null;
                        }
                        else
                        {
                            // D2a: provided facets ride the new version; absent facets re-detect
                            // deterministically. The pre-edit structure rides its snapshot.
                            var effective = await EffectiveFacets(change.Text!, facets, token);
                            await SnapshotChange(current, change.Text!, effective, embedder, token);
                            current.Content = new MessageContent(change.Text!, current.Content.CreatedAt, current.Content.ReplyTo);
                            current.EditedAt = clock.GetUtcNow();
                            current.Facets = effective;
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
                    if (change.Delete)
                    {
                        await SnapshotChange(current, "", null, embedder, token);
                        current.Removed = true; current.Content = new MessageContent("", current.Content.CreatedAt, current.Content.ReplyTo); current.RemovedAt = clock.GetUtcNow(); current.RemovedByDid = did;
                        current.Facets = null;
                    }
                    else
                    {
                        // Spaces records carry no facets: the live row keeps the honest drop and the
                        // pre-edit structure rides its snapshot, so each era's shape survives.
                        await SnapshotChange(current, change.Text!, null, embedder, token);
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

    /// <summary>Mint the pre-edit snapshot into the changelog partition and advance the live row's
    /// ChangeId pointer. Snapshots are write-once: this method is the only writer into the partition,
    /// every snapshot carries a fresh GUIDv7 identity, and an absence guard refuses any replay of a
    /// used identity. Must run inside the caller's governance transaction so the snapshot, the live
    /// mutation, the PostChange record and the activity journal commit together or not at all — a
    /// failure between the snapshot write and the live save rolls the transaction back.</summary>
    private static async Task SnapshotChange(Message live, string newText, IReadOnlyList<PostFacet>? newFacets,
        ChangeEmbedder? embedder, CancellationToken token)
    {
        var classification = await ChangeClassification.Classify(live.Content.Text, newText, live.Facets, newFacets, embedder, token);
        var snapshot = new Message
        {
            Id = Guid.CreateVersion7().ToString(),
            RoomKey = live.RoomKey, AuthorDid = live.AuthorDid, SourceUri = live.SourceUri, SourceCid = live.SourceCid,
            Sequence = live.Sequence, AcceptedAt = live.AcceptedAt, Content = live.Content, Removed = live.Removed,
            RemovedAt = live.RemovedAt, RemovedByDid = live.RemovedByDid, EditedAt = live.EditedAt,
            Permissions = live.Permissions, OperationId = live.OperationId, Facets = live.Facets,
            OfMessageId = live.Id, PreviousChangeId = live.ChangeId, ChangeClass = classification,
        };
        // The vendored framework copy exposes no partitioned insert (Entity.Insert arrives upstream
        // after its pin), so the write-once construction rests on the fresh GUIDv7 identity — this
        // method is the only writer into the changelog partition — plus an absence guard under the
        // upsert verb. Swap to Message.Insert(snapshot, ChangelogPartition, token) when the pin moves.
        if (await Message.Get(snapshot.Id, Message.ChangelogPartition, token) is not null)
            throw new InvalidDataException("The changelog snapshot identity already exists.");
        await snapshot.Save(Message.ChangelogPartition, token);
        live.ChangeId = snapshot.Id;
    }

    /// <summary>Facets for the edited live row (D2a): provided facets win; absent facets degrade to
    /// deterministic server re-detection. Computed at application time only — the idempotency
    /// conflict check compares the ledger's remembered client-sent payload instead.</summary>
    private async Task<IReadOnlyList<PostFacet>?> EffectiveFacets(string? text, IReadOnlyList<PostFacet>? provided, CancellationToken token)
        => text is null || provided is { Count: > 0 } ? provided : await DetectFacets(text, token);

    // The candidate rules mirror the digest parser (Experience/ExperienceMentions.cs), which offers
    // no offset-bearing API: the regexes, code-fence handling, token boundaries, trailing-punctuation
    // trim, exact-handle resolution and ambiguity guard are copied verbatim and must stay in step.
    [GeneratedRegex(@"(?<![\w@])@(?<handle>[A-Za-z0-9][A-Za-z0-9.-]{1,252})", RegexOptions.CultureInvariant)]
    private static partial Regex HandleToken();

    [GeneratedRegex(@"(?<![\w:])did:(?<method>[a-z]+):(?<identifier>[A-Za-z0-9._:%-]{1,512})", RegexOptions.CultureInvariant)]
    private static partial Regex DidToken();

    /// <summary>Server-side facet re-detection: mention facets from @handle and DID tokens that
    /// resolve to exactly one stored participant, group facets from @admins/@moderators/@members
    /// when no stored handle claims the spelling. One facet per distinct target at its first
    /// occurrence; the create-path bound of 32 applies; anything unresolved is left as plain text.
    /// Ranges are absolute whole-text UTF-8 byte offsets: each pass tracks the byte offset of the
    /// current line's start (raw segment bytes plus one byte per '\n'), matching what the composer
    /// mints and the renderer expects.</summary>
    private async Task<IReadOnlyList<PostFacet>?> DetectFacets(string text, CancellationToken token)
    {
        var facets = new List<PostFacet>();
        var targets = new HashSet<string>(StringComparer.Ordinal);
        var inFence = false;
        var lineStart = 0;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal)) inFence = !inFence;
            else if (!inFence)
            {
                foreach (Match match in HandleToken().Matches(line))
                {
                    var handle = match.Groups["handle"].Value.TrimEnd('.', ',', '!', '?', ';', ':', ')', ']', '"', '\'');
                    if (handle.Length < 2) continue;
                    var resolved = await ResolveHandle(handle.ToLowerInvariant(), token);
                    if (resolved is null || !targets.Add(resolved)) continue;
                    facets.Add(At(lineStart, line, match.Index, "@" + handle, PostFacet.Mention, did: resolved));
                }
                foreach (Match match in DidToken().Matches(line))
                {
                    var did = match.Value.TrimEnd('.', ',', '!', '?', ';', ':', ')', ']', '"', '\'');
                    if (did.Length is < 9 or > 576 || !targets.Add(did) || await Participant.Get(did, token) is null) continue;
                    facets.Add(At(lineStart, line, match.Index, did, PostFacet.Mention, did: did));
                }
            }
            // The raw segment (including any '\r') plus one byte for the '\n' separator.
            lineStart += Encoding.UTF8.GetByteCount(rawLine) + 1;
        }
        var claims = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in PostFacet.Groups)
            foreach (var claim in await Participant.Query(participant => participant.Handle == group, token))
                claims.Add(group);
        inFence = false;
        lineStart = 0;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal)) inFence = !inFence;
            else if (!inFence)
            {
                foreach (var group in PostFacet.Groups)
                {
                    if (claims.Contains(group) || !targets.Add(group)) continue;
                    var spelled = "@" + group;
                    var index = line.IndexOf(spelled, StringComparison.OrdinalIgnoreCase);
                    while (index >= 0)
                    {
                        var after = index + spelled.Length;
                        var boundaryBefore = index == 0 || char.IsWhiteSpace(line[index - 1]);
                        var boundaryAfter = after >= line.Length || char.IsPunctuation(line[after]) || char.IsWhiteSpace(line[after]);
                        if (boundaryBefore && boundaryAfter)
                        {
                            facets.Add(At(lineStart, line, index, spelled, PostFacet.Group, value: group));
                            break;
                        }
                        index = line.IndexOf(spelled, index + 1, StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
            // The raw segment (including any '\r') plus one byte for the '\n' separator.
            lineStart += Encoding.UTF8.GetByteCount(rawLine) + 1;
        }
        return facets.Count == 0 ? null : PostFacets.Check(text, facets.OrderBy(facet => facet.Start).ToList());
    }

    /// <summary>An exact handle match resolves to a participant DID only when exactly one stored
    /// participant carries that handle — the digest parser's ambiguity guard.</summary>
    private static async Task<string?> ResolveHandle(string lowered, CancellationToken token)
    {
        Participant? single = null;
        foreach (var candidate in await Participant.Query(participant => participant.Handle == lowered, token))
        {
            if (single is not null) return null;
            single = candidate;
        }
        return single?.Id;
    }

    /// <summary>A facet range in whole-text absolute UTF-8 byte offsets: the line's start offset
    /// plus the line-relative prefix bytes, then the token's own byte length.</summary>
    private static PostFacet At(int lineStart, string line, int charIndex, string token, string kind, string? did = null, string? value = null)
    {
        var start = lineStart + Encoding.UTF8.GetByteCount(line[..charIndex]);
        return new PostFacet { Kind = kind, Start = start, End = start + Encoding.UTF8.GetByteCount(token), Did = did, Value = value };
    }
}
