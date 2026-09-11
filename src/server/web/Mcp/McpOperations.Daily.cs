using System.Security.Claims;
using Koan.Data.Core;
using Newtonsoft.Json.Linq;
using TangentSpace.AtProtocol;
using TangentSpace.Communities;
using TangentSpace.Participation;
using TangentSpace.Rooms;

namespace TangentSpace.Mcp;

public sealed partial class McpOperationDispatcher
{
    private async Task<ToolResult> SelectCompanion(ClaimsPrincipal principal, JObject arguments, CancellationToken ct)
    {
        var args = McpArguments.SelectCompanion(arguments);
        var selection = await contexts.Select(principal, args.Moniker, ct);
        var companion = selection.Companion;
        var did = companion.ParticipantDid;
        var identity = new McpIdentity(did, CompanionIdentity.ActingAs(selection.Participant, did),
            CompanionIdentity.DisplayName(selection.Participant, did), Format(companion.ExpiresAt));
        // Selection establishes identity only: no context exists until Arrive names this server.
        return Assemble("SelectCompanion", "ok", companion.Id, null, identity, CompanionPlace(),
            new McpResult(new McpSelectData(companion.Id), null, null),
            new McpActivity(Timestamp(), "not_connected", [], false),
            new McpNext(["Arrive"],
                [NextCall("Arrive", "Continue", McpJson.Arguments(
                    new Dictionary<string, string?> { ["companionId"] = companion.Id, ["serverUrl"] = refs.Origin }))]));
    }

    private async Task<ToolResult> Arrive(ClaimsPrincipal principal, JObject arguments, CancellationToken ct)
    {
        var args = McpArguments.Arrive(arguments);
        ParticipationAccess.Require(principal, ParticipationGrants.Read);
        var selection = await contexts.SelectionOf(principal, args.CompanionId, ct);
        var companion = selection.Companion;
        var did = companion.ParticipantDid;
        var identity = new McpIdentity(did, CompanionIdentity.ActingAs(selection.Participant, did),
            CompanionIdentity.DisplayName(selection.Participant, did), Format(companion.ExpiresAt));
        // Inbound handles only its own configured origin; the selection survives a wrong destination
        // so the caller can recover by arriving here with the correct URL.
        if (!McpOptionsValidation.IsCanonicalOrigin(args.ServerUrl, out var requested) || requested != refs.Origin)
        {
            return await Problem("Arrive", companion.Id, null, identity,
                new McpPlace("server", HostLabel(args.ServerUrl), null, null, null, [], "unavailable"),
                McpProblem.Of(McpProblemCodes.Unreachable, "That destination cannot be reached from this server."),
                calls: [NextCall("Arrive", "Continue", McpJson.Arguments(
                    new Dictionary<string, string?> { ["companionId"] = companion.Id, ["serverUrl"] = refs.Origin }))],
                available: ["Arrive"],
                did: did, credential: companion.CredentialId, ct: ct);
        }
        var (context, _) = await contexts.Bind(companion, ct);
        identity = identity with { ExpiresAt = Format(context.ExpiresAt) };
        var (directory, continuation, incomplete) = await TangentPage(context, 1, 0, 10, ct);
        var permissions = GrantPermissions(principal);
        var label = await SiteLabel(ct);
        var place = new McpPlace("server", label, refs.ServerRef, null, null, permissions, await ReadinessOf(did, ct), (await server.Read(did, ct)).Permissions);
        return Assemble("Arrive", "ok", companion.Id, context.Id, identity, place,
            new McpResult(new McpArriveData($"Welcome, {identity.DisplayName}. Here's what happened while you were away.", directory, continuation, incomplete), null, null),
            await ActivitySegment(did, context.CredentialId, null, null, ct),
            new McpNext(DefaultAvailable(place, selected: true),
                [NextCall("ListTangents", "Browse Tangents", McpJson.Arguments(
                    new Dictionary<string, string?> { ["contextId"] = context.Id, ["serverRef"] = refs.ServerRef }))]));
    }

