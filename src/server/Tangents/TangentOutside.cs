using Tangent.Render;

namespace Tangent.Tangents;

/// <summary>A members-only Tangent as a participant outside it reads it at its own address, where they may join or ask to join
/// it (<see cref="Inside.Outside"/>): its card, as the list of Tangents shows it (<see cref="TangentsController.Outside"/>), which
/// says what it is about and holds the way in — join, ask, or where their ask stands, with the way to withdraw a waiting one — and
/// nothing it holds. <see cref="Place"/>, <see cref="Door"/> and <see cref="Who"/> say where the page is and who reads it.</summary>
[Page(Layout.Panel)]
public sealed record TangentOutside(Render.TangentEntry Tangent, string Place, string Door, Who? Who)
{
    [Slot(Slot.Column), Newtonsoft.Json.JsonIgnore]
    public PanelTitle Heading => new("Members only", "In " + Render.Sentence.Link(Place, Door));

    [Slot(Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.TangentEntry Card => Tangent;

    /// <summary>The frame the shell draws around the page: the Tangent its address names as its crumb, and who reads.</summary>
    public static Frame Frame(TangentOutside page) => new(page.Place, page.Door, [], page.Who);
}
