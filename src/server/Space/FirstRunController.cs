using System.Security.Claims;
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

namespace Tangent.Space;

/// <summary>First run, one resource both ways. Until the place is established, a visitor reads where to sign in, and
/// so does a signed-in person who may set it up, whose sign-in sets it up (<see cref="FirstRunSignIn"/>); anyone else
/// reads who may. Who may: the account <c>Tangent:Operator</c> names by handle or DID, or, with none named, whoever
/// signs in first. Once it is established, the operator and the proprietor read the site's pointers here, once its
/// base URL is public: while it is local no site could use them, so they read when the pointers will appear instead;
/// to everyone else it answers as a missing resource. Every other page of the tree answers this resource until the
/// place is established (<see cref="FirstRunGate"/>).</summary>
[AnswersBeforeFirstRun]
public sealed class FirstRunController(KoanWebBaseUrl baseUrl, IOptions<TangentSpaceOptions> options, IServer server) : ControllerBase
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
        var address = baseUrl.Resolve(HttpContext);
        var local = TangentSpace.IsLocal(address);
        return new ObjectResult(new FirstRun(space.Name, address + "/")
        {
            BaseUrl = address,
            Pointers = local ? [] : Pointers.For(address, Pointers.Upstream(listening)),
            PointersLater = local ? Pointers.Later(baseUrl.IsConfigured) : null,
            Reader = await ReaderOf(User, address, baseUrl.PathBase, ct)
        });
    }

    /// <summary>The first-run resource for a request to a place that is not established: the challenge for a visitor
    /// and for a signed-in person who may set it up, and who may for anyone else.</summary>
    public static async Task<IActionResult> Answer(HttpContext http, KoanWebBaseUrl baseUrl, TangentSpaceOptions options, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        var address = baseUrl.Resolve(http);
        var place = Place(address, options) with { Reader = await ReaderOf(http.User, address, baseUrl.PathBase, ct) };
        if (PersonId(http.User) is { } personId)
        {
            var handle = await HandleOf(http.User, personId, ct);
            if (!options.MaySetUp(personId, handle))
                return new ObjectResult(place with { Waiting = new FirstRunWaiting(handle, await OperatorHandle(options, ct)) });
        }
        return new ObjectResult(place with
        {
            Challenge = address + SignInController.ChallengePath + "?return=" + Uri.EscapeDataString(baseUrl.PathBase + Path)
        });
    }

    /// <summary>Whether the signed-in person is the established place's operator or proprietor, the two who find first
    /// run once it is done.</summary>
    private bool Theirs(TangentSpace space)
        => PersonId(User) is { } personId && (personId == space.OperatorId || personId == space.ProprietorId);

    /// <summary>What first run answers anyone else once the place is established: a missing resource's answer.</summary>
    private static IActionResult NotHere()
        => new Problem(StatusCodes.Status404NotFound, "Not found", "Nothing answers at this address.").ToResult();

    /// <summary>Whoever is signed in, as the account menu shows them: their participant's display name, else their
    /// handle; the highest seeded role they hold; and Koan's sign-out under <paramref name="address"/>, coming back here.
    /// Null for a visitor.</summary>
    private static async Task<FirstRunReader?> ReaderOf(ClaimsPrincipal user, string address, PathString pathBase, CancellationToken ct)
    {
        if (PersonId(user) is not { } personId) return null;
        var participant = await Participant.Get(personId, ct);
        var handle = participant?.Handle is { Length: > 0 } stored ? stored : Claimed(user) is { Length: > 0 } claimed ? claimed : personId;
        var name = participant?.Profile.DisplayName is { Length: > 0 } displayName ? displayName : handle;
        var place = participant is null ? null : await Capabilities.Load(personId, null, null, ct);
        var role = place is null ? null : Roles.Word(place.RoleIds);
        var signOut = address + AuthConstants.Routes.Logout + "?return=" + Uri.EscapeDataString(pathBase + Path);
        var settings = place is not null && await TangentSpace.Get(TangentSpace.SingleRow, ct) is not null
            ? TangentSpaceSettingsController.Entry(place, address)
            : null;
        return new FirstRunReader(personId, name, handle, role, participant?.Profile.Avatar, signOut, settings);
    }

    /// <summary>The place before it is established, with the account the configuration names to set it up.</summary>
    private static FirstRun Place(string address, TangentSpaceOptions options)
        => new(TangentSpace.Unnamed, address + "/", options.NamedOperator());

    /// <summary>The configured operator's handle: the one the setting names, or, for a DID, the handle of the participant
    /// with that DID where one has arrived here.</summary>
    private static async Task<string?> OperatorHandle(TangentSpaceOptions options, CancellationToken ct)
        => !options.NamedByDid() ? options.NamedOperator()
            : (await Participant.Get(options.NamedOperator()!, ct))?.Handle is { Length: > 0 } arrived ? arrived : null;

    private static string? PersonId(ClaimsPrincipal user)
        => user.Identity?.IsAuthenticated == true ? user.FindFirst(ClaimTypes.NameIdentifier)?.Value : null;

    /// <summary>The handle the atproto connector verified for the signed-in account: at this sign-in, or, for a request
    /// that carries no sign-in's claims, such as a delegate's, at the sign-in that last recorded it on their
    /// participant; empty where none was verified.</summary>
    private static async Task<string> HandleOf(ClaimsPrincipal user, string personId, CancellationToken ct)
        => Claimed(user) is { Length: > 0 } claimed ? claimed : (await Participant.Get(personId, ct))?.Handle ?? "";

    /// <summary>The handle the connector verified at this sign-in, or empty when it verified none.</summary>
    private static string Claimed(ClaimsPrincipal user) => user.FindFirst(AtprotoClaimTypes.Handle)?.Value ?? "";
}