    private static string HostLabel(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Host.Length > 0 ? uri.Host : "The requested server";

    private async Task<ToolResult> ListTangents(ClaimsPrincipal principal, McpContext context, McpIdentity identity, JObject arguments, CancellationToken ct)
    {
        var args = McpArguments.ListTangents(arguments);
        if (args.ServerRef != refs.ServerRef)
            return await Problem("ListTangents", context.CompanionId, context.Id, identity, await ServerFallback(principal, context.ParticipantDid, context.CredentialId, ct),
                McpProblem.Of(McpProblemCodes.Unreachable, "That server reference does not match this server."),
                did: context.ParticipantDid, credential: context.CredentialId, ct: ct);
        ParticipationAccess.Require(principal, ParticipationGrants.Read);
        var selected = refs.DecodeListCursor(args.Cursor, "tangents", context.ParticipantDid);
        var page = selected?.Page ?? 1;
        var offset = selected is not null && int.TryParse(selected.Inner, out var parsed) && parsed > 0 ? parsed : 0;
        var (tangents, continuation, incomplete) = await TangentPage(context, page, offset, args.Limit, ct);
        var permissions = GrantPermissions(principal);
        var label = await SiteLabel(ct);
        var place = new McpPlace("server", label, refs.ServerRef, null, null, permissions, await ReadinessOf(context.ParticipantDid, ct));
        return Assemble("ListTangents", "ok", context.CompanionId, context.Id, identity, place,
            new McpResult(new McpListTangentsData(tangents, continuation, incomplete), null, null),
            await ActivitySegment(context.ParticipantDid, context.CredentialId, null, null, ct),
            new McpNext(DefaultAvailable(place, selected: true), []));
    }

    private async Task<(IReadOnlyList<McpTangentDto> Tangents, string? NextCursor, bool Incomplete)> TangentPage(
        McpContext context, int page, int offset, int limit, CancellationToken ct)
    {
        var directory = await tangents.List(context.ParticipantDid, page, ct);
        var visible = directory.Tangents.Skip(offset).ToList();
        var slice = visible.Take(limit).ToList();
        string? continuation = null;
        if (visible.Count > limit)
            continuation = refs.EncodeListCursor("tangents", context.ParticipantDid, page, (offset + limit).ToString());
        else if (directory.NextPage is { } next)
            continuation = refs.EncodeListCursor("tangents", context.ParticipantDid, next, "0");
        var mapped = new List<McpTangentDto>(slice.Count);
        foreach (var tangent in slice) mapped.Add(ToDto(tangent));
        return (mapped, continuation, directory.DirectoryIncomplete);
    }

    /// <summary>Membership, admission and capability flags come from the directory service's own
    /// actor-filtered data; nothing is guessed here.</summary>
    internal McpTangentDto ToDto(TangentDescription tangent)
    {
        var state = tangent.IsOwner ? "owner"
            : tangent.MembershipPending ? "pending"
            : tangent.MembershipRole switch
            {
                TangentRole.Member => "member",
                TangentRole.Admin => "admin",
                TangentRole.Reader => "reader",
                _ => "visitor"
            };
        var admission = tangent.Admission switch
        {
            TangentAdmission.Open => "open",
            TangentAdmission.Approval => "approval",
            _ => "invite"
        };
        return new McpTangentDto(refs.Tangent(tangent.Key), Preview(tangent.Name, 80), Preview(tangent.Description, 240), state, admission,
            tangent.MembershipRole != TangentRole.Removed && state == "visitor" && tangent.Admission is TangentAdmission.Open or TangentAdmission.Approval,
            tangent.Channels.Any(channel => channel.CanRead),
            tangent.Channels.Any(channel => channel.CanWrite), tangent.Permissions);
    }

    private async Task<ToolResult> ListChannels(ClaimsPrincipal principal, McpContext context, McpIdentity identity, JObject arguments, CancellationToken ct)
    {
        var args = McpArguments.ListChannels(arguments);
        var tangentKey = refs.ParseTangent(args.TangentRef)
            ?? throw new McpInvalidArgumentsException("tangentRef", "Copy a tangent reference returned by this server.");
        ParticipationAccess.Require(principal, ParticipationGrants.Read);
        var scope = "channels:" + tangentKey;
        var cursor = refs.DecodeListCursor(args.Cursor, scope, context.ParticipantDid);
        var channelPage = cursor?.Page ?? 1;
        var selected = await FindTangent(context, tangentKey, ct, channelPage);
        if (selected is null) throw new UnauthorizedAccessException();
        var offset = cursor is not null && int.TryParse(cursor.Inner, out var parsed) && parsed > 0 ? parsed : 0;
        var visible = selected.Channels.Skip(offset).ToList();
        var slice = visible.Take(args.Limit).ToList();
        string? continuation = null;
        if (visible.Count > args.Limit)
            continuation = refs.EncodeListCursor(scope, context.ParticipantDid, channelPage, (offset + args.Limit).ToString());
        else if (selected.NextChannelsPage is { } nextPage)
            continuation = refs.EncodeListCursor(scope, context.ParticipantDid, nextPage, "0");
        var channels = slice.Select(room => ToDto(tangentKey, room)).ToList();
        var permissions = new List<string>();
        if (selected.Channels.Any(channel => channel.CanRead)) permissions.Add("read");
        if (selected.Channels.Any(channel => channel.CanWrite)) permissions.Add("post");
        var place = new McpPlace("tangent", Preview(selected.Name, 160), refs.ServerRef, refs.Tangent(tangentKey), null, permissions, "ready");
        return Assemble("ListChannels", "ok", context.CompanionId, context.Id, identity, place,
            new McpResult(new McpListChannelsData(channels, continuation, selected.ChannelsIncomplete), null, null),
            await ActivitySegment(context.ParticipantDid, context.CredentialId, tangentKey, null, ct),
            new McpNext(DefaultAvailable(place, selected: true),
                channels.Count > 0
                    ? [NextCall("ReadChannel", "Open the conversation", McpJson.Arguments(new Dictionary<string, string?>
                        { ["contextId"] = context.Id, ["channelRef"] = channels[0].ChannelRef }))]
                    : []));
    }

    internal McpChannelDto ToDto(string tangentKey, RoomDescription room)
        => new(refs.Channel(tangentKey, room.Key), Preview(room.Title, 80), Preview(room.Topic, 240), room.CanRead, room.CanWrite, room.Permissions);

    private static string Preview(string value, int limit)
        => value.Length <= limit ? value : value[..(char.IsHighSurrogate(value[limit - 2]) ? limit - 2 : limit - 1)] + "…";

    private static List<string> GrantPermissions(ClaimsPrincipal principal)
    {
        var permissions = new List<string>();
        if (principal.HasClaim(ParticipationConstants.GrantClaim, ParticipationGrants.Read)) permissions.Add("read");
        if (principal.HasClaim(ParticipationConstants.GrantClaim, ParticipationGrants.Post)) permissions.Add("post");
        return permissions;
    }
}
