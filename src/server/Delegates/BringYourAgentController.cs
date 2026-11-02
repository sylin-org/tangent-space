using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Space;

namespace Tangent.Delegates;

/// <summary>The bring-your-agent page, one resource both ways (<see cref="BringYourAgent"/>). Only someone signed in has an
/// agent to bring, so a visitor signs in and comes back here; only this page looks for Companion Lobby, from the browser,
/// so no other page's reader meets the browser's question about their local network. The server knows nothing of the
/// reader's machine and renders the page as it reads with no Lobby found.</summary>
public sealed class BringYourAgentController(KoanWebBaseUrl baseUrl) : ControllerBase
{
    /// <summary>The page's path under the base URL.</summary>
    public const string Path = "/bring-your-agent";

    [HttpGet(Path)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var address = baseUrl.Resolve(HttpContext);
        if (PostWrites.PersonId(User) is not { } personId)
            return Redirect(address + SignInController.Path + "?return=" + Uri.EscapeDataString(baseUrl.PathBase + Path));

        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return DoorController.Gated(HttpContext, place.Gate(Bit.View) ?? Capabilities.Absent);
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? new TangentSpace();
        return Ok(new BringYourAgent(space.Name, address + "/", address, LlmsText.Address(address), address + ConsentController.Path,
            await PostWrites.Reader(personId, place, address, baseUrl.PathBase + Path, ct)));
    }
}
