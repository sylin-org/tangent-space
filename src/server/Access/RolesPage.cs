using Tangent.Render;
using Tangent.Space;

namespace Tangent.Access;

/// <summary>The place's roles, one resource both ways: the <see cref="Sections"/> of the settings, roles marked current,
/// and one page of <see cref="Roles"/> in Koan's order, the highest first and everyone last, each with what the reader may
/// do to it; <see cref="Next"/> and <see cref="Previous"/> are the addresses of the pages beside this one, null at
/// either end. <see cref="Permissions"/> are what a role may grant, each in plain words and whether the reader holds it,
/// since a reader gives or takes away only what they hold. Only whoever holds <c>ManageRoles</c> reads it.
/// <see cref="Forms"/> are the forms that create, change and delete a role. <see cref="Can"/> is what the reader holds in
/// the place. After a refusal the page says why above the list and keeps the new role's <see cref="Name"/> and
/// <see cref="Chosen"/> permissions as they were sent.</summary>
[Render.Page(Render.Layout.Settings)]
public sealed record RolesPage(
    IReadOnlyList<SettingsSection> Sections,
    IReadOnlyList<RolesPage.Listed> Roles,
    IReadOnlyList<RolesPage.Permission> Permissions,
    string? Next,
    string? Previous,
    RoleForms Forms,
    IReadOnlyList<string> Can,
    string Place,
    string Door,
    Who? Who,
    string? Failure = null,
    string Name = "",
    IReadOnlyList<string>? Chosen = null)
{
    [Render.Slot(Render.Slot.Rail), Newtonsoft.Json.JsonIgnore]
    public Render.SettingsRail Rail => new(Sections);

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.PanelTitle Heading => new(TangentSpaceSettingsController.RolesSection, "Settings of " + Render.Sentence.Link(Place, Door));

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Notice? Line => Render.Notice.Of(null, Failure, null);

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Sentence Order => new("Roles stand one above another, the highest first. You shape the roles below your own highest, and change the standing and roles of someone whose highest role is below yours.");

    /// <summary>A role's permissions as checks, the ones it grants checked, one the reader does not hold locked.</summary>
    private IReadOnlyList<Render.Form.Choice> Grantable => Permissions.Select(permission => new Render.Form.Choice(permission.Bit, permission.Words) { Locked = !permission.Held }).ToList();

    /// <summary>A new role, which opens in place, opening already where a refused one comes back.</summary>
    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Form Creating => Forms.Create
        .Changing(RolesController.NameField, item => item with { Value = Name })
        .Changing(RolesController.PermissionsField, item => item with { Choices = Grantable, Values = Chosen ?? [] })
        with { Opens = "New role", Open = Chosen is not null, Note = "It stands directly above the member role until you move it.", Key = "role-new" };

    /// <summary>The roles in their order, each with what it grants and what the reader may do to it inline: see who holds it,
    /// move it one place, change its name and what it grants, opening in place, and delete it, confirmed in the page.</summary>
    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.EntryList Listing => new(Roles.Select(role => new Render.Entry(Render.Sentence.Plain(role.Name), Answers(role))
    {
        Detail = Render.Sentence.Plain(Grants(role))
    }).ToList());

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Pager Pages => new("More roles",
        Previous is { } previous ? new Render.Onward("Higher roles", previous) : null, Next is { } next ? new Render.Onward("Lower roles", next) : null);

    private IReadOnlyList<Render.IPart>? Answers(Listed role)
    {
        var answers = new List<Render.IPart>();
        if (role.People is { } people) answers.Add(new Render.LinkButton("Who holds it", people));
        if (role.Can.Count == 0) return answers.Count == 0 ? null : answers;
        var change = Forms.Change.Given(RolesController.RoleField, role.Word);
        if (role.Can.Contains(Verb.Higher))
            answers.Add(change.Keeping(RolesController.RoleField).Given(RolesController.MoveField, RolesController.Word(RoleMove.Higher)) with { Submit = "Move higher", Quiet = true });
        if (role.Can.Contains(Verb.Lower))
            answers.Add(change.Keeping(RolesController.RoleField).Given(RolesController.MoveField, RolesController.Word(RoleMove.Lower)) with { Submit = "Move lower", Quiet = true });
        if (role.Can.Contains(Verb.Change))
            answers.Add(change.Keeping(RolesController.RoleField, RolesController.NameField, RolesController.PermissionsField)
                .Changing(RolesController.NameField, item => item with { Value = role.Name })
                .Changing(RolesController.PermissionsField, item => item with { Choices = Grantable, Values = role.Permissions })
                with { Opens = "Change", Submit = "Save", Quiet = true, Key = "role-" + role.Word });
        if (role.Can.Contains(Verb.Delete))
            answers.Add(Forms.Delete.Given(RolesController.RoleField, role.Word) with
            {
                Opens = "Delete", Submit = "Delete", Confirm = "Everyone in it loses it, and so does every Tangent’s access for it."
            });
        return answers;
    }

    /// <summary>What a role grants, in plain words.</summary>
    private string Grants(Listed role)
    {
        if (role.Word == Access.Roles.Everyone) return "every reader; the place’s audience says what they read";
        if (role.Permissions.Count == 0) return "grants nothing of its own";
        var words = Permissions.ToDictionary(permission => permission.Bit, permission => permission.Words, StringComparer.Ordinal);
        return string.Join(", ", role.Permissions.Select(bit => words.GetValueOrDefault(bit, bit)));
    }

    /// <summary>The frame the shell draws around the page: its settings as its one crumb, and who reads.</summary>
    public static Render.Frame Frame(RolesPage page) => new(page.Place, page.Door, [new Render.Crumb("Settings")], page.Who, null);


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
    /// by the bits' names, whether it is one every place has, what the reader may do to it (<see cref="Verb"/>), and the
    /// address of the people who hold it, null for everyone, which no one holds as a member.</summary>
    public sealed record Listed(string Word, string Name, IReadOnlyList<string> Permissions, bool Seeded, IReadOnlyList<string> Can, string? People);

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
