using Tangent.Render;

namespace Tangent.Tangents;

/// <summary>The Tangents the reader may view, one resource both ways: a page of <see cref="Tangents"/> in the Tangents'
/// order, each with its kind and the Topics started there, <see cref="Next"/>, the address of the page after, null on the
/// last, and <see cref="First"/>, the first page's, for a page that <see cref="Continued"/> from a cursor. <see cref="Can"/>
/// is what the reader holds in the place; <see cref="NewTangent"/> is where a Tangent is created, for whoever may;
/// <see cref="SignIn"/> is where a visitor signs in, coming back here.</summary>
[Page(Layout.Panel)]
public sealed record TangentsPage(
    IReadOnlyList<Render.TangentEntry> Tangents,
    string? Next,
    bool Continued,
    string First,
    IReadOnlyList<string> Can,
    string? SignIn,
    string Place,
    string Door,
    Who? Who,
    string? NewTangent)
{
    [Slot(Slot.Column), Newtonsoft.Json.JsonIgnore]
    public PanelTitle Heading => new("Tangents", "In " + Render.Sentence.Link(Place, Door));

    [Slot(Slot.Column), Newtonsoft.Json.JsonIgnore]
    public TangentDirectory Directory => new(Tangents, Continued, Next, First, NewTangent);

    /// <summary>The frame the shell draws around the page: its Tangents as its one crumb, and who reads, or where a visitor
    /// signs in.</summary>
    public static Frame Frame(TangentsPage page) => new(page.Place, page.Door, [new Crumb("Tangents")], page.Who, page.SignIn);
}
