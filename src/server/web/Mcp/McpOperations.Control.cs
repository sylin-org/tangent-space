using System.Security.Claims;
using System.Text.Json.Nodes;
using Koan.Data.Core;
using Newtonsoft.Json.Linq;
using TangentSpace.Activity;
using TangentSpace.Communities;
using TangentSpace.Conversation;
using TangentSpace.Participation;
using TangentSpace.Rooms;

namespace TangentSpace.Mcp;

public sealed partial class McpOperationDispatcher
{
    private async Task<ToolResult> JoinTangent(ClaimsPrincipal principal, McpContext context, McpIdentity identity, JObject arguments, CancellationToken ct)
    {
        var args = McpArguments.JoinTangent(arguments);
        var tangentKey = refs.ParseTangent(args.TangentRef)
            ?? throw new McpInvalidArgumentsException("tangentRef", "Copy a tangent reference returned by this server.");
        string? invitationId = null;
        if (args.InviteRef is { } invite)
        {
            var parsed = refs.ParseInvite(invite)
                ?? throw new McpInvalidArgumentsException("inviteRef", "Copy an invitation reference returned by this server.");
            if (parsed.TangentKey != tangentKey)
                throw new McpInvalidArgumentsException("inviteRef", "That invitation belongs to another Tangent.");
            invitationId = parsed.InvitationId;
        }
        ParticipationAccess.Require(principal, ParticipationGrants.Read);
        var payload = new Dictionary<string, string?> { ["tangentRef"] = args.TangentRef, ["inviteRef"] = args.InviteRef };
        return await requests.Run(context.CredentialId, context.ParticipantId, args.RequestId, async () =>
        {
            var registration = await requests.Register(context.CredentialId, context.ParticipantId, args.RequestId,
                "JoinTangent", tangentKey, payload, ct);
            if (registration.Reused && registration.Record.State == "completed" && registration.Record.ResultData is not null)
                return await Replay("JoinTangent", principal, context, identity, registration.Record, ct);
            requests.CompleteWithDomain(registration.Record, raw =>
            {
                var joined = (CompanionJoinResult)raw!;
                var pending = joined.Outcome == CompanionJoinOutcome.PendingApproval;
                var membership = pending ? "pending" : joined.Membership?.Role switch
                    { TangentRole.Admin => "admin", TangentRole.Reader => "reader", TangentRole.Member => "member", _ => "owner" };
                return (pending ? "pending" : "completed", pending ? null : refs.Tangent(tangentKey),
                    SerializeData(new McpJoinData(membership, pending ? "Your request is with the administrators." : "Welcome to your Tangent.", [], null, true)));
            });
            CompanionJoinResult join;
            try
            {
                join = await companions.Join(context.ParticipantId, tangentKey, invitationId, ct);
            }
            catch (TangentRuleViolation denied) when (denied.Denial == TangentDenial.Forbidden)
            {
                // Join denials are admission outcomes, not authority failures.
                return await Problem("JoinTangent", context.CompanionId, context.Id, identity, await ServerFallback(principal, context.ParticipantId, context.CredentialId, ct),
                    McpProblem.Of(McpProblemCodes.NotAdmitted, denied.Message),
                    participantId: context.ParticipantId, credential: context.CredentialId, ct: ct);
            }
            var welcome = join.Outcome == CompanionJoinOutcome.PendingApproval
                ? "Your request is with the Tangent's administrators."
                : join.Outcome == CompanionJoinOutcome.AlreadyMember
                    ? "You already belong to this Tangent."
                    : $"{identity.DisplayName} is in. Make yourself at home.";
            var selected = await FindTangent(context, tangentKey, ct);
            var channels = selected?.Channels.Take(10).Select(room => ToDto(tangentKey, room)).ToList() ?? [];
            var nextChannels = selected?.Channels.Count > 10
                ? refs.EncodeListCursor("channels:" + tangentKey, context.ParticipantId, 1, "10")
                : selected?.NextChannelsPage is { } nextPage
                    ? refs.EncodeListCursor("channels:" + tangentKey, context.ParticipantId, nextPage, "0") : null;
            var membership = join.Membership is { } result
                ? result.Role switch
                {
                    TangentRole.Admin => "admin",
                    TangentRole.Reader => "reader",
                    _ => "member"
                }
                : "owner";
            if (join.Outcome == CompanionJoinOutcome.PendingApproval)
            {
                var pendingData = new McpJoinData("pending", welcome, [], null);
                await requests.Complete(registration.Record, "pending", null, SerializeData(pendingData), ct);
                return Assemble("JoinTangent", "pending", context.CompanionId, context.Id, identity,
                    await TangentPlace(context, tangentKey, ["discover"], ct),
                    new McpResult(pendingData, new McpReceipt(args.RequestId, "op_" + args.RequestId, "pending", null, 15), null),
                    await ActivitySegment(context.ParticipantId, context.CredentialId, null, null, ct),
                    new McpNext(["GetOperation"],
                        [NextCall("GetOperation", "Check your saved action", McpJson.Arguments(new Dictionary<string, string?>
                            { ["contextId"] = context.Id, ["requestId"] = args.RequestId }))]));
            }
            var data = new McpJoinData(membership, welcome, channels, nextChannels, selected?.ChannelsIncomplete ?? false);
            await requests.Complete(registration.Record, "completed", refs.Tangent(tangentKey), SerializeData(data), ct);
            var place = await TangentPlace(context, tangentKey, MembershipPermissions(channels), ct);
            return Assemble("JoinTangent", "ok", context.CompanionId, context.Id, identity, place,
                new McpResult(data, new McpReceipt(args.RequestId, "op_" + args.RequestId, "completed", refs.Tangent(tangentKey), null), null),
                await ActivitySegment(context.ParticipantId, context.CredentialId, tangentKey, null, ct),
                new McpNext(DefaultAvailable(place, selected: true),
                    channels.Count > 0
                        ? [NextCall("ReadChannel", "Open the conversation", McpJson.Arguments(new Dictionary<string, string?>
                            { ["contextId"] = context.Id, ["channelRef"] = channels[0].ChannelRef }))]
                        : []));
        }, ct);
    }

