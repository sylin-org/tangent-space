using Tangent.Render;

namespace Tangent.Space;

/// <summary>The place's admission, one resource both ways: the <see cref="Sections"/> of the settings, admission marked
/// current, and how a newcomer arrives, <see cref="Admission"/>, one of <see cref="Admissions"/>: <c>join</c>, admitted
/// and a member at once, or <c>apply</c>, waiting to be admitted. <see cref="Action"/> takes <c>admission</c>;
/// <see cref="Token"/> is the form's antiforgery token. <see cref="Can"/> is what the reader holds in the place, and
/// <see cref="Gate"/> why they may not change it, null when they may; <see cref="SignIn"/> is where a visitor signs in,
/// coming back here. After a refusal the form says why above the choices.</summary>
public sealed record AdmissionSettingsPage(
    IReadOnlyList<SettingsSection> Sections,
    string Admission,
    IReadOnlyList<string> Admissions,
    string Action,
    FormToken Token,
    IReadOnlyList<string> Can,
    string? Gate,
    string? SignIn,
    string Place,
    string Door,
    Who? Who,
    string? Failure = null);
