using System.Security.Claims;
using Koan.Data.Core;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sorting;
using Newtonsoft.Json.Linq;
using TangentSpace.AtProtocol;
using TangentSpace.Communities;
using TangentSpace.Conversation;
using TangentSpace.Participation;
using TangentSpace.Rooms;

namespace TangentSpace.Mcp;

public sealed partial class McpOperationDispatcher
{
    private async Task<ToolResult> ReadChannel(ClaimsPrincipal principal, McpContext context, McpIdentity identity, JObject arguments, CancellationToken ct)
    {
        var args = McpArguments.ReadChannel(arguments);
        var destination = ChannelDestination(args.ChannelRef, "channelRef");
        ParticipationAccess.Require(principal, ParticipationGrants.Read);
        McpMessageWindow window;
        string? anchorId = null;
        if (args.AroundMessageRef is { } anchorRef)
        {
            var anchor = refs.ParseMessage(anchorRef);
            if (anchor is null || anchor.Value.RoomKey != destination.Room || anchor.Value.TangentKey != destination.Tangent)
                throw new McpInvalidArgumentsException("aroundMessageRef", "Choose a message in this Channel.");
            anchorId = anchor.Value.MessageId;
        }
        try
        {
            window = await conversation.McpWindow(context.ParticipantDid, destination.Room, args.Cursor, anchorId, args.Limit, ct);
        }
        catch (ArgumentException error)
        {
            if (error.Message.Contains("cursor", StringComparison.Ordinal))
            {
                return await Problem("ReadChannel", context.CompanionId, context.Id, identity, await ServerFallback(principal, context.ParticipantDid, context.CredentialId, ct),
                    McpProblem.Of(McpProblemCodes.CursorExpired, "This history cursor expired or no longer matches. Open a fresh window explicitly."),
                    calls: [NextCall("ReadChannel", "Open the conversation", McpJson.Arguments(new Dictionary<string, string?>
                        { ["contextId"] = context.Id, ["channelRef"] = args.ChannelRef }))],
                    available: ["ReadChannel", "GetUpdates"],
                    did: context.ParticipantDid, credential: context.CredentialId, ct: ct);
            }
            throw new McpInvalidArgumentsException(args.AroundMessageRef is null ? "cursor" : "aroundMessageRef", error.Message);
        }
        var messages = await ToDtos(destination.Tangent, destination.Room, window.Messages, window.AuthorHandles, ct);
        var place = await ChannelPlace(context, destination, ct);
        var data = new McpReadData(messages, window.OlderCursor, window.NewerCursor, window.ReadCursor, window.Position);
        var calls = new List<McpNextCall>();
        if (window.OlderCursor is not null)
            calls.Add(NextCall("ReadChannel", "Read older messages", McpJson.Arguments(new Dictionary<string, string?>
                { ["contextId"] = context.Id, ["channelRef"] = args.ChannelRef, ["cursor"] = window.OlderCursor })));
        else if (messages.Count > 0)
            calls.Add(NextCall("ReadChannel", "Open the conversation", McpJson.Arguments(new Dictionary<string, string?>
                { ["contextId"] = context.Id, ["channelRef"] = args.ChannelRef })));
        return Assemble("ReadChannel", "ok", context.CompanionId, context.Id, identity, place,
            new McpResult(data, null, null),
            await ActivitySegment(context.ParticipantDid, context.CredentialId, destination.Tangent, destination.Room, ct),
            new McpNext(DefaultAvailable(place, selected: true), calls));
    }