    private async Task<ToolResult> LeaveTangent(ClaimsPrincipal principal, McpContext context, McpIdentity identity, JObject arguments, CancellationToken ct)
    {
        var args = McpArguments.LeaveTangent(arguments);
        var tangentKey = refs.ParseTangent(args.TangentRef)
            ?? throw new McpInvalidArgumentsException("tangentRef", "Copy a tangent reference returned by this server.");
        ParticipationAccess.Require(principal, ParticipationGrants.Read);
        return await requests.Run(context.CredentialId, context.ParticipantId, args.RequestId, async () =>
        {
            var registration = await requests.Register(context.CredentialId, context.ParticipantId, args.RequestId,
                "LeaveTangent", tangentKey, new Dictionary<string, string?> { ["tangentRef"] = args.TangentRef }, ct);
            if (registration.Reused && registration.Record.State == "completed" && registration.Record.ResultData is not null)
                return await Replay("LeaveTangent", principal, context, identity, registration.Record, ct);
            requests.CompleteWithDomain(registration.Record, _ => ("completed", null, SerializeData(new McpLeaveData("visitor", true))));
            await companions.Leave(context.ParticipantId, tangentKey, ct);
            // Leaving retains public and room history; authorship is never erased.
            var data = new McpLeaveData("visitor", true);
            await requests.Complete(registration.Record, "completed", null, SerializeData(data), ct);
            return Assemble("LeaveTangent", "ok", context.CompanionId, context.Id, identity,
                new McpPlace("tangent", tangentKey, refs.ServerRef, refs.Tangent(tangentKey), null, [], "not_applicable"),
                new McpResult(data, new McpReceipt(args.RequestId, "op_" + args.RequestId, "completed", null, null), null),
                await ActivitySegment(context.ParticipantId, context.CredentialId, null, null, ct),
                new McpNext(["ListTangents", "GetUpdates"], []));
        }, ct);
    }

    private async Task<ToolResult> SetWatch(ClaimsPrincipal principal, McpContext context, McpIdentity identity, JObject arguments, CancellationToken ct)
    {
        var args = McpArguments.SetWatch(arguments);
        var destination = ScopeDestination(args.ScopeRef);
        if (!await tangents.CanAccess(context.ParticipantId, destination.Tangent, ct)) throw new UnauthorizedAccessException();
        ParticipationAccess.Require(principal, ParticipationGrants.Read);
        return await requests.Run(context.CredentialId, context.ParticipantId, args.RequestId, async () =>
        {
            var registration = await requests.Register(context.CredentialId, context.ParticipantId, args.RequestId, "SetWatch",
                destination.Room ?? destination.Tangent, new Dictionary<string, string?> { ["scopeRef"] = args.ScopeRef, ["mode"] = args.Mode }, ct);
            if (registration.Reused && registration.Record.State == "completed" && registration.Record.ResultData is not null)
                return await Replay("SetWatch", principal, context, identity, registration.Record, ct);
            requests.CompleteWithDomain(registration.Record, _ => ("completed", null, SerializeData(new McpSetWatchData(args.ScopeRef, args.Mode))));
            var mode = args.Mode switch
            {
                "all" => WatchMode.All,
                "replies" => WatchMode.Replies,
                _ => WatchMode.None
            };
            if (destination.Room is { } watchedRoom)
                await companions.SetWatch(context.ParticipantId, watchedRoom, mode, ct);
            else await companions.SetTangentWatch(context.ParticipantId, destination.Tangent, mode, ct);
            var data = new McpSetWatchData(args.ScopeRef, args.Mode);
            await requests.Complete(registration.Record, "completed", null, SerializeData(data), ct);
            var place = await ScopePlace(context, destination, ct);
            return Assemble("SetWatch", "ok", context.CompanionId, context.Id, identity, place,
                new McpResult(data, new McpReceipt(args.RequestId, "op_" + args.RequestId, "completed", null, null), null),
                await ActivitySegment(context.ParticipantId, context.CredentialId, destination.Tangent, destination.Room, ct),
                new McpNext(DefaultAvailable(place, selected: true), []));
        }, ct);
    }

