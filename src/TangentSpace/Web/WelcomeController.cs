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
        string? did;
        try
        {
            did = User.Identity?.IsAuthenticated == true ? ParticipationAccess.Require(User, ParticipationGrants.Welcome) : null;
            if (did is null && Request.Headers.ContainsKey("Authorization")) return Unauthorized();
        }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
        using var fresh = EntityContext.NoCache();
        var site = await TangentSite.Get(TangentConstants.SiteId, ct);
        var participant = did is null ? null : await Participant.Get(did, ct);
        var identity = participant is null ? null : new ParticipantWelcome(participant.Id, participant.Handle,
            site?.IsOwner(participant.Id) == true, participant.JoinedAt);
        var settings = await server.Read(did, ct);
        var home = site?.IsOwner(did) == true ? await TangentCommunity.Get(TangentCommunity.HomeKey, ct) : null;
        var onboarding = site is null
            ? identity is null ? "sign_in" : settings.CanClaim ? "confirm_owner" : "waiting_owner"
            : site.IsOwner(did) && home?.SetupComplete != true ? "create_tangent" : "complete";
        return Ok(new SiteWelcome(site?.Name ?? options.Value.Name, site is not null, identity,
            TangentConstants.SignInPath, identity is null ? null : TangentConstants.SignOutPath,
            await rooms.List(did, 1, ct), settings, onboarding));
    }
}
