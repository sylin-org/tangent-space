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
    /// <see cref="Take"/> (<c>moderator</c>, <c>administrator</c>). Nothing is offered that the reader may not do.</summary>
    public sealed record Listed(string Id, string Name, string Handle, string Profile, string Kind, string Standing, DateTimeOffset? Until,
        IReadOnlyList<string> Roles, IReadOnlyList<string> Can, IReadOnlyList<string> Give, IReadOnlyList<string> Take);
}

/// <summary>The forms of the inline changes of standing and roles, each taking the participant's id in
/// <c>participant</c> and the page it returns to in <c>back</c>: <see cref="TimeOut"/>, with how long in <c>for</c>, one
/// of <see cref="Durations"/>, <see cref="Ban"/> and <see cref="Lift"/>, present where the reader holds
/// <c>ManageStanding</c>; <see cref="Give"/> and <see cref="Take"/>, with the role in <c>role</c>, present where they hold
/// <c>ManageRoles</c>. A participant's own row says which of them the reader may use on them.</summary>
public sealed record PeopleForms(Form? TimeOut, Form? Ban, Form? Lift, Form? Give, Form? Take, IReadOnlyList<string> Durations);