    private async Task<ToolResult> GetOperation(ClaimsPrincipal principal, McpContext context, McpIdentity identity, JObject arguments, CancellationToken ct)
    {
        var args = McpArguments.GetOperation(arguments);
        ParticipationAccess.Require(principal, ParticipationGrants.Read);
        // Lookup never re-executes; the durable registry and WriteIntent state are the only truth.
        var record = await requests.Find(context.CredentialId, context.ParticipantId, args.RequestId, ct);
        if (record is null)
            return await Problem("GetOperation", context.CompanionId, context.Id, identity, await ServerFallback(principal, context.ParticipantId, context.CredentialId, ct),
                McpProblem.Of(McpProblemCodes.ReceiptExpired, "No receipt is available for that request ID.", "requestId"),
                participantId: context.ParticipantId, credential: context.CredentialId, ct: ct);
        var state = record.State;
        string? resultRef = record.ResultRef;
        if (record.Operation == "PostMessage")
        {
            using var fresh = EntityContext.NoCache();
            var operationId = record.NamespacedOperationId
                ?? McpRequestRecord.BuildOperationId(context.CredentialId, context.ParticipantId, args.RequestId);
            // ADR 0007: a locally written post's projection row is its own receipt; the staged
            // intent remains the authority for the Spaces pipeline and legacy rows.
            var projected = (await Message.Query(m => m.AuthorParticipantId == context.ParticipantId && m.OperationId == operationId, One(), ct)).FirstOrDefault();
            if (projected is not null)
            {
                var rowRoom = await Room.Get(projected.RoomKey, ct);
                if (rowRoom is not null)
                {
                    state = projected.Removed ? "rejected" : "completed";
                    resultRef = refs.Message(rowRoom.TangentKey, projected.RoomKey, projected.Id);
                }
            }
            else
            {
                var intent = await WriteIntent.Get(WriteIntent.Key(context.ParticipantId, record.TargetKey, operationId), ct);
                if (intent is not null)
                {
                    state = intent.State == "accepted" ? "completed" : intent.State == "rejected" ? "rejected" : "pending";
                    if (intent.State == "accepted" && intent.SourceUri is not null)
                    {
                        var room = await Room.Get(intent.RoomKey, ct);
                        var staged = (await Message.Query(
                            message => message.RoomKey == intent.RoomKey && message.SourceUri == intent.SourceUri
                                && message.SourceCid == intent.SourceCid, One(), ct)).FirstOrDefault();
                        if (staged is not null && room is not null)
                            resultRef = refs.Message(room.TangentKey, intent.RoomKey, staged.Id);
                    }
                }
            }
        }
        if (record.Operation == "JoinTangent" && state == "pending" && record.ResultData is not null)
        {
            using var fresh = EntityContext.NoCache();
            var admission = await TangentJoinRequest.Get(TangentJoinRequest.Key(record.TargetKey, context.ParticipantId), ct);
            if (admission is { Pending: false })
            {
                state = admission.Accepted == true ? "completed" : "rejected";
                resultRef = admission.Accepted == true ? refs.Tangent(record.TargetKey) : null;
                await requests.Complete(record, state, resultRef, admission.Accepted == true
                    ? SerializeData(new McpJoinData("member", "Your arrival was approved. Welcome in.", [], null, true)) : null, ct);
            }
        }
        // Current access governs cached receipts: a revoked target's references are not re-disclosed.
        resultRef = await RefReadable(context.ParticipantId, resultRef, ct) ? resultRef : null;
        var receipt = new McpReceipt(record.RequestId, "op_" + record.RequestId,
            state is "completed" ? "completed" : state is "rejected" ? "rejected" : "pending", resultRef, null);
        var place = await ServerFallback(principal, context.ParticipantId, context.CredentialId, ct);
        var channelRef = resultRef is not null && refs.ParseMessage(resultRef) is { } message
            ? refs.Channel(message.TangentKey, message.RoomKey)
            : refs.ParseChannel(resultRef) is not null ? resultRef : null;
        var tangentRef = refs.ParseTangent(resultRef) is not null ? resultRef : null;
        var followups = new List<McpNextCall>();
        if (channelRef is not null) followups.Add(NextCall("ReadChannel", "Open the conversation",
            McpJson.Arguments(new Dictionary<string, string?> { ["contextId"] = context.Id, ["channelRef"] = channelRef })));
        else if (tangentRef is not null) followups.Add(NextCall("ListChannels", "Browse channels",
            McpJson.Arguments(new Dictionary<string, string?> { ["contextId"] = context.Id, ["tangentRef"] = tangentRef })));
        return Assemble("GetOperation", "ok", context.CompanionId, context.Id, identity, place,
            new McpResult(new McpOperationData(record.Operation, receipt), null, null),
            await ActivitySegment(context.ParticipantId, context.CredentialId, null, null, ct),
            new McpNext(["GetUpdates", "ReadChannel", "ListChannels"], followups));
    }

