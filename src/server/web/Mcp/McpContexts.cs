using System.Security.Claims;
using Koan.Data.Core;
using Koan.Web.Auth.Connector.Atproto;
using TangentSpace.Activity;
using TangentSpace.Infrastructure;
using TangentSpace.Participants;
using TangentSpace.Participation;

namespace TangentSpace.Mcp;

/// <summary>Selects and resolves companion selections and their server-bound participation contexts.
/// Selection can only confirm the identity already authorized by the presented credential; it can
/// never impersonate another participant, and a context never rebinds.</summary>
public sealed class McpContexts(TimeProvider clock, ActivityService activity, McpRefs refs, PolicyGate gate)
{
    public sealed record Selection(McpSelection Companion, Participant Participant, bool Created);

    public sealed record Arrival(McpContext Context, bool Created);

    public async Task<Selection> Select(ClaimsPrincipal principal, string moniker, CancellationToken ct)
    {
        var did = ParticipationAccess.Require(principal, ParticipationGrants.Welcome);
        var credential = CredentialId(principal);
        await activity.EnsureParticipantActive(did, credential, ct);
        using var fresh = EntityContext.NoCache();
        var participant = await Participant.Get(did, ct);
        if (participant is null || participant.IsSuspended || !CompanionIdentity.Matches(moniker, participant, did))
            throw new McpCompanionUnavailableException();
        var now = clock.GetUtcNow();
        // The query/create race runs under the host policy gate; the section holds no domain calls,
        // so the non-reentrant gate cannot deadlock with arrival or governance paths.
        await gate.Enter(ct);
        try
        {
            await activity.EnsureParticipantActive(did, credential, ct);
            var existing = (await McpSelection.Query(
                selection => selection.CredentialId == credential && selection.ParticipantDid == did, ct))
                .OrderByDescending(selection => selection.LastUsedAt).FirstOrDefault();
            // An expired selection is never resurrected; a fresh identifier is issued instead.
            if (existing is not null && existing.IsUsable(now))
            {
                existing.Touch(now);
                await existing.Save(ct);
                return new Selection(existing, participant, false);
            }
            var (created, _) = McpSelection.Issue(credential, did, now);
            await created.Save(ct);
            return new Selection(created, participant, true);
        }
        finally { gate.Exit(); }
    }

