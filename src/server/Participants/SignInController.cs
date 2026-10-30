using System.Security.Claims;
using Koan.Web.Auth.Infrastructure;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Space;

namespace Tangent.Participants;

/// <summary>The sign-in page, one resource both ways. A visitor reads the action that signs in with Bluesky and the
/// handle form for another server, or as JSON the Koan connector's challenge to follow; the challenge comes back here
/// signed in, the first sign-in having made the person a participant on the way, and a signed-in person is sent where
/// they were going. A failed sign-in comes back here with Koan's sanitized code in <c>error</c>. It answers as itself
/// before first run, for a place that reads as unestablished, and sends everyone on to first run, where a sign-in that
/// set the place up finds its pointers.</summary>
[AnswersBeforeFirstRun]
public sealed class SignInController(KoanWebBaseUrl baseUrl) : ControllerBase
{
    /// <summary>The page's path under the base URL.</summary>
    public const string Path = "/sign-in";

    /// <summary>The Koan connector's challenge under the base URL, as its README names it.</summary>
    public const string ChallengePath = "/auth/atproto/challenge";

    [HttpGet(Path)]
    public async Task<IActionResult> Get([FromQuery(Name = "return")] string? destination, [FromQuery] string? error,
        [FromQuery] string? handle, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct);
        var to = space is null ? baseUrl.PathBase + FirstRunController.Path : Destination(destination);
        if (User.Identity?.IsAuthenticated == true && User.FindFirst(ClaimTypes.NameIdentifier) is not null) return Redirect(to);
        var name = (space ?? TangentSpace.Unestablished()).Name;
        return new ObjectResult(Participants.SignIn.ForVisitor(name, Configured() + "/", to, Challenge(to), handle, error));
    }

    /// <summary>The connector's challenge, coming back to this page with where the participant goes next, signed in
    /// or failed (WEB-0080).</summary>
    private string Challenge(string to)
    {
        var here = Uri.EscapeDataString(baseUrl.PathBase + Path + "?return=" + Uri.EscapeDataString(to));
        return Configured() + ChallengePath + "?return=" + here + "&" + AuthConstants.Query.Failure + "=" + here;
    }

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
