using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core;
using Koan.Data.Core.Sorting;
using Tangent.Identity;
using Tangent.Infrastructure;
using Tangent.Spaces;

namespace Tangent.Community;

public sealed record TangentMemberView(string ParticipantId, string Did, string? Handle,
    string DisplayName, string? Avatar, string Role, bool IsOwner, bool CanChangeRole,
    IReadOnlyList<ParticipantRole> AllowedRoles, string Classification = "Undeclared");

public sealed record TangentMemberPage(IReadOnlyList<TangentMemberView> Items, string? NextCursor);

/// <summary>Current Tangent membership, with exact account lookup for role changes.</summary>
public sealed class TangentMemberDirectory(ParticipantGovernance governance, ParticipantDirectory directory,
    ParticipantProfiles profiles)
{
    public const int PageSize = 30;

    public async Task<TangentMemberPage> List(string actorId, string tangentKey, string? cursor, CancellationToken ct)
    {
        if (cursor is not null && (cursor.Length != 64 || cursor.Any(c => !char.IsAsciiHexDigit(c))))
            throw new TangentRuleViolation(TangentDenial.InvalidInput, "The member cursor is invalid.");
        var context = await Context(actorId, tangentKey, ct);
        IReadOnlyList<TangentMembership> members;
        using (EntityContext.NoCache())
        {
            var filter = Filter.All(
                Filter.Eq(nameof(TangentMembership.TangentKey), tangentKey),
                Filter.In(nameof(TangentMembership.Role),
                    [TangentRole.Member, TangentRole.Admin, TangentRole.Reader]));
            if (cursor is not null)
                filter = Filter.All(filter, Filter.On(FieldPath.Of(nameof(TangentMembership.Id)),
                    FilterOperator.Gt, FilterValue.Of(cursor)));
            var query = QueryDefinition.All.Where(filter) with
            {
                Page = 1, PageSize = PageSize + 1,
                Sort = SortSpecParser.ParseStrict<TangentMembership>(nameof(TangentMembership.Id))
            };
            members = await TangentMembership.All(query, ct);
        }
        var selected = members.Take(PageSize).ToArray();
        var items = new List<TangentMemberView>(PageSize + 1);
        if (cursor is null)
        {
            var owner = await Present(context, context.Tangent.OwnerParticipantId, null, ct);
            if (owner is not null) items.Add(owner);
        }
        foreach (var membership in selected)
        {
            if (membership.ParticipantId == context.Tangent.OwnerParticipantId) continue;
            var presented = await Present(context, membership.ParticipantId, membership.Role, ct);
            if (presented is not null) items.Add(presented);
        }
        // Cursor advances by the storage row, even when an orphaned membership cannot be displayed.
        return new TangentMemberPage(items, members.Count > PageSize ? selected[^1].Id : null);
    }

    public async Task<TangentMemberView> Preview(string actorId, string tangentKey, string identifier, CancellationToken ct)
    {
        var context = await Context(actorId, tangentKey, ct);
        if (string.IsNullOrWhiteSpace(identifier) || identifier.Length > 253)
            throw new TangentRuleViolation(TangentDenial.InvalidInput, "Enter a handle, DID, or participant identifier of at most 253 characters.");
        (Participant Participant, string MatchedForm)? resolved;
        using (EntityContext.NoCache()) resolved = await directory.ByIdentifier(identifier, ct);
        if (resolved is null)
            throw new TangentRuleViolation(TangentDenial.NotFound, "No account has established a verified arrival under that identifier.");
        TangentMembership? membership;
        using (EntityContext.NoCache())
            membership = await TangentMembership.Get(TangentMembership.Key(tangentKey, resolved.Value.Participant.Id), ct);
        return await Present(context, resolved.Value.Participant.Id, membership?.Role, ct)
            ?? throw new TangentRuleViolation(TangentDenial.NotFound, "The account is no longer available.");
    }

    private async Task<ContextData> Context(string actorId, string tangentKey, CancellationToken ct)
    {
        if (!Participant.IsValidId(actorId))
            throw new TangentRuleViolation(TangentDenial.Forbidden, "A verified participant is required.");
        Community.Tangent? tangent;
        Space? space;
        Participant? actor;
        TangentMembership? actorMembership;
        using (EntityContext.NoCache())
        {
            tangent = await Community.Tangent.Get(tangentKey, ct);
            space = await Space.Get(TangentConstants.SpaceId, ct);
            actor = await Participant.Get(actorId, ct);
            actorMembership = tangent is null ? null : await TangentMembership.Get(TangentMembership.Key(tangentKey, actorId), ct);
        }
        if (tangent is null)
            throw new TangentRuleViolation(TangentDenial.NotFound, "The requested Tangent does not exist.");
        if (actor is null || actor.IsSuspended || !await governance.CanAdminister(actorId, tangentKey, null, ct))
            throw new TangentRuleViolation(TangentDenial.Forbidden, "Only an active Tangent steward can inspect its members.");
        return new ContextData(tangent, space?.OwnerParticipantId, actorId,
            tangent.IsOwner(actorId) || space?.IsOwner(actorId) == true,
            actorMembership?.Role == TangentRole.Admin);
    }

    private async Task<TangentMemberView?> Present(ContextData context, string participantId, TangentRole? role, CancellationToken ct)
    {
        Participant? participant;
        using (EntityContext.NoCache()) participant = await Participant.Get(participantId, ct);
        if (participant is null) return null;
        var isOwner = context.Tangent.IsOwner(participantId);
        var protectedTarget = isOwner || participantId == context.HostOwnerId || participantId == context.ActorId;
        var allowed = protectedTarget ? Array.Empty<ParticipantRole>()
            : context.IsOwner ? [ParticipantRole.Admin, ParticipantRole.Member, ParticipantRole.Reader]
            : context.IsAdministrator && role != TangentRole.Admin
                ? [ParticipantRole.Member, ParticipantRole.Reader]
                : Array.Empty<ParticipantRole>();
        var profile = await profiles.Read(participantId, ct);
        var did = await directory.PerennialValue(participantId, ct);
        return new TangentMemberView(participantId, did, profile.Handle,
            profile.DisplayName ?? profile.Handle ?? did, profile.Avatar,
            isOwner ? "Owner" : role?.ToString() ?? "None", isOwner, allowed.Length > 0, allowed, participant.Classification.ToString());
    }

    private sealed record ContextData(Community.Tangent Tangent, string? HostOwnerId, string ActorId,
        bool IsOwner, bool IsAdministrator);
}
