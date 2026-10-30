using Tangent.Render;

namespace Tangent.Space;

/// <summary>The place's settings, one resource both ways: the <see cref="Sections"/> of the settings, the one this page
/// shows marked current, and that section, general: the place's <see cref="Name"/> and its <see cref="Description"/>,
/// the line under the name on the door. <see cref="Action"/> takes <c>name</c> and <c>description</c>, each left as it
/// is when not sent; <see cref="Token"/> is the form's antiforgery token. <see cref="Can"/> is what the reader holds in
/// the place, and <see cref="Gate"/> why they may not change its settings, null when they may; <see cref="SignIn"/> is
/// where a visitor signs in, coming back here. After a refusal the form keeps what was sent and says why: under the
/// name, under the description, or above the form.</summary>
public sealed record TangentSpaceSettingsPage(
    IReadOnlyList<SettingsSection> Sections,
    string Name,
    string Description,
    string Action,
    FormToken Token,
    IReadOnlyList<string> Can,
    string? Gate,
    string? SignIn,
    string Place,
    string Door,
    Who? Who,
    string? Failure = null,
    string? NameFailure = null,
    string? DescriptionFailure = null);

/// <summary>One section of the place's settings: its name, its address, and whether it is the one the page shows.</summary>
public sealed record SettingsSection(string Name, string Address, bool Current);

/// <summary>The place as a write of its settings leaves it, for an agent: its name and description, its door, its
/// settings, and what the writer <see cref="Can"/> do in it.</summary>
public sealed record WrittenTangentSpace(string Name, string Description, string Door, string Settings, IReadOnlyList<string> Can);