    private async Task<ToolResult> PostMessage(ClaimsPrincipal principal, McpContext context, McpIdentity identity, JObject arguments, CancellationToken ct)
    {
        var args = McpArguments.PostMessage(arguments);
        McpArguments.CheckText(args.Text);
        var destination = ChannelDestination(args.ChannelRef, "channelRef");
        ParticipationAccess.Require(principal, ParticipationGrants.Post);
        var currentPolicy = await conversation.ReadPolicy(context.ParticipantDid, destination.Room, ct);
        if (!currentPolicy.CanWrite) throw new UnauthorizedAccessException();
        SourceReference? replyTo = null;
        if (args.ReplyTo is { } reference)
        {
            var target = refs.ParseMessage(reference)
                ?? throw new McpInvalidArgumentsException("replyTo", "Copy a message reference returned by this server.");
            if (target.TangentKey != destination.Tangent || target.RoomKey != destination.Room || reference != refs.Message(target.TangentKey, target.RoomKey, target.MessageId))
                throw new McpInvalidArgumentsException("replyTo", "Reply to a message in this Channel.");
            using var fresh = EntityContext.NoCache();
            var anchor = await Message.Get(target.MessageId, ct);
            if (anchor is null || anchor.RoomKey != destination.Room)
                throw new McpInvalidArgumentsException("replyTo", "Reply to a message in this Channel.");
            replyTo = new SourceReference(anchor.SourceUri, anchor.SourceCid);
        }
        // Register before the side effect under the per-key gate; a crash reconciles through the same
        // durable WriteIntent at the namespaced operation id below.
        var payload = new Dictionary<string, string?> { ["text"] = args.Text, ["replyTo"] = args.ReplyTo };
        return await requests.Run(context.CredentialId, context.ParticipantDid, args.RequestId, async () =>
        {
            var registration = await requests.Register(context.CredentialId, context.ParticipantDid, args.RequestId, "PostMessage",
                destination.Room, payload, ct);
            if (registration.Reused && registration.Record.State == "completed" && registration.Record.ResultData is not null)
                return await Replay("PostMessage", principal, context, identity, registration.Record, ct);
            // Namespace the underlying operation id to this credential+DID so the same requestId under a
            // different runtime authorization can never collide in the shared write-intent key space.
            var operationId = registration.Record.NamespacedOperationId
                ?? McpRequestRecord.BuildOperationId(context.CredentialId, context.ParticipantDid, args.RequestId);
            var intent = await conversation.Post(context.ParticipantDid, destination.Room,
                new PostMessage(operationId, args.Text, replyTo), ct);
            return await PostOutcome(principal, context, identity, destination, args, intent, registration.Record, ct);
        }, ct);
    }

    private async Task<ToolResult> PostOutcome(ClaimsPrincipal principal, McpContext context, McpIdentity identity,
        (string Tangent, string Room) destination, McpArguments.PostMessageArgs args, WriteIntent intent,
        McpRequestRecord record, CancellationToken ct)
    {
        var place = await ChannelPlace(context, destination, ct);
        var checkOperation = NextCall("GetOperation", "Check your saved action", McpJson.Arguments(new Dictionary<string, string?>
            { ["contextId"] = context.Id, ["requestId"] = args.RequestId }));
        if (intent.State == "accepted")
        {
            string? resultRef = null;
            var authorHandle = context.ParticipantDid;
            using (var fresh = EntityContext.NoCache())
            {
                var projected = (await Message.Query(
                    message => message.RoomKey == destination.Room && message.SourceUri == intent.SourceUri
                        && message.SourceCid == intent.SourceCid, One(), ct)).FirstOrDefault();
                if (projected is not null)
                {
                    resultRef = refs.Message(destination.Tangent, destination.Room, projected.Id);
                    var author = await Participants.Participant.Get(projected.AuthorDid, ct);
                    if (author?.Handle is { Length: > 0 }) authorHandle = author.Handle;
                }
            }
            var message = new McpMessageDto(resultRef ?? intent.Id, context.ParticipantDid, authorHandle,
                intent.Content.Text, Format(intent.Content.CreatedAt), args.ReplyTo, false);
            var data = new McpPostData(message);
            await requests.Complete(record, "completed", resultRef, SerializeData(data), ct);
            var receipt = new McpReceipt(args.RequestId, "op_" + args.RequestId, "completed", resultRef, null);
            return Assemble("PostMessage", "ok", context.CompanionId, context.Id, identity, place,
                new McpResult(data, receipt, null),
                await ActivitySegment(context.ParticipantDid, context.CredentialId, destination.Tangent, destination.Room, ct),
                new McpNext(DefaultAvailable(place, selected: true),
                    [NextCall("ReadChannel", "Open the conversation", McpJson.Arguments(new Dictionary<string, string?>
                        { ["contextId"] = context.Id, ["channelRef"] = args.ChannelRef }))]));
        }
        var readiness = await ReadinessOf(context.ParticipantDid, ct);
        if (intent.State == "rejected")
        {
            await requests.Complete(record, "rejected", null, null, ct);
            return await Problem("PostMessage", context.CompanionId, context.Id, identity, place,
                McpProblem.Of(McpProblemCodes.SourceUnsupported, "The source rejected this message. Inspect the receipt."),
                calls: [checkOperation], available: ["GetOperation", "ReadChannel"],
                did: context.ParticipantDid, credential: context.CredentialId, ct: ct);
        }
        // Pending: the durable WriteIntent is the saved action; never a fake local message.
        await requests.Complete(record, "pending", null, null, ct);
        var receiptPending = new McpReceipt(args.RequestId, "op_" + args.RequestId, "pending", null, 15);
        if (readiness is "needs_connection" or "unsupported")
        {
            var problem = readiness == "unsupported"
                ? McpProblem.Of(McpProblemCodes.SourceUnsupported, "This account provider cannot supply the native capability this message needs.")
                : McpProblem.Of(McpProblemCodes.SourcePermissionMissing, "Your message is saved. The operator must connect native room access before this request can finish.");
            return Assemble("PostMessage", "blocked", context.CompanionId, context.Id, identity, place with { Readiness = readiness },
                new McpResult(null, receiptPending, problem),
                await ActivitySegment(context.ParticipantDid, context.CredentialId, null, null, ct),
                new McpNext(["GetOperation"], [checkOperation]));
        }
        return Assemble("PostMessage", "pending", context.CompanionId, context.Id, identity, place,
            new McpResult(null, receiptPending, null),
            await ActivitySegment(context.ParticipantDid, context.CredentialId, destination.Tangent, destination.Room, ct),
            new McpNext(["GetOperation", "ReadChannel"], [checkOperation]));
    }

