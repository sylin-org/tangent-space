using System.ComponentModel;
using Koan.Data.Core.Commands;
using Koan.Web.Attributes;
using Koan.Web.Controllers;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Posts;
using Tangent.Render;
using Tangent.Space;

namespace Tangent.Participants;

/// <summary>What deciding an application sends, as a form or one JSON object.</summary>
public sealed record DecideInput([Description(Holds.Applicant)] string? Participant = null);

/// <summary>The place's applications, a section of its settings at <c>/settings/applications</c>, for whoever holds
/// <see cref="Bit.ManageStanding"/>. Its <c>GET</c> is one bounded page of them (<see cref="Applications.Read"/>), one
/// resource both ways (<see cref="ApplicationsPage"/>), continued from the cursor in <c>after</c>; a reader who may not
/// decide reads the gate, with no applicant, answered <c>401</c> to sign in and <c>403</c> otherwise. Its two
/// <c>POST</c>s take the applicant's id in <c>participant</c>, as a form or a JSON object: <c>admit</c> sends
/// <see cref="Admit"/>; <c>decline</c> sends <see cref="Decline"/>. A form is sent back to
/// the applications; a JSON request reads the decision (<see cref="DecidedApplication"/>); a refusal reads the page again
/// with the reason above the list, or a problem. A reader for whom the place does not exist receives what an address with
/// nothing there receives.</summary>
[Managing]
public sealed class ApplicationsController(KoanWebBaseUrl baseUrl) : ControllerBase
{
    public const string Path = "/settings/applications";

    public const string AdmitPath = Path + "/admit";

    public const string DeclinePath = Path + "/decline";

    /// <summary>The field that names the applicant decided.</summary>
    public const string ParticipantField = "participant";

    /// <summary>The query parameter that carries the cursor of the page to continue from.</summary>
    public const string After = "after";

    /// <summary>The form that decides an application at <paramref name="action"/>: admit or decline, by the applicant's id.</summary>
    private static Form Deciding(string action) => Form.Posting(action,
        new Form.Field(ParticipantField, Holds.Applicant));

    [HttpGet(Path)]
    [Tool(Name = "read_applications")]
    [Description("Read who waits to be admitted and who was declined, a page at a time, for whoever holds ManageStanding. Follow with admit or decline on an applicant's id.")]
    public async Task<IActionResult> Get([FromQuery(Name = After), Description(Holds.After)] string? after, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return this.Refused(place.Gate(Bit.View)!);

        Applications.Cursor? cursor = null;
        if (after is not null && (cursor = Applications.Cursor.Parse(after)) is null)
            return new Problem(StatusCodes.Status400BadRequest,
                $"The {After} parameter is the cursor a page of the applications names as its next.") { Title = "Not a page of the applications" }.ToResult();

        var page = await Page(place, personId, cursor, ct);
        return place.Gate(Bit.ManageStanding) is { } gate ? new ObjectResult(page) { StatusCode = Answers.Status(HttpContext, gate) } : Ok(page);
    }

    [HttpPost(AdmitPath)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    [Tool(Name = "admit", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Admit an applicant from read_applications, who becomes a member, for whoever holds ManageStanding. It reverses a decline.")]
    public Task<IActionResult> Admit(DecideInput input, CancellationToken ct) => Decide(input, true, ct);

    [HttpPost(DeclinePath)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    [Tool(Name = "decline", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Decline an applicant from read_applications, who goes on reading what a visitor reads and is told, for whoever holds ManageStanding. A later admit reverses it.")]
    public Task<IActionResult> Decline(DecideInput input, CancellationToken ct) => Decide(input, false, ct);

    private async Task<IActionResult> Decide(DecideInput input, bool admitting, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return this.Refused(place.Gate(Bit.View)!);

        var applicantId = input.Participant ?? "";
        ICommand<Participant> command = admitting
            ? new Participants.Admit(personId ?? "", applicantId)
            : new Participants.Decline(personId ?? "", applicantId);
        var outcome = await command.Send();
        switch (outcome)
        {
            case Outcome<Participant>.Done { Result: var applicant }:
                return await Decided(applicant, personId, ct);
            case Outcome<Participant>.Refused { Reason: var reason }:
                return await Refused(personId, reason, ct);
            default:
                return Answers.NotDone(HttpContext, outcome);
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
    private async Task<IActionResult> Refused(string? personId, Reason reason, CancellationToken ct)
    {
        if (PostWrites.WantsJson(Request)) return Answers.Refused(HttpContext, reason);

        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return this.Refused(place.Gate(Bit.View)!);
        var page = await Page(place, personId, null, ct);
        if (reason.Kind != ReasonKind.Gated) page = page with { Failure = reason.Text };
        return new ObjectResult(page) { StatusCode = Answers.Status(HttpContext, reason) };
    }

    private async Task<ApplicationsPage> Page(Capabilities place, string? personId, Applications.Cursor? cursor, CancellationToken ct)
    {
        var baseAddress = BaseAddress();
        var here = baseUrl.PathBase + Path;
        var applications = baseAddress + Path;
        var gate = place.Gate(Bit.ManageStanding)?.Word;
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
            gate is null ? Deciding(baseAddress + AdmitPath) : null,
            gate is null ? Deciding(baseAddress + DeclinePath) : null,
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