    /// <summary>Validates a selected companion under the caller's credential. A selection bound to
    /// another credential or DID is indistinguishable from an expired one.</summary>
    public async Task<Selection> SelectionOf(ClaimsPrincipal principal, string? companionId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(companionId) || companionId.Length > 96
            || !companionId.StartsWith(McpSelection.IdPrefix, StringComparison.Ordinal))
            throw new McpContextExpiredException();
        var did = ParticipationAccess.Require(principal, ParticipationGrants.Welcome);
        var credential = CredentialId(principal);
        await activity.EnsureParticipantActive(did, credential, ct);
        using var fresh = EntityContext.NoCache();
        var selection = await McpSelection.Get(companionId, ct);
        if (selection is null || selection.CredentialId != credential || selection.ParticipantDid != did
            || !selection.IsUsable(clock.GetUtcNow()))
            throw new McpContextExpiredException();
        var participant = await Participant.Get(did, ct);
        if (participant is null || participant.IsSuspended) throw new McpCompanionUnavailableException();
        return new Selection(selection, participant, false);
    }

    /// <summary>Creates or reuses the context binding one validated selection to this server's
    /// canonical origin. The binding is immutable: reuse only ever matches the same credential, DID,
    /// companion and origin.</summary>
    public async Task<Arrival> Bind(McpSelection selection, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            await activity.EnsureParticipantActive(selection.ParticipantDid, selection.CredentialId, ct);
            var savedSelection = await McpSelection.Get(selection.Id, ct);
            if (savedSelection is null || savedSelection.CredentialId != selection.CredentialId
                || savedSelection.ParticipantDid != selection.ParticipantDid || !savedSelection.IsUsable(clock.GetUtcNow()))
                throw new McpContextExpiredException();
            selection = savedSelection;
            var existing = (await McpContext.Query(
                context => context.CredentialId == selection.CredentialId
                    && context.ParticipantDid == selection.ParticipantDid
                    && context.CompanionId == selection.Id
                    && context.Origin == refs.Origin, ct))
                .OrderByDescending(context => context.LastUsedAt).FirstOrDefault();
            if (existing is not null && existing.IsUsable(now))
            {
                existing.Touch(now);
                await existing.Save(ct);
                selection.Touch(now);
                await selection.Save(ct);
                return new Arrival(existing, false);
            }
            var (created, _) = McpContext.Issue(selection.CredentialId, selection.ParticipantDid,
                selection.Id, refs.Origin, now);
            await created.Save(ct);
            selection.Touch(now);
            await selection.Save(ct);
            return new Arrival(created, true);
        }
        finally { gate.Exit(); }
    }

    /// <summary>Per-call validation: the context must belong to this credential, stay bound to this
    /// companion and this server's configured origin, and the participant must remain active with a
    /// live credential. Renewal happens only after all checks pass.</summary>
    public async Task<McpContext> Resolve(ClaimsPrincipal principal, string? contextId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(contextId) || contextId.Length > 96 || !contextId.StartsWith(McpContext.IdPrefix, StringComparison.Ordinal))
            throw new McpContextExpiredException();
        var did = ParticipationAccess.Require(principal, ParticipationGrants.Welcome);
        var credential = CredentialId(principal);
        await activity.EnsureParticipantActive(did, credential, ct);
        using var fresh = EntityContext.NoCache();
        var context = await McpContext.Get(contextId, ct);
        var now = clock.GetUtcNow();
        // A context bound to another credential or DID, to another origin, or to nothing at all is
        // indistinguishable from an expired one; pre-split unbound contexts expire safely.
        if (context is null || context.CredentialId != credential || context.ParticipantDid != did
            || !context.IsBound || context.Origin != refs.Origin || !context.IsUsable(now))
            throw new McpContextExpiredException();
        await activity.EnsureParticipantActive(did, credential, ct);
        context.Touch(now);
        await context.Save(ct);
        return context;
    }

    public static string CredentialId(ClaimsPrincipal principal)
        => principal.FindFirst(ParticipationConstants.CredentialClaim)?.Value
           ?? throw new UnauthorizedAccessException("An MCP context requires a participation credential.");

    public static (string Did, string CredentialId)? ReadBearerIdentity(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true) return null;
        var did = principal.FindFirst(AtprotoClaimTypes.Did)?.Value;
        var credential = principal.FindFirst(ParticipationConstants.CredentialClaim)?.Value;
        return did is null || credential is null ? null : (did, credential);
    }
}

/// <summary>Moniker matching for inbound selection: only the credential's own handle or DID is selectable.
/// A single leading @ is accepted because people write handles that way; stored handles omit it.</summary>
public static class CompanionIdentity
{
    public static bool Matches(string moniker, Participant participant, string did)
    {
        if (string.IsNullOrWhiteSpace(moniker) || moniker.Length > 253) return false;
        if (string.Equals(moniker, did, StringComparison.Ordinal)) return true;
        if (participant.Handle is not { Length: > 0 } handle) return false;
        var supplied = moniker.Trim();
        if (supplied.StartsWith('@') && supplied.Length > 1) supplied = supplied[1..];
        return string.Equals(supplied, handle.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public static string ActingAs(Participant participant, string did)
        => participant.Handle is { Length: > 0 } ? participant.Handle : did;

    public static string DisplayName(Participant participant, string did)
    {
        var acting = ActingAs(participant, did);
        if (participant.Handle is { Length: > 0 })
        {
            var at = acting.IndexOf('.');
            if (at > 0) return acting[..at];
        }
        return acting.Length <= 80 ? acting : acting[..80];
    }
}

public sealed class McpContextExpiredException : Exception;

public sealed class McpCompanionUnavailableException : Exception;
