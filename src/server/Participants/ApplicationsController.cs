using System.Globalization;
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
public sealed record DecideInput([Description(Holds.Applicant)] string? Participant = null, [Description(Holds.DecidedState)] string? State = null);

/// <summary>The place's applications, a section of its settings at <c>/settings/applications</c>, for whoever holds
/// <see cref="Bit.ManageStanding"/>. Its <c>GET</c> is one bounded page of them (<see cref="Applications.Read"/>), one
/// resource both ways (<see cref="ApplicationsPage"/>), continued from the cursor in <c>after</c>, and, with
/// <c>state=waiting</c>, those that wait alone (<see cref="Applications.ReadWaiting"/>), continued from the sequence in
/// <c>after</c>; a cursor that is not one, or another state, is refused with <c>400</c>, and a reader who may not
/// decide is refused with the gate, <c>401</c> to sign in and <c>403</c> otherwise: a page reads it with no applicant,
/// and a JSON request as its problem. Its two
/// <c>POST</c>s take the applicant's id in <c>participant</c>, as a form or a JSON object: <c>admit</c> sends
/// <see cref="Admit"/>; <c>decline</c> sends <see cref="Decline"/>. A form is sent back to
/// the applications, those that wait where it carries <c>state=waiting</c>; a JSON request reads the decision (<see cref="DecidedApplication"/>); a refusal reads the page again
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

    /// <summary>The query parameter, and the field of a decision, that narrows the applications to those that wait.</summary>
    public const string StateQuery = "state";

    /// <summary>The address of the applications that wait, under <paramref name="baseAddress"/>.</summary>
    public static string WaitingAddress(string baseAddress) => baseAddress + Path + "?" + StateQuery + "=" + ApplicationsPage.Waiting;

    /// <summary>The form that decides an application at <paramref name="action"/>: admit or decline, by the applicant's id,
    /// coming back to those that wait where <paramref name="waiting"/>.</summary>
    private static Form Deciding(string action, bool waiting)
    {
        var form = Form.Posting(action, new Form.Field(ParticipantField, Holds.Applicant));
        return waiting ? form.Given(StateQuery, ApplicationsPage.Waiting) : form;
    }

    [HttpGet(Path)]
    [Tool(Name = "read_applications")]
    [Description("Read who waits to be admitted and who was declined, or with state waiting those who wait alone, a page at a time, for whoever holds ManageStanding. Follow with admit or decline on an applicant's id.")]
    [Needs(Bit.View)]
    public async Task<IActionResult> Get([FromQuery(Name = After), Description(Holds.After)] string? after,
        [FromQuery(Name = StateQuery), Description(Holds.ApplicationsState)] string? state, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User);
        var place = Here.Of(HttpContext).Around;
        if (place.Gate(Bit.ManageStanding) is { } refused && PostWrites.WantsJson(Request)) return this.Refused(refused);

        if (!string.IsNullOrEmpty(state) && state != ApplicationsPage.Waiting)
            return new Problem(StatusCodes.Status400BadRequest,
                $"The {StateQuery} parameter is {ApplicationsPage.Waiting}, or left out for every application.") { Title = "Not a state of the applications" }.ToResult();
        var waiting = state == ApplicationsPage.Waiting;
        Applications.Cursor? cursor = null;
        long? before = null;
        if (after is not null && (waiting ? (before = Before(after)) is null : (cursor = Applications.Cursor.Parse(after)) is null))
            return new Problem(StatusCodes.Status400BadRequest,
                $"The {After} parameter is the cursor a page of the applications names as its next.") { Title = "Not a page of the applications" }.ToResult();

        var page = await Page(place, personId, waiting, cursor, before, ct);
        return place.Gate(Bit.ManageStanding) is { } gate ? new ObjectResult(page) { StatusCode = Answers.Status(HttpContext, gate) } : Ok(page);
    }

    /// <summary>The log's sequence a page of those that wait names as its next, or null where the text is not one.</summary>
    private static long? Before(string text)
        => long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var seq) && seq > 0 ? seq : null;

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
                return await Decided(applicant, personId, input.State == ApplicationsPage.Waiting, ct);
            case Outcome<Participant>.Refused { Reason: var reason }:
                return await Refused(personId, reason, input.State == ApplicationsPage.Waiting, ct);
            default:
                return Answers.NotDone(HttpContext, outcome);
        }
    }

    /// <summary>A decision that committed, or that found it already made: a form is sent back to the applications, to
    /// those that wait where it came from them; a JSON request reads the applicant's standing now, with the decider's
    /// <c>can</c>.</summary>
    private async Task<IActionResult> Decided(Participant applicant, string? personId, bool waiting, CancellationToken ct)
    {
        var applications = BaseAddress() + Path;
        if (!PostWrites.WantsJson(Request))
        {
            Response.Headers.Location = waiting ? WaitingAddress(BaseAddress()) : applications;
            return StatusCode(StatusCodes.Status303SeeOther);
        }

        var place = await Capabilities.Load(personId, null, null, ct);
        var (name, handle) = (applicant.ShownName(), applicant.ShownHandle());
        return new ObjectResult(new DecidedApplication(applicant.Id, name, handle, People.Word(applicant.StandingAt(DateTimeOffset.UtcNow)),
            applications, place.Can))
        {
            ContentTypes = { "application/json" }
        };
    }

    /// <summary>A refusal: for a JSON request, the problem its reason answers as; for a form, the page again, read afresh,
    /// with the reason above the list.</summary>
    private async Task<IActionResult> Refused(string? personId, Reason reason, bool waiting, CancellationToken ct)
    {
        if (PostWrites.WantsJson(Request)) return Answers.Refused(HttpContext, reason);

        var place = await Capabilities.Load(personId, null, null, ct);
        if (!place.Exists) return this.Refused(place.Gate(Bit.View)!);
        var page = await Page(place, personId, waiting, null, null, ct);
        if (reason.Kind != ReasonKind.Gated) page = page with { Failure = reason.Text };
        return new ObjectResult(page) { StatusCode = Answers.Status(HttpContext, reason) };
    }

    /// <summary>One page of the applications, of those that wait alone where <paramref name="waiting"/>, continued from
    /// <paramref name="cursor"/> or, among those that wait, from before <paramref name="before"/>.</summary>
    private async Task<ApplicationsPage> Page(Capabilities place, string? personId, bool waiting, Applications.Cursor? cursor, long? before,
        CancellationToken ct)
    {
        var baseAddress = BaseAddress();
        var here = baseUrl.PathBase + Path + (waiting ? "?" + StateQuery + "=" + ApplicationsPage.Waiting : "");
        var applications = waiting ? WaitingAddress(baseAddress) : baseAddress + Path;
        var gate = place.Gate(Bit.ManageStanding)?.Word;
        IReadOnlyList<Participant> read = [];
        string? next = null;
        if (gate is null && waiting)
        {
            var page = await Applications.ReadWaiting(before, ct);
            (read, next) = (page.Applicants, page.Next?.ToString(CultureInfo.InvariantCulture));
        }
        else if (gate is null)
        {
            var page = await Applications.Read(cursor, ct);
            (read, next) = (page.Applicants, page.Next?.ToString());
        }

        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? TangentSpace.Unestablished();
        return new ApplicationsPage(
            TangentSpaceSettingsController.Sections(place, baseAddress, TangentSpaceSettingsController.ApplicationsSection),
            read.Select(applicant =>
            {
                var (name, handle) = (applicant.ShownName(), applicant.ShownHandle());
                return new ApplicationsPage.Applicant(applicant.Id, name, handle, baseAddress + "/p/" + Uri.EscapeDataString(handle),
                    applicant.Standing == Standing.Declined ? ApplicationsPage.Declined : ApplicationsPage.Waiting,
                    applicant.AppliedAt ?? DateTimeOffset.MinValue);
            }).ToList(),
            next is null ? null : applications + (waiting ? "&" : "?") + After + "=" + Uri.EscapeDataString(next),
            cursor is not null || before is not null,
            applications,
            waiting ? ApplicationsPage.Waiting : "",
            baseAddress + Path,
            gate is null ? Deciding(baseAddress + AdmitPath, waiting) : null,
            gate is null ? Deciding(baseAddress + DeclinePath, waiting) : null,
            place.Can,
            gate,
            personId is null ? baseAddress + SignInController.Path + "?return=" + Uri.EscapeDataString(here) : null,
            space.Name,
            baseAddress + "/",
            await PostWrites.Reader(personId, place, baseAddress, here, ct),
            DateOnly.FromDateTime(DateTime.UtcNow));
    }


    private string BaseAddress() => baseUrl.Resolve(HttpContext);
}
