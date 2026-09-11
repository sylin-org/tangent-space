using CarpaNet.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TangentSpace.Rooms;

namespace TangentSpace.AtProtocol;

[ApiController, AllowAnonymous]
public sealed class SpacesAccessController(ServiceAuthentication authentication, RoomGovernance rooms, IOptions<SpacesOptions> configured) : ControllerBase
{
    [HttpGet("xrpc/" + SpacesOptions.AccessMethod)]
    public async Task<IActionResult> Check([FromQuery] string space, [FromQuery] string user, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (!await authentication.Verify(Request.Headers.Authorization.ToString(), ct)) return Unauthorized(new { error = "InvalidServiceAuthentication" });
        if (!IdentityResolver.IsValidDid(user)) return BadRequest(new { error = "InvalidUser" });
        var prefix = configured.Value.Space("");
        if (!space.StartsWith(prefix, StringComparison.Ordinal)) return Ok(new { authorized = false });
        var key = space[prefix.Length..];
        if (string.IsNullOrWhiteSpace(key) || key.Contains('/') || key.Length > 64) return Ok(new { authorized = false });
        var authorized = await rooms.WithCurrentPolicy(user, key, (policy, _) => Task.FromResult(
            policy.SpaceUri == space && policy.SpaceState == RoomSpaceState.Ready
            && (policy.CanRead || user == configured.Value.AuthorityDid)), ct);
        return Ok(new { authorized });
    }
}