    private async Task<ToolResult> GetUpdates(ClaimsPrincipal principal, McpContext context, McpIdentity identity, JObject arguments, CancellationToken ct)
    {
        var args = McpArguments.GetUpdates(arguments);
        string? tangentScope = null, roomScope = null;
        if (args.ScopeRef is { } scope)
        {
            if (refs.ParseChannel(scope) is { } channel)
            {
                await conversation.ReadPolicy(context.ParticipantDid, channel.RoomKey, ct);
                tangentScope = channel.TangentKey;
                roomScope = channel.RoomKey;
            }
            else if (refs.ParseTangent(scope) is { } tangent)
            {
                if (!await tangents.CanAccess(context.ParticipantDid, tangent, ct)) throw new UnauthorizedAccessException();
                tangentScope = tangent;
            }
            else if (scope != refs.ServerRef) throw new McpInvalidArgumentsException("scopeRef", "Copy a server, channel or tangent reference returned by this server.");
        }
        ParticipationAccess.Require(principal, ParticipationGrants.Read);
        var snapshot = await SnapshotActivity(context.ParticipantDid, context.CredentialId, args.Cursor, tangentScope, roomScope, args.Limit, ct);
        var serverPlace = new McpPlace("server", await SiteLabel(ct), refs.ServerRef, null, null,
            GrantPermissions(principal), await ReadinessOf(context.ParticipantDid, ct));
        var calls = new List<McpNextCall>();
        if (snapshot.Notices.Count > 0)
            calls.Add(NextCall("ReadChannel", "Open the conversation", McpJson.Arguments(new Dictionary<string, string?>
                { ["contextId"] = context.Id, ["channelRef"] = snapshot.Notices[0].ChannelRef })));
        if (snapshot.Continuation is { } continuation)
            calls.Add(NextCall("GetUpdates", "Continue your updates", McpJson.Arguments(new Dictionary<string, string?>
                { ["contextId"] = context.Id, ["scopeRef"] = args.ScopeRef ?? refs.ServerRef, ["cursor"] = continuation })));
        return Assemble("GetUpdates", "ok", context.CompanionId, context.Id, identity, serverPlace,
            new McpResult(new McpUpdatesData(snapshot.Notices, snapshot.Continuation, snapshot.Checkpoint, snapshot.Incomplete), null, null),
            await ActivitySegment(context.ParticipantDid, context.CredentialId, null, null, ct),
            new McpNext(DefaultAvailable(serverPlace, selected: true), calls));
    }

