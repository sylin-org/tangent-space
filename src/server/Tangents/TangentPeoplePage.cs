using Tangent.Render;

namespace Tangent.Tangents;

/// <summary>A Tangent's people, one resource both ways, for whoever manages it: its <see cref="Name"/> and
/// <see cref="Audience"/>; how someone outside it comes in (<see cref="Joining"/>, one of <see cref="Joinings"/>), which
/// <see cref="ChangeJoining"/> sets; the participants <see cref="Asking"/> to join; who reads and posts there by role
/// (<see cref="Roles"/>, set at <see cref="Access"/>) and by person (<see cref="People"/>), a page at a time, with
/// <see cref="Next"/> the page after, null on the last, and <see cref="First"/> the first page, on a page after it; and,
/// for a search by the start of a handle (<see cref="Find"/>), the participants <see cref="Found"/>. Each person says what
/// they can do there — <c>in</c> by their own place, <c>by-role</c>, <c>kept-out</c>, <c>asked</c>, <c>declined</c> or
/// <c>out</c> — and the choices the reader may send for them through <see cref="Change"/>, which lets them in or keeps them
/// out. <see cref="Fixed"/> says this is the moderators' Tangent, whose access stays as it is.</summary>
[Render.Page(Render.Layout.Panel)]
public sealed record TangentPeoplePage(
    string Slug,
    string Name,
    string Address,
    string Settings,
    string Access,
    string Audience,
    bool Fixed,
    string Joining,
    IReadOnlyList<string> Joinings,
    Form? ChangeJoining,
    IReadOnlyList<TangentPeoplePage.Someone> Asking,
    IReadOnlyList<TangentPeoplePage.RoleHere> Roles,
    IReadOnlyList<TangentPeoplePage.Someone> People,
    string? Next,
    string? Find,
    IReadOnlyList<TangentPeoplePage.Someone>? Found,
    Form? Change,
    string Place,
    string Door,
    Who? Who,
    string? Failure = null,
    string? First = null)
{
    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.PanelTitle Heading => new("People", "Settings of " + Render.Sentence.Link(Name, Address));

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Notice? Line => Render.Notice.Of(null, Failure, null);

    /// <summary>Who comes in, by its audience and how people join it.</summary>
    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Sentence Reach => new(Fixed
        ? "Only moderators read this Tangent, and its access stays as it is."
        : Audience == "public"
            ? "It is public: everyone who reads the place reads it. Make it private in its settings to keep it to its members."
            : Joining switch
            {
                "join" => "It is members-only: anyone who takes part in the place may join it.",
                "ask" => "It is members-only: people ask to join, and you decide.",
                _ => "It is private: only the roles and people below read it, and no one else finds it."
            });

    /// <summary>How people come in, each way one press, the one it stands at pressed.</summary>
    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Form? Ways => Fixed ? null : ChangeJoining;

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.EntryList? Asks => Asking.Count == 0 ? null : new(Asking.Select(Entry).ToList(), "Asking to join");

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Form? Finding => Fixed ? null : Render.Form.Reading("",
        new Render.Form.Field(TangentPeopleController.FindQuery, "", Find, Render.Form.Input.Search) { Label = "Find someone by handle", Placeholder = "ada.example" })
        with { Tool = "find-people", Submit = "Find" };

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.EntryList? Finds => Fixed || Found is not { } found ? null
        : new(found.Select(Entry).ToList(), Empty: "No one here has a handle that starts with " + Render.Sentence.Plain(Find ?? "") + ".");

    /// <summary>Who reads and posts here: administrators, the roles given access, and each person let in or kept out by name.</summary>
    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.EntryList Readers => new(
        [
            new Render.Entry("Administrators") { Detail = "read and post in every Tangent" },
            .. Roles.Select(role => new Render.Entry(Render.Sentence.Plain(role.Name))
            {
                Detail = (role.Access == "post" ? "read and post here" : "read here") + " · " + Render.Sentence.Link("change", Access)
            }),
            .. People.Select(Entry)
        ], "Who reads and posts here");

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Sentence? NoOne => People.Count == 0 ? new("No one is in or kept out by name yet.") : null;

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Pager Pages => new("More people", First is { } first ? new Render.Onward("First people", first) : null,
        Next is { } next ? new Render.Onward("More people →", next) : null);

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Onward Back => new("Back to the settings", Settings) { Back = true };

    /// <summary>One person as the page lists them: their name, their handle and where they stand here, and what may be done for
    /// them, each its own small form — an ask let in or declined, anyone else let in, and someone in taken out once
    /// confirmed in the page.</summary>
    private Render.Entry Entry(Someone someone) => new(null, Change is not { } change ? null : someone.Choices.Select(choice =>
        (Render.IPart)(change.Given(TangentPeopleController.PersonField, someone.Person.Id).Given(TangentPeopleController.ChoiceField, choice) with
        {
            Tool = "let-into-tangent",
            Submit = choice == TangentPeopleController.InChoice ? "Let in" : choice == TangentPeopleController.OutChoice && someone.State != "asked" ? "Take out" : "Decline",
            Quiet = true,
            Opens = choice == TangentPeopleController.OutChoice && someone.State != "asked" ? "Take out" : null,
            Confirm = choice == TangentPeopleController.OutChoice && someone.State != "asked" ? "They stop reading it at once, and cannot join or ask again until you let them in." : null
        })).ToList())
    {
        Subject = someone.Person,
        Detail = Render.Sentence.Plain("@" + someone.Person.Handle) + (someone.State == "asked" && Asking.Contains(someone) ? "" : " · " + someone.State switch
        {
            "in" => "reads and posts here",
            TangentPeopleController.RoleState => "reads here through a role",
            TangentPeopleController.KeptOutState => Audience == "public" ? "kept out while it is private" : "kept out",
            "asked" => "asked to join",
            "declined" => "ask declined",
            _ => "not in"
        })
    };

    /// <summary>The frame the shell draws around the page: its settings and its people as its own crumbs past the Tangent, and
    /// who reads.</summary>
    public static Render.Frame Frame(TangentPeoplePage page)
        => new(page.Place, page.Door, [new Render.Crumb("Settings", page.Settings), new Render.Crumb("People")], page.Who);


    /// <summary>One participant as the people tab lists them: their id, name, handle, kind, profile, where they stand
    /// here, and the choices the reader may send for them: <c>in</c>, <c>out</c>, both for an ask, or none.</summary>
    public sealed record Someone(Render.Person Person, string State, IReadOnlyList<string> Choices);

    /// <summary>A role with access of its own here: its word, its name, and <c>read</c> or <c>post</c>.</summary>
    public sealed record RoleHere(string Word, string Name, string Access);
}
