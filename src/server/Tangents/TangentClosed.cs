using Tangent.Render;

namespace Tangent.Tangents;

/// <summary>What the sender of a Tangent's settings reads when their own change made the Tangent private to them: it is
/// saved, the Tangent named <see cref="Name"/> no longer shows for them, and the <see cref="Door"/> of the
/// <see cref="Place"/> is where they go on.</summary>
[Page(Layout.Panel)]
public sealed record TangentClosed(string Name, string Place, string Door, Who? Who)
{
    [Slot(Slot.Column), Newtonsoft.Json.JsonIgnore]
    public PanelTitle Heading => new("Saved", Lede: Saved);

    [Slot(Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Onward Onward => new("Go to the door", Door);

    /// <summary>The frame the shell draws around the page: the saved change as its one crumb, and who reads.</summary>
    public static Frame Frame(TangentClosed page) => new(page.Place, page.Door, [new Crumb("Saved")], page.Who);

    /// <summary>What the sender reads: it is saved, and no longer shows for them.</summary>
    public string Saved => Name + " is private now, and no longer shows for you.";
}
