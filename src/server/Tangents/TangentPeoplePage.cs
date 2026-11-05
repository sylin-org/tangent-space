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
    IReadOnlyList<TangentPeoplePage.Person> Asking,
    IReadOnlyList<TangentPeoplePage.RoleHere> Roles,
    IReadOnlyList<TangentPeoplePage.Person> People,
    string? Next,
    string? Find,
    IReadOnlyList<TangentPeoplePage.Person>? Found,
    Form? Change,
    string Place,
    string Door,
    Who? Who,
    string? Failure = null,
    string? First = null)
{
    /// <summary>One participant as the people tab lists them: their id, name, handle, kind, profile, where they stand
    /// here, and the choices the reader may send for them: <c>in</c>, <c>out</c>, both for an ask, or none.</summary>
    public sealed record Person(string Id, string Name, string Handle, string Kind, string? Profile, string State, IReadOnlyList<string> Choices);

    /// <summary>A role with access of its own here: its word, its name, and <c>read</c> or <c>post</c>.</summary>
    public sealed record RoleHere(string Word, string Name, string Access);
}
