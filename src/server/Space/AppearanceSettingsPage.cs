using Tangent.Render;

namespace Tangent.Space;

/// <summary>The place's appearance, one resource both ways: the <see cref="Sections"/> of the settings, appearance marked
/// current, and its <see cref="Accent"/>, one of <see cref="Accents"/>. <see cref="Change"/>, present only where the reader
/// may change it, takes <c>accent</c>. <see cref="Can"/> is what the reader holds in the place, and <see cref="Gate"/> why
/// they may not change it, null when they may; <see cref="SignIn"/> is where a visitor signs in, coming back here. After a
/// refusal the form says why above the choices.</summary>
public sealed record AppearanceSettingsPage(
    IReadOnlyList<SettingsSection> Sections,
    string Accent,
    IReadOnlyList<string> Accents,
    Form? Change,
    IReadOnlyList<string> Can,
    string? Gate,
    string? SignIn,
    string Place,
    string Door,
    Who? Who,
    string? Failure = null);
