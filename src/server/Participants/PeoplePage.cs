using Tangent.Render;
using Tangent.Space;

namespace Tangent.Participants;

/// <summary>The place's people, one resource both ways: the <see cref="Sections"/> of the settings, people marked
/// current, and one bounded page of <see cref="People"/>, in handle order, each with what the reader may do to them;
/// <see cref="Next"/> is the address of the page after this one, null on the last, and <see cref="First"/> the first
/// page's, for a page that <see cref="Continued"/> from a cursor. <see cref="Handle"/> is the start of a handle the page
/// is narrowed to, <see cref="Standing"/> the standing (<c>timed-out</c> or <c>banned</c>) and <see cref="Role"/> the word
/// of the role whose holders it lists, each empty for none, with <see cref="RoleName"/> the role's name, null where no
/// role here has that word; <see cref="Search"/> is where the search form asks, by the <c>handle</c> parameter, and where
/// everyone is listed. Only whoever holds <c>ManageStanding</c> or <c>ManageRoles</c> reads it. <see cref="Forms"/> are the
/// forms of the verbs the reader may use. <see cref="Can"/> is what the reader holds in the place, and <see cref="Day"/>
/// the UTC day the times are written for. After a refusal the page says why above the list.</summary>
[Render.Page(Render.Layout.Settings)]
public sealed record PeoplePage(
    IReadOnlyList<SettingsSection> Sections,
    IReadOnlyList<PeoplePage.Listed> People,
    string? Next,
    bool Continued,
    string First,
    string Handle,
    string Standing,
    string Role,
    string? RoleName,
    string Search,
    PeopleForms? Forms,
    IReadOnlyList<string> Can,
    string Place,
    string Door,
    Who? Who,
    DateOnly Day,
    string? Failure = null)
{
    /// <summary>One participant as the reader of the people finds them: their id, the name to show, their handle, their
    /// profile's address, their kind (<c>person</c> or <c>agent</c>), their standing (<c>admitted</c>,
    /// <c>timed-out</c>, <c>banned</c>, or for an applicant <c>applied</c> or <c>declined</c>) with when a timeout
    /// ends, the roles they hold by name, highest first, and what the reader may do to them: the standings they may give
    /// them in <see cref="Can"/>, drawn from <see cref="Gives"/>, the rows of <see cref="Participant.Standings"/> from where
    /// they stand, and the roles they may <see cref="Give"/> and <see cref="Take"/>, lowest first. Nothing is offered that
    /// the reader may not do.</summary>
    public sealed record Listed(Render.Person Person, string Standing, DateTimeOffset? Until, IReadOnlyList<string> Roles,
        [property: Newtonsoft.Json.JsonIgnore] IReadOnlyList<Participant.Transition> Gives, IReadOnlyList<Offered> Give, IReadOnlyList<Offered> Take)
    {
        /// <summary>The standings the reader may give them, by the word <c>to</c> takes.</summary>
        public IReadOnlyList<string> Can => Participant.Can(Gives);

        /// <summary>Where they stand, in words, with when a timeout ends.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public string Stands => Until is { } until ? "timed out until " + Render.Wording.Until(until) : Standing;
    }

    [Render.Slot(Render.Slot.Rail), Newtonsoft.Json.JsonIgnore]
    public Render.SettingsRail Rail => new(Sections);

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.PanelTitle Heading => new(Space.TangentSpaceSettingsController.PeopleSection, "Settings of " + Render.Sentence.Link(Place, Door));

    /// <summary>Whose holders the page lists, where it lists a role's, with the way back to everyone.</summary>
    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Sentence? Holding => RoleName is not { } name ? null
        : new("Those who hold the " + Render.Sentence.Strong(name) + " role. " + Render.Sentence.Link("Everyone here", Search));

    /// <summary>The box that finds people by the start of their handle, within the standing and the role the page is
    /// narrowed to.</summary>
    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Form Finding => Narrowing(Render.Form.Reading(Search,
            new Render.Form.Field(PeopleController.HandleQuery, "", Handle, Render.Form.Input.Search) { Label = "Find by handle", Placeholder = "The start of a handle", Limit = 253 })
        with { Tool = "people-search read-people", Submit = "Find" }, PeopleController.HandleQuery);

    /// <summary>The standings the page narrows to, one press each, the one it stands at pressed, keeping the handle and the
    /// role it is narrowed to.</summary>
    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Form Standings => Narrowing(Render.Form.Reading(Search,
            new Render.Form.Field(PeopleController.StandingQuery, "", Standing, Render.Form.Input.Switch)
            {
                Choices = [new("", "Every standing"), new(Participants.People.Word(Participants.Standing.TimedOut), "Timed out"), new(Participants.People.Word(Participants.Standing.Banned), "Banned")]
            })
        with { Tool = "people-standing read-people", Key = "people-standing" }, PeopleController.StandingQuery);

    /// <summary><paramref name="form"/> carrying what the page is narrowed to but its own field, <paramref name="own"/>.</summary>
    private Render.Form Narrowing(Render.Form form, string own)
    {
        foreach (var (name, value) in new[] { (PeopleController.HandleQuery, Handle), (PeopleController.StandingQuery, Standing), (PeopleController.RoleQuery, Role) })
        {
            if (name != own && value.Length > 0) form = form.Given(name, value);
        }

        return form;
    }

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Notice? Refused => Render.Notice.Of(null, Failure, null);

    /// <summary>The people, each with their kind, where they stand and their roles, and what the reader may change of them.</summary>
    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.EntryList Listing => new(People.Select(person => new Render.Entry(null, Forms?.For(person))
    {
        Subject = person.Person,
        Detail = Render.Sentence.Plain("@" + person.Person.Handle + " · " + person.Person.Kind + " · " + person.Stands + (person.Roles.Count > 0 ? " · " + string.Join(", ", person.Roles) : ""))
    }).ToList(), Empty: Role.Length > 0 && RoleName is null
        ? "No role here has the word " + Render.Sentence.Plain(Role) + "."
        : Narrowed() is { Count: > 0 } narrowed ? "No one here " + Render.Wording.Joined(narrowed) + "."
        : Continued ? "No one further." : "No one is here yet.");

    /// <summary>What the page is narrowed to, as what no one here does where it lists no one.</summary>
    private List<string> Narrowed()
    {
        var said = new List<string>();
        if (Standing.Length > 0) said.Add(Standing == Participants.People.Word(Participants.Standing.Banned) ? "is banned" : "is timed out");
        if (RoleName is { } name) said.Add("holds the " + Render.Sentence.Plain(name) + " role");
        if (Handle.Length > 0) said.Add("has a handle that starts with " + Render.Sentence.Plain(Handle));
        return said;
    }

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Pager Pages => new("More people", Continued ? new Render.Onward("First page", First) : null,
        Next is { } next ? new Render.Onward("Next page", next) : null);

    /// <summary>The frame the shell draws around the page: its settings as its one crumb, and who reads.</summary>
    public static Render.Frame Frame(PeoplePage page) => new(page.Place, page.Door, [new Render.Crumb("Settings")], page.Who, null);

    /// <summary>A role the reader may give or take: its word, which a form sends and which never changes
    /// (<c>moderator</c>, <c>administrator</c>, or a word of the place's own), and its name as the place now calls it.</summary>
    public sealed record Offered(string Word, string Name);
}