    private async Task<bool> RefReadable(string did, string? reference, CancellationToken ct)
    {
        if (reference is null) return true;
        try
        {
            var channel = refs.ParseChannel(reference);
            if (refs.ParseMessage(reference) is { } message) channel = (message.TangentKey, message.RoomKey);
            if (channel is { } room)
            {
                var description = await rooms.Describe(did, room.RoomKey, ct);
                return description?.TangentKey == room.TangentKey && description.CanRead;
            }
            if (refs.ParseTangent(reference) is { } tangent) return await tangents.CanAccess(did, tangent, ct);
            return false;
        }
        catch (Exception error) when (error is UnauthorizedAccessException or RoomRuleViolation or TangentRuleViolation)
        { return false; }
    }

    private static string ChannelRefFromMessage(string messageRef)
    {
        var index = messageRef.LastIndexOf("::", StringComparison.Ordinal);
        return index > 0 ? messageRef[..index] : messageRef;
    }

    private async Task<ToolResult> InviteParticipant(ClaimsPrincipal principal, McpContext context, McpIdentity identity, bool owner, JObject arguments, CancellationToken ct)
    {
        var args = McpArguments.InviteParticipant(arguments);
        var tangentKey = refs.ParseTangent(args.TangentRef)
            ?? throw new McpInvalidArgumentsException("tangentRef", "Copy a tangent reference returned by this server.");
        return await requests.Run(context.CredentialId, context.ParticipantId, args.RequestId, async () =>
        {
            var registration = await requests.Register(context.CredentialId, context.ParticipantId, args.RequestId, "InviteParticipant",
                tangentKey, new Dictionary<string, string?> { ["participantDid"] = args.ParticipantId, ["role"] = args.Role }, ct);
            if (registration.Reused && registration.Record.State == "completed" && registration.Record.ResultData is not null)
                return await Replay("InviteParticipant", principal, context, identity, registration.Record, ct);
            var role = args.Role switch
            {
                "admin" => CompanionRole.Admin,
                "reader" => CompanionRole.Reader,
                _ => CompanionRole.Member
            };
            requests.CompleteWithDomain(registration.Record, raw =>
            {
                var issued = (TangentInvitationResult)raw!;
                return ("completed", null, SerializeData(new McpInviteData(refs.Invite(tangentKey, issued.InvitationId),
                    refs.Origin + "/invite/" + issued.InvitationId, "not_sent")));
            });
            var invitation = await companions.Invite(context.ParticipantId, tangentKey, args.ParticipantId, role, ct);
            var data = new McpInviteData(refs.Invite(tangentKey, invitation.InvitationId),
                refs.Origin + "/invite/" + invitation.InvitationId, "not_sent");
            await requests.Complete(registration.Record, "completed", null, SerializeData(data), ct);
            return Assemble("InviteParticipant", "ok", context.CompanionId, context.Id, identity,
                await TangentPlace(context, tangentKey, ["manage"], ct),
                new McpResult(data, new McpReceipt(args.RequestId, "op_" + args.RequestId, "completed", null, null), null),
                await ActivitySegment(context.ParticipantId, context.CredentialId, tangentKey, null, ct),
                new McpNext(["GetUpdates"], []));
        }, ct);
    }

