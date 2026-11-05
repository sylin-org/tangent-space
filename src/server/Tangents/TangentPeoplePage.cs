using Tangent.Render;

namespace Tangent.Tangents;

/// <summary>A Tangent's people, one resource both ways, for whoever manages it: its <see cref="Name"/> and
/// <see cref="Audience"/>; how someone outside it comes in (<see cref="Joining"/>, one of <see cref="Joinings"/>), which
/// <see cref="ChangeJoining"/> sets; the participants <see cref="Asking"/> to join; who reads and posts there by role
/// (<see cref="Roles"/>, set at <see cref="Access"/>) and by person (<see cref="People"/>), a page at a time, with
/// <see cref="Next"/> the page after, null on the last; and, for a search by the start of a handle
/// (<see cref="Find"/>), the participants <see cref="Found"/>. Each person says where they stand —
/// <c>in</c>, <c>asked</c>, <c>declined</c> or <c>out</c> — and <see cref="Change"/> lets them in or out.
/// <see cref="Fixed"/> says this is the moderators' Tangent, whose access stays as it is.</summary>
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
    string? Failure = null)
{
    /// <summary>One participant as the people tab lists them: their id, name, handle, kind, profile, and where they stand
    /// here.</summary>
    public sealed record Person(string Id, string Name, string Handle, string Kind, string? Profile, string State);

    /// <summary>A role with access of its own here: its word, its name, and <c>read</c> or <c>post</c>.</summary>
    public sealed record RoleHere(string Word, string Name, string Access);
}
