using System.Security.Claims;
using Koan.Identity.Roles;
using Koan.Web.Auth.Connector.Atproto;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Render;
using Tangent.Writer;

namespace Tangent.Space;

/// <summary>First run, one resource both ways. Until the place is established, a visitor reads where to sign in and a
/// signed-in person the form that establishes it; posting the form, with its antiforgery token, sends
/// <see cref="Establish"/>, and once it commits the operator joins the administrator role and reads the site's
/// pointers here. After that only the operator and the proprietor find anything at this address. Every other page
/// of the tree answers this resource until the place is established (<see cref="FirstRunGate"/>).</summary>
[AnswersBeforeFirstRun]
public sealed class FirstRunController(KoanWebBaseUrl baseUrl, WriteLoop writer, IAntiforgery antiforgery, RoleCollection roles,
    ILogger<FirstRunController> logger) : ControllerBase
{
    /// <summary>The page's path under the base URL.</summary>
    public const string Path = "/first-run";

    /// <summary>The configuration key the base URL is read from, which the page names as the place to change it.</summary>
    public const string Setting = TangentModule.BaseUrlKey;

    [HttpGet(Path)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (await TangentSpace.Get(TangentSpace.SingleRow, ct) is not { } space) return Answer(HttpContext, baseUrl);
        var personId = PersonId(User);
        if (personId is null || personId != space.OperatorId && personId != space.ProprietorId)
            return new Problem(StatusCodes.Status404NotFound, "Not found", "Nothing answers at this address.").ToResult();
        return new ObjectResult(Place(baseUrl, space.Name) with { Pointers = Pointers.For(space.BaseUrl) });
    }

    [HttpPost(Path)]
    public async Task<IActionResult> Post([FromForm] string? name, [FromForm] string? proprietor, [FromForm] string? handle, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (PersonId(User) is not { } personId)
            return new Problem(StatusCodes.Status401Unauthorized, "Sign in first", "Setting up the Tangent Space needs a signed-in person.", Capabilities.SignIn).ToResult();
        try
        {
            await antiforgery.ValidateRequestAsync(HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return new Problem(StatusCodes.Status400BadRequest, "Open the form again",
                "This form’s token is missing or has expired, so nothing was set up.").ToResult();
        }

        var someone = proprietor == FirstRunForm.Someone;
        var named = Proprietor(handle);
        var sent = Form(HttpContext, baseUrl) with
        {
            Name = name?.Trim(),
            Proprietor = someone ? FirstRunForm.Someone : FirstRunForm.Me,
            ProprietorHandle = handle?.Trim()
        };
        if (someone && named is null)
            return Again(sent with { ProprietorFailure = "Write the handle of the participant whose place this is." });

        var configured = Configured(baseUrl);
        var outcome = await writer.Send(new Establish(personId, Handle(User), name ?? "", configured, someone ? named : null), ct);
        switch (outcome)
        {
            case Outcome<TangentSpace>.Done:
                // Koan's role write is not part of the writer's transaction: it follows the commit, and is idempotent.
                await roles.Add(Roles.Administrator, personId, ct);
                if (await Participant.Get(personId, ct) is { } arrived) await ProfileCapture.SubmitWhenStale(arrived, logger, ct);
                Response.Headers.Location = baseUrl.PathBase + Path;
                return StatusCode(StatusCodes.Status303SeeOther);
            case Outcome<TangentSpace>.Refused { Reason: Establish.AlreadyEstablished } refused:
                return new Problem(StatusCodes.Status409Conflict, "Already set up", refused.Reason).ToResult();
            case Outcome<TangentSpace>.Refused { Reason: TangentSpace.NameRule } refused:
                return Again(sent with { NameFailure = refused.Reason });
            case Outcome<TangentSpace>.Refused refused when named is not null && refused.Reason == Establish.NoParticipant(named):
                return Again(sent with { ProprietorFailure = refused.Reason + " They sign in here once before they can be named." });
            case Outcome<TangentSpace>.Refused refused:
                return Again(sent with { Failure = refused.Reason });
            case Outcome<TangentSpace>.Failed failed:
                throw new InvalidOperationException($"First run by {personId} failed.", failed.Cause);
            default:
                throw new InvalidOperationException($"The writer answered {outcome} for first run by {personId}.");
        }
    }

    /// <summary>The first-run resource for a request to a place that is not established: the challenge for a
    /// visitor, the form for a signed-in person.</summary>
    public static IActionResult Answer(HttpContext http, KoanWebBaseUrl baseUrl)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (PersonId(http.User) is null)
        {
            var configured = Configured(baseUrl);
            var comeBack = baseUrl.PathBase + Path;
            return new ObjectResult(Place(baseUrl, TangentSpace.Unnamed) with
            {
                Challenge = configured + SignInController.ChallengePath + "?return=" + Uri.EscapeDataString(comeBack)
            });
        }

        return new ObjectResult(Place(baseUrl, TangentSpace.Unnamed) with { Form = Form(http, baseUrl) });
    }

    /// <summary>The form again, keeping what was sent, with the reason it was refused.</summary>
    private ObjectResult Again(FirstRunForm form)
        => new(Place(baseUrl, TangentSpace.Unnamed) with { Form = form }) { StatusCode = StatusCodes.Status400BadRequest };

    private static FirstRun Place(KoanWebBaseUrl baseUrl, string name)
    {
        var configured = Configured(baseUrl);
        return new FirstRun(name, configured, Setting, configured + "/");
    }

    private static FirstRunForm Form(HttpContext http, KoanWebBaseUrl baseUrl)
        => new(Configured(baseUrl) + Path, FormToken.For(http), Handle(http.User));

    /// <summary>A proprietor handle as written, without the <c>@</c> a person may type and in the lower case handles
    /// are compared in; null when there is none or it is longer than a handle can be.</summary>
    private static string? Proprietor(string? handle)
        => handle?.Trim().TrimStart('@').ToLowerInvariant() is { Length: > 0 and <= Participants.SignIn.HandleLimit } written ? written : null;

    private static string Configured(KoanWebBaseUrl baseUrl) => baseUrl.Configured
        ?? throw new InvalidOperationException($"{Setting} must be set to the base URL the Tangent Space answers at.");

    private static string? PersonId(ClaimsPrincipal user)
        => user.Identity?.IsAuthenticated == true ? user.FindFirst(ClaimTypes.NameIdentifier)?.Value : null;

    /// <summary>The handle the connector verified at this sign-in, or empty when it verified none.</summary>
    private static string Handle(ClaimsPrincipal user) => user.FindFirst(AtprotoClaimTypes.Handle)?.Value ?? "";
}
