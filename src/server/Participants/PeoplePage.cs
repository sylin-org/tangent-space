using Tangent.Render;
using Tangent.Space;

namespace Tangent.Participants;

/// <summary>The place's people, one resource both ways: the <see cref="Sections"/> of the settings, people marked
/// current, and one bounded page of <see cref="People"/>, in handle order, each with what the reader may do to them;
/// <see cref="Next"/> is the address of the page after this one, null on the last, and <see cref="First"/> the first
/// page's, for a page that <see cref="Continued"/> from a cursor. <see cref="Handle"/> is the start of a handle the page
/// is narrowed to, empty for none, and <see cref="Search"/> where the search form asks, by the <c>handle</c> parameter.
/// <see cref="Forms"/> are the forms of the verbs, present only where the reader may read the people.
/// <see cref="Can"/> is what the reader holds in the place, and
/// <see cref="Gate"/> why they may not read the people, null when they may, when the list is empty whoever is here;
/// <see cref="SignIn"/> is where a visitor signs in, coming back here. <see cref="Day"/> is the UTC day the times are
/// written for. After a refusal the page says why above the list.</summary>
[Render.Page(Render.Layout.Settings)]
public sealed record PeoplePage(
    IReadOnlyList<SettingsSection> Sections,
    IReadOnlyList<PeoplePage.Listed> People,
    string? Next,
    bool Continued,
    string First,
    string Handle,
    string Search,
    PeopleForms? Forms,
    IReadOnlyList<string> Can,
    string? Gate,
    string? SignIn,
    string Place,
    string Door,
    Who? Who,
    DateOnly Day,
    string? Failure = null)
{
    /// <summary>One participant as the reader of the people finds them: their id, the name to show, their handle, their
    /// profile's address, their kind (<c>person</c> or <c>agent</c>), their standing (<c>admitted</c>,
    /// <c>timed-out</c>, <c>banned</c>, or for an applicant <c>applied</c> or <c>declined</c>) with when a timeout
    /// ends, the roles they hold by name, highest first, and what the reader may do to them: the standing verbs in
    /// <see cref="Can"/> (<c>time-out</c>, <c>ban</c>, <c>lift</c>) and the roles they may <see cref="Give"/> and
    /// <see cref="Take"/>, lowest first. Nothing is offered that the reader may not do.</summary>
    public sealed record Listed(Render.Person Person, string Standing, DateTimeOffset? Until, IReadOnlyList<string> Roles, IReadOnlyList<string> Can,
        IReadOnlyList<Offered> Give, IReadOnlyList<Offered> Take)
    {
        /// <summary>Where they stand, in words, with when a timeout ends.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public string Stands => Until is { } until ? "timed out until " + Render.Wording.Until(until) : Standing;
    }

    [Render.Slot(Render.Slot.Rail), Newtonsoft.Json.JsonIgnore]
    public Render.SettingsRail Rail => new(Sections);

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.PanelTitle Heading => new(Space.TangentSpaceSettingsController.PeopleSection, "Settings of " + Render.Sentence.Link(Place, Door));

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Notice? Line => Gate is { } gate ? Render.Notice.Of(gate, null, SignIn) : null;

    /// <summary>The box that finds people by the start of their handle.</summary>
    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Form? Finding => Gate is not null ? null : Render.Form.Reading(Search,
        new Render.Form.Field(PeopleController.HandleQuery, "", Handle, Render.Form.Input.Search) { Label = "Find by handle", Placeholder = "The start of a handle", Limit = 253 })
        with { Tool = "people-search read-people", Submit = "Find" };

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Notice? Refused => Gate is not null ? null : Render.Notice.Of(null, Failure, null);

    /// <summary>The people, each with their kind, where they stand and their roles, and what the reader may change of them.</summary>
    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.EntryList? Listing => Gate is not null ? null : new(People.Select(person => new Render.Entry(null, Forms?.For(person))
    {
        Subject = person.Person,
        Detail = Render.Sentence.Plain("@" + person.Person.Handle + " · " + person.Person.Kind + " · " + person.Stands + (person.Roles.Count > 0 ? " · " + string.Join(", ", person.Roles) : ""))
    }).ToList(), Empty: Handle.Length > 0
        ? "No one here has a handle that starts with " + Render.Sentence.Plain(Handle) + "."
        : Continued ? "No one further." : "No one is here yet.");

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Pager? Pages => Gate is not null ? null : new("More people", Continued ? new Render.Onward("First page", First) : null,
        Next is { } next ? new Render.Onward("Next page", next) : null);

    /// <summary>The frame the shell draws around the page: its settings as its one crumb, and who reads, or where a visitor
    /// signs in.</summary>
    public static Render.Frame Frame(PeoplePage page) => new(page.Place, page.Door, [new Render.Crumb("Settings")], page.Who, page.SignIn);

    /// <summary>A role the reader may give or take: its word, which a form sends and which never changes
    /// (<c>moderator</c>, <c>administrator</c>, or a word of the place's own), and its name as the place now calls it.</summary>
    public sealed record Offered(string Word, string Name);
}

