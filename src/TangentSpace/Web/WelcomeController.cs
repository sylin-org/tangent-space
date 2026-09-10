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
public sealed class WelcomeController(IOptions<SiteOptions> options, RoomGovernance rooms) : ControllerBase
{
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
        var site = await TangentSite.Get(TangentConstants.SiteId, ct);
        var participant = did is null ? null : await Participant.Get(did, ct);
        var identity = participant is null ? null : new ParticipantWelcome(participant.Id, participant.Handle,
            site?.IsOwner(participant.Id) == true, participant.JoinedAt);
        return Ok(new SiteWelcome(site?.Name ?? options.Value.Name, site is not null, identity,
            TangentConstants.SignInPath, identity is null ? null : TangentConstants.SignOutPath,
            await rooms.List(did, 1, ct)));
    }
}
