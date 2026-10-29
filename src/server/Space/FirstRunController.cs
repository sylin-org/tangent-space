using System.Security.Claims;
using Koan.Identity.Roles;
using Koan.Web.Auth.Connector.Atproto;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Render;
using Tangent.Writer;

namespace Tangent.Space;

/// <summary>First run, one resource both ways. Until the place is established, a visitor reads where to sign in; a
/// signed-in person who may set it up reads the form that establishes it, and anyone else reads who may. Who may: the
/// person whose DID <c>Tangent:Operator</c> names, or, with none named, whoever types the setup code the server wrote
/// to its log at this start. Posting the form, with its antiforgery token, sends <see cref="Establish"/>; once it
/// commits the operator joins the administrator role and reads the site's pointers here. After that only the operator
/// and the proprietor find anything at this address. Every other page of the tree answers this resource until the
/// place is established (<see cref="FirstRunGate"/>).</summary>
[AnswersBeforeFirstRun]
public sealed class FirstRunController(KoanWebBaseUrl baseUrl, IOptions<TangentSpaceOptions> options, SetupCode setupCode,
    WriteLoop writer, IAntiforgery antiforgery, RoleCollection roles, ILogger<FirstRunController> logger) : ControllerBase
{
    /// <summary>The page's path under the base URL.</summary>
    public const string Path = "/first-run";

    /// <summary>The configuration key the base URL is read from, which the page names as the place to change it.</summary>
    public const string Setting = TangentModule.BaseUrlKey;

    /// <summary>The value the <c>accountable</c> field carries when the sender makes the statement.</summary>
    public const string Affirmed = "yes";

    [HttpGet(Path)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (await TangentSpace.Get(TangentSpace.SingleRow, ct) is not { } space) return Answer(HttpContext, baseUrl, options.Value, setupCode);
        var personId = PersonId(User);
        if (personId is null || personId != space.OperatorId && personId != space.ProprietorId)
            return new Problem(StatusCodes.Status404NotFound, "Not found", "Nothing answers at this address.").ToResult();
        return new ObjectResult(Place(baseUrl, space.Name, null) with { Pointers = Pointers.For(Configured(baseUrl)) });
    }

    [HttpPost(Path)]
    public async Task<IActionResult> Post([FromForm] string? name, [FromForm] string? proprietor, [FromForm] string? handle,
        [FromForm] string? accountable, [FromForm] string? code, CancellationToken ct)
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

        var pinned = options.Value.Operator;
        if (pinned is not null && pinned != personId)
            return new Problem(StatusCodes.Status403Forbidden, "Not yours to set up",
                $"Only the person whose DID is {pinned}, as {FirstRun.OperatorSetting} names, can set up this Tangent Space.").ToResult();

        var someone = proprietor == FirstRunForm.Someone;
        var named = Proprietor(handle);
        var affirmed = accountable == Affirmed;
        var sent = Form(HttpContext, baseUrl, pinned is null) with
        {
            Name = name?.Trim(),
            Proprietor = someone ? FirstRunForm.Someone : FirstRunForm.Me,
            ProprietorHandle = handle?.Trim(),
            Accountable = affirmed
        };
        if (pinned is null && !setupCode.Matches(code))
            return Again(sent with
            {
                CodeFailure = string.IsNullOrWhiteSpace(code)
                    ? "Write the setup code from this Tangent Space’s log."
                    : "That is not the setup code this Tangent Space wrote to its log at its last start."
            });
        if (!affirmed)
            return Again(sent with { AccountableFailure = "Confirm that you are the person who answers for this place." });
        if (someone && named is null)
            return Again(sent with { ProprietorFailure = "Write the handle of the participant whose place this is." });

        var outcome = await writer.Send(new Establish(personId, Handle(User), name ?? "", someone ? named : null), ct);
        switch (outcome)
        {
            case Outcome<TangentSpace>.Done:
                // Koan's role write is not part of the writer's transaction: it follows the commit, and is idempotent;
                // a start repairs it if this request never gets that far.
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
            case Outcome<TangentSpace>.Refused refused when named is not null
                && (refused.Reason == Establish.SharedHandle(named) || refused.Reason == Establish.Banned(named)):
                return Again(sent with { ProprietorFailure = refused.Reason });
            case Outcome<TangentSpace>.Refused refused:
                return Again(sent with { Failure = refused.Reason });
            case Outcome<TangentSpace>.Failed failed:
                throw new InvalidOperationException($"First run by {personId} failed.", failed.Cause);
            default:
                throw new InvalidOperationException($"The writer answered {outcome} for first run by {personId}.");
        }
    }

    /// <summary>The first-run resource for a request to a place that is not established: the challenge for a
    /// visitor, the form for a signed-in person who may set it up, and who may for anyone else.</summary>
    public static IActionResult Answer(HttpContext http, KoanWebBaseUrl baseUrl, TangentSpaceOptions options, SetupCode setupCode)
    {
        http.Response.Headers.CacheControl = "no-store";
        var pinned = options.Operator;
        var place = Place(baseUrl, TangentSpace.Unnamed, pinned);
        if (PersonId(http.User) is not { } personId)
            return new ObjectResult(place with
            {
                Challenge = Configured(baseUrl) + SignInController.ChallengePath + "?return=" + Uri.EscapeDataString(baseUrl.PathBase + Path)
            });
        if (pinned is not null && pinned != personId)
            return new ObjectResult(place with { Waiting = new FirstRunWaiting(Handle(http.User)) });
        return new ObjectResult(place with { Form = Form(http, baseUrl, pinned is null && setupCode.Required) });
    }

    /// <summary>The form again, keeping what was sent, with the reason it was refused.</summary>
    private ObjectResult Again(FirstRunForm form)
        => new(Place(baseUrl, TangentSpace.Unnamed, options.Value.Operator) with { Form = form }) { StatusCode = StatusCodes.Status400BadRequest };

    private static FirstRun Place(KoanWebBaseUrl baseUrl, string name, string? pinned)
    {
        var configured = Configured(baseUrl);
        return new FirstRun(name, configured, Setting, configured + "/", pinned);
    }

    private static FirstRunForm Form(HttpContext http, KoanWebBaseUrl baseUrl, bool asksCode)
        => new(Configured(baseUrl) + Path, FormToken.For(http), Handle(http.User), asksCode);

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