/// <summary>The forms of the inline changes of standing and roles, each taking the participant's id in
/// <c>participant</c> and the page it returns to in <c>back</c>: <see cref="TimeOut"/>, with how long in <c>for</c>, one
/// of <see cref="Durations"/>, <see cref="Ban"/> and <see cref="Lift"/>, present where the reader holds
/// <c>ManageStanding</c>; <see cref="Give"/> and <see cref="Take"/>, with the role in <c>role</c>, present where they hold
/// <c>ManageRoles</c>. A participant's own row says which of them the reader may use on them.</summary>
public sealed record PeopleForms(Form? TimeOut, Form? Ban, Form? Lift, Form? Give, Form? Take, IReadOnlyList<string> Durations)
{
    /// <summary>What the reader may change of <paramref name="person"/>, inline, each change a form naming them: lift, at once;
    /// time out, which opens in place to say what a timeout leaves and offers its durations; each role given or taken; and ban,
    /// which opens in place to say what a ban does, confirmed there.</summary>
    public IReadOnlyList<Render.IPart> For(PeoplePage.Listed person)
    {
        var answers = new List<Render.IPart>();
        Form Naming(Form form) => form.Value(PeopleController.BackField) is { } back
            ? form.Given(PeopleController.ParticipantField, person.Person.Id).Given(PeopleController.BackField, back)
            : form.Given(PeopleController.ParticipantField, person.Person.Id);
        if (person.Can.Contains(People.Verb.Lift) && Lift is { } lift)
            answers.Add(Naming(lift) with { Tool = "lift", Submit = person.Standing == People.Word(Standing.Banned) ? "Lift the ban" : "Lift the timeout" });
        if (person.Can.Contains(People.Verb.TimeOut) && TimeOut is { } timeOut)
            answers.Add(Naming(timeOut).Changing(PeopleController.ForField, item => item with
            {
                As = Form.Input.Switch,
                Choices = Durations.Select(duration => new Form.Choice(duration, duration switch
                {
                    "hour" => "For an hour",
                    "day" => "For a day",
                    "week" => "For a week",
                    _ => "For a " + duration
                })).ToList()
            }) with
            {
                Tool = "time-out", Opens = "Time out",
                Confirm = "They keep reading and reporting here, and post, reply and start nothing until it ends; you can lift it sooner."
            });
        if (Give is { } give)
            answers.AddRange(person.Give.Select(role => (Render.IPart)(Naming(give).Given(PeopleController.RoleField, role.Word) with
            {
                Tool = "give-role", Submit = "Give " + role.Name + " role", Quiet = true
            })));
        if (Take is { } take)
            answers.AddRange(person.Take.Select(role => (Render.IPart)(Naming(take).Given(PeopleController.RoleField, role.Word) with
            {
                Tool = "take-role", Submit = "Take " + role.Name + " role", Quiet = true
            })));
        if (person.Can.Contains(People.Verb.Ban) && Ban is { } ban)
            answers.Add(Naming(ban) with
            {
                Tool = "ban", Opens = "Ban", Submit = "Ban", Confirm = "This place disappears for them, every page and every stream, until you lift the ban."
            });
        return answers;
    }
}