    private async Task<ToolResult> SetRole(ClaimsPrincipal principal, McpContext context, McpIdentity identity, bool owner, JObject arguments, CancellationToken ct)
    {
        var args = McpArguments.SetRole(arguments);
        var scope = ScopeDestination(args.ScopeRef);
        return await requests.Run(context.CredentialId, context.ParticipantId, args.RequestId, async () =>
        {
            var registration = await requests.Register(context.CredentialId, context.ParticipantId, args.RequestId, "SetRole",
                scope.Room is { } room ? room : scope.Tangent,
                new Dictionary<string, string?> { ["scopeRef"] = args.ScopeRef, ["participantDid"] = args.ParticipantId, ["role"] = args.Role }, ct);
            if (registration.Reused && registration.Record.State == "completed" && registration.Record.ResultData is not null)
                return await Replay("SetRole", principal, context, identity, registration.Record, ct);
            var role = args.Role switch
            {
                "admin" => CompanionRole.Admin,
                "reader" => CompanionRole.Reader,
                _ => CompanionRole.Member
            };
            requests.CompleteWithDomain(registration.Record, _ => ("completed", null,
                SerializeData(new McpSetRoleData(args.ScopeRef, args.ParticipantId, args.Role))));
            var assigned = await companions.SetRole(context.ParticipantId, scope.Tangent, scope.Room, args.ParticipantId, role, ct);
            if (assigned.Channel is { Accepted: false }) throw new UnauthorizedAccessException();
            var data = new McpSetRoleData(args.ScopeRef, args.ParticipantId, args.Role);
            await requests.Complete(registration.Record, "completed", null, SerializeData(data), ct);
            return Assemble("SetRole", "ok", context.CompanionId, context.Id, identity, await ScopePlace(context, scope, ct),
                new McpResult(data, new McpReceipt(args.RequestId, "op_" + args.RequestId, "completed", null, null), null),
                await ActivitySegment(context.ParticipantId, context.CredentialId, scope.Tangent, scope.Room, ct),
                new McpNext(["GetUpdates"], []));
        }, ct);
    }

    private async Task<ToolResult> SetParticipationPolicy(ClaimsPrincipal principal, McpContext context, McpIdentity identity, bool owner, JObject arguments, CancellationToken ct)
    {
        var args = McpArguments.SetPolicy(arguments);
        var tangentKey = refs.ParseTangent(args.TangentRef)
            ?? throw new McpInvalidArgumentsException("tangentRef", "Copy a tangent reference returned by this server.");
        return await requests.Run(context.CredentialId, context.ParticipantId, args.RequestId, async () =>
        {
            var registration = await requests.Register(context.CredentialId, context.ParticipantId, args.RequestId, "SetParticipationPolicy",
                tangentKey, new Dictionary<string, string?>
                {
                    ["admission"] = args.Admission, ["preset"] = args.Preset, ["undeclared"] = args.Undeclared
                }, ct);
            if (registration.Reused && registration.Record.State == "completed" && registration.Record.ResultData is not null)
                return await Replay("SetParticipationPolicy", principal, context, identity, registration.Record, ct);
            var policy = await companions.SetParticipationPolicy(context.ParticipantId, tangentKey,
                args.Admission switch
                {
                    "open" => TangentAdmission.Open,
                    "approval" => TangentAdmission.Approval,
                    _ => TangentAdmission.Invite
                },
                args.Preset switch
                {
                    "humans_only" => ParticipationPreset.HumansOnly,
                    "agents_only" => ParticipationPreset.AgentsOnly,
                    "humans_write_agents_read" => ParticipationPreset.HumansWriteAgentsRead,
                    "humans_read_agents_write" => ParticipationPreset.HumansReadAgentsWrite,
                    _ => ParticipationPreset.Everyone
                },
                args.Undeclared switch
                {
                    "read" => UndeclaredAccess.Read,
                    "write" => UndeclaredAccess.Write,
                    _ => UndeclaredAccess.Deny
                }, ct);
            var data = new McpPolicyData(args.Admission, args.Preset, args.Undeclared);
            await requests.Complete(registration.Record, "completed", null, SerializeData(data), ct);
            return Assemble("SetParticipationPolicy", "ok", context.CompanionId, context.Id, identity,
                await TangentPlace(context, tangentKey, ["manage"], ct),
                new McpResult(data, new McpReceipt(args.RequestId, "op_" + args.RequestId, "completed", null, null), null),
                await ActivitySegment(context.ParticipantId, context.CredentialId, tangentKey, null, ct),
                new McpNext(["GetUpdates"], []));
        }, ct);
    }

