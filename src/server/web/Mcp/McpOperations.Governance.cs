using System.Security.Claims;
using Koan.Data.Core;
using Newtonsoft.Json.Linq;
using TangentSpace.Authorization;
using TangentSpace.Conversation;
using TangentSpace.Participants;
using TangentSpace.Participation;
using TangentSpace.Rooms;
using TangentSpace.Site;

namespace TangentSpace.Mcp;

public sealed partial class McpOperationDispatcher
{
    private static ParticipantClassification ParseClassification(string value) => value switch { "human" => ParticipantClassification.Human, "agent" => ParticipantClassification.Agent, "undeclared" => ParticipantClassification.Undeclared, _ => throw new McpInvalidArgumentsException("classification", "Choose human, agent, or undeclared.") };

    private static bool ActionGranted(string action, bool read, bool post, bool manage) => action switch
    {
        "read" => read,
        "reply" or "editOwnPost" or "deleteOwnPost" or "createTopic" or "createTangent" => post,
        _ => manage
    };

    private static PermissionView? ClampAccess(PermissionView? access, bool read, bool post, bool manage)
        => access is null ? null : access with { AllowedActions = access.AllowedActions.Where(a => ActionGranted(a, read, post, manage)).ToArray() };

    private async Task<ToolResult> WithAccess(McpContext context, ToolResult result, CancellationToken ct)
    {
        var place = result.Envelope.Place;
        PermissionView? access;
        if (refs.ParseChannel(place.ChannelRef) is {} topic)
            access = Permissions.Topic(await conversation.ReadPolicy(context.ParticipantDid, topic.RoomKey, ct));
        else if (refs.ParseTangent(place.TangentRef) is {} tangent)
            access = (await FindTangent(context, tangent, ct))?.Permissions;
        else access = (await server.Read(context.ParticipantDid, ct)).Permissions;
        var next = result.Envelope.Next.Available.ToList();
        next.Add("GetPermissions");
        if (access?.AllowedActions.Contains("manageServer") == true) next.Add("ConfigureServer");
        if (access?.AllowedActions.Contains("createTangent") == true) next.Add("CreateTangent");
        if (access?.AllowedActions.Contains("manageTangent") == true) next.Add("ConfigureTangent");
        if (access?.AllowedActions.Contains("createTopic") == true) next.Add("CreateChannel");
        if (access?.AllowedActions.Contains("manageTopic") == true) next.Add("ConfigureTopic");
        return result with { Envelope = result.Envelope with {
            Place = place with { Access = access }, Next = result.Envelope.Next with { Available = next.Distinct().ToArray() } } };
    }

