using Tangent.Render;
using Tangent.Space;

namespace Tangent.Participants;

/// <summary>The place's applications, one resource both ways: the <see cref="Sections"/> of the settings, applications
/// marked current, and one bounded page of <see cref="Applications"/>, newest first, each waiting or declined, with
/// <see cref="Next"/>, the address of the older ones, null on the last page, and <see cref="First"/>, the newest page's,
/// for a page that <see cref="Continued"/> from a cursor. <see cref="State"/> is <c>waiting</c> where the page holds those
/// that wait alone, as the settings open it, empty where it holds every application, declined ones too, and
/// <see cref="All"/> the address of every application. Only whoever holds <c>ManageStanding</c> reads it.
/// <see cref="Admit"/> and <see cref="Decline"/> are the forms that decide, each taking the applicant's id in
/// <c>participant</c>; a waiting application is admitted or declined, a declined one only admitted. <see cref="Can"/> is
/// what the reader holds in the place, and <see cref="Day"/> the UTC day the times are written for. After a refusal the
/// page says why above the list. Where admission says apply, either page also holds <see cref="Invitations"/>, one bounded
/// page of the accounts invited, newest
/// first, with <see cref="InvitationsNext"/> the address of the older ones and <see cref="InvitationsFirst"/> the newest
/// page's, for a page continued from a cursor; <see cref="Invite"/> invites a Bluesky handle and <see cref="Withdraw"/>
/// withdraws an invitation by its account's DID.</summary>
[Render.Page(Render.Layout.Settings)]
public sealed record ApplicationsPage(
    IReadOnlyList<SettingsSection> Sections,
    IReadOnlyList<ApplicationsPage.Applicant> Applications,
    string? Next,
    bool Continued,
    string First,
    string State,
    string All,
    Form Admit,
    Form Decline,
    IReadOnlyList<string> Can,
    string Place,
    string Door,
    Who? Who,
    DateOnly Day,
    string? Failure = null)
{
    /// <summary>One applicant: their id, the name to show, their handle, their profile's address, the state of their
    /// application (<c>waiting</c> or <c>declined</c>) and when they applied.</summary>
    public sealed record Applicant(string Id, string Name, string Handle, string Profile, string State, DateTimeOffset AppliedAt);

    /// <summary>One account invited: its DID, its handle and the name and picture its public profile showed, when it was
    /// invited and by whom.</summary>
    public sealed record Invitee(string Account, string Handle, string Name, string? Avatar, DateTimeOffset InvitedAt, Render.Person By);

    public IReadOnlyList<Invitee>? Invitations { get; init; }

    public string? InvitationsNext { get; init; }

    public string? InvitationsFirst { get; init; }

    public Form? Invite { get; init; }

    public Form? Withdraw { get; init; }

    [Render.Slot(Render.Slot.Rail), Newtonsoft.Json.JsonIgnore]
    public Render.SettingsRail Rail => new(Sections);

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.PanelTitle Heading => new(Space.TangentSpaceSettingsController.ApplicationsSection, "Settings of " + Render.Sentence.Link(Place, Door));

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Notice? Line => Render.Notice.Of(null, Failure, null);

    /// <summary>Those that wait alone, where the settings open the applications, or every application, declined ones too,
    /// one press each, the one the page holds pressed.</summary>
    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Form States => Render.Form.Reading(All,
            new Render.Form.Field(ApplicationsController.StateQuery, "", State, Render.Form.Input.Switch)
            {
                Choices = [new(Waiting, "Waiting"), new("", "Every application")]
            })
        with { Tool = "applications-state read-applications", Key = "applications-state" };

    /// <summary>Each applicant: who, linked to their profile, their handle, when they applied and whether they were declined, and
    /// what the reader may decide — admit, and for one waiting, decline, confirmed in the page.</summary>
    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.EntryList Listed => new(Applications.Select(applicant => new Render.Entry(null,
    [
        Admit.Given(ApplicationsController.ParticipantField, applicant.Id) with { Tool = "admit", Submit = "Admit" },
        .. applicant.State == Waiting
            ? new Render.IPart[]
            {
                Decline.Given(ApplicationsController.ParticipantField, applicant.Id) with
                {
                    Tool = "decline", Opens = "Decline", Submit = "Decline", Confirm = "They keep reading what a visitor reads, and you can still admit them later."
                }
            }
            : []
    ])
    {
        Subject = new Render.Person(applicant.Id, applicant.Handle, applicant.Name, "person", null, applicant.Profile),
        Detail = Render.Sentence.Plain("@" + applicant.Handle + " · applied " + Render.Wording.When(applicant.AppliedAt, Day)
            + (applicant.State == Declined ? " · declined" : "")),
        State = applicant.State
    }).ToList(), Empty: Continued ? "No older applications." : "No one is waiting to be admitted.");

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Pager Pages => new("More applications", Continued ? new Render.Onward("Newest applications", First) : null, Next is { } next ? new Render.Onward("Older applications", next) : null);

    /// <summary>Inviting a Bluesky account by its handle.</summary>
    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Form? Inviting => Invite;

    /// <summary>Each account invited: its name, its handle, when and by whom, and withdrawing it, one press; nothing where
    /// none is.</summary>
    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.EntryList? Invited => Invitations is { Count: > 0 } invited ? new(invited.Select(invitee => new Render.Entry(null,
        Withdraw is { } withdraw
            ? [withdraw.Given(ApplicationsController.AccountField, invitee.Account) with { Tool = "withdraw-invitation", Submit = "Withdraw", Quiet = true }]
            : null)
    {
        Subject = new Render.Person(invitee.Account, invitee.Handle, invitee.Name, "person", invitee.Avatar),
        Detail = Render.Sentence.Plain("@" + invitee.Handle + " · invited " + Render.Wording.When(invitee.InvitedAt, Day) + " by " + invitee.By.Name),
        State = "invited"
    }).ToList(), Heading: "Invited") : null;

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Pager? InvitationPages => Invitations is null ? null
        : new("More invitations", InvitationsFirst is { } first ? new Render.Onward("Newest invitations", first) : null,
            InvitationsNext is { } next ? new Render.Onward("Older invitations", next) : null);

    /// <summary>The frame the shell draws around the page: its settings as its one crumb, and who reads.</summary>
    public static Render.Frame Frame(ApplicationsPage page) => new(page.Place, page.Door, [new Render.Crumb("Settings")], page.Who, null);

    public const string Waiting = "waiting";

    public const string Declined = "declined";
}

/// <summary>An invitation as inviting leaves it, for an agent: the account's DID, its handle and name, whether it waits to
/// arrive (<c>invited</c>) or was admitted now, having applied (<c>admitted</c>), the applications' address, and what the
/// inviter <see cref="Can"/> do in the place.</summary>
public sealed record InvitedAccount(string Account, string Handle, string Name, string State, string Applications, IReadOnlyList<string> Can);

/// <summary>An invitation withdrawn, for an agent: the account it named and the applications' address.</summary>
public sealed record WithdrawnInvitation(string Account, string Applications);

/// <summary>An application as a decision leaves it, for an agent: the applicant's id, name and handle, their standing
/// now (<c>admitted</c> or <c>declined</c>), the applications' address, and what the decider <see cref="Can"/> do in the
/// place.</summary>
public sealed record DecidedApplication(string Id, string Name, string Handle, string Standing, string Applications, IReadOnlyList<string> Can);
