using Tangent.Render;

namespace Tangent.Tangents;

/// <summary>The form that creates a Tangent, one resource both ways. It asks for a name; everything else stands at its
/// default until the sender names another: an empty <see cref="Description"/>, the default kind (<see cref="Kind"/>) <c>topic</c>
/// (discussions) and the <see cref="Audience"/> <c>public</c>, where everyone who reads the place reads it and members
/// post. <see cref="Create"/>, present only where the reader may create a Tangent, takes <c>name</c>,
/// <c>description</c>, <c>kind</c>, its default kind (one of <see cref="Kinds"/>) and <c>audience</c> (one of <see cref="Audiences"/>); the
/// slug derives from the name and never changes, and the Tangent comes after the existing ones. <see cref="Can"/> is what the
/// reader holds in the place, and <see cref="Gate"/> why they may not create a Tangent, null when they may;
/// <see cref="SignIn"/> is where a visitor signs in, coming back here. After a refusal the form keeps what was sent and
/// says why: under the name, under the description, or above the form.</summary>
[Render.Page(Render.Layout.Panel)]
public sealed record NewTangentPage(
    Form? Create,
    IReadOnlyList<string> Can,
    string? Gate,
    string Name,
    string Description,
    string Kind,
    string Audience,
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
    public Render.PanelTitle Heading => new("New Tangent", "In " + Render.Sentence.Link(Place, Door));

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Notice? Line => Render.Notice.Of(Gate, null, SignIn);

    /// <summary>The form as the page draws it: the reader's to send, or read only where it is not, with what was sent and why
    /// it was refused.</summary>
    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Form Shown => (Create ?? (TangentWrites.CreateForm("") with { ReadOnly = true }))
        .Changing(TangentWrites.NameField, item => item with { Value = Name, Failure = NameFailure, Focus = Gate is null })
        .Changing(TangentWrites.DescriptionField, item => item with { Value = Description, Failure = DescriptionFailure })
        .With(TangentWrites.KindField, Kind)
        .With(TangentWrites.AudienceField, Audience)
        with { Failure = Failure, Key = "new-tangent", By = Who is { } who ? new Render.Signed("Created by", "@" + who.Handle) : null };

    /// <summary>The frame the shell draws around the page: the form as its one crumb, and who reads, or where a visitor signs
    /// in.</summary>
    public static Render.Frame Frame(NewTangentPage page) => new(page.Place, page.Door, [new Render.Crumb("New Tangent")], page.Who, page.SignIn);
}
