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

/// <summary>What inviting an account sends, as a form or one JSON object.</summary>
public sealed record InviteInput([Description(Holds.InviteHandle)] string? Handle = null);

/// <summary>What withdrawing an invitation sends, as a form or one JSON object.</summary>
public sealed record WithdrawInput([Description(Holds.Invitation)] string? Account = null);

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
/// nothing there receives. Where admission says apply, every application's page also holds one bounded page of the
/// invitations (<see cref="Invitations.Read"/>), continued from the cursor in <c>invited</c>, and two more <c>POST</c>s
/// for whoever holds <see cref="Bit.ManageStanding"/>: <c>invite</c> takes a Bluesky handle in <c>handle</c>, reads whose
/// account it names from its public profile, in the request and outside any command (<see cref="ProfileReader.Find"/>),
/// and sends <see cref="Participants.Invite"/>; <c>withdraw</c> takes the account's DID in <c>account</c> and sends
/// <see cref="WithdrawInvitation"/>. A form is sent back to the applications, and a refusal of a handle reads the page again
/// with the reason under the field and the handle kept; a JSON request reads where the invitation stands.</summary>
[Managing]
public sealed class ApplicationsController(KoanWebBaseUrl baseUrl) : ControllerBase
{
    public const string Path = "/settings/applications";

    public const string AdmitPath = Path + "/admit";

    public const string DeclinePath = Path + "/decline";

    public const string InvitePath = Path + "/invite";

    public const string WithdrawPath = Path + "/withdraw";

    /// <summary>The field that carries the handle invited.</summary>
    public const string HandleField = "handle";

    /// <summary>The field that names an invitation, by its account's DID.</summary>
    public const string AccountField = "account";

    /// <summary>The query parameter that carries the cursor of the page of invitations to continue from.</summary>
    public const string InvitedQuery = "invited";

    /// <summary>The refusal of a handle that cannot be one.</summary>
    public static readonly Reason NotAHandle = Reason.Invalid(HandleField, "That is not a Bluesky handle, as alice.bsky.social.");

    /// <summary>The refusal of a handle no account answers to.</summary>
    public static Reason NoAccount(string handle) => Reason.Invalid(HandleField, "No Bluesky account answers to @" + handle + ". Check its spelling.");

    /// <summary>The refusal of a withdrawal that names no invitation.</summary>
    public static readonly Reason NotAnInvitation = Reason.Invalid(AccountField, "Name an invitation by the DID its row carries.");

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
    [Description("Read who waits to be admitted and who was declined, or with state waiting those who wait alone, a page at a time, for whoever holds ManageStanding; where admission says apply, every application's page also lists who is invited. Then call admit or decline on an applicant's id, invite on a Bluesky handle, or withdraw_invitation on an invitation's account.")]
    [Needs(Bit.View)]
    public async Task<IActionResult> Get([FromQuery(Name = After), Description(Holds.After)] string? after,
        [FromQuery(Name = StateQuery), Description(Holds.ApplicationsState)] string? state,
        [FromQuery(Name = InvitedQuery), Description(Holds.InvitationsAfter)] string? invited, CancellationToken ct)
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

        Invitations.Cursor? invitedAfter = null;
        if (invited is not null && (invitedAfter = Invitations.Cursor.Parse(invited)) is null)
            return new Problem(StatusCodes.Status400BadRequest,
                $"The {InvitedQuery} parameter is the cursor a page of the invitations names as its next.") { Title = "Not a page of the invitations" }.ToResult();

        var page = await Page(place, personId, waiting, cursor, before, ct, invitedAfter);
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

