using Tangent.Render;
using Tangent.Space;

namespace Tangent.Participants;

/// <summary>The place's applications, one resource both ways: the <see cref="Sections"/> of the settings, applications
/// marked current, and one bounded page of <see cref="Applications"/>, newest first, each waiting or declined, with
/// <see cref="Next"/>, the address of the older ones, null on the last page, and <see cref="First"/>, the newest page's,
/// for a page that <see cref="Continued"/> from a cursor. <see cref="State"/> is <c>waiting</c> where the page holds those
/// that wait alone, empty where it holds every application, and <see cref="All"/> the address of every application.
/// <see cref="Admit"/> and <see cref="Decline"/> are the forms that
/// decide, each taking the applicant's id in <c>participant</c>, present only where the reader may decide; a waiting
/// application is admitted or declined, a declined one only admitted. <see cref="Can"/> is what the
/// reader holds in the place, and <see cref="Gate"/> why they may not decide, null when they may, when the list is empty
/// whatever waits; <see cref="SignIn"/> is where a visitor signs in, coming back here. <see cref="Day"/> is the UTC day the
/// times are written for. After a refusal the page says why above the list.</summary>
[Render.Page(Render.Layout.Settings)]
public sealed record ApplicationsPage(
    IReadOnlyList<SettingsSection> Sections,
    IReadOnlyList<ApplicationsPage.Applicant> Applications,
    string? Next,
    bool Continued,
    string First,
    string State,
    string All,
    Form? Admit,
    Form? Decline,
    IReadOnlyList<string> Can,
    string? Gate,
    string? SignIn,
    string Place,
    string Door,
    Who? Who,
    DateOnly Day,
    string? Failure = null)
{
    /// <summary>One applicant: their id, the name to show, their handle, their profile's address, the state of their
    /// application (<c>waiting</c> or <c>declined</c>) and when they applied.</summary>
    public sealed record Applicant(string Id, string Name, string Handle, string Profile, string State, DateTimeOffset AppliedAt);

    [Render.Slot(Render.Slot.Rail), Newtonsoft.Json.JsonIgnore]
    public Render.SettingsRail Rail => new(Sections);

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.PanelTitle Heading => new(Space.TangentSpaceSettingsController.ApplicationsSection, "Settings of " + Render.Sentence.Link(Place, Door));

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Notice? Line => Render.Notice.Of(Gate, Failure, SignIn);

    /// <summary>Every application or those that wait alone, one press each, the one the page holds pressed.</summary>
    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Form? States => Gate is not null ? null : Render.Form.Reading(All,
            new Render.Form.Field(ApplicationsController.StateQuery, "", State, Render.Form.Input.Switch)
            {
                Choices = [new("", "Every application"), new(Waiting, "Waiting")]
            })
        with { Tool = "applications-state read-applications", Key = "applications-state" };

    /// <summary>Each applicant: who, linked to their profile, their handle, when they applied and whether they were declined, and
    /// what the reader may decide — admit, and for one waiting, decline, confirmed in the page.</summary>
    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.EntryList? Listed => Gate is not null ? null : new(Applications.Select(applicant => new Render.Entry(null,
    [
        .. Admit is { } admit ? new Render.IPart[] { admit.Given(ApplicationsController.ParticipantField, applicant.Id) with { Tool = "admit", Submit = "Admit" } } : [],
        .. applicant.State == Waiting && Decline is { } decline
            ? new Render.IPart[]
            {
                decline.Given(ApplicationsController.ParticipantField, applicant.Id) with
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
    public Render.Pager? Pages => Gate is not null ? null
        : new("More applications", Continued ? new Render.Onward("Newest applications", First) : null, Next is { } next ? new Render.Onward("Older applications", next) : null);

    /// <summary>The frame the shell draws around the page: its settings as its one crumb, and who reads, or where a visitor
    /// signs in.</summary>
    public static Render.Frame Frame(ApplicationsPage page) => new(page.Place, page.Door, [new Render.Crumb("Settings")], page.Who, page.SignIn);

    public const string Waiting = "waiting";

    public const string Declined = "declined";
}

/// <summary>An application as a decision leaves it, for an agent: the applicant's id, name and handle, their standing
/// now (<c>admitted</c> or <c>declined</c>), the applications' address, and what the decider <see cref="Can"/> do in the
/// place.</summary>
public sealed record DecidedApplication(string Id, string Name, string Handle, string Standing, string Applications, IReadOnlyList<string> Can);
