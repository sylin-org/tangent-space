using Koan.Web.Controllers;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Posts;
using Tangent.Space;

namespace Tangent.Participants;

/// <summary>The bring-your-agent page, one resource both ways (<see cref="BringYourAgent"/>), open to anyone who may read
/// the place, a visitor too. It hands the place's address to Companion Lobby with one link to the page the Lobby serves
/// on the reader's own machine (<see cref="HandOff"/>); nothing looks for the Lobby before they follow it.</summary>
public sealed class BringYourAgentController(KoanWebBaseUrl baseUrl) : ControllerBase
{
    /// <summary>The page's path under the base URL.</summary>
    public const string Path = "/bring-your-agent";

    /// <summary>Where Companion Lobby's manager answers on the machine it runs on.</summary>
    public const string LobbyOrigin = "http://127.0.0.1:5219";

    [HttpGet(Path)]
    [Needs(Bit.View)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var address = baseUrl.Resolve(HttpContext);
        var here = Here.Of(HttpContext);
        var (personId, place) = (PostWrites.PersonId(User), here.Around);
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? new TangentSpace();
        var back = baseUrl.PathBase + Path;
        return Ok(new BringYourAgent(space.Name, address + "/", address, HandOff(address),
            await PostWrites.Reader(personId, place, address, back, ct),
            personId is null ? address + SignInController.Path + "?return=" + Uri.EscapeDataString(back) : null));
    }

    /// <summary>The page of Companion Lobby that receives the place at <paramref name="address"/>, named in the words of
    /// its own connections: the service, and the address.</summary>
    public static string HandOff(string address)
        => LobbyOrigin + "/?service=tangent&address=" + Uri.EscapeDataString(address);
}
