using System.Globalization;
using Tangent.Render;

namespace Tangent.Posts;

/// <summary>Where whoever moderates a Topic's Tangent moves it, one resource both ways: the Topic, by its title and address, how
/// many replies go with it, and <see cref="Move"/>, the form whose <c>to</c> offers each Tangent they moderate, but this one and
/// the moderators', as its kind and its audience, and where the Topic is an article whose copy is out, that the copy comes down
/// in a private one; null where they moderate no other. After a refusal the form says why.</summary>
[Render.Page(Render.Layout.Panel)]
public sealed record MoveTopicPage(
    string Id,
    string Title,
    string Address,
    int ReplyCount,
    Form? Move,
    string Place,
    string Door,
    Who? Who)
{
    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.PanelTitle Heading => new("Move this topic", Render.Sentence.Link(Title, Address));

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Sentence Goes => new(ReplyCount switch
    {
        0 => "Its address here forwards to the new one.",
        1 => "Its reply goes with it, and its address here forwards to the new one.",
        _ => "Its " + ReplyCount.ToString(CultureInfo.InvariantCulture) + " replies go with it, and its address here forwards to the new one."
    });

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Sentence? Nowhere => Move is null ? new("You moderate no other Tangent to move it to.") : null;

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Onward? Back => Move is null ? new("Back to the topic", Address) { Back = true } : null;

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Form? Moving => Move;

    /// <summary>The frame the shell draws around the page: the move as its own crumb past the Topic's Tangent and the Topic,
    /// and who reads.</summary>
    public static Render.Frame Frame(MoveTopicPage page) => new(page.Place, page.Door, [new Render.Crumb("Move")], page.Who, null);
}
