using Tangent.Render;

namespace Tangent.Space;

/// <summary>The place's admission, one resource both ways: the <see cref="Sections"/> of the settings, admission marked
/// current, and how a newcomer arrives, <see cref="Admission"/>, one of <see cref="Admissions"/>: <c>join</c>, admitted
/// and a member at once, or <c>apply</c>, waiting to be admitted. <see cref="Change"/>, present only where the reader may
/// change it, takes <c>admission</c>. <see cref="Can"/> is what the reader holds in the place, and
/// <see cref="Gate"/> why they may not change it, null when they may; <see cref="SignIn"/> is where a visitor signs in,
/// coming back here. After a refusal the form says why above the choices.</summary>
[Page(Layout.Settings)]
public sealed record AdmissionSettingsPage(
    IReadOnlyList<SettingsSection> Sections,
    string Admission,
    IReadOnlyList<string> Admissions,
    Form? Change,
    IReadOnlyList<string> Can,
    string? Gate,
    string? SignIn,
    string Place,
    string Door,
    Who? Who,
    string? Failure = null)
{
    [Slot(Slot.Rail), Newtonsoft.Json.JsonIgnore]
    public SettingsRail Rail => new(Sections);

    [Slot(Slot.Column), Newtonsoft.Json.JsonIgnore]
    public PanelTitle Heading => new(TangentSpaceSettingsController.AdmissionSection, "Settings of", Place, Door);

    [Slot(Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Notice? Line => Notice.Of(Gate, Failure, SignIn);

    /// <summary>The form as the page draws it: the reader's to send, or read only where it is not.</summary>
    [Slot(Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Form Shown => (Change ?? (TangentSpaceSettingsController.AdmissionForm(Door.TrimEnd('/')) with { ReadOnly = true }))
        .With(TangentSpaceSettingsController.AdmissionField, Admission);

    /// <summary>The frame the shell draws around the page: its settings as its one crumb, and who reads, or where a visitor
    /// signs in.</summary>
    public static Frame Frame(AdmissionSettingsPage page) => new(page.Place, page.Door, [new Crumb("Settings")], page.Who, page.SignIn);
}
