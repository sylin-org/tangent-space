using Koan.Web.Auth.Connector.Atproto;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TangentSpace.Infrastructure;
using TangentSpace.Rooms;
using TangentSpace.Site;

namespace TangentSpace.AtProtocol;

[ApiController, Authorize, Route("api/connections")]
public sealed class ConnectionsController(IOptions<SpacesOptions> options, TangentServer hub) : ControllerBase
{
    [HttpGet("rooms")]
    public IActionResult Rooms([FromQuery] string? room = null)
    {
        var did = User.FindFirst(AtprotoClaimTypes.Did)?.Value;
        if (did is null || Request.Headers.Authorization.Count > 0) return Unauthorized();
        if (room is not null)
        {
            try { Room.CheckKey(room); }
            catch (RoomRuleViolation error) { return BadRequest(new { reason = error.Message }); }
        }
        return Connect(did, options.Value.ParticipantScope, room is null ? "/" : "/?room=" + Uri.EscapeDataString(room));
    }

    [HttpGet("authority")]
    public async Task<IActionResult> Authority(CancellationToken ct)
    {
        var did = User.FindFirst(AtprotoClaimTypes.Did)?.Value;
        var site = await TangentSite.Get(TangentConstants.SiteId, ct);
        var holder = did is null ? null : await hub.Directory.ByDid(did, ct);
        if (did is null || site?.IsOwner(holder?.Id) != true || Request.Headers.Authorization.Count > 0) return Forbid();
        return Connect(options.Value.AuthorityDid, options.Value.AuthorityScope);
    }

    private IActionResult Connect(string did, string scope, string returnTo = "/")
    {
        // The connector reads challenge input on this request. Neither identity nor scope comes from a submitted query.
        Request.QueryString = QueryString.Create("identifier", did);
        var properties = new AuthenticationProperties { RedirectUri = returnTo };
        AtprotoChallenge.WithScopes(properties, "atproto", scope);
        return Challenge(properties, TangentConstants.AtprotoProvider);
    }
}