    private async Task<ToolResult> SetRestriction(ClaimsPrincipal principal, McpContext context, McpIdentity identity, bool owner, JObject arguments, CancellationToken ct)
    {
        var args = McpArguments.SetRestriction(arguments);
        var scope = ScopeDestination(args.ScopeRef);
        DateTimeOffset? until = args.Until is null ? null : DateTimeOffset.TryParse(args.Until, null,
            System.Globalization.DateTimeStyles.RoundtripKind, out var parsed) ? parsed : null;
        return await requests.Run(context.CredentialId, context.ParticipantId, args.RequestId, async () =>
        {
            var registration = await requests.Register(context.CredentialId, context.ParticipantId, args.RequestId, "SetRestriction",
                scope.Room is { } room ? room : scope.Tangent,
                new Dictionary<string, string?>
                {
                    ["scopeRef"] = args.ScopeRef, ["participantDid"] = args.ParticipantId,
                    ["restriction"] = args.Restriction, ["until"] = args.Until, ["reason"] = args.Reason
                }, ct);
            if (registration.Reused && registration.Record.State == "completed" && registration.Record.ResultData is not null)
                return await Replay("SetRestriction", principal, context, identity, registration.Record, ct);
            requests.CompleteWithDomain(registration.Record, raw =>
            {
                var restricted = (RestrictionResult)raw!;
                var audit = scope.Room is { } channel ? refs.Audit(scope.Tangent, channel, restricted.AuditId)
                    : refs.Tangent(scope.Tangent) + "::" + McpRefs.AuditPrefix + restricted.AuditId;
                return ("completed", null, SerializeData(new McpRestrictionData(args.Restriction, args.Until, audit)));
            });
            var result = await companions.SetRestriction(context.ParticipantId, scope.Tangent, scope.Room, args.ParticipantId,
                args.Restriction switch
                {
                    "timeout" => RestrictionKind.Timeout,
                    "ban" => RestrictionKind.Ban,
                    _ => RestrictionKind.None
                }, until, args.Reason, ct);
            var auditRef = scope.Room is { } channelRoom
                ? refs.Audit(scope.Tangent, channelRoom, result.AuditId)
                : refs.Tangent(scope.Tangent) + "::" + McpRefs.AuditPrefix + result.AuditId;
            var data = new McpRestrictionData(args.Restriction, args.Until, auditRef);
            await requests.Complete(registration.Record, "completed", null, SerializeData(data), ct);
            return Assemble("SetRestriction", "ok", context.CompanionId, context.Id, identity, await ScopePlace(context, scope, ct),
                new McpResult(data, new McpReceipt(args.RequestId, "op_" + args.RequestId, "completed", null, null), null),
                await ActivitySegment(context.ParticipantId, context.CredentialId, scope.Tangent, scope.Room, ct),
                new McpNext(["GetUpdates"], []));
        }, ct);
    }

    private (string Tangent, string? Room) ScopeDestination(string? reference)
    {
        if (refs.ParseChannel(reference) is { } channel)
            return (channel.TangentKey, channel.RoomKey);
        if (refs.ParseTangent(reference) is { } tangent)
            return (tangent, null);
        throw new McpInvalidArgumentsException("scopeRef", "Copy a channel or tangent reference returned by this server.");
    }

    private async Task<ToolResult> CreateTangent(ClaimsPrincipal principal, McpContext context, McpIdentity identity, bool owner, JObject arguments, CancellationToken ct)
    {
        var args = McpArguments.CreateTangent(arguments);
        if (args.ServerRef != refs.ServerRef)
            throw new McpInvalidArgumentsException("serverRef", "That server reference does not match this server.");
        return await requests.Run(context.CredentialId, context.ParticipantId, args.RequestId, async () =>
        {
            var registration = await requests.Register(context.CredentialId, context.ParticipantId, args.RequestId, "CreateTangent",
                "site", new Dictionary<string, string?>
                {
                    ["name"] = args.Name, ["description"] = args.Description, ["visibility"] = args.Visibility, ["firstChannelName"] = args.FirstChannelName
                }, ct);
            if (registration.Reused && registration.Record.State == "completed" && registration.Record.ResultData is not null)
                return await Replay("CreateTangent", principal, context, identity, registration.Record, ct);
            // Deterministic key: a crash between registration and creation reconciles to the same key.
            var key = DeterministicKey(args.Name, registration.Record.NamespacedOperationId!);
            TangentDescription created;
            try
            {
                created = await tangents.Create(context.ParticipantId, key, args.Name, args.Description, null, null, null, ct, args.Visibility == "public" ? TangentAdmission.Open : TangentAdmission.Invite);
            }
            catch (TangentRuleViolation error) when (error.Denial == TangentDenial.AlreadyExists)
            {
                // A crash between registration and creation: the deterministic key already exists.
                created = await FindTangent(context, key, ct)
                    ?? throw new TangentRuleViolation(TangentDenial.AlreadyExists, "That key already exists but is not visible to you.");
            }
            var channels = new List<McpChannelDto>();
            if (args.FirstChannelName is { } firstName)
                channels.Add(ToDto(created.Key, await CreateChannelInternal(context, created.Key, firstName, null, args.Visibility,
                    registration.Record.NamespacedOperationId!, ct)));
            created = await FindTangent(context, created.Key, ct) ?? created;
            var data = new McpCreateTangentData(ToDto(created), channels);
            await requests.Complete(registration.Record, "completed", refs.Tangent(created.Key), SerializeData(data), ct);
            var place = new McpPlace("tangent", created.Name, refs.ServerRef, refs.Tangent(created.Key), null, ["read", "post"], "ready");
            return Assemble("CreateTangent", "ok", context.CompanionId, context.Id, identity, place,
                new McpResult(data, new McpReceipt(args.RequestId, "op_" + args.RequestId, "completed", refs.Tangent(created.Key), null), null),
                await ActivitySegment(context.ParticipantId, context.CredentialId, created.Key, null, ct),
                new McpNext(["ListChannels", "CreateChannel"],
                    [NextCall("ListChannels", "Browse channels", McpJson.Arguments(new Dictionary<string, string?>
                        { ["contextId"] = context.Id, ["tangentRef"] = refs.Tangent(created.Key) }))]));
        }, ct);
    }

