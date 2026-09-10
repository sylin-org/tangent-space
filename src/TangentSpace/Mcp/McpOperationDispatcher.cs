using System.Security.Claims;
using Koan.Data.Core;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using TangentSpace.Activity;
using TangentSpace.AtProtocol;
using TangentSpace.Communities;
using TangentSpace.Conversation;
using TangentSpace.Participants;
using TangentSpace.Participation;
using TangentSpace.Rooms;

namespace TangentSpace.Mcp;

/// <summary>
/// Dispatches inbound MCP tool calls onto existing domain services and assembles the fixed
/// five-segment envelope. All policy decisions come from the domain; nothing here fakes success.
/// </summary>
public sealed partial class McpOperationDispatcher(
    McpContractCatalog catalog,
    McpContexts contexts,
    McpRequests requests,
    McpRefs refs,
    TimeProvider clock,
    TangentServer hub)
{
    private ActivityService activity => hub.Activity;
    private ConversationService conversation => hub.Posts;
    private TangentGovernance tangents => hub.Tangents;
    private CompanionGovernance companions => hub.Participants;
    private SpacesService spaces => hub.Source;
    private RoomGovernance rooms => hub.Topics;
    private SourceReadiness readiness => hub.Readiness;
    private TangentSpace.Site.ServerGovernance server => hub.Site;

    public sealed record ToolResult(McpEnvelope Envelope)
    {
        public bool IsError => Envelope.Status is "blocked" or "error";
        public string Screen => BbsScreen.Render(Envelope);
        public string Json => McpJson.Serialize(McpVocabulary.Outbound(McpJson.ToWire(Envelope)));
    }

    /// <summary>Coarse transport grants gate which operations are advertised and callable. The
    /// authenticated credential decides this; the selected context never changes the tool list.</summary>
    public bool IsCallable(string name, ClaimsPrincipal principal)
    {
        var tool = catalog.Find(McpVocabulary.Public(name));
        if (tool is null || tool.Endpoint == "connector") return false;
        if (McpContractCatalog.IsOwner(tool))
            return principal.HasClaim(ParticipationConstants.GrantClaim, ParticipationGrants.Manage);
        return McpVocabulary.Internal(name) switch
        {
            "SelectCompanion" => true,
            "DeletePost" => principal.HasClaim(ParticipationConstants.GrantClaim, ParticipationGrants.Post) || principal.HasClaim(ParticipationConstants.GrantClaim, ParticipationGrants.Manage),
            "PostMessage" or "CreateTangent" or "CreateChannel" or "EditPost" => principal.HasClaim(ParticipationConstants.GrantClaim, ParticipationGrants.Post),
            _ => principal.HasClaim(ParticipationConstants.GrantClaim, ParticipationGrants.Read)
        };
    }

    public async Task<ToolResult> Call(ClaimsPrincipal principal, string name, JObject? arguments, CancellationToken ct)
    {
        if (!IsCallable(name, principal)) throw new McpUnknownToolException(name);
        try
        {
            return await Routed(principal, McpVocabulary.Internal(name), McpVocabulary.Arguments(arguments ?? []), ct);
        }
        catch (McpInvalidArgumentsException error)
        {
            return await Problem(name, null, null, null, CompanionPlace(),
                McpProblem.Of(McpProblemCodes.InvalidArguments, error.Message, error.Field));
        }
        catch (McpCompanionUnavailableException)
        {
            return await Problem(name, null, null, null, CompanionPlace(),
                McpProblem.Of(McpProblemCodes.CompanionUnavailable, "That companion is not available to this runtime."));
        }
        catch (McpContextExpiredException)
        {
            return await Problem(name, null, null, null, CompanionPlace(),
                McpProblem.Of(McpProblemCodes.ContextExpired, "Select your companion again. Keep any saved request IDs."),
                available: ["SelectCompanion"]);
        }
        catch (McpRequestConflictException error)
        {
            return await Problem(name, null, null, null, CompanionPlace(),
                McpProblem.Of(McpProblemCodes.RequestConflict,
                    $"{error.RequestId} already identifies a different action. Inspect its receipt before creating another action."),
                available: ["GetOperation"]);
        }
        catch (UnauthorizedAccessException)
        {
            return await Problem(name, null, null, null, CompanionPlace(),
                McpProblem.Of(McpProblemCodes.PermissionDenied, "Your current access does not permit this operation."));
        }
        catch (TangentRuleViolation denied)
        {
            return await Problem(name, null, null, null, CompanionPlace(), McpProblem.Of(MapDenial(denied.Denial), denied.Message));
        }
        catch (RoomRuleViolation denied)
        {
            return await Problem(name, null, null, null, CompanionPlace(), McpProblem.Of(MapDenial(denied.Denial), denied.Message));
        }
        catch (InvalidOperationException error)
        {
            return await Problem(name, null, null, null, CompanionPlace(), McpProblem.Of(McpProblemCodes.InvalidArguments, error.Message));
        }
        catch (ArgumentException error)
        {
            return await Problem(name, null, null, null, CompanionPlace(), McpProblem.Of(error.Message.Contains("cursor", StringComparison.OrdinalIgnoreCase) ? McpProblemCodes.CursorExpired : McpProblemCodes.InvalidArguments, error.Message));
        }
    }

    private async Task<ToolResult> Routed(ClaimsPrincipal principal, string name, JObject arguments, CancellationToken ct)
    {
        if (name == "SelectCompanion") return FilterToGrants(principal, await SelectCompanion(principal, arguments, ct));
        if (name == "Arrive") return FilterToGrants(principal, await Arrive(principal, arguments, ct));
        var requested = arguments.TryGetValue("contextId", out var token) && token.Type == JTokenType.String
            ? token.Value<string>() : null;
        var context = await contexts.Resolve(principal, requested, ct);
        var identity = await IdentityOf(context, ct);
        var did = context.ParticipantDid;
        var credential = context.CredentialId;
        var owner = principal.HasClaim(ParticipationConstants.GrantClaim, ParticipationGrants.Manage);
        try
        {
            await ValidateReferences(arguments, ct);
            var outcome = name switch
            {
                "EditPost" => await EditPost(principal, context, identity, arguments, ct),
                "DeletePost" => await DeletePost(principal, context, identity, arguments, ct),
                "GetPermissions" or "ConfigureServer" or "ConfigureTangent" or "ConfigureTopic" or "DeclareParticipant" or "ClaimServer" => await Govern(principal, context, identity, name, arguments, ct),
                "ListTangents" => await ListTangents(principal, context, identity, arguments, ct),
                "JoinTangent" => await JoinTangent(principal, context, identity, arguments, ct),
                "ListChannels" => await ListChannels(principal, context, identity, arguments, ct),
                "ReadChannel" => await ReadChannel(principal, context, identity, arguments, ct),
                "PostMessage" => await PostMessage(principal, context, identity, arguments, ct),
                "GetUpdates" => await GetUpdates(principal, context, identity, arguments, ct),
                "MarkRead" => await MarkRead(principal, context, identity, arguments, ct),
                "LeaveTangent" => await LeaveTangent(principal, context, identity, arguments, ct),
                "SetWatch" => await SetWatch(principal, context, identity, arguments, ct),
                "GetOperation" => await GetOperation(principal, context, identity, arguments, ct),
                "CreateTangent" => await CreateTangent(principal, context, identity, owner, arguments, ct),
                "CreateChannel" => await CreateChannel(principal, context, identity, owner, arguments, ct),
                "InviteParticipant" => await InviteParticipant(principal, context, identity, owner, arguments, ct),
                "SetRole" => await SetRole(principal, context, identity, owner, arguments, ct),
                "SetParticipationPolicy" => await SetParticipationPolicy(principal, context, identity, owner, arguments, ct),
                "SetRestriction" => await SetRestriction(principal, context, identity, owner, arguments, ct),
                _ => throw new McpUnknownToolException(name)
            };
            await activity.EnsureParticipantActive(did, credential, ct);
            if (name is "ReadChannel" or "PostMessage" && outcome.Envelope.Place.ChannelRef is { } disclosedChannel
                && !await RefReadable(did, disclosedChannel, ct)) throw new UnauthorizedAccessException();
            return FilterToGrants(principal, await WithAccess(context, outcome, ct));
        }
        catch (McpInvalidArgumentsException error)
        {
            return await Problem(name, context.CompanionId, context.Id, identity, await ServerFallback(principal, did, credential, ct),
                McpProblem.Of(McpProblemCodes.InvalidArguments, error.Message, error.Field), did: did, credential: credential, ct: ct);
        }
        catch (McpRequestConflictException error)
        {
            return await Problem(name, context.CompanionId, context.Id, identity, await ServerFallback(principal, did, credential, ct),
                McpProblem.Of(McpProblemCodes.RequestConflict,
                    $"{error.RequestId} already identifies a different action. Inspect its receipt before creating another action."),
                calls: [NextCall("GetOperation", "Check your saved action",
                    McpJson.Arguments(new Dictionary<string, string?> { ["contextId"] = context.Id, ["requestId"] = error.RequestId }))],
                available: ["GetOperation"]);
        }
        catch (UnauthorizedAccessException)
        {
            return await Problem(name, context.CompanionId, context.Id, identity, await ServerFallback(principal, did, credential, ct),
                McpProblem.Of(McpProblemCodes.PermissionDenied, "Your current access does not permit this operation."), did: did, credential: credential, ct: ct);
        }
        catch (TangentRuleViolation denied)
        {
            return await Problem(name, context.CompanionId, context.Id, identity, await ServerFallback(principal, did, credential, ct),
                McpProblem.Of(MapDenial(denied.Denial), denied.Message), did: did, credential: credential, ct: ct);
        }
        catch (RoomRuleViolation denied)
        {
            return await Problem(name, context.CompanionId, context.Id, identity, await ServerFallback(principal, did, credential, ct),
                McpProblem.Of(MapDenial(denied.Denial), denied.Message), did: did, credential: credential, ct: ct);
        }
        catch (Exception failure) when (failure is SpacesUnavailable or HttpRequestException or InvalidDataException)
        {
            var requestId = arguments["requestId"]?.Type == JTokenType.String ? (string?)arguments["requestId"] : null;
            McpReceipt? receipt = null;
            if (requestId is not null && await requests.Find(credential, did, requestId, ct) is { } record)
                receipt = new McpReceipt(record.RequestId, "op_" + record.RequestId, "pending", null, 15);
            return Assemble(name, "blocked", context.CompanionId, context.Id, identity,
                new McpPlace("server", await SiteLabel(ct), refs.ServerRef, null, null, [], "unavailable"),
                new McpResult(null, receipt, McpProblem.Of(McpProblemCodes.Unreachable,
                    "The native service could not confirm this action. Keep the saved request ID and check its receipt.")),
                new McpActivity(Timestamp(), "unavailable", [], false),
                new McpNext(receipt is null ? ["GetUpdates"] : ["GetOperation"], []));
        }
        catch (ArgumentException error)
        {
            return await Problem(name, context.CompanionId, context.Id, identity, await ServerFallback(principal, did, credential, ct),
                McpProblem.Of(error.Message.Contains("cursor", StringComparison.OrdinalIgnoreCase) ? McpProblemCodes.CursorExpired : McpProblemCodes.InvalidArguments, error.Message), did: did, credential: credential, ct: ct);
        }
    }

    /// <summary>next.available only ever names operations the authenticated grants can actually invoke.</summary>
    private ToolResult FilterToGrants(ClaimsPrincipal principal, ToolResult outcome)
    {
        var allowed = McpContractCatalog.InboundOperations.Where(name => IsCallable(name, principal)).Select(McpVocabulary.Internal).ToHashSet(StringComparer.Ordinal);
        var available = outcome.Envelope.Next.Available.Where(allowed.Contains).Take(8).ToList();
        var calls = outcome.Envelope.Next.Calls.Where(c => available.Contains(c.Tool)).ToList();
        var canRead = principal.HasClaim(ParticipationConstants.GrantClaim, ParticipationGrants.Read);
        var canPost = principal.HasClaim(ParticipationConstants.GrantClaim, ParticipationGrants.Post);
        var data = outcome.Envelope.Result.Data is null ? null
            : System.Text.Json.Nodes.JsonNode.Parse(SerializeData(outcome.Envelope.Result.Data));
        var manage = principal.HasClaim(ParticipationConstants.GrantClaim, ParticipationGrants.Manage);
        ClampCapabilities(data, canRead, canPost, manage);
        var permissions = outcome.Envelope.Place.Permissions.Where(p => p != "read" || canRead)
            .Where(p => p != "post" || canPost)
            .Where(p => p != "manage" || principal.HasClaim(ParticipationConstants.GrantClaim, ParticipationGrants.Manage)).ToArray();
        return outcome with { Envelope = outcome.Envelope with {
            Place = outcome.Envelope.Place with { Permissions = permissions, Access = ClampAccess(outcome.Envelope.Place.Access, canRead, canPost, manage) },
            Result = outcome.Envelope.Result with { Data = data },
            Next = outcome.Envelope.Next with { Available = available, Calls = calls } } };
    }

    private static string MapDenial(TangentDenial denial) => denial switch
    {
        TangentDenial.NotFound => McpProblemCodes.PermissionDenied,
        TangentDenial.AlreadyExists => McpProblemCodes.InvalidArguments,
        _ => McpProblemCodes.PermissionDenied
    };

    private static string MapDenial(RoomDenial denial) => denial switch
    {
        RoomDenial.NotFound => McpProblemCodes.PermissionDenied,
        RoomDenial.AlreadyExists or RoomDenial.PolicyChanged or RoomDenial.SpaceAlreadyMapped => McpProblemCodes.InvalidArguments,
        _ => McpProblemCodes.PermissionDenied
    };

    private static McpPlace CompanionPlace()
        => new("connector", "Your companions", null, null, null, [], "ready");

    internal async Task<McpPlace> ServerFallback(ClaimsPrincipal principal, string did, string credential, CancellationToken ct)
    {
        var permissions = new List<string>();
        if (principal.HasClaim(ParticipationConstants.GrantClaim, ParticipationGrants.Read)) permissions.Add("read");
        if (principal.HasClaim(ParticipationConstants.GrantClaim, ParticipationGrants.Post)) permissions.Add("post");
        return new McpPlace("server", await SiteLabel(ct), refs.ServerRef, null, null, permissions, await ReadinessOf(did, ct));
    }

    internal async Task<string> SiteLabel(CancellationToken ct)
    {
        using var fresh = EntityContext.NoCache();
        var site = await Site.TangentSite.Get(TangentSpace.Infrastructure.TangentConstants.SiteId, ct);
        return site?.Name.Length > 0 ? site.Name : "This Tangent server";
    }

    internal async Task<ToolResult> Problem(string operation, string? companionId, string? contextId, McpIdentity? identity, McpPlace place,
        McpProblem problem, IReadOnlyList<McpNextCall>? calls = null, IReadOnlyList<string>? available = null,
        string? did = null, string? credential = null, CancellationToken ct = default)
    => Assemble(operation, "blocked", companionId, contextId, identity, place, new McpResult(null, null, problem),
        did is null ? new McpActivity(Timestamp(), "not_connected", [], false)
            : await ActivitySegment(did, credential, null, null, ct),
        new McpNext(available ?? DefaultAvailable(place, identity is not null), calls ?? []));

    internal ToolResult Assemble(string operation, string status, string? companionId, string? contextId, McpIdentity? identity,
        McpPlace place, McpResult result, McpActivity activity, McpNext next)
        => new(new McpEnvelope(operation, status, companionId, contextId, identity, place, result, activity, next));

    internal string Timestamp() => clock.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

    internal string Format(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

    internal async Task<McpIdentity> IdentityOf(McpContext context, CancellationToken ct)
    {
        using var fresh = EntityContext.NoCache();
        var participant = await Participant.Get(context.ParticipantDid, ct)
            ?? Participant.FirstArrival(context.ParticipantDid, null, context.CreatedAt);
        return new McpIdentity(context.ParticipantDid, CompanionIdentity.ActingAs(participant, context.ParticipantDid),
            CompanionIdentity.DisplayName(participant, context.ParticipantDid), Format(context.ExpiresAt));
    }

    internal IReadOnlyList<string> DefaultAvailable(McpPlace place, bool selected)
    {
        if (!selected) return ["SelectCompanion"];
        var tools = new List<string> { "GetUpdates" };
        if (place.Kind is "server") tools.Add("ListTangents");
        if (place.Kind is "server" or "tangent") tools.Add("ListChannels");
        if (place.Kind is "tangent" or "channel")
        {
            tools.Add("ReadChannel");
            tools.Add("MarkRead");
        }
        if (place.Kind == "channel" && place.Permissions.Contains("post")) tools.Add("PostMessage");
        if (place.Kind == "tangent") tools.Add("JoinTangent");
        return tools.Take(8).ToList();
    }

    internal static McpNextCall NextCall(string tool, string label, string argumentsJson) => new(tool, label, argumentsJson);

    /// <summary>A bounded activity snapshot: the domain service already excludes the actor's own
    /// messages, applies watch state and orders channels by attention priority.</summary>
    internal async Task<(McpActivity Activity, IReadOnlyList<McpNotice> Notices, string? Continuation, string Checkpoint, bool Incomplete)> SnapshotActivity(
        string did, string? credential, string? cursor, string? scopeTangent, string? scopeRoom, int noticeLimit, CancellationToken ct)
    {
        var scope = (scopeTangent ?? "") + ":" + (scopeRoom ?? "");
        var requested = refs.DecodeUpdates(cursor, did, credential, scope, clock.GetUtcNow());
        var snapshot = await activity.Snapshot(did, credential, requested?.Events, requested?.Channels, ct);
        await activity.EnsureSnapshotCurrent(did, credential, snapshot, ct);
        var labels = await ChannelLabels(snapshot.Channels, ct);
        var candidates = snapshot.Channels
            .Where(channel => (scopeTangent is null || channel.TangentKey == scopeTangent)
                && (scopeRoom is null || channel.RoomKey == scopeRoom))
            .Select(channel => ToNotice(channel, labels))
            // Only channels with actual waiting attention are notices.
            .Where(notice => notice.Unread.Value > 0 || notice.RepliesToYou.Value > 0)
            .OrderBy(notice => notice.ChannelRef, StringComparer.Ordinal).ToList();
        var offset = requested?.Offset ?? 0;
        var chosen = candidates.Skip(offset).Take(noticeLimit).ToList();
        var expiry = requested?.ExpiresAt ?? clock.GetUtcNow().AddDays(7);
        var baseCursor = new McpUpdatesCursor(did, credential, scope, snapshot.Checkpoint, null, 0, expiry);
        var checkpoint = refs.EncodeUpdates(baseCursor);
        string? next = null;
        if (candidates.Count > offset + chosen.Count)
            next = refs.EncodeUpdates(baseCursor with { Channels = requested?.Channels, Offset = offset + chosen.Count });
        else if (snapshot.NextChannelCursor is { } nextChannels)
            next = refs.EncodeUpdates(baseCursor with { Channels = nextChannels });
        else if (snapshot.HasMore)
            next = refs.EncodeUpdates(baseCursor with { Channels = requested?.Channels, Offset = candidates.Count });
        var incomplete = snapshot.ChannelsIncomplete || snapshot.ResetRequired;
        var coverage = incomplete || snapshot.ChannelsHasMore || snapshot.Channels.Any(channel => channel.Freshness != "checked")
                ? "partial" : "current";
        return (new McpActivity(Timestamp(), coverage, chosen.OrderByDescending(n => n.RepliesToYou.Value).Take(3).ToList(),
                    candidates.Count > offset + 3 || snapshot.ChannelsHasMore || incomplete),
            chosen, next, checkpoint, incomplete);
    }

    private McpNotice ToNotice(ActivityChannel channel, IReadOnlyDictionary<string, string> labels)
        => new(refs.Channel(channel.TangentKey, channel.RoomKey),
            Preview(labels.GetValueOrDefault(channel.RoomKey, channel.RoomKey), 160),
            new McpCount(Math.Min(channel.UnreadCount, 50), channel.UnreadCountCapped || channel.UnreadCount > 50),
            new McpCount(Math.Min(channel.DirectReplies, 50), channel.DirectReplies > 50),
            new McpCount(0, false),
            channel.RoomKey + ":" + channel.LastSequence.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private async Task<IReadOnlyDictionary<string, string>> ChannelLabels(IReadOnlyList<ActivityChannel> channels, CancellationToken ct)
    {
        using var fresh = EntityContext.NoCache();
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var channel in channels)
        {
            if (labels.ContainsKey(channel.RoomKey)) continue;
            var room = await Room.Get(channel.RoomKey, ct);
            var tangent = room is null ? null : await TangentCommunity.Get(room.TangentKey, ct);
            labels[channel.RoomKey] =
                $"{(tangent?.Name.Length > 0 ? tangent.Name : channel.TangentKey)} / {(room?.Title.Length > 0 ? room.Title : channel.RoomKey)}";
        }
        return labels;
    }

    internal async Task<McpActivity> ActivitySegment(string did, string? credential, string? scopeTangent, string? scopeRoom, CancellationToken ct)
        => (await SnapshotActivity(did, credential, null, null, null, 100, ct)).Activity;

    internal async Task<string> ReadinessOf(string did, CancellationToken ct)
    {
        try
        {
            var source = await readiness.Get(did, ct: ct);
            return source.State switch
            {
                "ready" => "ready",
                "provider-unsupported" => "unsupported",
                _ => "needs_connection"
            };
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return "unavailable";
        }
    }
}

public sealed class McpUnknownToolException(string name) : Exception
{
    public string Name { get; } = name;
}
