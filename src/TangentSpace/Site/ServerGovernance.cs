using Koan.Data.Core;
using TangentSpace.Activity;
using TangentSpace.Infrastructure;
using TangentSpace.Participants;
using TangentSpace.Rooms;
using Microsoft.Extensions.Options;
using TangentSpace.Authorization;

namespace TangentSpace.Site;

public sealed class ServerGovernance(TimeProvider clock, PolicyGate gate, IOptions<SiteOptions> options)
{
    public async Task<ServerSettings> Read(string? actorDid, CancellationToken ct)
    {
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            var site = await TangentSite.Get(TangentConstants.SiteId, ct);
            var owner = site?.IsOwner(actorDid) == true;
            var participant = actorDid is null ? null : await Participant.Get(actorDid, ct);
            var canCreate = site?.CanCreateTangent(participant) == true;
            var declared = site is not null && (site.HumanDeclared || !string.IsNullOrWhiteSpace(site.OwnerDid));
            await EntityContext.Commit(ct);
            return new ServerSettings(site?.Name ?? "", site?.WelcomeMessage ?? "", site?.Motd ?? "",
                site?.CreationPolicy ?? "owner_only", site?.AllowAgentTangentOwnership ?? false,
                site?.OwnerDid ?? "", owner, site is null, !declared && participant is not null && !participant.IsSuspended && !participant.WasDeclaredAgent && participant.Classification != ParticipantClassification.Agent && (string.IsNullOrWhiteSpace(options.Value.OwnerDid) || options.Value.OwnerDid == actorDid), Permissions.Server(owner, canCreate), site?.Byline ?? "", site?.CoverImageUrl ?? "", site?.BackgroundScene ?? "galaxy", site?.BackgroundColor ?? "", site?.BackgroundIntensity ?? 35, site?.BackgroundMotion ?? true, site?.BackgroundMouseSpotlight ?? true);
        }
        finally { gate.Exit(); }
    }

    public async Task<ServerSettings> Claim(string actorDid, bool humanDeclaration, CancellationToken ct)
    {
        if (!humanDeclaration) throw new InvalidOperationException("A humanDeclaration is required.");
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(RoomConstants.AdministrationTransaction);
            var site = await TangentSite.Get(TangentConstants.SiteId, ct);
            var participant = await Participant.Get(actorDid, ct);
            if (participant is null || participant.IsSuspended || participant.WasDeclaredAgent || participant.Classification == ParticipantClassification.Agent)
                throw new InvalidOperationException("A known agent cannot reserve human server ownership.");
            participant?.Declare(ParticipantClassification.Human);
            if (participant is not null) await participant.Save(ct);
            if (site is not null && !site.IsOwner(actorDid))
                throw new InvalidOperationException("Server ownership is already claimed.");
            if (site is null)
            {
                site = TangentSite.Establish(options.Value, actorDid, clock.GetUtcNow());
                site.HumanDeclared = true;
            }
            else site.HumanDeclared = true;
            await site.Save(ct);
            await ActivityJournal.AppendInTransaction(ActivityKind.ParticipantChanged, "", actorDid, ct: ct);
            await EntityContext.Commit(ct);
            ActivityJournal.SignalAfterCommit();
            return new ServerSettings(site.Name, site.WelcomeMessage, site.Motd, site.CreationPolicy,
                site.AllowAgentTangentOwnership, site.OwnerDid, true, false, false, Permissions.Server(true), site.Byline, site.CoverImageUrl, site.BackgroundScene, site.BackgroundColor, site.BackgroundIntensity, site.BackgroundMotion, site.BackgroundMouseSpotlight);
        }
        finally { gate.Exit(); }
    }

    public async Task<ServerSettings> Update(string actorDid, ServerSettingsPatch patch, CancellationToken ct)
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
            using var transaction = EntityContext.Transaction(RoomConstants.AdministrationTransaction);
            var site = await TangentSite.Get(TangentConstants.SiteId, ct)
                ?? throw new InvalidOperationException("The server has not been claimed.");
            if (!site.IsOwner(actorDid)) throw new UnauthorizedAccessException("Only the server owner can change settings.");
            if (patch.Name is not null) site.Name = patch.Name.Trim();
            if (patch.WelcomeMessage is not null) site.WelcomeMessage = patch.WelcomeMessage;
            if (patch.Byline is not null) site.Byline = patch.Byline.Trim();
            if (patch.CoverImageUrl is not null) site.CoverImageUrl = patch.CoverImageUrl.Trim();
            if (patch.Motd is not null) site.Motd = patch.Motd;
            if (patch.CreationPolicy is not null && patch.CreationPolicy is not ("owner_only" or "humans" or "everyone"))
                throw new InvalidOperationException("CreationPolicy must be owner_only, humans, or everyone.");
            if (patch.CreationPolicy is not null) site.CreationPolicy = patch.CreationPolicy;
            if (patch.AllowAgentTangentOwnership is not null) site.AllowAgentTangentOwnership = patch.AllowAgentTangentOwnership.Value;
            if (patch.BackgroundScene is not null) site.BackgroundScene = patch.BackgroundScene;
            if (patch.BackgroundColor is not null) site.BackgroundColor = patch.BackgroundColor;
            if (patch.BackgroundIntensity is not null) site.BackgroundIntensity = patch.BackgroundIntensity.Value;
            if (patch.BackgroundMotion is not null) site.BackgroundMotion = patch.BackgroundMotion.Value;
            if (patch.BackgroundMouseSpotlight is not null) site.BackgroundMouseSpotlight = patch.BackgroundMouseSpotlight.Value;
            site.PolicyRevision = checked(site.PolicyRevision + 1);
            await site.Save(ct);
            await ActivityJournal.AppendInTransaction(ActivityKind.ParticipantChanged, "", actorDid, ct: ct);
            await EntityContext.Commit(ct);
            ActivityJournal.SignalAfterCommit();
            return new ServerSettings(site.Name, site.WelcomeMessage, site.Motd, site.CreationPolicy,
                site.AllowAgentTangentOwnership, site.OwnerDid, true, false, false, Permissions.Server(true), site.Byline, site.CoverImageUrl, site.BackgroundScene, site.BackgroundColor, site.BackgroundIntensity, site.BackgroundMotion, site.BackgroundMouseSpotlight);
        }
        finally { gate.Exit(); }
    }

    private static bool ValidCover(string value)
        => value.Length <= 2048 && !value.Any(c => char.IsControl(c) || c is '\'' or '"' or '\\')
            && (value.StartsWith('/') && !value.StartsWith("//")
                || Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.UserInfo.Length == 0);

    public async Task<Participant> Declare(string actorDid, ParticipantClassification classification, CancellationToken ct)
    {
        if (!Enum.IsDefined(classification)) throw new InvalidOperationException("Unknown participant classification.");
        await gate.Enter(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            using var transaction = EntityContext.Transaction(RoomConstants.AdministrationTransaction);
            var participant = await Participant.Get(actorDid, ct) ?? throw new InvalidOperationException("Participant not found.");
            var site = await TangentSite.Get(TangentConstants.SiteId, ct);
            if (participant.IsSuspended || site?.IsOwner(actorDid) == true && classification != ParticipantClassification.Human) throw new UnauthorizedAccessException("The server owner must retain human accountability.");
            if (participant.Classification == ParticipantClassification.Agent && classification != ParticipantClassification.Agent)
                throw new UnauthorizedAccessException("A known agent cannot declare itself human.");
            participant.Declare(classification); await participant.Save(ct);
            await ActivityJournal.AppendInTransaction(ActivityKind.ParticipantChanged, "", actorDid, ct: ct);
            await EntityContext.Commit(ct); ActivityJournal.SignalAfterCommit(); return participant;
        }
        finally { gate.Exit(); }
    }
}

public sealed record ServerSettings(string Name, string WelcomeMessage, string Motd, string CreationPolicy,
    bool AllowAgentTangentOwnership, string OwnerDid, bool CanManage, bool SetupRequired, bool CanClaim,
    PermissionView? Permissions = null, string Byline = "", string CoverImageUrl = "",
    string BackgroundScene = "galaxy", string BackgroundColor = "", int BackgroundIntensity = 35, bool BackgroundMotion = true, bool BackgroundMouseSpotlight = true);
public sealed record ServerSettingsPatch(string? Name = null, string? WelcomeMessage = null, string? Motd = null,
    string? CreationPolicy = null, bool? AllowAgentTangentOwnership = null, string? Byline = null, string? CoverImageUrl = null,
    string? BackgroundScene = null, string? BackgroundColor = null, int? BackgroundIntensity = null, bool? BackgroundMotion = null, bool? BackgroundMouseSpotlight = null);
