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
public sealed class McpContexts(TimeProvider clock, ActivityService activity, McpRefs refs, PolicyGate gate,
    TangentSpace.Participants.ParticipantDirectory directory)
{
    public sealed record Selection(McpSelection Companion, Participant Participant, bool Created);

    public sealed record Arrival(McpContext Context, bool Created);

    public async Task<Selection> Select(ClaimsPrincipal principal, string moniker, CancellationToken ct)
    {
        var participantId = ParticipationAccess.Require(principal, ParticipationGrants.Welcome);
        var credential = CredentialId(principal);
        await activity.EnsureParticipantActive(participantId, credential, ct);
        using var fresh = EntityContext.NoCache();
        var participant = await Participant.Get(participantId, ct);
        if (participant is null || participant.IsSuspended
            || !CompanionIdentity.Matches(moniker, participant, await directory.IdentitiesOf(participantId, ct)))
            throw new McpCompanionUnavailableException();
        var now = clock.GetUtcNow();
        // The query/create race runs under the host policy gate; the section holds no domain calls,
        // so the non-reentrant gate cannot deadlock with arrival or governance paths.
        await gate.Enter(ct);
        try
        {
            await activity.EnsureParticipantActive(participantId, credential, ct);
            var existing = (await McpSelection.Query(
                selection => selection.CredentialId == credential && selection.ParticipantId == participantId, ct))
                .OrderByDescending(selection => selection.LastUsedAt).FirstOrDefault();
            // An expired selection is never resurrected; a fresh identifier is issued instead.
            if (existing is not null && existing.IsUsable(now))
            {
                existing.Touch(now);
                await existing.Save(ct);
                return new Selection(existing, participant, false);
            }
            var (created, _) = McpSelection.Issue(credential, participantId, now);
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
        var participantId = ParticipationAccess.Require(principal, ParticipationGrants.Welcome);
        var credential = CredentialId(principal);
        await activity.EnsureParticipantActive(participantId, credential, ct);
        using var fresh = EntityContext.NoCache();
        var selection = await McpSelection.Get(companionId, ct);
        if (selection is null || selection.CredentialId != credential || selection.ParticipantId != participantId
            || !selection.IsUsable(clock.GetUtcNow()))
            throw new McpContextExpiredException();
        var participant = await Participant.Get(participantId, ct);
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
            await activity.EnsureParticipantActive(selection.ParticipantId, selection.CredentialId, ct);
            var savedSelection = await McpSelection.Get(selection.Id, ct);
            if (savedSelection is null || savedSelection.CredentialId != selection.CredentialId
                || savedSelection.ParticipantId != selection.ParticipantId || !savedSelection.IsUsable(clock.GetUtcNow()))
                throw new McpContextExpiredException();
            selection = savedSelection;
            var existing = (await McpContext.Query(
                context => context.CredentialId == selection.CredentialId
                    && context.ParticipantId == selection.ParticipantId
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
            var (created, _) = McpContext.Issue(selection.CredentialId, selection.ParticipantId,
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
        var participantId = ParticipationAccess.Require(principal, ParticipationGrants.Welcome);
        var credential = CredentialId(principal);
        await activity.EnsureParticipantActive(participantId, credential, ct);
        using var fresh = EntityContext.NoCache();
        var context = await McpContext.Get(contextId, ct);
        var now = clock.GetUtcNow();
        // A context bound to another credential or participant, to another origin, or to nothing at all is
        // indistinguishable from an expired one; pre-split unbound contexts expire safely.
        if (context is null || context.CredentialId != credential || context.ParticipantId != participantId
            || !context.IsBound || context.Origin != refs.Origin || !context.IsUsable(now))
            throw new McpContextExpiredException();
        await activity.EnsureParticipantActive(participantId, credential, ct);
        context.Touch(now);
        await context.Save(ct);
        return context;
    }

    public static string CredentialId(ClaimsPrincipal principal)
        => principal.FindFirst(ParticipationConstants.CredentialClaim)?.Value
           ?? throw new UnauthorizedAccessException("An MCP context requires a participation credential.");

    public static (string ParticipantId, string CredentialId)? ReadBearerIdentity(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true) return null;
        var participant = principal.FindFirst(ParticipationConstants.ParticipantClaim)?.Value;
        var credential = principal.FindFirst(ParticipationConstants.CredentialClaim)?.Value;
        return participant is null || credential is null ? null : (participant, credential);
    }
}

/// <summary>Moniker matching for inbound selection: only the credential's own identity is
/// selectable — one of its values (atproto DID or internal DID) or one of its labels, matched
/// trimmed with a single optional leading @ (labels OrdinalIgnoreCase, values Ordinal).</summary>
public static class CompanionIdentity
{
    public static bool Matches(string moniker, Participant participant, IReadOnlyList<TangentSpace.Participants.ParticipantIdentity> identities)
    {
        if (string.IsNullOrWhiteSpace(moniker) || moniker.Length > 253 || participant is null) return false;
        if (identities.Any(identity => string.Equals(moniker, identity.Value, StringComparison.Ordinal))) return true;
        var supplied = moniker.Trim();
        if (supplied.StartsWith('@') && supplied.Length > 1) supplied = supplied[1..];
        return identities.Any(identity => identity.Label is { Length: > 0 } label
            && string.Equals(supplied, label.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static string ActingAs(string? label, string identityValue)
        => label is { Length: > 0 } ? label : identityValue;

    public static string DisplayName(string? label, string identityValue)
    {
        var acting = ActingAs(label, identityValue);
        if (label is { Length: > 0 })
        {
            var at = acting.IndexOf('.');
            if (at > 0) return acting[..at];
        }
        return acting.Length <= 80 ? acting : acting[..80];
    }
}

public sealed class McpContextExpiredException : Exception;

public sealed class McpCompanionUnavailableException : Exception;
