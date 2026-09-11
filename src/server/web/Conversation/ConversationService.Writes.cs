using Koan.Data.Core;
using TangentSpace.Rooms;
using TangentSpace.Activity;
using TangentSpace.AtProtocol;

namespace TangentSpace.Conversation;

public sealed partial class ConversationService
{
    public Task<PendingMessagePage> PendingMessages(string did, string roomKey, CancellationToken ct)
        => governance.WithCurrentPolicy(did, roomKey, async (policy, token) =>
        {
            if (!policy.CanWrite) throw new UnauthorizedAccessException("This room's current rules do not allow recovering pending posts.");
            var pending = await WriteIntent.Query(i => i.AuthorDid == did && i.RoomKey == roomKey && i.State == "pending",
                Window<WriteIntent>(nameof(WriteIntent.Id), 1, 8), token);
            return new PendingMessagePage(pending.Select(i => new PendingMessage(i.OperationId, i.Content.Text, i.Content.ReplyTo, i.Detail)).ToArray());
        }, ct);

    public Task<WriteIntent> Post(string did, string roomKey, PostMessage input, CancellationToken ct)
        => Post(did, roomKey, input, ct, background: false);

    private async Task<WriteIntent> Post(string did, string roomKey, PostMessage input, CancellationToken ct, bool background)
    {
        MessageContent.CheckText(input.Text);
        if (string.IsNullOrWhiteSpace(input.OperationId) || input.OperationId.Length > 128
            || input.OperationId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new ArgumentException("Use an operation ID of 1–128 letters, digits, hyphens or underscores.");
        var facets = PostFacets.Check(input.Text, input.Facets);
        await writes.WaitAsync(ct);
        try
        {
            // Local storage is the atomic upsert path (ADR 0007): the client-minted operation
            // identity is the idempotency key, the row is its own receipt, and no staging
            // intent exists. The durable intent machinery below exists for the asynchronous
            // Spaces pipeline only.
            using (var scope = EntityContext.NoCache())
            {
                var local = await Room.Get(roomKey, ct) ?? throw new InvalidOperationException("The Topic no longer exists.");
                if (local.SpaceState == RoomSpaceState.Local) return await UpsertLocalPost(did, roomKey, input, facets, ct);
            }
            var id = WriteIntent.Key(did, roomKey, input.OperationId);
            var intent = await governance.WithCurrentPolicy(did, roomKey, async (policy, token) =>
            {
                if (!policy.CanWrite) throw new UnauthorizedAccessException("This room's current rules do not allow posting.");
                var previous = await WriteIntent.Get(id, token);
                if (previous is not null)
                {
                    if (previous.Content.Text != input.Text || previous.Content.ReplyTo != input.ReplyTo)
                        throw new ArgumentException("That operation ID was already used with different content.");
                    return previous;
                }
                if (input.ReplyTo is { } reply)
                {
                    var target = await SourceDecision.Get(SourceDecision.Key(roomKey, reply.Uri, reply.Cid), token);
                    if (target?.Accepted != true) throw new ArgumentException("Reply to an accepted message in this room.");
                }
                var created = new WriteIntent { Id = id, AuthorDid = did, RoomKey = roomKey,
                    OperationId = input.OperationId, RecordKey = "op-" + id, SpaceUri = policy.SpaceUri!,
                    Content = new MessageContent(input.Text, clock.GetUtcNow(), input.ReplyTo) };
                await created.Save(token);
                return created;
            }, ct);
            // Recheck after loading under the write gate: another attempt may have discovered
            // missing authorization since the background worker selected this intent.
            if (!ConversationRecovery.CanAttempt(intent, background)) return intent;
            try
            {
                // Creation is insert-only at one deterministic key. RecordAlreadyExists triggers the same source reconciliation.
                await spaces.CreateRecord(did, intent.SpaceUri, intent.RecordKey, intent.Content.ToRecord(), ct);
                var source = await spaces.ReadRepo(did, intent.SpaceUri, did, ct);
                var record = source.Records.SingleOrDefault(r => r.Collection == SpacesOptions.Collection && r.RecordKey == intent.RecordKey)
                    ?? throw new InvalidDataException("The created record is not present in its source repository.");
                try { if (MessageContent.FromCbor(record.DagCbor) != intent.Content) throw new WriteConflict(); }
                catch (Exception error) when (error is ArgumentException or InvalidDataException or System.Formats.Cbor.CborContentException)
                { throw new WriteConflict(); }
                await Accept(roomKey, source, ct);
                await governance.WithCurrentPolicy(did, roomKey, async (_, token) =>
                {
                    intent.SourceUri = intent.SpaceUri + "/" + did + "/" + SpacesOptions.Collection + "/" + intent.RecordKey;
                    intent.SourceCid = record.Cid;
                    var decision = await SourceDecision.Get(SourceDecision.Key(roomKey, intent.SourceUri, record.Cid), token);
                    intent.State = decision?.Accepted == true ? "accepted" : "rejected";
                    intent.Detail = decision?.Reason ?? "source-not-accepted";
                    await intent.Save(token);
                    return true;
                }, ct);
            }
            catch (Exception error) when (error is SpacesUnavailable or HttpRequestException or InvalidDataException or System.Text.Json.JsonException or WriteConflict)
            {
                if (error is SpacesUnavailable unavailable)
                    logger.LogWarning("Conversation source request incomplete: stage={Stage}, HTTP={Status}, error={Code}",
                        unavailable.Stage, unavailable.Status,
                        unavailable.Code.Length < 80 && unavailable.Code.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') ? unavailable.Code : "provider-error");
                else if (error is InvalidDataException)
                    logger.LogWarning("Conversation source verification incomplete: {Reason}", error.Message);
                await governance.WithCurrentPolicy(did, roomKey, async (_, token) =>
                {
                    ConversationRecovery.RecordFailure(intent, error);
                    await intent.Save(token);
                    return true;
                }, ct);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                await governance.WithCurrentPolicy(did, roomKey, async (_, token) =>
                {
                    intent.Detail = "source-timeout-retry-same-operation";
                    await intent.Save(token);
                    return true;
                }, ct);
            }
            return intent;
        }
        finally { writes.Release(); }
    }

    /// <summary>Local-storage write (ADR 0007): an atomic upsert at the client-minted
    /// operation identity. First delivery creates the durable decision, its projection and
    /// the sequence in one transaction; a re-delivery of the same package returns the same
    /// row unchanged; the same key with different content is a conflict. The returned intent
    /// is a result carrier, not a staged row — the projection is the receipt.</summary>
    private async Task<WriteIntent> UpsertLocalPost(string did, string roomKey, PostMessage input, IReadOnlyList<PostFacet>? facets, CancellationToken ct)
    {
        var uri = $"local://{roomKey}/{input.OperationId}";
        var cid = "local-" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(uri)))[..24];
        WriteIntent? result = null;
        var conflict = false;
        await governance.WithCurrentPolicy(did, roomKey, async (policy, token) =>
        {
            if (!policy.CanWrite) throw new UnauthorizedAccessException("This room's current rules do not allow posting.");
            var existing = (await Message.Query(m => m.RoomKey == roomKey && m.AuthorDid == did && m.OperationId == input.OperationId,
                Window<Message>(nameof(Message.Sequence), 1, 1), token)).FirstOrDefault();
            if (existing is { } found)
            {
                if (found.Removed || found.Content.Text != input.Text || found.Content.ReplyTo != input.ReplyTo
                    || Canonical(found.Facets) != Canonical(facets))
                {
                    conflict = true;
                    return true;
                }
                result = Carried(did, roomKey, input, facets, uri, cid, found.Content.CreatedAt);
                return true;
            }
            if (input.ReplyTo is { } reply)
            {
                var target = await SourceDecision.Get(SourceDecision.Key(roomKey, reply.Uri, reply.Cid), token);
                if (target?.Accepted != true) throw new ArgumentException("Reply to an accepted message in this room.");
            }
            var state = await RoomConversation.Get(roomKey, token) ?? new RoomConversation { Id = roomKey };
            var content = new MessageContent(input.Text, clock.GetUtcNow(), input.ReplyTo);
            var decision = new SourceDecision
            {
                Id = SourceDecision.Key(roomKey, uri, cid), RoomKey = roomKey, AuthorDid = did, SourceUri = uri, SourceCid = cid,
                Accepted = true, Reason = "accepted-local", DecidedAt = clock.GetUtcNow(),
                PolicyRevision = policy.SelectedPolicyRevision, SitePolicyRevision = policy.SitePolicyRevision,
                Content = content, Sequence = checked(++state.LastSequence),
            };
            await decision.Save(token);
            var projected = Message.Project(decision);
            projected.OperationId = input.OperationId;
            projected.Facets = facets;
            await projected.Save(token);
            await state.Save(token);
            var room = await Room.Get(roomKey, token);
            await ActivityJournal.AppendInTransaction(ActivityKind.MessageAccepted, roomKey, did, null,
                room?.TangentKey, decision.Sequence, decision.DecidedAt, token);
            result = Carried(did, roomKey, input, facets, uri, cid, content.CreatedAt);
            return true;
        }, ct);
        if (conflict) throw new ArgumentException("That operation ID was already used with different content.");
        updates.Pulse(roomKey);
        ActivityJournal.SignalAfterCommit();
        return result!;
    }

    /// <summary>The unsaved result carrier shaped like the Spaces intent, so both paths share
    /// one caller contract: accepted local posts are terminal the moment the row exists.</summary>
    private static WriteIntent Carried(string did, string roomKey, PostMessage input, IReadOnlyList<PostFacet>? facets, string uri, string cid, DateTimeOffset createdAt)
        => new()
        {
            AuthorDid = did, RoomKey = roomKey, OperationId = input.OperationId,
            Content = new MessageContent(input.Text, createdAt, input.ReplyTo),
            State = "accepted", Detail = "local-storage", SourceUri = uri, SourceCid = cid,
        };

    /// <summary>Canonical facet payload for the idempotency conflict check.</summary>
    private static string Canonical(IReadOnlyList<PostFacet>? facets)
        => facets is null || facets.Count == 0 ? ""
            : string.Join("|", facets.OrderBy(facet => facet.Start).ThenBy(facet => facet.Kind).Select(facet => facet.Canonical()));
}
