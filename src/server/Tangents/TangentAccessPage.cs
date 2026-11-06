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
[Render.Page(Render.Layout.Panel)]
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
    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.PanelTitle Heading => new("Access", "Settings of " + Render.Sentence.Link(Name, Address));

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Notice? Line => Render.Notice.Of(Gate, Failure, SignIn);

    /// <summary>Who reads the Tangent, by its audience, and where its settings change it.</summary>
    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Sentence? Reach => Gate is not null ? null : new((Fixed
        ? "Only moderators read this Tangent, and its access stays as it is."
        : Audience == "private"
            ? "It is private: administrators read it, and so do the roles given read or post below."
            : "It is public: everyone who reads the place reads it, and each role posts as it does in the place unless it is given read alone.")
        + (Fixed ? "" : " " + Render.Sentence.Link("Its settings", Settings) + " change who reads it."));

    /// <summary>The roles in their order, each with what it holds here and, for a reader who may change it, each other access
    /// as a button of its own.</summary>
    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.EntryList? Listing => Gate is not null ? null : new(Roles.Select(role => new Render.Entry(Render.Sentence.Plain(role.Name),
        Change is { } change
            ? role.Offered.Select(access => (Render.IPart)(change.Given(TangentAccessController.RoleField, role.Word).Given(TangentAccessController.AccessField, access)
                with { Tool = "change-tangent-access", Submit = access switch { "read" => "Reads only", "post" => "Reads and posts", _ => "As in the place" }, Quiet = true })).ToList()
            : null)
    {
        Detail = role.Access switch { "read" => "reads here, and posts nothing", "post" => "reads and posts here", _ => "as its role allows in the place" }
    }).ToList());

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Sentence? Administrators => Gate is not null ? null : new("Administrators read and post in every Tangent.");

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Pager? Pages => Gate is not null ? null : new("More roles",
        Previous is { } previous ? new Render.Onward("Higher roles", previous) : null, Next is { } next ? new Render.Onward("Lower roles", next) : null);

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Onward? Back => Gate is not null ? null : new("Back to the settings", Settings) { Back = true };

    /// <summary>The frame the shell draws around the page: its settings and its access as its own crumbs past the Tangent, and
    /// who reads, or where a visitor signs in.</summary>
    public static Render.Frame Frame(TangentAccessPage page)
        => new(page.Place, page.Door, [new Render.Crumb("Settings", page.Settings), new Render.Crumb("Access")], page.Who, page.SignIn);


    /// <summary>One role as the access lists it: its word, its name, what it holds here, and the access the reader may set
    /// it to instead, none where they do not manage it.</summary>
    public sealed record Listed(string Word, string Name, string Access, IReadOnlyList<string> Offered);
}