    private async Task<ToolResult> CreateChannel(ClaimsPrincipal principal, McpContext context, McpIdentity identity, bool owner, JObject arguments, CancellationToken ct)
    {
        var args = McpArguments.CreateChannel(arguments);
        var tangentKey = refs.ParseTangent(args.TangentRef)
            ?? throw new McpInvalidArgumentsException("tangentRef", "Copy a tangent reference returned by this server.");
        return await requests.Run(context.CredentialId, context.ParticipantId, args.RequestId, async () =>
        {
            var registration = await requests.Register(context.CredentialId, context.ParticipantId, args.RequestId, "CreateChannel",
                tangentKey, new Dictionary<string, string?> { ["name"] = args.Name, ["topic"] = args.Topic, ["visibility"] = args.Visibility }, ct);
            if (registration.Reused && registration.Record.State == "completed" && registration.Record.ResultData is not null)
                return await Replay("CreateChannel", principal, context, identity, registration.Record, ct);
            var created = await CreateChannelInternal(context, tangentKey, args.Name, args.Topic, args.Visibility,
                registration.Record.NamespacedOperationId!, ct);
            var data = new McpCreateChannelData(ToDto(tangentKey, created));
            await requests.Complete(registration.Record, "completed", refs.Channel(tangentKey, created.Key), SerializeData(data), ct);
            var tangentName = await TangentName(tangentKey, ct);
            var place = new McpPlace("channel", Preview($"{tangentName} / {created.Title}", 160), refs.ServerRef, refs.Tangent(tangentKey),
                refs.Channel(tangentKey, created.Key), created.CanWrite ? ["read", "post"] : ["read"], await ReadinessOf(context.ParticipantId, ct));
            return Assemble("CreateChannel", "ok", context.CompanionId, context.Id, identity, place,
                new McpResult(data, new McpReceipt(args.RequestId, "op_" + args.RequestId, "completed",
                    refs.Channel(tangentKey, created.Key), null), null),
                await ActivitySegment(context.ParticipantId, context.CredentialId, tangentKey, created.Key, ct),
                new McpNext(["ReadChannel", "GetUpdates"], []));
        }, ct);
    }

    /// <summary>Retries of a committed action return the recorded result verbatim; no side effect repeats.</summary>
    private async Task<ToolResult> Replay(string operation, ClaimsPrincipal principal, McpContext context, McpIdentity identity, McpRequestRecord record, CancellationToken ct)
    {
        await AuthorizeReplay(context, record, ct);
        var data = JsonNode.Parse(record.ResultData!);
        await RefreshReplayData(context.ParticipantId, data, ct);
        var receipt = new McpReceipt(record.RequestId, "op_" + record.RequestId,
            record.State == "completed" ? "completed" : record.State, record.ResultRef, null);
        var place = await ServerFallback(principal, context.ParticipantId, context.CredentialId, ct);
        return Assemble(operation, "ok", context.CompanionId, context.Id, identity, place,
            new McpResult(data, receipt, null),
            await ActivitySegment(context.ParticipantId, context.CredentialId, null, null, ct),
            new McpNext(["GetOperation", "GetUpdates"], []));
    }

