using Koan.Identity.Roles;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Posts;
using Tangent.Render;
using Tangent.Space;
using Tangent.Writer;

namespace Tangent.Participants;

/// <summary>The place's applications, a section of its settings at <c>/settings/applications</c>, for whoever holds
/// <see cref="Bit.ManageStanding"/>. Its <c>GET</c> is one bounded page of them (<see cref="Applications.Read"/>), one
/// resource both ways (<see cref="ApplicationsPage"/>), continued from the cursor in <c>after</c>; a reader who may not
/// decide reads the gate, with no applicant, answered <c>401</c> to sign in and <c>403</c> otherwise. Its two
/// <c>POST</c>s take the applicant's id in <c>participant</c>, as a form or a JSON object: <c>admit</c> sends
/// <see cref="Admit"/> and, once it commits, joins the applicant to the member role (<see cref="MemberRole"/>), which their
/// next sign-in repairs should this write not happen; <c>decline</c> sends <see cref="Decline"/>. A form is sent back to
/// the applications; a JSON request reads the decision (<see cref="DecidedApplication"/>); a refusal reads the page again
/// with the reason above the list, or a problem. A reader for whom the place does not exist receives what an address with
/// nothing there receives.</summary>
public sealed class ApplicationsController(KoanWebBaseUrl baseUrl, WriteLoop writer, RoleCollection roles,
    ILogger<ApplicationsController> logger) : ControllerBase
{
    public const string Path = "/settings/applications";

    public const string AdmitPath = Path + "/admit";

    public const string DeclinePath = Path + "/decline";

    /// <summary>The field that names the applicant decided.</summary>
    public const string ParticipantField = "participant";

    /// <summary>The query parameter that carries the cursor of the page to continue from.</summary>
    public const string After = "after";

    [HttpGet(Path)]
    public async Task<IActionResult> Get([FromQuery(Name = After)] string? after, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return PostWrites.Unseen(place.Gate(Bit.View));

        Applications.Cursor? cursor = null;
        if (after is not null && (cursor = Applications.Cursor.Parse(after)) is null)
            return new Problem(StatusCodes.Status400BadRequest, "Not a page of the applications",
                $"The {After} parameter is the cursor a page of the applications names as its next.").ToResult();

        var page = await Page(place, personId, cursor, ct);
        return page.Gate is { } gate ? new ObjectResult(page) { StatusCode = PostWrites.Status(gate) } : Ok(page);
    }

    [HttpPost(AdmitPath)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    public Task<IActionResult> Admit(CancellationToken ct) => Decide(true, ct);

    [HttpPost(DeclinePath)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    public Task<IActionResult> Decline(CancellationToken ct) => Decide(false, ct);

    private async Task<IActionResult> Decide(bool admitting, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        if (personId is not null && !await FormToken.Validate(HttpContext)) return PostWrites.TokenRefused();
        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return PostWrites.Unseen(place.Gate(Bit.View));
        if (await PostWrites.Fields(Request, ct) is not { } fields) return PostWrites.Unreadable();

        var applicantId = fields.GetValueOrDefault(ParticipantField) ?? "";
        ICommand<Participant> command = admitting
            ? new Participants.Admit(personId ?? "", applicantId)
            : new Participants.Decline(personId ?? "", applicantId);
        var outcome = await writer.Send(command, ct);
        switch (outcome)
        {
            case Outcome<Participant>.Done { Result: var applicant }:
                if (admitting) await JoinMemberRole(applicant, ct);
                return await Decided(applicant, personId, ct);
            case Outcome<Participant>.Refused { Reason: Applications.NoPlace }:
                return PostWrites.Absent();
            case Outcome<Participant>.Refused { Reason: var reason }:
                return await Refused(personId, reason, ct);
            case Outcome<Participant>.Failed { Cause: var cause }:
                throw new InvalidOperationException($"Deciding the application of {applicantId} failed.", cause);
            default:
                throw new InvalidOperationException($"The writer answered {outcome} for deciding the application of {applicantId}.");
        }
    }

    /// <summary>The member role for an applicant just admitted, after the commit; one this write misses is joined at their
    /// next sign-in.</summary>
    private async Task JoinMemberRole(Participant applicant, CancellationToken ct)
    {
        try
        {
            await MemberRole.Join(roles, applicant, ct);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            logger.LogWarning(failure, "{Person} was admitted and could not join the member role; their next sign-in joins them", applicant.Id);
        }
    }

    /// <summary>A decision that committed, or that found it already made: a form is sent back to the applications; a JSON
    /// request reads the applicant's standing now, with the decider's <c>can</c>.</summary>
    private async Task<IActionResult> Decided(Participant applicant, string? personId, CancellationToken ct)
    {
        var applications = BaseAddress() + Path;
        if (!PostWrites.WantsJson(Request))
        {
            Response.Headers.Location = applications;
            return StatusCode(StatusCodes.Status303SeeOther);
        }

        var place = await Capabilities.Load(personId, null, null, ct);
        var (name, handle) = Named(applicant);
        return new ObjectResult(new DecidedApplication(applicant.Id, name, handle, People.Word(applicant.StandingAt(DateTimeOffset.UtcNow)),
            applications, place.Can))
        {
            ContentTypes = { "application/json" }
        };
    }

    /// <summary>A refusal: for a JSON request, the problem its reason answers as; for a form, the page again, read afresh,
    /// with the reason above the list.</summary>
    private async Task<IActionResult> Refused(string? personId, string reason, CancellationToken ct)
    {
        var status = reason switch
        {
            Applications.NoApplicant => StatusCodes.Status404NotFound,
            Participant.NotAnApplication => StatusCodes.Status409Conflict,
            _ => PostWrites.Status(reason)
        };
        var gated = status is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden;
        if (PostWrites.WantsJson(Request))
            return gated ? PostWrites.Gated(reason) : new Problem(status, "Not decided", reason).ToResult();

        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return PostWrites.Unseen(place.Gate(Bit.View));
        var page = await Page(place, personId, null, ct);
        if (!gated) page = page with { Failure = reason };
        return new ObjectResult(page) { StatusCode = status };
    }

    private async Task<ApplicationsPage> Page(Capabilities place, string? personId, Applications.Cursor? cursor, CancellationToken ct)
    {
        var baseAddress = BaseAddress();
        var here = baseUrl.PathBase + Path;
        var applications = baseAddress + Path;
        var gate = place.Gate(Bit.ManageStanding);
        var read = gate is null ? await Applications.Read(cursor, ct) : new Applications.Page([], null);
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? TangentSpace.Unestablished();
        return new ApplicationsPage(
            TangentSpaceSettingsController.Sections(place, baseAddress, TangentSpaceSettingsController.ApplicationsSection),
            read.Applicants.Select(applicant =>
            {
                var (name, handle) = Named(applicant);
                return new ApplicationsPage.Applicant(applicant.Id, name, handle, baseAddress + "/p/" + Uri.EscapeDataString(handle),
                    applicant.Standing == Standing.Declined ? ApplicationsPage.Declined : ApplicationsPage.Waiting,
                    applicant.AppliedAt ?? DateTimeOffset.MinValue);
            }).ToList(),
            read.Next is { } next ? applications + "?" + After + "=" + Uri.EscapeDataString(next.ToString()) : null,
            cursor is not null,
            applications,
            baseAddress + AdmitPath,
            baseAddress + DeclinePath,
            FormToken.For(HttpContext),
            place.Can,
            gate,
            personId is null ? baseAddress + SignInController.Path + "?return=" + Uri.EscapeDataString(here) : null,
            space.Name,
            baseAddress + "/",
            await PostWrites.Reader(personId, place, baseAddress, here, ct),
            DateOnly.FromDateTime(DateTime.UtcNow));
    }

    /// <summary>The name a participant shows and their handle, their id where they have none.</summary>
    private static (string Name, string Handle) Named(Participant participant)
    {
        var handle = participant.Handle.Length > 0 ? participant.Handle : participant.Id;
        return (participant.Profile.DisplayName is { Length: > 0 } displayName ? displayName : handle, handle);
    }

    private string BaseAddress() => baseUrl.Resolve(HttpContext);
}
