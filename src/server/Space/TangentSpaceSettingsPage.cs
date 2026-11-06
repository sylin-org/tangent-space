using Tangent.Render;

namespace Tangent.Space;

/// <summary>The place's settings, one resource both ways: the <see cref="Sections"/> of the settings, the one this page
/// shows marked current, and that section, general: the place's <see cref="Name"/> and its <see cref="Audience"/>, one of
/// <see cref="Audiences"/>: <c>public</c>, read by anyone, or <c>private</c>, read by its participants alone.
/// <see cref="Change"/>, present only where the reader may change them, takes <c>name</c> and <c>audience</c>, each left as
/// it is when not sent.
/// <see cref="Can"/> is what the reader holds in
/// the place, and <see cref="Gate"/> why they may not change its settings, null when they may; <see cref="SignIn"/> is
/// where a visitor signs in, coming back here. After a refusal the form keeps what was sent and says why: under the
/// name, or above the form.</summary>
[Page(Layout.Settings)]
public sealed record TangentSpaceSettingsPage(
    IReadOnlyList<SettingsSection> Sections,
    string Name,
    string Audience,
    IReadOnlyList<string> Audiences,
    Form? Change,
    IReadOnlyList<string> Can,
    string? Gate,
    string? SignIn,
    string Place,
    string Door,
    Who? Who,
    string? Failure = null,
    string? NameFailure = null)
{
    [Slot(Slot.Rail), Newtonsoft.Json.JsonIgnore]
    public SettingsRail Rail => new(Sections);

    [Slot(Slot.Column), Newtonsoft.Json.JsonIgnore]
    public PanelTitle Heading => new(Sections.FirstOrDefault(section => section.Current)?.Name ?? TangentSpaceSettingsController.General, "Settings of", Place, Door);

    [Slot(Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Notice? Line => Notice.Of(Gate, Failure, SignIn);

    /// <summary>The form as the page draws it: the reader's to send, or read only where it is not.</summary>
    [Slot(Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Form Shown
    {
        get
        {
            var form = Change ?? (TangentSpaceSettingsController.GeneralForm(Door.TrimEnd('/')) with { ReadOnly = true });
            return form with
            {
                Fields = form.Fields.Select(each => each.Name == TangentSpaceSettingsController.NameField ? each with { Value = Name, Failure = NameFailure }
                    : each.Name == TangentSpaceSettingsController.AudienceField ? each with { Value = Audience }
                    : each).ToList()
            };
        }
    }

    /// <summary>The frame the shell draws around the page: its settings as its one crumb, and who reads, or where a visitor
    /// signs in.</summary>
    public static Frame Frame(TangentSpaceSettingsPage page) => new(page.Place, page.Door, [new Crumb("Settings")], page.Who, page.SignIn);
}

/// <summary>One section of the place's settings: its name, its address, and whether it is the one the page shows.</summary>
public sealed record SettingsSection(string Name, string Address, bool Current);

/// <summary>The place as a write of its settings leaves it, for an agent: its name and description, its admission
/// (<c>join</c> or <c>apply</c>), its audience (<c>public</c> or <c>private</c>), its base, its palette, its own CSS, its image,
/// its words in Markdown, its door, its settings, and what the sender <see cref="Can"/> do in it.</summary>
public sealed record WrittenTangentSpace(string Name, string Description, string Admission, string Audience, string Base, string Palette,
    string Css, string Image, string Intro, string Masthead, string Footer, string ArticleHeader, string Door, string Settings, IReadOnlyList<string> Can);