    [HttpPost(InvitePath)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    [Tool(Name = "invite", Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Invite a Bluesky account by its handle where admission says apply, for whoever holds ManageStanding: it is admitted, as a member, when it first signs in. Naming someone who already applied admits them now. withdraw_invitation takes it back.")]
    [Needs(Bit.ManageStanding)]
    public async Task<IActionResult> Invite(InviteInput input, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User) ?? "";
        if (Handle(input.Handle) is not { } handle) return await Uninvited(personId, NotAHandle, input.Handle, ct);
        if (Here.Of(HttpContext).Rows.Space.Admission != Admission.Apply) return await Uninvited(personId, Participants.Invite.Joins, handle, ct);

        ProfileAnswer found;
        try
        {
            found = await HttpContext.RequestServices.GetRequiredService<ProfileReader>().Find(handle, ct);
        }
        catch (ProfileReadFailed)
        {
            return await Uninvited(personId, NoAccount(handle), handle, ct);
        }

        var outcome = await new Participants.Invite(personId, found.Did, handle, found.DisplayName, Monogram.Avatar(found.Avatar)).Send();
        switch (outcome)
        {
            case Outcome<Participants.Invite.Result>.Done { Result: var result }:
                if (!PostWrites.WantsJson(Request)) return Back(false);
                var place = await Capabilities.Load(personId, null, null, ct);
                return Ok(new InvitedAccount(found.Did, handle, found.DisplayName ?? handle, result.Admitted is null ? "invited" : "admitted",
                    BaseAddress() + Path, place.Can));
            case Outcome<Participants.Invite.Result>.Refused { Reason: var reason }:
                return await Uninvited(personId, reason, handle, ct);
            default:
                return Answers.NotDone(HttpContext, outcome);
        }
    }

    [HttpPost(WithdrawPath)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    [Tool(Name = "withdraw_invitation", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Withdraw an invitation from read_applications, for whoever holds ManageStanding: whoever it named applies as anyone does.")]
    [Needs(Bit.ManageStanding)]
    public async Task<IActionResult> Withdraw(WithdrawInput input, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = PostWrites.PersonId(User) ?? "";
        if (input.Account is not { } account || !Participant.IsDid(account)) return await Refused(personId, NotAnInvitation, false, ct);
        var outcome = await new WithdrawInvitation(personId, account).Send();
        if (outcome is Outcome<string>.Refused { Reason: var reason }) return await Refused(personId, reason, false, ct);
        if (outcome is not Outcome<string>.Done) return Answers.NotDone(HttpContext, outcome);
        return PostWrites.WantsJson(Request) ? Ok(new WithdrawnInvitation(account, BaseAddress() + Path)) : Back(false);
    }

    /// <summary>A handle as atproto writes one, from what was typed: without spaces or a leading @, in lower case, at most
    /// <see cref="Participants.SignIn.HandleLimit"/> characters of letters, digits, hyphens and dots, with a dot between two labels;
    /// null where it cannot be one.</summary>
    private static string? Handle(string? typed)
    {
        var handle = typed?.Trim().TrimStart('@').ToLowerInvariant();
        return handle is { Length: > 2 and <= Participants.SignIn.HandleLimit } && handle.Contains('.') && handle[0] != '.' && handle[^1] != '.'
               && !handle.Contains("..", StringComparison.Ordinal) && handle.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.')
            ? handle
            : null;
    }

    /// <summary>A refused invitation: for a JSON request, the problem its reason answers as; for a form, the page again, read
    /// afresh, with the reason under the handle field and the handle as it was typed, or above the list where the page
    /// offers no invitation.</summary>
    private async Task<IActionResult> Uninvited(string personId, Reason reason, string? typed, CancellationToken ct)
    {
        if (PostWrites.WantsJson(Request)) return Answers.Refused(HttpContext, reason);
        var place = await Capabilities.Load(personId, null, null, ct);
        var page = await Page(place, personId, false, null, null, ct);
        page = page.Invite is { } invite
            ? page with { Invite = invite.Changing(HandleField, field => field with { Value = typed, Failure = reason.Text }) }
            : page with { Failure = reason.Kind == ReasonKind.Gated ? null : reason.Text };
        return new ObjectResult(page) { StatusCode = Answers.Status(HttpContext, reason) };
    }

    /// <summary>A form sent back to the applications, to those that wait where <paramref name="waiting"/>.</summary>
    private IActionResult Back(bool waiting)
    {
        Response.Headers.Location = waiting ? WaitingAddress(BaseAddress()) : BaseAddress() + Path;
        return StatusCode(StatusCodes.Status303SeeOther);
    }

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
        if (!PostWrites.WantsJson(Request)) return Back(waiting);

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
    /// <paramref name="cursor"/> or, among those that wait, from before <paramref name="before"/>; on every application's
    /// page, where admission says apply and the reader may decide, one page of the invitations too, continued from
    /// <paramref name="invited"/>, with the forms that invite and withdraw.</summary>
    private async Task<ApplicationsPage> Page(Capabilities place, string? personId, bool waiting, Applications.Cursor? cursor, long? before,
        CancellationToken ct, Invitations.Cursor? invited = null)
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
        var inviting = gate is null && !waiting && space.Admission == Admission.Apply;
        var invitations = inviting ? await Invitations.Read(invited, ct) : null;
        var inviters = invitations is null ? new Dictionary<string, Render.Person>() : await Inviters(invitations.Invited, baseAddress, ct);
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
            DateOnly.FromDateTime(DateTime.UtcNow))
        {
            Invitations = invitations?.Invited.Select(invitation => new ApplicationsPage.Invitee(invitation.Id, invitation.Handle,
                invitation.Name ?? invitation.Handle, invitation.Avatar, invitation.InvitedAt,
                inviters.GetValueOrDefault(invitation.InvitedBy) ?? Render.Person.Left(invitation.InvitedBy))).ToList(),
            InvitationsNext = invitations?.Next is { } older ? applications + "?" + InvitedQuery + "=" + Uri.EscapeDataString(older.ToString()) : null,
            InvitationsFirst = inviting && invited is not null ? applications : null,
            Invite = inviting
                ? Form.Posting(baseAddress + InvitePath, new Form.Field(HandleField, Holds.InviteHandle)
                {
                    Label = "Invite by handle", Prefix = "@", Placeholder = "friend.bsky.social", Handle = true, Required = true, Limit = Participants.SignIn.HandleLimit,
                    Note = Sentence.Plain("They are admitted when they first sign in.")
                }) with { Tool = "invite", Submit = "Invite", Busy = "Finding them on Bluesky…" }
                : null,
            Withdraw = inviting ? Form.Posting(baseAddress + WithdrawPath, new Form.Field(AccountField, Holds.Invitation)) : null
        };
    }

    /// <summary>Who invited each of <paramref name="invited"/>, by id, as the page names them, in one keyed read.</summary>
    private static async Task<Dictionary<string, Render.Person>> Inviters(IReadOnlyList<Invitation> invited, string baseAddress, CancellationToken ct)
    {
        var ids = invited.Select(invitation => invitation.InvitedBy).Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0) return new Dictionary<string, Render.Person>();
        return (await Participant.Get(ids, ct)).OfType<Participant>().ToDictionary(row => row.Id, row => Render.Person.Of(row, baseAddress), StringComparer.Ordinal);
    }


    private string BaseAddress() => baseUrl.Resolve(HttpContext);
}
