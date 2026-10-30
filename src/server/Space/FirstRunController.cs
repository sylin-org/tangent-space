using System.Security.Claims;
using Koan.Identity.Roles;
using Koan.Web.Auth.Connector.Atproto;
using Koan.Web.Auth.Infrastructure;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Render;
using Tangent.Writer;

namespace Tangent.Space;

/// <summary>First run, one resource both ways. Until the place is established, a visitor reads where to sign in; a
/// signed-in person who may set it up reads the one action that establishes it, and anyone else reads who may. Who
/// may: the account <c>Tangent:Operator</c> names by handle or DID, or, with none named, whoever signs in first. Posting
/// the action under <see cref="FormToken.Validate"/>, with its antiforgery token from a session, sends
/// <see cref="Establish"/> with the name derived from the sender's handle (<see cref="TangentSpace.NameFor"/>); once it
/// commits the sender joins the administrator role and reads the site's pointers here. After that only the operator and
/// the proprietor find anything at this address, and a post to it is refused as already set up before anything else is
/// asked; to everyone else it answers as a missing resource. Every other page of the tree answers this resource until
/// the place is established (<see cref="FirstRunGate"/>).</summary>
[AnswersBeforeFirstRun]
public sealed class FirstRunController(KoanWebBaseUrl baseUrl, IOptions<TangentSpaceOptions> options,
    WriteLoop writer, RoleCollection roles, IServer server, ILogger<FirstRunController> logger) : ControllerBase
{
    /// <summary>The page's path under the base URL.</summary>
    public const string Path = "/first-run";

    [HttpGet(Path)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (await TangentSpace.Get(TangentSpace.SingleRow, ct) is not { } space) return await Answer(HttpContext, baseUrl, options.Value, ct);
        if (!Theirs(space)) return NotHere();
        var listening = server.Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
        return new ObjectResult(new FirstRun(space.Name, Configured(baseUrl) + "/")
        {
            BaseUrl = Configured(baseUrl),
            Pointers = Pointers.For(Configured(baseUrl), Pointers.Upstream(listening)),
            Reader = await ReaderOf(User, baseUrl, ct)
        });
    }

    [HttpPost(Path)]
    public async Task<IActionResult> Post(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (await TangentSpace.Get(TangentSpace.SingleRow, ct) is { } established)
            return Theirs(established)
                ? new Problem(StatusCodes.Status409Conflict, "Already set up",
                    $"{Establish.AlreadyEstablished} Its pointers are at {Configured(baseUrl) + Path}.").ToResult()
                : NotHere();
        if (PersonId(User) is not { } personId)
            return new Problem(StatusCodes.Status401Unauthorized, "Sign in first", "Setting up the Tangent Space needs a signed-in person.", Capabilities.SignIn).ToResult();
        if (!await FormToken.Validate(HttpContext))
            return new Problem(StatusCodes.Status400BadRequest, "Open the form again",
                "This form’s token is missing or has expired, so nothing was set up.").ToResult();

        var handle = await HandleOf(User, personId, ct);
        if (!options.Value.MaySetUp(personId, handle))
            return new Problem(StatusCodes.Status403Forbidden, "Not yours to set up",
                $"Only {Named(options.Value)}, as {FirstRun.OperatorSetting} names, can set up this Tangent Space.").ToResult();

        var outcome = await writer.Send(new Establish(personId, handle, TangentSpace.NameFor(handle)), ct);
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
                return new Problem(StatusCodes.Status409Conflict, "Already set up",
                    $"{refused.Reason} Its pointers are at {Configured(baseUrl) + Path}.").ToResult();
            case Outcome<TangentSpace>.Refused refused:
                return new ObjectResult(Place(baseUrl, options.Value) with
                {
                    SetUp = SetUpFor(HttpContext, baseUrl, handle) with { Failure = refused.Reason },
                    Reader = await ReaderOf(User, baseUrl, ct)
                }) { StatusCode = StatusCodes.Status400BadRequest };
            case Outcome<TangentSpace>.Failed failed:
                throw new InvalidOperationException($"First run by {personId} failed.", failed.Cause);
            default:
                throw new InvalidOperationException($"The writer answered {outcome} for first run by {personId}.");
        }
    }

    /// <summary>The first-run resource for a request to a place that is not established: the challenge for a
    /// visitor, the one action for a signed-in person who may set it up, and who may for anyone else.</summary>
    public static async Task<IActionResult> Answer(HttpContext http, KoanWebBaseUrl baseUrl, TangentSpaceOptions options, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        var place = Place(baseUrl, options) with { Reader = await ReaderOf(http.User, baseUrl, ct) };
        if (PersonId(http.User) is not { } personId)
            return new ObjectResult(place with
            {
                Challenge = Configured(baseUrl) + SignInController.ChallengePath + "?return=" + Uri.EscapeDataString(baseUrl.PathBase + Path)
            });
        var handle = await HandleOf(http.User, personId, ct);
        if (!options.MaySetUp(personId, handle))
            return new ObjectResult(place with { Waiting = new FirstRunWaiting(handle, await OperatorHandle(options, ct)) });
        return new ObjectResult(place with { SetUp = SetUpFor(http, baseUrl, handle) });
    }

    /// <summary>Whether the signed-in person is the established place's operator or proprietor, the two who find first
    /// run once it is done.</summary>
    private bool Theirs(TangentSpace space)
        => PersonId(User) is { } personId && (personId == space.OperatorId || personId == space.ProprietorId);

    /// <summary>What first run answers anyone else once the place is established: a missing resource's answer.</summary>
    private static IActionResult NotHere()
        => new Problem(StatusCodes.Status404NotFound, "Not found", "Nothing answers at this address.").ToResult();

    /// <summary>Whoever is signed in, as the account menu shows them: their participant's display name, else their
    /// handle; the highest seeded role they hold; and Koan's sign-out, coming back here. Null for a visitor.</summary>
    private static async Task<FirstRunReader?> ReaderOf(ClaimsPrincipal user, KoanWebBaseUrl baseUrl, CancellationToken ct)
    {
        if (PersonId(user) is not { } personId) return null;
        var participant = await Participant.Get(personId, ct);
        var handle = participant?.Handle is { Length: > 0 } stored ? stored : Claimed(user) is { Length: > 0 } claimed ? claimed : personId;
        var name = participant?.Profile.DisplayName is { Length: > 0 } displayName ? displayName : handle;
        var role = participant is null ? null : Roles.Word((await Capabilities.Load(personId, null, null, ct)).RoleIds);
        var signOut = Configured(baseUrl) + AuthConstants.Routes.Logout + "?return=" + Uri.EscapeDataString(baseUrl.PathBase + Path);
        return new FirstRunReader(personId, name, handle, role, participant?.Profile.Avatar, signOut);
    }

    /// <summary>The place before it is established, with the account the configuration names to set it up.</summary>
    private static FirstRun Place(KoanWebBaseUrl baseUrl, TangentSpaceOptions options)
        => new(TangentSpace.Unnamed, Configured(baseUrl) + "/", options.NamedOperator());

    private static FirstRunSetUp SetUpFor(HttpContext http, KoanWebBaseUrl baseUrl, string handle)
        => new(Configured(baseUrl) + Path, FormToken.For(http), handle, TangentSpace.NameFor(handle));

    /// <summary>The configured operator as a refusal names them: <c>@</c> and their handle, or their DID.</summary>
    private static string Named(TangentSpaceOptions options)
        => options.NamedByDid() ? options.NamedOperator()! : "@" + options.NamedOperator();

    /// <summary>The configured operator's handle: the one the setting names, or, for a DID, the handle of the participant
    /// with that DID where one has arrived here.</summary>
    private static async Task<string?> OperatorHandle(TangentSpaceOptions options, CancellationToken ct)
        => !options.NamedByDid() ? options.NamedOperator()
            : (await Participant.Get(options.NamedOperator()!, ct))?.Handle is { Length: > 0 } arrived ? arrived : null;

    private static string Configured(KoanWebBaseUrl baseUrl) => baseUrl.Configured
        ?? throw new InvalidOperationException($"{TangentModule.BaseUrlKey} must be set to the base URL the Tangent Space answers at.");

    private static string? PersonId(ClaimsPrincipal user)
        => user.Identity?.IsAuthenticated == true ? user.FindFirst(ClaimTypes.NameIdentifier)?.Value : null;

    /// <summary>The handle the atproto connector verified for the signed-in account: at this sign-in, or, for a request
    /// that carries no sign-in's claims, such as a bearer's, at the sign-in that last recorded it on their participant;
    /// empty where none was verified.</summary>
    private static async Task<string> HandleOf(ClaimsPrincipal user, string personId, CancellationToken ct)
        => Claimed(user) is { Length: > 0 } claimed ? claimed : (await Participant.Get(personId, ct))?.Handle ?? "";

    /// <summary>The handle the connector verified at this sign-in, or empty when it verified none.</summary>
    private static string Claimed(ClaimsPrincipal user) => user.FindFirst(AtprotoClaimTypes.Handle)?.Value ?? "";
}