    private async Task<ToolResult> MarkRead(ClaimsPrincipal principal, McpContext context, McpIdentity identity, JObject arguments, CancellationToken ct)
    {
        var args = McpArguments.MarkRead(arguments);
        var destination = ChannelDestination(args.ChannelRef, "channelRef");
        ParticipationAccess.Require(principal, ParticipationGrants.Read);
        var payload = new Dictionary<string, string?> { ["readCursor"] = args.ReadCursor };
        return await requests.Run(context.CredentialId, context.ParticipantDid, args.RequestId, async () =>
        {
            var registration = await requests.Register(context.CredentialId, context.ParticipantDid, args.RequestId, "MarkRead",
                destination.Room, payload, ct);
            if (registration.Reused && registration.Record.State == "completed" && registration.Record.ResultData is not null)
                return await Replay("MarkRead", principal, context, identity, registration.Record, ct);
            requests.CompleteWithDomain(registration.Record, _ => ("completed", null,
                SerializeData(new McpMarkReadData(null, "did_channel"))));
            long sequence;
            try
            {
                sequence = await conversation.Acknowledge(context.ParticipantDid, destination.Room, args.ReadCursor, ct);
            }
            catch (ArgumentException error)
            {
                var invalid = error.Message.Contains("resume", StringComparison.Ordinal) || error.Message.Contains("Acknowledge", StringComparison.Ordinal);
                return await Problem("MarkRead", context.CompanionId, context.Id, identity, await ServerFallback(principal, context.ParticipantDid, context.CredentialId, ct),
                    McpProblem.Of(invalid ? McpProblemCodes.InvalidArguments : McpProblemCodes.CursorExpired,
                        invalid ? "Acknowledge a read cursor returned with a history page."
                            : "This read cursor expired or belongs to another participant. Open a fresh window.", "readCursor"),
                    available: ["ReadChannel", "GetUpdates"],
                    did: context.ParticipantDid, credential: context.CredentialId, ct: ct);
            }
            string? throughRef = null;
            using (var fresh = EntityContext.NoCache())
            {
                var through = (await Message.Query(message => message.RoomKey == destination.Room && message.Sequence == sequence, One(), ct))
                    .FirstOrDefault();
                throughRef = through is null ? null : refs.Message(destination.Tangent, destination.Room, through.Id);
            }
            var data = new McpMarkReadData(throughRef, "did_channel");
            await requests.Complete(registration.Record, "completed", null, SerializeData(data), ct);
            var place = await ChannelPlace(context, destination, ct);
            return Assemble("MarkRead", "ok", context.CompanionId, context.Id, identity, place,
                new McpResult(data, new McpReceipt(args.RequestId, "op_" + args.RequestId, "completed", null, null), null),
                await ActivitySegment(context.ParticipantDid, context.CredentialId, destination.Tangent, destination.Room, ct),
                new McpNext(DefaultAvailable(place, selected: true),
                    [NextCall("ReadChannel", "Open the conversation", McpJson.Arguments(new Dictionary<string, string?>
                        { ["contextId"] = context.Id, ["channelRef"] = args.ChannelRef }))]));
        }, ct);
    }

    private (string Tangent, string Room) ChannelDestination(string? reference, string field)
    {
        var parsed = refs.ParseChannel(reference)
            ?? throw new McpInvalidArgumentsException(field, "Copy a channel reference returned by this server.");
        if (reference != refs.Channel(parsed.TangentKey, parsed.RoomKey))
            throw new McpInvalidArgumentsException(field, "That channel reference does not belong to this server.");
        return (parsed.TangentKey, parsed.RoomKey);
    }

    private async Task<McpPlace> ChannelPlace(McpContext context, (string Tangent, string Room) destination, CancellationToken ct)
    {
        var policy = await conversation.ReadPolicy(context.ParticipantDid, destination.Room, ct);
        using var fresh = EntityContext.NoCache();
        var room = await Room.Get(destination.Room, ct);
        var tangent = await TangentCommunity.Get(destination.Tangent, ct);
        var label = $"{(tangent?.Name.Length > 0 ? tangent.Name : destination.Tangent)} / {(room?.Title.Length > 0 ? room.Title : destination.Room)}";
        var permissions = new List<string>();
        if (policy.CanRead) permissions.Add("read");
        if (policy.CanWrite) permissions.Add("post");
        return new McpPlace("channel", Preview(label, 160), refs.ServerRef, refs.Tangent(destination.Tangent),
            refs.Channel(destination.Tangent, destination.Room), permissions, await ReadinessOf(context.ParticipantDid, ct));
    }

    private async Task<List<McpMessageDto>> ToDtos(string tangentKey, string roomKey, IReadOnlyList<Message> messages,
        IReadOnlyDictionary<string, string> handles, CancellationToken ct)
    {
        using var fresh = EntityContext.NoCache();
        var dtos = new List<McpMessageDto>(messages.Count);
        foreach (var message in messages)
        {
            string? replyTo = null;
            if (message.Content.ReplyTo is { } parent)
            {
                var projection = (await Message.Query(
                    row => row.RoomKey == roomKey && row.SourceUri == parent.Uri, One(), ct))
                    .FirstOrDefault();
                replyTo = projection is null ? null : refs.Message(tangentKey, roomKey, projection.Id);
            }
            dtos.Add(new McpMessageDto(refs.Message(tangentKey, roomKey, message.Id), message.AuthorDid,
                handles.GetValueOrDefault(message.AuthorDid, message.AuthorDid), message.Removed ? "" : message.Content.Text,
                Format(message.AcceptedAt), replyTo, message.Removed, message.Permissions, message.EditedAt is {} edited ? Format(edited) : null));
        }
        return dtos;
    }

    private static string SerializeData<T>(T data)
        => System.Text.Json.JsonSerializer.Serialize(data, McpJson.Options);

    internal static QueryDefinition One()
    {
        var member = typeof(Message).GetProperty(nameof(Message.Id))!;
        return new QueryDefinition { Page = 1, PageSize = 1,
            Sort = [new SortSpec(new MemberPath(typeof(Message), [member], member.PropertyType, false, -1), false)] };
    }
}