/// <summary>The forms of the inline changes of standing and roles, each taking the participant's id in
/// <c>participant</c>: <see cref="Standing"/>, with the standing in <c>to</c> and for a timeout how long in <c>for</c>, sending
/// a page's form on to the page it names in <c>return</c>, present where the reader holds <c>ManageStanding</c>;
/// <see cref="Give"/> and <see cref="Take"/>, with the role in <c>role</c> and the page they return to in <c>back</c>, present
/// where they hold <c>ManageRoles</c>. A participant's own row says which of them the reader may use on them.</summary>
public sealed record PeopleForms(Form? Standing, Form? Give, Form? Take)
{
    /// <summary>What the reader may change of <paramref name="person"/>, inline, each change a form naming them: each role
    /// given or taken, then each standing they may give them (<see cref="Buttons"/>).</summary>
    public IReadOnlyList<Render.IPart> For(PeoplePage.Listed person)
    {
        var answers = new List<Render.IPart>();
        Form Naming(Form form) => form.Value(PeopleController.BackField) is { } back
            ? form.Given(PeopleController.ParticipantField, person.Person.Id).Given(PeopleController.BackField, back)
            : form.Given(PeopleController.ParticipantField, person.Person.Id);
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
        if (Standing is { } standing) answers.AddRange(Buttons(standing, person.Person.Id, person.Gives));
        return answers;
    }

    /// <summary>A button for each of <paramref name="gives"/>, rows of <see cref="Participant.Standings"/>, on
    /// <paramref name="form"/>, the form that gives a standing (<see cref="PeopleController.Giving"/>), naming the participant
    /// <paramref name="id"/>: its word, and where the row confirms, opening in place to say its sentence, confirmed there; a
    /// timeout offering its lengths, each one press. Each carries the tool it sends, <c>set_standing</c>.</summary>
    public static IEnumerable<Render.IPart> Buttons(Form form, string id, IEnumerable<Participant.Transition> gives)
    {
        foreach (var transition in gives)
        {
            var to = People.Word(transition.To);
            var button = form.Given(PeopleController.ParticipantField, id).Given(PeopleController.ToField, to) with { Tool = PeopleController.StandingTool };
            if (transition.Confirm is { } confirm) button = button with { Opens = transition.Button, Confirm = confirm };
            yield return transition.To == Participants.Standing.TimedOut
                ? button.Changing(PeopleController.ForField, field => field with
                {
                    As = Form.Input.Switch,
                    Choices = People.Durations.Select(duration => new Form.Choice(duration.Word, duration.Label)).ToList()
                })
                : button.Keeping(PeopleController.ParticipantField, PeopleController.ToField) with { Submit = transition.Button };
        }
    }
}
