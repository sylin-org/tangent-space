using System.Security.Claims;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Space;

namespace Tangent.Participants;

/// <summary>The sign-in page, one resource both ways. A visitor reads the handle form, or as JSON the Koan connector's
/// challenge to follow; the challenge comes back here signed in, the first sign-in having made the person a
/// participant on the way, and a signed-in person is sent where they were going.</summary>
public sealed class SignInController(KoanWebBaseUrl baseUrl) : ControllerBase
{
    /// <summary>The page's path under the base URL.</summary>
    public const string Path = "/sign-in";

    /// <summary>The Koan connector's challenge under the base URL, as its README names it.</summary>
    public const string ChallengePath = "/auth/atproto/challenge";

    [HttpGet(Path)]
    public async Task<IActionResult> Get([FromQuery(Name = "return")] string? destination, [FromQuery] string? failure,
        [FromQuery] string? handle, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var to = Destination(destination);
        if (User.Identity?.IsAuthenticated == true && User.FindFirst(ClaimTypes.NameIdentifier) is not null) return Redirect(to);
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct)
            ?? throw new InvalidOperationException("The Tangent Space row is missing.");
        return new ObjectResult(Participants.SignIn.ForVisitor(space.Name, Configured() + "/", to, Challenge(to), handle, failure));
    }

    /// <summary>The connector's challenge, coming back to this page with where the participant goes next.</summary>
    private string Challenge(string to)
        => Configured() + ChallengePath + "?return=" + Uri.EscapeDataString(baseUrl.PathBase + Path + "?return=" + Uri.EscapeDataString(to));

    private string Configured() => baseUrl.Configured
        ?? throw new InvalidOperationException("Koan:Web:ApplicationUrl must be set to the base URL the Tangent Space answers at.");

    /// <summary>Where the participant goes next: a relative path under the base URL, of visible characters, without
    /// scheme or authority; anything else is the door.</summary>
    private string Destination(string? requested)
    {
        var door = baseUrl.PathBase + "/";
        if (requested is not { Length: > 0 } || requested[0] != '/' || requested.Length > 1 && requested[1] is '/' or '\\'
            || !requested.All(c => c is > ' ' and <= '~' and not '\\'))
            return door;
        var path = new PathString(requested.Split('?', '#')[0]);
        return !baseUrl.PathBase.HasValue || path.StartsWithSegments(baseUrl.PathBase) ? requested : door;
    }
}
