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
using static Tangent.Participants.ApplicationsController;

namespace Tangent.Participants;

/// <summary>What deciding an application sends, as a form or one JSON object.</summary>
public sealed record DecideInput([Description(Holds.Applicant)] string? Participant = null, [Description(Holds.DecidedState)] string? State = null);

/// <summary>What inviting an account sends, as a form or one JSON object.</summary>
public sealed record InviteInput([Description(Holds.InviteHandle)] string? Handle = null);

/// <summary>What withdrawing an invitation sends, as a form or one JSON object.</summary>
public sealed record WithdrawInput([Description(Holds.Invitation)] string? Account = null);

/// <summary>What is decided among the applications (<see cref="ApplicationsController"/>), each for whoever holds
/// <see cref="Bit.ManageStanding"/>. Two <c>POST</c>s take the applicant's id in <c>participant</c>, as a form or a JSON
/// object: <c>admit</c> sends <see cref="Admit"/>; <c>decline</c> sends <see cref="Decline"/>. A form is sent back to the
/// applications, those that wait where it carries <c>state=waiting</c>; a JSON request reads the decision
/// (<see cref="DecidedApplication"/>); a refusal reads the first page again with the reason above the list, or a problem.
/// Where admission says apply, two more: <c>invite</c> takes a Bluesky handle in <c>handle</c>, reads whose account it names
/// from its public profile, in the request and outside any command (<see cref="ProfileReader.Find"/>), and sends
/// <see cref="Participants.Invite"/>; <c>withdraw</c> takes the account's DID in <c>account</c> and sends
/// <see cref="WithdrawInvitation"/>. A form is sent back to the applications that wait, where the section opens, and a
/// refusal of a handle reads that page again with the reason under the field and the handle kept; a JSON request reads
/// where the invitation stands.</summary>
[Managing]
public sealed class ApplicationVerbsController(KoanWebBaseUrl baseUrl) : ControllerBase
{
    /// <summary>The refusal of a handle that cannot be one.</summary>
    public static readonly Reason NotAHandle = Reason.Invalid(HandleField, "That is not a Bluesky handle, as alice.bsky.social.");

    /// <summary>The refusal of a handle no account answers to.</summary>
    public static Reason NoAccount(string handle) => Reason.Invalid(HandleField, "No Bluesky account answers to @" + handle + ". Check its spelling.");

    /// <summary>The refusal of a withdrawal that names no invitation.</summary>
    public static readonly Reason NotAnInvitation = Reason.Invalid(AccountField, "Name an invitation by the DID its row carries.");

