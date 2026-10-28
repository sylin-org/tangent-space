using Koan.Data.Core;
using Tangent.Activity;
using Tangent.Community;
using Tangent.Infrastructure;
using Tangent.Application;
using Tangent.Identity;
using Tangent.Stewardship;
using Microsoft.Extensions.Options;
using Tangent.Access;

namespace Tangent.Spaces;

public sealed class ServerGovernance(TimeProvider clock, PolicyGate gate, IOptions<SpaceOptions> options, ParticipantDirectory directory,
    TangentRoleAccess roleAccess, SignalBus signals, TangentGovernance tangents)
{
    public async Task<ServerSettings> Read(string? actorId, CancellationToken ct)
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
        return new ServerSettings(space?.Name ?? "", space?.WelcomeMessage ?? "", space?.Motd ?? "",
            space?.AllowAgentTangentOwnership ?? false,
            space?.OwnerParticipantId ?? "", canManage, !declared && HumanHostAccountability.CanClaim(participant) && configured, Permissions.Server(owner, canCreate), space?.Byline ?? "", space?.CoverImageUrl ?? "", space?.BackgroundScene ?? "galaxy", space?.BackgroundColor ?? "", space?.BackgroundIntensity ?? 35, space?.BackgroundMotion ?? true, space?.BackgroundMouseSpotlight ?? true, space?.MarkImageUrl ?? "", space?.Accent ?? "gold", CanManageParticipants: await roleAccess.CanManageParticipants(actorId, ct));
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
            var (space, establishing) = await ClaimWithinTransaction(actorId, verifiedAtprotoDid, ct);
            // The Host and its home Tangent are established together; the owner names it during onboarding.
            if (establishing) await Community.Tangent.Home(space).Save(ct);
            await EntityContext.Commit(ct);
            signals.Pulse();
            result = new ServerSettings(space.Name, space.WelcomeMessage, space.Motd, space.AllowAgentTangentOwnership,
                space.OwnerParticipantId, true, false, Permissions.Server(true), space.Byline, space.CoverImageUrl, space.BackgroundScene, space.BackgroundColor, space.BackgroundIntensity, space.BackgroundMotion, space.BackgroundMouseSpotlight, space.MarkImageUrl, space.Accent, CanManageParticipants: true);
        }
        finally { gate.Exit(); }
        // The Owner role projects the committed space; startup repeats the projection (TangentRoleAccess.Seed).
        return result;
    }

    /// <summary>Completes onboarding through ordered, retryable writes. Koan's deferred
    /// transaction is not atomic; a partial claim is resumed by the same owner's next request.</summary>
    public async Task<TangentDescription> CompleteOnboarding(string actorId, string? verifiedAtprotoDid,
        string? expectedDid, string? name, string? description, string? artwork, bool skip, CancellationToken ct,
        string? kind = null)
    {
        await gate.Enter(ct);
        try
        {
            if (string.IsNullOrWhiteSpace(expectedDid)
                || !string.Equals(expectedDid, verifiedAtprotoDid, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("The signed-in account changed. Return to onboarding with the current account.");
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(TopicConstants.AdministrationTransaction);
            var space = await Space.Get(TangentConstants.SpaceId, ct);
            var participant = await Participant.Get(actorId, ct);
            if (space is not null && !space.IsOwner(actorId))
                throw new InvalidOperationException("Server ownership is already claimed.");
            if (!HumanHostAccountability.CanClaim(participant))
                throw new UnauthorizedAccessException("This account cannot establish the Space.");
            var home = space is null ? null : await Community.Tangent.Get(Community.Tangent.HomeKey, ct);
            if (home?.SetupComplete != true)
            {
                // Validate all submitted content before the first owner/participant/home write.
                Community.Tangent.ValidateOnboardingCard(name, description, artwork, skip);
                var selectedKind = TangentKind.Check(kind);
                (space, _) = await ClaimWithinTransaction(actorId, verifiedAtprotoDid, ct);
                home ??= Community.Tangent.Home(space);
                home.Change(actorId, skip ? "My Tangent" : name, skip ? "" : description,
                    "Make room for the next thought.", "#f4b942", skip ? "" : artwork, clock.GetUtcNow());
                home.Kind = selectedKind;
                await ActivityJournal.AppendInTransaction(ActivityKind.TangentChanged, "", actorId,
                    tangentKey: home.Id, ct: ct);
                // Completion is the final durable marker. If an earlier operation commits and
                // a later one fails, the owner still sees create_tangent and can retry safely.
                await home.Save(ct);
                await EntityContext.Commit(ct);
                signals.Pulse();
            }
            else
            {
                await EntityContext.Commit(ct);
            }
        }
        finally { gate.Exit(); }
        // A lost HTTP response can repeat this read without changing a completed card.
        return await tangents.Describe(actorId, Community.Tangent.HomeKey, ct)
            ?? throw new InvalidOperationException("The home Tangent is unavailable after onboarding.");
    }

    private async Task<(Space Space, bool Establishing)> ClaimWithinTransaction(
        string actorId, string? verifiedAtprotoDid, CancellationToken ct)
    {
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
        // A refused claim cannot classify the participant as a side effect.
        HumanHostAccountability.RequireDeclaration(participant!, ParticipantClassification.Human, isHostOwner: true);
        participant!.Declare(ParticipantClassification.Human);
        await participant.Save(ct);
        space.HumanDeclared = true;
        await space.Save(ct);
        await ActivityJournal.AppendInTransaction(ActivityKind.ParticipantChanged, "", actorId, ct: ct);
        return (space, establishing);
    }

    public async Task<ServerSettings> Update(string actorId, ServerSettingsPatch patch, CancellationToken ct)
    {
        if (patch.Name is not null && (string.IsNullOrWhiteSpace(patch.Name) || patch.Name.Length > 120)
            || patch.WelcomeMessage?.Length > 4096 || patch.Motd?.Length > 4096 || patch.Byline?.Length > 240)
            throw new InvalidOperationException("Use a server name of 1-120 characters and welcome/MOTD text up to 4096 characters.");
        if (patch.CoverImageUrl is { Length: > 0 } image && !ValidCover(image))
            throw new InvalidOperationException("Use a local image path or an HTTPS image URL.");
        if (patch.MarkImageUrl is { Length: > 0 } mark && !ValidCover(mark))
            throw new InvalidOperationException("Use a local image path or an HTTPS image URL for the mark.");
        if (patch.Accent is not null && patch.Accent is not ("gold" or "jade" or "iris" or "copper"))
            throw new InvalidOperationException("Choose gold, jade, iris or copper.");
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
            if (patch.MarkImageUrl is not null) space.MarkImageUrl = patch.MarkImageUrl.Trim();
            if (patch.Accent is not null) space.Accent = patch.Accent;
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
            signals.Pulse();
            return new ServerSettings(space.Name, space.WelcomeMessage, space.Motd, space.AllowAgentTangentOwnership,
                space.OwnerParticipantId, true, false, Permissions.Server(space.IsOwner(actorId)), space.Byline, space.CoverImageUrl, space.BackgroundScene, space.BackgroundColor, space.BackgroundIntensity, space.BackgroundMotion, space.BackgroundMouseSpotlight, space.MarkImageUrl, space.Accent, CanManageParticipants: await roleAccess.CanManageParticipants(actorId, ct));
        }
        finally { gate.Exit(); }
    }

    public async Task<AccessMapView> GetAccess(string actorId, CancellationToken ct)
    {
        using var fresh = EntityContext.NoCache();
        var space = await Space.Get(TangentConstants.SpaceId, ct)
            ?? throw new InvalidOperationException("The server has not been claimed.");
        var participant = await Participant.Get(actorId, ct);
        var bag = await roleAccess.Bag(actorId, ct);
        if (participant?.IsSuspended == true || !TangentRoleAccess.CanDo((space.Access ?? AccessMap.ServerDefaults()).ResolveServer().Manage, bag))
            throw new UnauthorizedAccessException("The current role cannot inspect server access settings.");
        var selected = (space.Access ?? AccessMap.ServerDefaults()).NormalizeForServer();
        return new AccessMapView(selected, selected.ResolveServer(), []);
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
            if (!canManage) throw new UnauthorizedAccessException("The current role cannot change server access.");
            space.ChangeAccess(access);
            await space.Save(ct);
            await ActivityJournal.AppendInTransaction(ActivityKind.ParticipantChanged, "", actorId, ct: ct);
            await EntityContext.Commit(ct);
            signals.Pulse();
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
            await EntityContext.Commit(ct); signals.Pulse(); return participant;
        }
        finally { gate.Exit(); }
    }
}

public sealed record ServerSettings(string Name, string WelcomeMessage, string Motd,
    bool AllowAgentTangentOwnership, string OwnerParticipantId, bool CanManage, bool CanClaim,
    PermissionView? Permissions = null, string Byline = "", string CoverImageUrl = "",
    string BackgroundScene = "galaxy", string BackgroundColor = "", int BackgroundIntensity = 35, bool BackgroundMotion = true, bool BackgroundMouseSpotlight = true,
    string MarkImageUrl = "", string Accent = "gold", bool CanManageParticipants = false);
public sealed record ServerSettingsPatch(string? Name = null, string? WelcomeMessage = null, string? Motd = null,
    bool? AllowAgentTangentOwnership = null, string? Byline = null, string? CoverImageUrl = null,
    string? BackgroundScene = null, string? BackgroundColor = null, int? BackgroundIntensity = null, bool? BackgroundMotion = null, bool? BackgroundMouseSpotlight = null,
    string? MarkImageUrl = null, string? Accent = null);
