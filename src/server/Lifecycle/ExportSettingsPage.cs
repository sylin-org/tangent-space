using Tangent.Render;
using Tangent.Space;

namespace Tangent.Lifecycle;

/// <summary>Taking the place along, one resource both ways: the <see cref="Sections"/> of the settings, export marked
/// current; <see cref="Whole"/>, where the whole Tangent Space downloads, present only where the reader may export; and a
/// page of <see cref="Tangents"/> they may view, each with where it downloads, <see cref="Next"/> the address of the page
/// after, null on the last, and <see cref="First"/> the first page's, for a page that <see cref="Continued"/> from a cursor.
/// <see cref="Can"/> is what the reader holds in the place, and <see cref="Gate"/> why they may not export, null when they
/// may; <see cref="SignIn"/> is where a visitor signs in, coming back here.</summary>
public sealed record ExportSettingsPage(
    IReadOnlyList<SettingsSection> Sections,
    string? Whole,
    IReadOnlyList<ExportSettingsPage.Download> Tangents,
    string? Next,
    bool Continued,
    string First,
    IReadOnlyList<string> Can,
    string? Gate,
    string? SignIn,
    string Place,
    string Door,
    Who? Who)
{
    /// <summary>A Tangent the reader may take: its slug, name, kind, how many Topics were started there, and where its zip
    /// is.</summary>
    public sealed record Download(string Slug, string Name, string Kind, int TopicCount, string Address);
}