    [HttpPost(AdmitPath)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    [Tool(Name = "admit", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Needs(Bit.ManageStanding)]
    [Description("Admit an applicant from read_applications, who becomes a member, for whoever holds ManageStanding. It reverses a decline.")]
    public Task<IActionResult> Admit(DecideInput input, CancellationToken ct) => Decide(input, true, ct);

    [HttpPost(DeclinePath)]
    [RequestSizeLimit(PostWrites.RequestLimit)]
    [Tool(Name = "decline", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Needs(Bit.ManageStanding)]
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
        if (Handle(input.Handle) is not { } handle) return await Uninvited(NotAHandle, input.Handle, ct);
        if (Here.Of(HttpContext).Rows.Space.Admission != Admission.Apply) return await Uninvited(Participants.Invite.Joins, handle, ct);

        ProfileAnswer found;
        try
        {
            found = await HttpContext.RequestServices.GetRequiredService<ProfileReader>().Find(handle, ct);
        }
        catch (ProfileReadFailed)
        {
            return await Uninvited(NoAccount(handle), handle, ct);
        }

        var outcome = await new Participants.Invite(personId, found.Did, handle, found.DisplayName, Monogram.Avatar(found.Avatar)).Send();
        switch (outcome)
        {
            case Outcome<Participants.Invite.Result>.Done { Result: var result }:
                if (!PostWrites.WantsJson(Request)) return Back(true);
                var place = await Capabilities.Load(personId, null, null, ct);
                return Ok(new InvitedAccount(found.Did, handle, found.DisplayName ?? handle, result.Admitted is null ? "invited" : "admitted",
                    BaseAddress() + ApplicationsController.Path, place.Can));
            case Outcome<Participants.Invite.Result>.Refused { Reason: var reason }:
                return await Uninvited(reason, handle, ct);
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
        if (input.Account is not { } account || !Participant.IsDid(account)) return await Refused(NotAnInvitation, true, ct);
        var outcome = await new WithdrawInvitation(personId, account).Send();
        if (outcome is Outcome<string>.Refused { Reason: var reason }) return await Refused(reason, true, ct);
        if (outcome is not Outcome<string>.Done) return Answers.NotDone(HttpContext, outcome);
        return PostWrites.WantsJson(Request) ? Ok(new WithdrawnInvitation(account, BaseAddress() + ApplicationsController.Path)) : Back(true);
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

    /// <summary>A refused invitation: for a JSON request, the problem its reason answers as; for a form, the applications that
    /// wait again, read afresh, with the reason under the handle field and the handle as it was typed, or above the list
    /// where the page offers no invitation.</summary>
    private async Task<IActionResult> Uninvited(Reason reason, string? typed, CancellationToken ct)
    {
        if (PostWrites.WantsJson(Request)) return Answers.Refused(HttpContext, reason);
        var page = await First(HttpContext, baseUrl, true, ct);
        page = page.Invite is { } invite
            ? page with { Invite = invite.Changing(HandleField, field => field with { Value = typed, Failure = reason.Text }) }
            : page with { Failure = reason.Text };
        return new ObjectResult(page) { StatusCode = Answers.Status(HttpContext, reason) };
    }

    /// <summary>A form sent back to the applications, to those that wait where <paramref name="waiting"/>.</summary>
    private IActionResult Back(bool waiting)
    {
        Response.Headers.Location = waiting ? WaitingAddress(BaseAddress()) : BaseAddress() + ApplicationsController.Path;
        return StatusCode(StatusCodes.Status303SeeOther);
    }

    private async Task<IActionResult> Decide(DecideInput input, bool admitting, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var personId = Here.Of(HttpContext).PersonId!;
        var applicantId = input.Participant ?? "";
        ICommand<Participant> command = admitting
            ? new Participants.Admit(personId, applicantId)
            : new Participants.Decline(personId, applicantId);
        var outcome = await command.Send();
        switch (outcome)
        {
            case Outcome<Participant>.Done { Result: var applicant }:
                return await Decided(applicant, personId, input.State == ApplicationsPage.Waiting, ct);
            case Outcome<Participant>.Refused { Reason: var reason }:
                return await Refused(reason, input.State == ApplicationsPage.Waiting, ct);
            default:
                return Answers.NotDone(HttpContext, outcome);
        }
    }

    /// <summary>A decision that committed, or that found it already made: a form is sent back to the applications, to
    /// those that wait where it came from them; a JSON request reads the applicant's standing now, with the decider's
    /// <c>can</c>.</summary>
    private async Task<IActionResult> Decided(Participant applicant, string? personId, bool waiting, CancellationToken ct)
    {
        if (!PostWrites.WantsJson(Request)) return Back(waiting);

        var place = await Capabilities.Load(personId, null, null, ct);
        var (name, handle) = (applicant.ShownName(), applicant.ShownHandle());
        return new ObjectResult(new DecidedApplication(applicant.Id, name, handle, People.Word(applicant.StandingAt(DateTimeOffset.UtcNow)),
            BaseAddress() + ApplicationsController.Path, place.Can))
        {
            ContentTypes = { "application/json" }
        };
    }

    /// <summary>A refusal: for a JSON request, the problem its reason answers as; for a form, the first page again, read
    /// afresh for the reader the Doorway let in, with the reason above the list.</summary>
    private async Task<IActionResult> Refused(Reason reason, bool waiting, CancellationToken ct)
    {
        if (PostWrites.WantsJson(Request)) return Answers.Refused(HttpContext, reason);

        var page = await First(HttpContext, baseUrl, waiting, ct) with { Failure = reason.Text };
        return new ObjectResult(page) { StatusCode = Answers.Status(HttpContext, reason) };
    }

    private string BaseAddress() => baseUrl.Resolve(HttpContext);
}
