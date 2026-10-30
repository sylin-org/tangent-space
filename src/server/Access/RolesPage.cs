using Tangent.Render;
using Tangent.Space;

namespace Tangent.Access;

/// <summary>The place's roles, one resource both ways: the <see cref="Sections"/> of the settings, roles marked current,
/// and one page of <see cref="Roles"/> in Koan's order, the highest first and everyone last, each with what the reader may
/// do to it; <see cref="Next"/> and <see cref="Previous"/> are the addresses of the pages beside this one, null at
/// either end. <see cref="Permissions"/> are what a role may grant, each in plain words and whether the reader holds it,
/// since a reader gives or takes away only what they hold. <see cref="Forms"/> are the forms that create, change and
/// delete a role, present only where the reader holds <c>ManageRoles</c>. <see cref="Can"/> is what the reader holds in
/// the place, and <see cref="Gate"/> why they may not shape its roles, null when they may, when the list is empty;
/// <see cref="SignIn"/> is where a visitor signs in, coming back here. After a refusal the page says why above the list
/// and keeps the new role's <see cref="Name"/> and <see cref="Chosen"/> permissions as they were sent.</summary>
public sealed record RolesPage(
    IReadOnlyList<SettingsSection> Sections,
    IReadOnlyList<RolesPage.Listed> Roles,
    IReadOnlyList<RolesPage.Permission> Permissions,
    string? Next,
    string? Previous,
    RoleForms? Forms,
    IReadOnlyList<string> Can,
    string? Gate,
    string? SignIn,
    string Place,
    string Door,
    Who? Who,
    string? Failure = null,
    string Name = "",
    IReadOnlyList<string>? Chosen = null)
{
    /// <summary>What a reader may do to a role, as a role's <c>can</c> lists it: change its name and what it grants, move
    /// it one place higher or lower, and delete it.</summary>
    public static class Verb
    {
        public const string Change = "change";
        public const string Higher = "higher";
        public const string Lower = "lower";
        public const string Delete = "delete";
    }

    /// <summary>One role as the reader finds it: its word, which the forms send and never changes, its name, what it grants
    /// by the bits' names, whether it is one every place has, and what the reader may do to it (<see cref="Verb"/>).</summary>
    public sealed record Listed(string Word, string Name, IReadOnlyList<string> Permissions, bool Seeded, IReadOnlyList<string> Can);

    /// <summary>One permission a role may grant: the bit's name, what it lets the role's members do, and whether the
    /// reader holds it, so may give it to a role or take it away.</summary>
    public sealed record Permission(string Bit, string Words, bool Held);
}

/// <summary>The forms that shape the place's roles: <see cref="Create"/> takes <c>name</c> and <c>permissions</c>;
/// <see cref="Change"/> takes the role's word in <c>role</c> and any of <c>name</c>, <c>permissions</c> and <c>move</c>
/// (<c>higher</c> or <c>lower</c>), each left as it is when not sent; <see cref="Delete"/> takes <c>role</c>.
/// <c>permissions</c> names bits separated by spaces, as a role's <c>permissions</c> lists them, or is sent once for each
/// bit; sent empty, it grants nothing.</summary>
public sealed record RoleForms(Form Create, Form Change, Form Delete);
