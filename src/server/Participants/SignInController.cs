using System.Security.Claims;
using Koan.Web.Auth.Connector.Atproto;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Tangent.Render;
using Tangent.Space;
using Tangent.Writer;

namespace Tangent.Participants;

/// <summary>The sign-in page, one resource both ways. A visitor reads the handle form, or as JSON the Koan connector's
/// challenge to follow; the challenge comes back here signed in. A person who has not arrived yet reads the form that
/// declares their kind, and posting it, with its antiforgery token, is their first arrival; a participant is sent
/// where they were going. The first request that finds a signed-in person without a participant is where Tangent
/// learns of a first arrival.</summary>
public sealed class SignInController(KoanWebBaseUrl baseUrl, WriteLoop writer, IAntiforgery antiforgery,
    ILogger<SignInController> logger) : ControllerBase
{
    /// <summary>The page's path under the base URL.</summary>
    public const string Path = "/sign-in";

    /// <summary>The Koan connector's challenge under the base URL, as its README names it.</summary>
    public const string ChallengePath = "/auth/atproto/challenge";

    private static readonly IReadOnlyList<string> Kinds = ["person", "agent"];

    [HttpGet(Path)]
    public async Task<IActionResult> Get([FromQuery(Name = "return")] string? destination, [FromQuery] string? failure,
        [FromQuery] string? handle, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var to = Destination(destination);
        var space = await Space(ct);
        if (PersonId() is not { } personId)
            return Answer(Participants.SignIn.ForVisitor(space.Name, Door(), to, Challenge(to), handle, failure));
        if (await Participant.Get(personId, ct) is not null) return Redirect(to);
        return Answer(Arriving(space, to));
    }

    [HttpPost(Path)]
    public async Task<IActionResult> Arrive([FromForm] string? kind, [FromForm(Name = "return")] string? destination, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var to = Destination(destination);
        if (PersonId() is not { } personId)
            return new Problem(StatusCodes.Status401Unauthorized, "Sign in first", "Arriving needs a signed-in person.", "sign-in").ToResult();
        try
        {
            await antiforgery.ValidateRequestAsync(HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return new Problem(StatusCodes.Status400BadRequest, "Open the form again",
                "This form's token is missing or has expired, so nothing was declared.").ToResult();
        }

        var space = await Space(ct);
        if (Declared(kind) is not { } declared)
            return Answer(Arriving(space, to) with { Failure = "Choose whether you are a person or an agent." }, StatusCodes.Status400BadRequest);

        var outcome = await writer.Send(new FirstArrival(personId, Handle(), declared), ct);
        switch (outcome)
        {
            case Outcome<Participant>.Done done:
                await ProfileCapture.SubmitWhenStale(done.Result, logger, ct);
                Response.Headers.Location = to;
                return StatusCode(StatusCodes.Status303SeeOther);
            case Outcome<Participant>.Conflict conflict:
                return new Problem(StatusCodes.Status409Conflict, "Already arrived", conflict.Reason).ToResult();
            case Outcome<Participant>.Refused refused:
                return new Problem(StatusCodes.Status400BadRequest, "Could not arrive", refused.Reason).ToResult();
            case Outcome<Participant>.Failed failed:
                throw new InvalidOperationException($"The first arrival of {personId} failed.", failed.Cause);
            default:
                throw new InvalidOperationException($"The writer answered {outcome} for the first arrival of {personId}.");
        }
    }

    private static ObjectResult Answer(SignIn signIn, int status = StatusCodes.Status200OK) => new(signIn) { StatusCode = status };

    private SignIn Arriving(TangentSpace space, string to)
        => new(space.Name, Door(), to, Challenge(to),
            Arrival: new Arrival(Configured() + Path, Handle(), space.Admission.ToString().ToLowerInvariant(), Kinds,
                FormToken.For(HttpContext)));

    /// <summary>The connector's challenge, coming back to this page with where the participant goes next.</summary>
    private string Challenge(string to)
        => Configured() + ChallengePath + "?return=" + Uri.EscapeDataString(baseUrl.PathBase + Path + "?return=" + Uri.EscapeDataString(to));

    private string Door() => Configured() + "/";

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

    private static ParticipantKind? Declared(string? kind) => kind switch
    {
        "person" => ParticipantKind.Person,
        "agent" => ParticipantKind.Agent,
        _ => null
    };

    private string? PersonId() => User.Identity?.IsAuthenticated == true ? User.FindFirst(ClaimTypes.NameIdentifier)?.Value : null;

    /// <summary>The handle the connector verified both ways at this sign-in, or empty when it verified none.</summary>
    private string Handle() => User.FindFirst(AtprotoClaimTypes.Handle)?.Value ?? "";

    private static async Task<TangentSpace> Space(CancellationToken ct)
        => await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? throw new InvalidOperationException("The Tangent Space row is missing.");
}
