using Koan.Data.Core;
using TangentSpace.Communities;
using Koan.Web.Auth.Connector.Atproto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TangentSpace.Infrastructure;
using TangentSpace.Participants;
using TangentSpace.Participation;
using TangentSpace.Rooms;
using TangentSpace.Site;

namespace TangentSpace.Web;

[ApiController]
public sealed class WelcomeController(IOptions<SiteOptions> options, TangentServer hub) : ControllerBase
{
    private RoomGovernance rooms => hub.Topics;
    private ServerGovernance server => hub.Site;

    [AllowAnonymous]
    [HttpGet(TangentConstants.WelcomeRoute)]
    public async Task<IActionResult> Welcome(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        string? participantId;
        try
        {
            participantId = User.Identity?.IsAuthenticated == true ? ParticipationAccess.Require(User, ParticipationGrants.Welcome) : null;
            if (participantId is null && Request.Headers.ContainsKey("Authorization")) return Unauthorized();
        }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
        using var fresh = EntityContext.NoCache();
        var site = await TangentSite.Get(TangentConstants.SiteId, ct);
        var participant = participantId is null ? null : await Participant.Get(participantId, ct);
        var identity = participant is null ? null : new ParticipantWelcome(participant.Id,
            await hub.Directory.AtprotoDidOf(participant.Id, ct), await hub.Directory.LabelOf(participant.Id, ct),
            site?.IsOwner(participant.Id) == true, participant.JoinedAt);
        var settings = await server.Read(participantId, ct);
        var home = site?.IsOwner(participantId) == true ? await TangentCommunity.Get(TangentCommunity.HomeKey, ct) : null;
        var onboarding = site is null
            ? identity is null ? "sign_in" : settings.CanClaim ? "confirm_owner" : "waiting_owner"
            : site.IsOwner(participantId) && home?.SetupComplete != true ? "create_tangent" : "complete";
        return Ok(new SiteWelcome(site?.Name ?? options.Value.Name, site is not null, identity,
            TangentConstants.SignInPath, identity is null ? null : TangentConstants.SignOutPath,
            await rooms.List(participantId, 1, ct), settings, onboarding));
    }
}