    private async Task<ToolResult> Govern(ClaimsPrincipal principal, McpContext context, McpIdentity identity,
        string operation, JObject args, CancellationToken ct)
    {
        var schema = catalog.Find(operation)!.InputSchema["properties"]!.AsObject();
        foreach (var field in args.Properties())
            if (!schema.ContainsKey(McpVocabulary.Public(field.Name))) throw new McpInvalidArgumentsException(field.Name, "Unknown field.");
        string? Text(string field, int maximum = 4096, bool required = false)
        {
            if (args[field] is null && !required) return null;
            if (args[field]?.Type != JTokenType.String || args.Value<string>(field) is not {} value || value.Length > maximum || (required && value.Length == 0))
                throw new McpInvalidArgumentsException(field, "Supply a string within the documented length.");
            return value;
        }
        bool? Flag(string field)
        {
            if (args[field] is null) return null;
            if (args[field]?.Type != JTokenType.Boolean) throw new McpInvalidArgumentsException(field, "Supply true or false.");
            return args.Value<bool>(field);
        }
        int? Number(string field)
        {
            if (args[field] is null) return null;
            if (args[field]?.Type != JTokenType.Integer || !int.TryParse(args[field]!.ToString(), out var value))
                throw new McpInvalidArgumentsException(field, "Supply an integer.");
            return value;
        }
        var did = context.ParticipantDid;
        var scope = Text("scopeRef", 512) ?? Text("channelRef", 512) ?? Text("tangentRef", 512) ?? refs.ServerRef;
        var place = await ServerFallback(principal, did, context.CredentialId, ct);
        object data;
        if (operation == "GetPermissions")
        {
            if (refs.ParseMessage(scope) is {} post)
            {
                var policy = await conversation.ReadPolicy(did, post.RoomKey, ct);
                if (!policy.CanRead) throw new UnauthorizedAccessException();
                using var fresh = EntityContext.NoCache();
                var message = await Message.Get(post.MessageId, ct);
                if (message is null || message.RoomKey != post.RoomKey) throw new UnauthorizedAccessException();
                data = Permissions.Post(policy, message.AuthorDid, message.Removed);
                place = await ChannelPlace(context, (post.TangentKey, post.RoomKey), ct);
            }
            else if (refs.ParseChannel(scope) is {} topic)
            {
                place = await ChannelPlace(context, (topic.TangentKey, topic.RoomKey), ct);
                var policy = await conversation.ReadPolicy(did, topic.RoomKey, ct);
                if (!policy.CanRead && !policy.CanManage) throw new UnauthorizedAccessException();
                data = Permissions.Topic(policy);
            }
            else if (refs.ParseTangent(scope) is {} tangent)
                data = (await FindTangent(context, tangent, ct))?.Permissions ?? throw new UnauthorizedAccessException();
            else if (scope == refs.ServerRef) data = await server.Read(did, ct);
            else throw new McpInvalidArgumentsException("scopeRef", "Copy a reference returned by this server.");
            return Assemble(operation, "ok", context.CompanionId, context.Id, identity, place,
                new McpResult(data, null, null), await ActivitySegment(did, context.CredentialId, null, null, ct), new McpNext(["GetUpdates"], []));
        }
        ParticipationAccess.Require(principal, operation == "DeclareParticipant" ? ParticipationGrants.Read : ParticipationGrants.Manage);
        var requestId = Text("requestId", 128, true)!;
        McpRequestRecord.CheckRequestId(requestId);
        return await requests.Run(context.CredentialId, did, requestId, async () =>
        {
            var payload = args.Properties().Where(p => p.Name is not "requestId" and not "contextId")
                .ToDictionary(p => p.Name, p => (string?)p.Value.ToString(Newtonsoft.Json.Formatting.None));
            var registration = await requests.Register(context.CredentialId, did, requestId, operation, scope, payload, ct);
            if (registration.Reused && registration.Record.State == "completed")
            {
                var authorized = operation is "DeclareParticipant" or "ClaimServer" || operation == "ConfigureServer" && (await server.Read(did, ct)).CanManage;
                if (operation == "ConfigureTopic" && refs.ParseChannel(scope) is {} existingTopic)
                    authorized = (await conversation.ReadPolicy(did, existingTopic.RoomKey, ct)).CanManage;
                if (operation == "ConfigureTangent" && refs.ParseTangent(scope) is {} existingTangent)
                    authorized = (await FindTangent(context, existingTangent, ct))?.CanManage == true;
                if (!authorized) throw new UnauthorizedAccessException();
                return Assemble(operation, "ok", context.CompanionId, context.Id, identity, place,
                    new McpResult(new { applied = true }, new McpReceipt(requestId,"op_" + requestId,"completed",null,null),null),
                    await ActivitySegment(did, context.CredentialId, null, null, ct), new McpNext(["GetPermissions"], []));
            }
            data = operation switch
            {
                "ConfigureServer" => await server.Update(did, new ServerSettingsPatch(Text("name",120), Text("welcomeMessage"), Text("motd"), Text("creationPolicy",32), Flag("allowAgentTangentOwnership"), Text("byline",240), Text("coverImageUrl",2048), Text("backgroundScene",16), Text("backgroundColor",7), Number("backgroundIntensity"), Flag("backgroundMotion"), Flag("backgroundMouseSpotlight")), ct),
                "ClaimServer" => await server.Claim(did, Flag("humanDeclaration") == true, ct),
                "DeclareParticipant" => await server.Declare(did, ParseClassification(Text("classification",16,true)!), ct),
                "ConfigureTangent" => await tangents.Change(did, refs.ParseTangent(Text("tangentRef",512,true)) ?? throw new McpInvalidArgumentsException("tangentRef", "Copy a Tangent reference."), Text("name",80), Text("description",240), Text("motto",160), Text("accent",32), Text("artwork",1024), ct, Flag("allowMemberTopics")),
                "ConfigureTopic" => await ConfigureTopic(),
                _ => throw new McpUnknownToolException(operation)
            };
            await requests.Complete(registration.Record, "completed", null, SerializeData(data), ct);
            return Assemble(operation, "ok", context.CompanionId, context.Id, identity, place,
                new McpResult(data, new McpReceipt(requestId,"op_" + requestId,"completed",null,null),null),
                await ActivitySegment(did, context.CredentialId, null, null, ct), new McpNext(["GetPermissions", "GetUpdates"], []));
        }, ct);

        async Task<object> ConfigureTopic()
        {
            var topic = ChannelDestination(Text("channelRef",512,true), "channelRef");
            var policy = await conversation.ReadPolicy(did, topic.Room, ct);
            var result = await rooms.SetSettings(did, topic.Room, Flag("allowPostEditing") ?? policy.EditingAllowed,
                Flag("isLocked") ?? policy.Locked, Text("title",120), Text("topic",4096), ct);
            if (!result.Accepted) throw new RoomRuleViolation(result.Denial ?? RoomDenial.Forbidden, result.Reason);
            return result;
        }
    }
}
