using Koan.Data.Core;
using Tangent.Activity;
using Tangent.Community;
using Tangent.Infrastructure;
using Tangent.Application;
using Tangent.Identity;
using Tangent.Community;
using Tangent.Stewardship;
using Microsoft.Extensions.Options;
using Tangent.Access;

namespace Tangent.Spaces;

public sealed class ServerGovernance(TimeProvider clock, PolicyGate gate, IOptions<SpaceOptions> options, ParticipantDirectory directory,
    TangentRoleAccess roleAccess)
{
    public async Task<ServerSettings> Read(string? actorId, CancellationToken ct)
    {
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            var space = await Space.Get(TangentConstants.SpaceId, ct);
            var owner = space?.IsOwner(actorId) == true;
            var participant = actorId is null ? null : await Participant.Get(actorId, ct);
            var bag = await roleAccess.Bag(actorId, ct);
            if (space is not null && !TangentRoleAccess.CanDo((space.Access ?? AccessMap.ServerDefaults()).ResolveServer().See, bag))
                throw new UnauthorizedAccessException("This server is not available to the current participant.");
            var canCreate = await CanCreateTangent(space, participant, ct);
            var canManage = participant?.IsSuspended != true && space is not null
                && TangentRoleAccess.CanDo((space.Access ?? AccessMap.ServerDefaults()).ResolveServer().Manage, bag);
            var declared = space is not null && (space.HumanDeclared || !string.IsNullOrWhiteSpace(space.OwnerParticipantId));
            // First ownership is claimable only by the configured DID's current holder (or anyone when unconfigured).
            var configured = string.IsNullOrWhiteSpace(options.Value.OwnerDid)
                || actorId is not null && await directory.ByDid(options.Value.OwnerDid, ct) is { } pinned && pinned.Id == actorId;
            await EntityContext.Commit(ct);
            return new ServerSettings(space?.Name ?? "", space?.WelcomeMessage ?? "", space?.Motd ?? "",
                space?.AllowAgentTangentOwnership ?? false,
                space?.OwnerParticipantId ?? "", canManage, !declared && HumanHostAccountability.CanClaim(participant) && configured, Permissions.Server(owner, canCreate), space?.Byline ?? "", space?.CoverImageUrl ?? "", space?.BackgroundScene ?? "galaxy", space?.BackgroundColor ?? "", space?.BackgroundIntensity ?? 35, space?.BackgroundMotion ?? true, space?.BackgroundMouseSpotlight ?? true);
        }
        finally { gate.Exit(); }
    }

    /// <summary>Ownership claim. Establishment is the atproto gate: the verified sign-in DID must
    /// satisfy the configured pin, and its holder becomes the space owner participant.</summary>
    /// <summary>Establishes the Space and its accountable human owner. The declaring act is the
    /// caller's: naming the first Tangent. Accountability itself is enforced below, by
    /// RequireDeclaration, not by a flag the caller passes.</summary>
    public async Task<ServerSettings> Claim(string actorId, string? verifiedAtprotoDid, CancellationToken ct)
    {
        ServerSettings result;
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(TopicConstants.AdministrationTransaction);
            var space = await Space.Get(TangentConstants.SpaceId, ct);
            var participant = await Participant.Get(actorId, ct);
            if (!HumanHostAccountability.CanClaim(participant))
                throw new InvalidOperationException("A known agent cannot reserve human server ownership.");
            if (space is not null && !space.IsOwner(actorId))
                throw new InvalidOperationException("Server ownership is already claimed.");
            var establishing = space is null;
            if (space is null)
            {
                if (verifiedAtprotoDid is null)
                    throw new UnauthorizedAccessException("Server ownership requires a verified atproto sign-in.");
                if (await directory.ByDid(verifiedAtprotoDid, ct) is not { } holder || holder.Id != actorId)
                    throw new UnauthorizedAccessException("The verified atproto sign-in must belong to the claiming participant.");
                space = Space.Establish(options.Value, verifiedAtprotoDid, actorId, clock.GetUtcNow());
            }
            // Validate every claim prerequisite before changing either durable row. In particular,
            // a refused ownership claim must not classify its caller as human as a side effect.
            HumanHostAccountability.RequireDeclaration(participant!, ParticipantClassification.Human, isHostOwner: true);
            participant!.Declare(ParticipantClassification.Human);
            await participant.Save(ct);
            space.HumanDeclared = true;
            await space.Save(ct);
            // The Host and its home Tangent are established together; the owner names it during onboarding.
            if (establishing) await Community.Tangent.Home(space).Save(ct);
            await ActivityJournal.AppendInTransaction(ActivityKind.ParticipantChanged, "", actorId, ct: ct);
            await EntityContext.Commit(ct);
            ActivityJournal.SignalAfterCommit();
            result = new ServerSettings(space.Name, space.WelcomeMessage, space.Motd, space.AllowAgentTangentOwnership,
                space.OwnerParticipantId, true, false, Permissions.Server(true), space.Byline, space.CoverImageUrl, space.BackgroundScene, space.BackgroundColor, space.BackgroundIntensity, space.BackgroundMotion, space.BackgroundMouseSpotlight);
        }
        finally { gate.Exit(); }
        // The Owner role projects the committed space; startup repeats the projection (TangentRoleAccess.Seed).
        await roleAccess.EnsureOwner(result.OwnerParticipantId, ct);
        return result;
    }

    public async Task<ServerSettings> Update(string actorId, ServerSettingsPatch patch, CancellationToken ct)
    {
        if (patch.Name is not null && (string.IsNullOrWhiteSpace(patch.Name) || patch.Name.Length > 120)
            || patch.WelcomeMessage?.Length > 4096 || patch.Motd?.Length > 4096 || patch.Byline?.Length > 240)
            throw new InvalidOperationException("Use a server name of 1-120 characters and welcome/MOTD text up to 4096 characters.");
        if (patch.CoverImageUrl is { Length: > 0 } image && !ValidCover(image))
            throw new InvalidOperationException("Use a local image path or an HTTPS image URL.");
        if (patch.BackgroundScene is not null && patch.BackgroundScene is not ("galaxy" or "synapses" or "aurora" or "tides" or "orrery" or "mycelium" or "rain" or "nebula" or "none"))
            throw new InvalidOperationException("BackgroundScene must be one of galaxy, synapses, aurora, tides, orrery, mycelium, rain, nebula, none.");
        if (patch.BackgroundColor is { Length: > 0 } color && (color.Length != 7 || !System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9A-Fa-f]{6}$")))
            throw new InvalidOperationException("BackgroundColor must be a #RRGGBB value or empty.");
        if (patch.BackgroundIntensity is { } intensity && (intensity < 0 || intensity > 100))
            throw new InvalidOperationException("BackgroundIntensity must be between 0 and 100.");
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(TopicConstants.AdministrationTransaction);
            var space = await Space.Get(TangentConstants.SpaceId, ct)
                ?? throw new InvalidOperationException("The server has not been claimed.");
            var participant = await Participant.Get(actorId, ct);
            var bag = await roleAccess.Bag(actorId, ct);
            if (participant?.IsSuspended == true || !TangentRoleAccess.CanDo((space.Access ?? AccessMap.ServerDefaults()).ResolveServer().Manage, bag))
                throw new UnauthorizedAccessException("The current role cannot change server settings.");
            if (patch.Name is not null) space.Name = patch.Name.Trim();
            if (patch.WelcomeMessage is not null) space.WelcomeMessage = patch.WelcomeMessage;
            if (patch.Byline is not null) space.Byline = patch.Byline.Trim();
            if (patch.CoverImageUrl is not null) space.CoverImageUrl = patch.CoverImageUrl.Trim();
            if (patch.Motd is not null) space.Motd = patch.Motd;
            if (patch.AllowAgentTangentOwnership is not null) space.AllowAgentTangentOwnership = patch.AllowAgentTangentOwnership.Value;
            if (patch.BackgroundScene is not null) space.BackgroundScene = patch.BackgroundScene;
            if (patch.BackgroundColor is not null) space.BackgroundColor = patch.BackgroundColor;
            if (patch.BackgroundIntensity is not null) space.BackgroundIntensity = patch.BackgroundIntensity.Value;
            if (patch.BackgroundMotion is not null) space.BackgroundMotion = patch.BackgroundMotion.Value;
            if (patch.BackgroundMouseSpotlight is not null) space.BackgroundMouseSpotlight = patch.BackgroundMouseSpotlight.Value;
            space.PolicyRevision = checked(space.PolicyRevision + 1);
            await space.Save(ct);
            await ActivityJournal.AppendInTransaction(ActivityKind.ParticipantChanged, "", actorId, ct: ct);
            await EntityContext.Commit(ct);
            ActivityJournal.SignalAfterCommit();
            return new ServerSettings(space.Name, space.WelcomeMessage, space.Motd, space.AllowAgentTangentOwnership,
                space.OwnerParticipantId, true, false, Permissions.Server(space.IsOwner(actorId)), space.Byline, space.CoverImageUrl, space.BackgroundScene, space.BackgroundColor, space.BackgroundIntensity, space.BackgroundMotion, space.BackgroundMouseSpotlight);
        }
        finally { gate.Exit(); }
    }

    public async Task<AccessMapView> GetAccess(string actorId, CancellationToken ct)
    {
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            var space = await Space.Get(TangentConstants.SpaceId, ct)
                ?? throw new InvalidOperationException("The server has not been claimed.");
            var participant = await Participant.Get(actorId, ct);
            var bag = await roleAccess.Bag(actorId, ct);
            if (participant?.IsSuspended == true || !TangentRoleAccess.CanDo((space.Access ?? AccessMap.ServerDefaults()).ResolveServer().Manage, bag))
                throw new UnauthorizedAccessException("The current role cannot inspect server access settings.");
            var selected = (space.Access ?? AccessMap.ServerDefaults()).NormalizeForServer();
            await EntityContext.Commit(ct);
            return new AccessMapView(selected, selected.ResolveServer(), []);
        }
        finally { gate.Exit(); }
    }

    public async Task<AccessMapView> SetAccess(string actorId, AccessMap access, CancellationToken ct)
    {
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(TopicConstants.AdministrationTransaction);
            var space = await Space.Get(TangentConstants.SpaceId, ct)
                ?? throw new InvalidOperationException("The server has not been claimed.");
            var participant = await Participant.Get(actorId, ct);
            var bag = await roleAccess.Bag(actorId, ct);
            var canManage = participant?.IsSuspended != true
                && TangentRoleAccess.CanDo((space.Access ?? AccessMap.ServerDefaults()).ResolveServer().Manage, bag);
            space.ChangeAccess(actorId, access, canManage);
            await space.Save(ct);
            await ActivityJournal.AppendInTransaction(ActivityKind.ParticipantChanged, "", actorId, ct: ct);
            await EntityContext.Commit(ct);
            ActivityJournal.SignalAfterCommit();
            var selected = (space.Access ?? AccessMap.ServerDefaults()).NormalizeForServer();
            return new AccessMapView(selected, selected.ResolveServer(), []);
        }
        finally { gate.Exit(); }
    }

    private static bool ValidCover(string value)
        => value.Length <= 2048 && !value.Any(c => char.IsControl(c) || c is '\'' or '"' or '\\')
            && (value.StartsWith('/') && !value.StartsWith("//")
                || Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.UserInfo.Length == 0);

    private async Task<bool> CanCreateTangent(Space? space, Participant? actor, CancellationToken ct)
    {
        if (space is null || actor is null || actor.IsSuspended) return false;
        var bag = await roleAccess.Bag(actor.Id, ct);
        return TangentRoleAccess.CanDo((space.Access ?? AccessMap.ServerDefaults()).ResolveServer().CreateTangents, bag);
    }

    public async Task<Participant> Declare(string actorId, ParticipantClassification classification, CancellationToken ct)
    {
        if (!Enum.IsDefined(classification)) throw new InvalidOperationException("Unknown participant classification.");
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(TopicConstants.AdministrationTransaction);
            var participant = await Participant.Get(actorId, ct) ?? throw new InvalidOperationException("Participant not found.");
            var space = await Space.Get(TangentConstants.SpaceId, ct);
            if (participant.IsSuspended) throw new UnauthorizedAccessException("A suspended participant cannot change its classification.");
            HumanHostAccountability.RequireDeclaration(participant, classification, space?.IsOwner(actorId) == true);
            participant.Declare(classification); await participant.Save(ct);
            await ActivityJournal.AppendInTransaction(ActivityKind.ParticipantChanged, "", actorId, ct: ct);
            await EntityContext.Commit(ct); ActivityJournal.SignalAfterCommit(); return participant;
        }
        finally { gate.Exit(); }
    }
}

public sealed record ServerSettings(string Name, string WelcomeMessage, string Motd,
    bool AllowAgentTangentOwnership, string OwnerParticipantId, bool CanManage, bool CanClaim,
    PermissionView? Permissions = null, string Byline = "", string CoverImageUrl = "",
    string BackgroundScene = "galaxy", string BackgroundColor = "", int BackgroundIntensity = 35, bool BackgroundMotion = true, bool BackgroundMouseSpotlight = true);
public sealed record ServerSettingsPatch(string? Name = null, string? WelcomeMessage = null, string? Motd = null,
    bool? AllowAgentTangentOwnership = null, string? Byline = null, string? CoverImageUrl = null,
    string? BackgroundScene = null, string? BackgroundColor = null, int? BackgroundIntensity = null, bool? BackgroundMotion = null, bool? BackgroundMouseSpotlight = null);
