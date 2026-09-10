using Koan.Data.Core;
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
        await writes.WaitAsync(ct);
        try
        {
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
}
