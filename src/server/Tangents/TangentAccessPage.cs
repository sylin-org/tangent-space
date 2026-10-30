using Tangent.Render;

namespace Tangent.Tangents;

/// <summary>A Tangent's access, one resource both ways: its <see cref="Name"/>, its <see cref="Audience"/>
/// (<c>public</c> or <c>private</c>), which its settings at <see cref="Settings"/> change, and one page of the
/// <see cref="Roles"/> in Koan's order, the highest first, each with what it holds here beyond the place: <c>default</c>,
/// <c>read</c> or <c>post</c> (<see cref="RoleAccess"/>), and the others the reader may set it to; everyone, whose access
/// is the audience, and the roles that administer, which read and post everywhere, are not listed. <see cref="Next"/> and
/// <see cref="Previous"/> are the pages beside this one, null at either end. <see cref="Fixed"/> says this is the
/// moderators' Tangent, whose access stays as it is. <see cref="Change"/>, present only where the reader may manage the
/// Tangent, takes <c>role</c> and <c>access</c>. <see cref="Can"/> is what the reader holds on the Tangent, and
/// <see cref="Gate"/> why they may not change its access, null when they may, when no role is listed; <see cref="SignIn"/> is where a visitor
/// signs in, coming back here. After a refusal the page says why above the list.</summary>
public sealed record TangentAccessPage(
    string Slug,
    string Name,
    string Address,
    string Settings,
    string Audience,
    bool Fixed,
    IReadOnlyList<TangentAccessPage.Listed> Roles,
    string? Next,
    string? Previous,
    Form? Change,
    IReadOnlyList<string> Can,
    string? Gate,
    string? SignIn,
    string Place,
    string Door,
    Who? Who,
    string? Failure = null)
{
    /// <summary>One role as the access lists it: its word, its name, what it holds here, and the access the reader may set
    /// it to instead, none where they do not manage it.</summary>
    public sealed record Listed(string Word, string Name, string Access, IReadOnlyList<string> Offered);
}