    private async Task<TangentDescription?> FindTangent(McpContext context, string tangentKey, CancellationToken ct, int channelPage = 1)
    {
        for (var page = 1; page <= 10; page++)
        {
            var directory = await tangents.List(context.ParticipantId, page, channelPage, ct);
            var found = directory.Tangents.FirstOrDefault(tangent => tangent.Key == tangentKey);
            if (found is not null) return found;
            if (directory.NextPage is null) return null;
        }
        return null;
    }

    private async Task<Rooms.RoomDescription> CreateChannelInternal(McpContext context, string tangentKey, string name,
        string? topic, string visibility, string operationScope, CancellationToken ct)
    {
        var key = DeterministicKey(name, operationScope);
        RoomDescription created;
        try
        {
            var creation = await tangents.CreateChannel(context.ParticipantId, tangentKey, key, name,
                RoomAdmission.SignedIn, topic, ct, membersOnly: visibility == "members");
            created = creation.Channel ?? throw new InvalidOperationException("The created channel was not returned.");
        }
        catch (TangentRuleViolation error) when (error.Denial == TangentDenial.AlreadyExists)
        {
            created = await rooms.Describe(context.ParticipantId, key, ct) ?? throw new UnauthorizedAccessException();
            if (created.TangentKey != tangentKey || !created.CanManage) throw new UnauthorizedAccessException();
        }
        if (created.SpaceState == RoomSpaceState.Pending)
        {
            var provisioned = await spaces.Provision(context.ParticipantId, key, ct);
            if (!provisioned.Accepted) throw new UnauthorizedAccessException();
            created = await rooms.Describe(context.ParticipantId, key, ct) ?? throw new UnauthorizedAccessException();
        }
        return created;
    }

    /// <summary>Stable key derived from the title and the namespaced operation id, so retries and
    /// crash reconciliation always name the same durable entity.</summary>
    private static string DeterministicKey(string name, string operationScope)
        => McpSlug.Of(name, operationScope);

    private async Task<string> TangentName(string tangentKey, CancellationToken ct)
    {
        using var fresh = EntityContext.NoCache();
        return (await TangentCommunity.Get(tangentKey, ct))?.Name is { Length: > 0 } name ? name : tangentKey;
    }

    private async Task<List<McpChannelDto>> ChannelsOf(McpContext context, string tangentKey, int limit, CancellationToken ct)
        => await FindTangent(context, tangentKey, ct) is { } selected
            ? selected.Channels.Take(limit).Select(room => ToDto(tangentKey, room)).ToList()
            : [];

    private static List<string> MembershipPermissions(IReadOnlyList<McpChannelDto> channels)
    {
        var permissions = new List<string>();
        if (channels.Any(channel => channel.CanRead)) permissions.Add("read");
        if (channels.Any(channel => channel.CanPost)) permissions.Add("post");
        return permissions;
    }

    private async Task<McpPlace> TangentPlace(McpContext context, string tangentKey, IReadOnlyList<string> permissions, CancellationToken ct)
    {
        using var fresh = EntityContext.NoCache();
        var tangent = await TangentCommunity.Get(tangentKey, ct);
        return new McpPlace("tangent", tangent?.Name is { Length: > 0 } name ? name : tangentKey,
            refs.ServerRef, refs.Tangent(tangentKey), null, permissions, "ready");
    }

    private async Task<McpPlace> ScopePlace(McpContext context, (string Tangent, string? Room) scope, CancellationToken ct)
    {
        if (scope.Room is { } room)
        {
            var place = await ChannelPlace(context, (scope.Tangent, room), ct);
            return await companions.CanAdminister(context.ParticipantId, scope.Tangent, room, ct)
                ? place with { Permissions = [.. place.Permissions, "manage"] } : place;
        }
        return await TangentPlace(context, scope.Tangent,
            await companions.CanAdminister(context.ParticipantId, scope.Tangent, null, ct) ? ["read", "manage"] : ["read"], ct);
    }
}

/// <summary>Stable, readable key derived from a title plus the namespaced operation id.</summary>
public static class McpSlug
{
    public static string Of(string name, string operationScope)
    {
        var builder = new System.Text.StringBuilder();
        var previousDash = true;
        foreach (var c in name.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c)) { builder.Append(c); previousDash = false; }
            else if (!previousDash) { builder.Append('-'); previousDash = true; }
        }
        var slug = builder.ToString().Trim('-');
        if (slug.Length > 32) slug = slug[..32].Trim('-');
        if (slug.Length == 0) slug = "place";
        var suffix = operationScope.Length >= 12 ? operationScope[^12..] : operationScope.TrimStart('-');
        return slug + "-" + suffix;
    }
}
