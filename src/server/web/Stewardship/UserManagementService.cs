using Koan.Data.Core;
using Koan.Identity.Roles;
using Tangent.Access;
using Tangent.Activity;
using Tangent.Application;
using Tangent.Community;
using Tangent.Identity;
using Tangent.Infrastructure;
using Tangent.Spaces;

namespace Tangent.Stewardship;

/// <summary>
/// The user-management ladder behind the experience API's one management endpoint. A rung is a
/// fact, not a workflow: warn is communicative and journaled as moderation; timeout is a
/// reversible, time-boxed Tangent restriction; ban is that restriction without a clock; suspend
/// is the server-wide participant suspension; add_role/remove_role go through Koan roles. The
/// authority is the actor's role bag — each rung is a global permission, `assign_roles` rides
/// role management, and no one grants a role they do not hold. The connector projects these
/// permissions as its action enum; the enforcement lives only here.
/// </summary>
public sealed class UserManagementService(TimeProvider clock, PolicyGate gate, ParticipantDirectory directory,
    TangentRoleAccess roleAccess, RoleCollection roles, References refs, SignalBus signals)
{
    public const int MaximumReasonLength = 2000;
    public static readonly TimeSpan MaximumTimeout = TimeSpan.FromDays(30);

    /// <summary>The rung strings the management endpoint speaks (the connector's ManageAction).</summary>
    public const string Warn = "warn";
    public const string Timeout = "timeout";
    public const string Suspend = "suspend";
    public const string Ban = "ban";
    public const string AddRole = "add_role";
    public const string RemoveRole = "remove_role";

    public sealed record Outcome(string Action, string TargetParticipantId, string Reason,
        string? Role, DateTimeOffset? Until, string? TangentKey, string? CaseRef, string AuditId);

    public async Task<Outcome> Manage(string actorId, string targetIdentifier, string action, string reason,
        long? durationSeconds, string? role, string? caseRef, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetIdentifier);
        if (action is not (Warn or Timeout or Suspend or Ban or AddRole or RemoveRole))
            throw new ArgumentException("Choose warn, timeout, suspend, ban, add_role, or remove_role.", nameof(action));
        CheckReason(reason);
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            var now = clock.GetUtcNow();
            var space = await Space.Get(TangentConstants.SpaceId, ct)
                ?? throw new InvalidOperationException("The server's Space row is missing.");
            var actor = await Participant.Get(actorId, ct)
                ?? throw new UnauthorizedAccessException("A verified arrival is required first.");
            if (actor.IsSuspended) throw new UnauthorizedAccessException("A suspended participant cannot manage users.");
            var target = (await directory.ByIdentifier(targetIdentifier, ct))?.Participant
                ?? throw new ArgumentException("That participant has not arrived on this server yet.");
            if (target.Id == actorId) throw new UnauthorizedAccessException("The ladder never points at its holder.");
            if (space.IsOwner(target.Id)) throw new UnauthorizedAccessException("The server's accountable owner is beyond the ladder.");
            var bag = await roleAccess.Bag(actorId, ct);

            var outcome = action switch
            {
                Warn => await WarnParticipant(actorId, target.Id, reason, bag, now, ct),
                Timeout or Ban => await Restrict(actorId, target.Id, action, reason, durationSeconds, caseRef, bag, now, ct),
                Suspend => await SuspendParticipant(actorId, target.Id, reason, space, bag, now, ct),
                _ => await AssignRole(actorId, target.Id, action, reason, role, bag, now, ct)
            };
            return outcome;
        }
        finally { gate.Exit(); }
    }

    /// <summary>One coordinated write per rung. The role rung cannot ride a deferred
    /// transaction: the Role entity is RoleCollection-managed, and its guarded write cancels
    /// when coordination defers it, so the mutation commits immediately and its record follows
    /// in a small transaction of its own.</summary>
    private async Task<Outcome> Coordinate(Func<Task<Outcome>> act, CancellationToken ct)
    {
        using var transaction = EntityContext.Transaction(TopicConstants.AdministrationTransaction);
        var outcome = await act();
        await EntityContext.Commit(ct);
        signals.Pulse();
        return outcome;
    }

    private async Task<Outcome> WarnParticipant(string actorId, string targetId, string reason,
        IReadOnlyCollection<string> bag, DateTimeOffset now, CancellationToken ct)
    {
        RequireRung(TangentPermissions.WarnUsers, bag);
        return await Coordinate(async () =>
        {
            var audit = await Audit(actorId, targetId, TopicAdministration.Warn, true, reason, now, ct);
            await ActivityJournal.AppendInTransaction(ActivityKind.ModerationNotice, "", actorId, targetId, ct: ct);
            return new Outcome(Warn, targetId, reason, null, null, null, null, audit.Id);
        }, ct);
    }

    /// <summary>Timeout and ban are Tangent-scoped restrictions and must cite the case that
    /// grounds them: the case names the Tangent where the sanction binds, so the act and its
    /// evidence cannot drift apart. A warning or a filed case precedes either.</summary>
    private async Task<Outcome> Restrict(string actorId, string targetId, string action, string reason,
        long? durationSeconds, string? caseRef, IReadOnlyCollection<string> bag, DateTimeOffset now, CancellationToken ct)
    {
        RequireRung(action == Timeout ? TangentPermissions.TimeoutUsers : TangentPermissions.BanUsers, bag);
        var parsedCase = refs.ParseCase(caseRef) ?? throw new ArgumentException(
            "Copy the moderation case reference this action follows; a restriction binds where its evidence lives.", nameof(caseRef));
        var tangentKey = parsedCase.TangentKey;
        DateTimeOffset? until = null;
        if (action == Timeout)
        {
            if (durationSeconds is not { } seconds || seconds <= 0)
                throw new ArgumentException("A timeout carries a positive durationSeconds.", nameof(durationSeconds));
            var expiry = now.AddSeconds(seconds);
            if (expiry > now + MaximumTimeout)
                throw new ArgumentException($"A timeout runs at most {MaximumTimeout.TotalDays:0} days.", nameof(durationSeconds));
            until = expiry;
        }
        else if (durationSeconds is not null)
            throw new ArgumentException("A ban on this server is indefinite; a timed restriction is a timeout.", nameof(durationSeconds));

        await RequireUnrestricted(actorId, tangentKey, now, ct);
        return await Coordinate(async () =>
        {
            var stored = ScopedRestriction.Impose(RestrictionScope.Tangent, tangentKey, targetId,
                action == Timeout ? RestrictionKind.Timeout : RestrictionKind.Ban, until, reason, actorId, now);
            await stored.Save(ct);
            await ActivityJournal.AppendInTransaction(ActivityKind.RestrictionChanged, "", actorId, targetId,
                tangentKey, ct: ct);
            var audit = await Audit(actorId, targetId, TopicAdministration.SetRestriction, true, reason, now, ct);
            return new Outcome(action, targetId, reason, null, until, tangentKey, caseRef, audit.Id);
        }, ct);
    }

    private async Task<Outcome> SuspendParticipant(string actorId, string targetId, string reason,
        Space space, IReadOnlyCollection<string> bag, DateTimeOffset now, CancellationToken ct)
    {
        RequireRung(TangentPermissions.ManageParticipants, bag);
        return await Coordinate(async () =>
        {
            var participant = await Participant.Get(targetId, ct)
                ?? throw new ArgumentException("That participant has not arrived on this server yet.");
            participant.IsSuspended = true;
            space.PolicyRevision = checked(space.PolicyRevision + 1);
            await participant.Save(ct);
            await space.Save(ct);
            await ActivityJournal.AppendInTransaction(ActivityKind.ParticipantChanged, "", actorId, targetId, ct: ct);
            var audit = await Audit(actorId, targetId, TopicAdministration.SetSuspension, true, reason, now, ct,
                revision: space.PolicyRevision);
            return new Outcome(Suspend, targetId, reason, null, null, null, null, audit.Id);
        }, ct);
    }

    /// <summary>One string covers both role rungs. Besides role management the actor must hold
    /// the role in question, and the Owner role is not assignable here at all.</summary>
    private async Task<Outcome> AssignRole(string actorId, string targetId, string action, string reason,
        string? role, IReadOnlyCollection<string> bag, DateTimeOffset now, CancellationToken ct)
    {
        RequireRung(TangentPermissions.ManageRoles, bag);
        if (string.IsNullOrWhiteSpace(role))
            throw new ArgumentException("Name the role to assign or remove.", nameof(role));
        var roleToken = role.StartsWith("role:", StringComparison.Ordinal) ? role : $"role:{role.Trim()}";
        var target = await roles.Get(roleToken, ct)
            ?? throw new ArgumentException("That role does not exist on this server.", nameof(role));
        if (target.Id == TangentBuiltInRoles.Owner.Token)
            throw new UnauthorizedAccessException("The Owner role follows the server's accountable human owner.");
        if (target.Id == TangentBuiltInRoles.Participant.Token || target.Id == "role:everyone")
            throw new UnauthorizedAccessException("Everyone signed in already holds that role; it is derived and never granted or removed.");
        // Permission containment decides (ADR 0013): no positional hierarchy, so granting a role
        // means holding every permission it carries.
        var missing = target.Permissions.FirstOrDefault(permission => !bag.Contains(permission));
        if (missing is not null)
            throw new UnauthorizedAccessException("No one grants a role whose permissions they do not hold.");
        if (action == AddRole) await roles.Add(target.Id, targetId, ct);
        else await roles.Remove(target.Id, targetId, ct);
        string auditId;
        using (var transaction = EntityContext.Transaction(TopicConstants.AcceptanceTransaction))
        {
            await ActivityJournal.AppendInTransaction(ActivityKind.RoleMembershipChanged, "", actorId, targetId, ct: ct);
            var audit = await Audit(actorId, targetId, TopicAdministration.AssignRoles, true,
                $"{action} {roleToken}: {reason}", now, ct);
            await EntityContext.Commit(ct);
            auditId = audit.Id;
        }
        signals.Pulse();
        return new Outcome(action, targetId, reason, roleToken, null, null, null, auditId);
    }

    private static void RequireRung(string permission, IReadOnlyCollection<string> bag)
    {
        if (!TangentRoleAccess.CanDo([permission], bag))
            throw new UnauthorizedAccessException("The current roles do not carry this rung of the ladder.");
    }

    private async Task RequireUnrestricted(string actorId, string tangentKey, DateTimeOffset now, CancellationToken ct)
    {
        if (await Restrictions.ForTangent(actorId, tangentKey, now, ct) is not null)
            throw new UnauthorizedAccessException("A scoped restriction currently denies this administration.");
    }

    private static async Task<TopicAudit> Audit(string actorId, string targetId, TopicAdministration operation,
        bool accepted, string reason, DateTimeOffset now, CancellationToken ct,
        long? revision = null, long? selectedPolicyRevision = null)
    {
        var audit = new TopicAudit
        {
            ActorParticipantId = actorId, TopicKey = "", TargetParticipantId = targetId,
            Operation = operation, Accepted = accepted, Reason = reason,
            SelectedPolicyRevision = selectedPolicyRevision ?? 0, SpacePolicyRevision = revision ?? 0,
            OccurredAt = now
        };
        await audit.Save(ct);
        return audit;
    }

    private static void CheckReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > MaximumReasonLength || reason.Contains('\0'))
            throw new ArgumentException($"A reason must contain 1–{MaximumReasonLength} characters and no null characters.", nameof(reason));
    }
}
