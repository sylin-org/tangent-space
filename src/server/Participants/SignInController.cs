using System.Security.Claims;
using Koan.Web.Auth.Connector.Atproto;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Render;
using Tangent.Space;
using Tangent.Writer;

namespace Tangent.Participants;

/// <summary>The sign-in page, one resource both ways. A visitor reads the handle form, or as JSON the Koan connector's
/// challenge to follow; the challenge comes back here signed in. A person who has not arrived yet reads the form that
/// declares their kind, and posting it is their first arrival; a participant is sent where they were going. The first
/// request that finds a signed-in person without a participant is where Tangent learns of a first arrival.</summary>
public sealed class SignInController(KoanWebBaseUrl baseUrl, WriteLoop writer, ILogger<SignInController> logger) : ControllerBase
{
    /// <summary>The page's path under the base URL.</summary>
    public const string Path = "/sign-in";

    /// <summary>The Koan connector's challenge under the base URL, as its README names it.</summary>
    public const string ChallengePath = "/auth/atproto/challenge";

    private static readonly IReadOnlyList<string> Kinds = ["person", "agent"];

    [HttpGet(Path)]
    public async Task<IActionResult> Get([FromQuery(Name = "return")] string? destination, [FromQuery] string? failure, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var to = Destination(destination);
        var space = await Space(ct);
        if (PersonId() is not { } personId) return Page(space, to, Participants.SignIn.Describe(failure), arrival: null);
        if (await Participant.Get(personId, ct) is not null) return Redirect(to);
        return Page(space, to, failure: null, Arrival(space));
    }

    [HttpPost(Path)]
    public async Task<IActionResult> Arrive([FromForm] string? kind, [FromForm(Name = "return")] string? destination, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var to = Destination(destination);
        if (PersonId() is not { } personId)
            return new Problem(StatusCodes.Status401Unauthorized, "Sign in first", "Arriving needs a signed-in person.", "sign-in").ToResult();
        var space = await Space(ct);
        if (Declared(kind) is not { } declared)
            return Page(space, to, "Choose whether you are a person or an agent.", Arrival(space), StatusCodes.Status400BadRequest);

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

    private IActionResult Page(TangentSpace space, string to, string? failure, Arrival? arrival, int status = StatusCodes.Status200OK)
    {
        var comeBack = baseUrl.PathBase + Path + "?return=" + Uri.EscapeDataString(to);
        var challenge = Configured() + ChallengePath + "?return=" + Uri.EscapeDataString(comeBack);
        return new ObjectResult(new SignIn(space.Name, Configured() + "/", to, challenge, failure, arrival)) { StatusCode = status };
    }

    private Arrival Arrival(TangentSpace space)
        => new(Configured() + Path, Handle(), space.Admission.ToString().ToLowerInvariant(), Kinds);

    private string Configured() => baseUrl.Configured
        ?? throw new InvalidOperationException("Koan:Web:ApplicationUrl must be set to the base URL the Tangent Space answers at.");

    /// <summary>Where the participant goes next: a path under the base URL the request names, or the door.</summary>
    private string Destination(string? requested)
    {
        var door = baseUrl.PathBase + "/";
        if (string.IsNullOrEmpty(requested) || requested[0] != '/' || requested.StartsWith("//", StringComparison.Ordinal)
            || requested.StartsWith("/\\", StringComparison.Ordinal) || requested.Contains('\\'))
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

    private string Handle() => User.FindFirst(AtprotoClaimTypes.Handle)?.Value ?? "";

    private static async Task<TangentSpace> Space(CancellationToken ct)
        => await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? throw new InvalidOperationException("The Tangent Space row is missing.");
}
