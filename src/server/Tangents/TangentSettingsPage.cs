using Tangent.Render;

namespace Tangent.Tangents;

/// <summary>A Tangent's settings, one resource both ways: its <see cref="Slug"/>, which never changes, and what may:
/// its <see cref="Name"/>, <see cref="Description"/>, its <see cref="Accent"/>, empty for the <see cref="Tone"/> its slug gives
/// it, its <see cref="Image"/>, empty or one of the <see cref="Covers"/> or an image brought in here, default <see cref="Kind"/> (one of <see cref="Kinds"/>), its
/// <see cref="Audience"/> (one of <see cref="Audiences"/>, which is empty where the audience is fixed) and, for a
/// reader who <see cref="Movable"/> it — who holds <c>ManageSpace</c>, since a move arranges the place's list — its place
/// among the Tangents, between <see cref="Before"/> and <see cref="After"/>, the names of the nearest Tangents that
/// reader may view, null at either end and for every other reader. <see cref="Change"/>, present only where the reader
/// may change the Tangent, takes <c>name</c>, <c>description</c>, <c>accent</c>, <c>image</c>, <c>kind</c>, <c>audience</c> and <c>move</c>
/// (<c>earlier</c> or <c>later</c>), each left as it is when not sent. <see cref="Fixed"/> says this is the moderators'
/// Tangent, whose audience and place stay as they are. <see cref="Access"/> is where the roles' access to it is set
/// (<see cref="TangentAccessPage"/>). <see cref="Can"/> is what the reader holds on the Tangent, and
/// <see cref="Gate"/> why they may not change it, null when they may; <see cref="SignIn"/> is where a visitor signs in,
/// coming back here. After a refusal the form keeps what was sent and says why: under the name, under the description,
/// or above the form.</summary>
[Render.Page(Render.Layout.Panel)]
public sealed record TangentSettingsPage(
    string Slug,
    string Name,
    string Description,
    string Accent,
    string Tone,
    string Image,
    IReadOnlyList<Space.AppearanceSettingsPage.Cover> Covers,
    string Kind,
    string Audience,
    string? Before,
    string? After,
    bool Fixed,
    bool Movable,
    string Address,
    string Access,
    Form? Change,
    IReadOnlyList<string> Can,
    string? Gate,
    IReadOnlyList<string> Kinds,
    IReadOnlyList<string> Audiences,
    string? SignIn,
    string Place,
    string Door,
    Who? Who,
    string? Failure = null,
    string? NameFailure = null,
    string? DescriptionFailure = null)
{
    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.TangentSettingsForm Body => new(this);
}
