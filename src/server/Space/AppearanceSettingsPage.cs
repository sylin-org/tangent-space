using Tangent.Render;

namespace Tangent.Space;

/// <summary>The place's appearance, one resource both ways: the <see cref="Sections"/> of the settings, appearance marked
/// current; its <see cref="Base"/>, one of <see cref="Bases"/>; its colours, its <see cref="Palette"/>, one of
/// <see cref="Palettes"/>; its own <see cref="Css"/>; its <see cref="Image"/>, empty, a cover's name or <c>/media/{id}</c>, at
/// <see cref="ImageAddress"/>, with the <see cref="Covers"/> Tangent bundles; and its words in Markdown: its
/// <see cref="Description"/>, its <see cref="Intro"/> under the door's head, the <see cref="Masthead"/> and <see cref="Footer"/>
/// lines and the <see cref="ArticleHeader"/>. <see cref="Change"/>, present only where the reader may change them, takes each
/// of them; <see cref="Preview"/>, beside it, takes the same and answers the door drawn in them, saving nothing; and
/// <see cref="Upload"/> takes an image of the reader's own as the place's. <see cref="Can"/> is what the reader holds in the
/// place, and <see cref="Gate"/> why they may not change them, null when they may; <see cref="SignIn"/> is where a visitor
/// signs in, coming back here. After a refusal the form says why above the choices.</summary>
public sealed record AppearanceSettingsPage(
    IReadOnlyList<SettingsSection> Sections,
    string Base,
    IReadOnlyList<string> Bases,
    string Palette,
    IReadOnlyList<string> Palettes,
    string Css,
    string Image,
    string? ImageAddress,
    IReadOnlyList<AppearanceSettingsPage.Cover> Covers,
    string Description,
    string Intro,
    string Masthead,
    string Footer,
    string ArticleHeader,
    Form? Change,
    string? Preview,
    string? Upload,
    IReadOnlyList<string> Can,
    string? Gate,
    string? SignIn,
    string Place,
    string Door,
    Who? Who,
    string? Failure = null)
{
    /// <summary>A bundled cover a place may wear: its name, as <c>image</c> takes it, its title and its address.</summary>
    public sealed record Cover(string Name, string Title, string Address);
}
