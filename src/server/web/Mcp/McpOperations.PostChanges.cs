using System.Security.Claims;
using Newtonsoft.Json.Linq;
using Koan.Data.Core;
using TangentSpace.Conversation;
using TangentSpace.Participation;

namespace TangentSpace.Mcp;

public sealed partial class McpOperationDispatcher
{
    // These handlers deliberately resolve the message before choosing the transport grant:
    // authors need post, while a manager removing somebody else's post needs manage.
    private Task<ToolResult> EditPost(ClaimsPrincipal principal, McpContext context, McpIdentity identity,
        JObject arguments, CancellationToken ct) => ChangePostMcp(principal, context, identity, arguments, delete: false, ct);

    private Task<ToolResult> DeletePost(ClaimsPrincipal principal, McpContext context, McpIdentity identity,
        JObject arguments, CancellationToken ct) => ChangePostMcp(principal, context, identity, arguments, delete: true, ct);

    private async Task<ToolResult> ChangePostMcp(ClaimsPrincipal principal, McpContext context, McpIdentity identity,
        JObject arguments, bool delete, CancellationToken ct)
    {
        var parsed = ParsePostChange(arguments, delete);
        var target = refs.ParseMessage(parsed.MessageRef)
            ?? throw new McpInvalidArgumentsException("messageRef", "Copy a message reference returned by this server.");
        var channel = refs.ParseChannel(refs.Channel(target.TangentKey, target.RoomKey))!.Value;
        using var fresh = EntityContext.NoCache();
        var message = await Message.Get(target.MessageId, ct);
        if (message is null || message.RoomKey != channel.RoomKey)
            throw new McpInvalidArgumentsException("messageRef", "Choose a message in this Channel.");
        var own = message.AuthorDid == context.ParticipantDid;
        ParticipationAccess.Require(principal, own ? ParticipationGrants.Post : ParticipationGrants.Manage);
        await conversation.ReadPolicy(context.ParticipantDid, channel.RoomKey, ct);
        var destination = (Tangent: channel.TangentKey, Room: channel.RoomKey);
        var payload = new Dictionary<string, string?> { ["messageRef"] = parsed.MessageRef, ["text"] = parsed.Text };
        return await requests.Run(context.CredentialId, context.ParticipantDid, parsed.RequestId, async () =>
        {
            var registration = await requests.Register(context.CredentialId, context.ParticipantDid, parsed.RequestId,
                delete ? "DeletePost" : "EditPost", channel.RoomKey, payload, ct);
            if (registration.Reused && registration.Record.State == "completed" && registration.Record.ResultData is not null)
                return await Replay(delete ? "DeletePost" : "EditPost", principal, context, identity, registration.Record, ct);
            var operationId = registration.Record.NamespacedOperationId
                ?? McpRequestRecord.BuildOperationId(context.CredentialId, context.ParticipantDid, parsed.RequestId);
            var result = await conversation.ChangePost(context.ParticipantDid, channel.RoomKey, target.MessageId,
                parsed.Text, delete, operationId, ct);
            var terminal = result.State is "accepted" or "deleted" or "moderated";
            var state = terminal ? "completed" : "pending";
            var resultRef = refs.Message(channel.TangentKey, channel.RoomKey, result.MessageId);
            var receipt = new McpReceipt(parsed.RequestId, "op_" + parsed.RequestId, state,
                terminal ? resultRef : null, terminal ? null : 15);
            var place = await ChannelPlace(context, destination, ct);
            await requests.Complete(registration.Record, state, terminal ? resultRef : null,
                terminal ? SerializeData(new { messageRef = resultRef, state = result.State, detail = result.Detail }) : null, ct);
            var problem = terminal ? null : McpProblem.Of(McpProblemCodes.Unreachable,
                "The native service could not confirm this post change. Keep the request ID and check its receipt.");
            return Assemble(delete ? "DeletePost" : "EditPost", terminal ? "ok" : "pending", context.CompanionId,
                context.Id, identity, place,
                new McpResult(terminal ? new { messageRef = resultRef, state = result.State, detail = result.Detail } : null,
                    receipt, problem),
                await ActivitySegment(context.ParticipantDid, context.CredentialId, destination.Tangent, destination.Room, ct),
                new McpNext(terminal ? ["ReadChannel", "GetUpdates"] : ["GetOperation"], []));
        }, ct);
    }

    private static (string MessageRef, string? Text, string RequestId) ParsePostChange(JObject args, bool delete)
    {
        var allowed = delete ? new[] { "contextId", "messageRef", "requestId" } : new[] { "contextId", "messageRef", "text", "requestId" };
        foreach (var property in args.Properties())
            if (!allowed.Contains(property.Name, StringComparer.Ordinal))
                throw new McpInvalidArgumentsException(property.Name, "This field is not supported for this operation.");
        static string Required(JObject value, string name, int max)
        {
            if (value[name]?.Type != JTokenType.String) throw new McpInvalidArgumentsException(name, "This field is required.");
            var text = value.Value<string>(name)!;
            if (text.Length < 1 || text.Length > max) throw new McpInvalidArgumentsException(name, "This field has an invalid length.");
            return text;
        }
        var message = Required(args, "messageRef", 512);
        _ = Required(args, "contextId", 96);
        var request = Required(args, "requestId", 128);
        McpRequestRecord.CheckRequestId(request);
        string? text = null;
        if (!delete)
        {
            text = Required(args, "text", 4096);
            MessageContent.CheckText(text);
        }
        return (message, text, request);
    }
}
