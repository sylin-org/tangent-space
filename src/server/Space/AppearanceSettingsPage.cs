using Tangent.Render;

namespace Tangent.Space;

/// <summary>The place's appearance, one resource both ways: the <see cref="Sections"/> of the settings, appearance marked
/// current; its layers as the place fills them — its <see cref="Base"/>, one of <see cref="Bases"/>, its <see cref="Palette"/>,
/// one of <see cref="Palettes"/>, with its <see cref="Accent"/>, one of <see cref="Accents"/>, and its own <see cref="Css"/> — and
/// its <see cref="Slots"/>. <see cref="Change"/>, present only where the reader may change them, takes <c>base</c>,
/// <c>palette</c>, <c>accent</c>, <c>css</c> and each slot; <see cref="Preview"/>, beside it, takes the same and answers the door
/// drawn in them, saving nothing. <see cref="Can"/> is what the reader holds in the place, and <see cref="Gate"/> why
/// they may not change them, null when they may; <see cref="SignIn"/> is where a visitor signs in, coming back here. After a
/// refusal the form says why above the choices.</summary>
public sealed record AppearanceSettingsPage(
    IReadOnlyList<SettingsSection> Sections,
    string Base,
    IReadOnlyList<string> Bases,
    string Palette,
    IReadOnlyList<string> Palettes,
    string Accent,
    IReadOnlyList<string> Accents,
    string Css,
    Slots Slots,
    Form? Change,
    string? Preview,
    IReadOnlyList<string> Can,
    string? Gate,
    string? SignIn,
    string Place,
    string Door,
    Who? Who,
    string? Failure = null);
