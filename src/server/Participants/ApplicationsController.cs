using System.ComponentModel;
using Koan.Data.Core.Querying;
using Koan.Web.Attributes;
using Koan.Web.Controllers;
using Koan.Web.Hooks;
using Koan.Web.Hosting;
using Koan.Web.Queries;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Posts;
using Tangent.Render;
using Tangent.Space;

namespace Tangent.Participants;

/// <summary>What reading the applications takes beside where its page continues: <c>waiting</c> for those that wait alone,
/// and where the page of invitations continues.</summary>
public sealed record ApplicationsInput(
    [property: Description(Holds.ApplicationsState)] string? State = null,
    [property: Description(Holds.InvitationsAfter)] string? Invited = null);

/// <summary>The place's applications, a section of its settings at <c>/settings/applications</c>, for whoever holds
/// <see cref="Bit.ManageStanding"/>: a declared collection of Koan's over <see cref="Participant"/>, which serves its read
/// alone. Its <c>GET</c> is one bounded page of the participants who applied, waiting or declined, newest application first
/// through <c>ix_participant_applications</c> (<see cref="Participant.AppliedAt"/>), continued from the sealed cursor in
/// <c>after</c>, and with <c>state=waiting</c> those that wait alone, which is where the settings open the section
/// (<see cref="WaitingAddress"/>); another state is refused with <c>400</c>, and so is a cursor that does not open. Its own
/// options hook narrows the rows to the applications (<see cref="Applications.Among"/>), and its emit hook composes the page
/// both ways (<see cref="ApplicationsPage"/>), which, where admission says apply, also holds one bounded page of the
/// invitations (<see cref="Invitations.Read"/>), continued from the cursor in <c>invited</c>. The Doorway refuses it, before
/// anything is bound, to whoever does not hold <see cref="Bit.ManageStanding"/>, <c>401</c> to sign in and <c>403</c>
/// otherwise, a page as the refusal's page and a JSON request as its problem, and answers a reader for whom the place does
/// not exist what an address with nothing there receives. Deciding and inviting are <see cref="ApplicationVerbsController"/>'s.</summary>
[Route(Path)]
[Declared]
[Managing]
public sealed class ApplicationsController(KoanWebBaseUrl baseUrl) : EntityController<Participant>, IRequestOptionsHook<Participant>, IEmitHook<Participant>
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

    /// <summary>The field that names the applicant decided.</summary>
    public const string ParticipantField = "participant";

    /// <summary>The query parameter that carries the cursor of the page to continue from.</summary>
    public const string After = "after";

    /// <summary>The query parameter, and the field of a decision, that narrows the applications to those that wait.</summary>
    public const string StateQuery = "state";

    /// <summary>The order the applications are read in, newest application first.</summary>
    public const string Newest = "-" + nameof(Participant.AppliedAt);

    private static readonly Keyset<Participant> Order = Keyset<Participant>.By(Newest);

    /// <summary>The address of the applications that wait, under <paramref name="baseAddress"/>.</summary>
    public static string WaitingAddress(string baseAddress) => baseAddress + Path + "?" + StateQuery + "=" + ApplicationsPage.Waiting;

    private bool _waiting;

    private Invitations.Cursor? _invited;

    [HttpGet("")]
    [Tool(Name = "read_applications")]
    [Description("Read who waits to be admitted and who was declined, or with state waiting those who wait alone, a page at a time, for whoever holds ManageStanding; where admission says apply, every application's page also lists who is invited. Then call admit or decline on an applicant's id, invite on a Bluesky handle, or withdraw_invitation on an invitation's account.")]
    [Needs(Bit.ManageStanding)]
    [Pagination(DefaultSize = Applications.PageSize, MaxSize = Applications.PageSize, DefaultSort = Newest)]
    [Input<ApplicationsInput>]
    public override Task<IActionResult> GetCollection(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return base.GetCollection(ct);
    }

    /// <summary>The applications alone, those that wait alone where the input says <c>waiting</c>; another state, or an
    /// invitations cursor that is not one, is refused before anything is read.</summary>
    public Task OnBuildingOptions(HookContext<Participant> ctx, QueryOptions opts)
    {
        var input = ctx.Input<ApplicationsInput>();
        if (!string.IsNullOrEmpty(input?.State) && input.State != ApplicationsPage.Waiting)
            ctx.ShortCircuit(new Problem(StatusCodes.Status400BadRequest,
                $"The {StateQuery} parameter is {ApplicationsPage.Waiting}, or left out for every application.") { Title = "Not a state of the applications" }.ToResult());
        else if (input?.Invited is { } invited && (_invited = Invitations.Cursor.Parse(invited)) is null)
            ctx.ShortCircuit(new Problem(StatusCodes.Status400BadRequest,
                $"The {InvitedQuery} parameter is the cursor a page of the invitations names as its next.") { Title = "Not a page of the invitations" }.ToResult());
        else
        {
            _waiting = input?.State == ApplicationsPage.Waiting;
            opts.AddPredicate(Applications.Among(_waiting));
        }
        return Task.CompletedTask;
    }

    /// <summary>The page the rows read make, linking the next one by the cursor Koan sealed.</summary>
    public async Task<EmitDecision> OnEmitCollection(HookContext<Participant> ctx, object payload)
        => EmitDecision.With(await Compose(ctx.Http!, baseUrl, _waiting, ((IEnumerable<Participant>)payload).ToList(), ctx.Next,
            ctx.Http!.Request.Query.ContainsKey(After), _invited, ctx.Ct));

    /// <summary>The first page of the applications, of those that wait alone where <paramref name="waiting"/>, read afresh as
    /// the list reads it, for a decision or an invitation refused, which reads the page again with its reason.</summary>
    internal static async Task<ApplicationsPage> First(HttpContext http, KoanWebBaseUrl baseUrl, bool waiting, CancellationToken ct)
    {
        var page = await Participant.Page(Order, null, Applications.PageSize, Applications.Among(waiting), ct);
        var next = page.Next is { } key
            ? SealedCursor.Seal(http.RequestServices.GetRequiredService<IDataProtectionProvider>(), typeof(ApplicationsController).FullName!, Order, key)
            : null;
        return await Compose(http, baseUrl, waiting, page.Items, next, false, null, ct);
    }

    /// <summary>The form that decides an application at <paramref name="action"/>: admit or decline, by the applicant's id,
    /// coming back to those that wait where <paramref name="waiting"/>.</summary>
    private static Form Deciding(string action, bool waiting)
    {
        var form = Form.Posting(action, new Form.Field(ParticipantField, Holds.Applicant));
        return waiting ? form.Given(StateQuery, ApplicationsPage.Waiting) : form;
    }

    /// <summary>One page of the applications, of those that wait alone where <paramref name="waiting"/>: the rows read and the
    /// sealed cursor of the next page; where admission says apply, one page of the invitations too, continued from
    /// <paramref name="invited"/>, with the forms that invite and withdraw.</summary>
    private static async Task<ApplicationsPage> Compose(HttpContext http, KoanWebBaseUrl baseUrl, bool waiting, IReadOnlyList<Participant> read,
        string? next, bool continued, Invitations.Cursor? invited, CancellationToken ct)
    {
        var place = Here.Of(http).Around;
        var personId = PostWrites.PersonId(http.User);
        var baseAddress = baseUrl.Resolve(http);
        var here = baseUrl.PathBase + Path + (waiting ? "?" + StateQuery + "=" + ApplicationsPage.Waiting : "");
        var applications = waiting ? WaitingAddress(baseAddress) : baseAddress + Path;

        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? TangentSpace.Unestablished();
        var inviting = space.Admission == Admission.Apply;
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
            continued,
            applications,
            waiting ? ApplicationsPage.Waiting : "",
            baseAddress + Path,
            Deciding(baseAddress + AdmitPath, waiting),
            Deciding(baseAddress + DeclinePath, waiting),
            place.Can,
            space.Name,
            baseAddress + "/",
            await PostWrites.Reader(personId, place, baseAddress, here, ct),
            DateOnly.FromDateTime(DateTime.UtcNow))
        {
            Invitations = invitations?.Invited.Select(invitation => new ApplicationsPage.Invitee(invitation.Id, invitation.Handle,
                invitation.Name ?? invitation.Handle, invitation.Avatar, invitation.InvitedAt,
                inviters.GetValueOrDefault(invitation.InvitedBy) ?? Render.Person.Left(invitation.InvitedBy))).ToList(),
            InvitationsNext = invitations?.Next is { } older ? applications + (waiting ? "&" : "?") + InvitedQuery + "=" + Uri.EscapeDataString(older.ToString()) : null,
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
}
