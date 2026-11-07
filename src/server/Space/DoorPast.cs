using Tangent.Render;

namespace Tangent.Space;

/// <summary>The door paged back, at its own address with <c>before</c>, for a signed-in reader: what reached them before that
/// sequence of the log and is no longer new to them, newest first (<see cref="Events.Unread.Earlier"/>), in
/// <see cref="Older"/>, with where the page before it begins, and the way back to what is new; the Tangents they may view in
/// the rail, as the door lists them. It holds nothing live: a page of the past stays as it is while the log moves.
/// <see cref="Name"/>, <see cref="Address"/> and <see cref="Who"/> say where the page is and who reads it; <see cref="Day"/>
/// is the UTC day its times are written for. <c>api</c> is the stability promise an agent reads.</summary>
[Page(Layout.Home)]
public sealed record DoorPast(
    string Name,
    string Address,
    IReadOnlyList<TangentEntry> Tangents,
    string? AllTangents,
    string? NewTangent,
    Who? Who,
    DateOnly Day,
    DoorPast.Earlier Older)
{
    public int Api => 1;

    /// <summary>The frame the shell draws around the page: the place, and who reads.</summary>
    public static Frame Frame(DoorPast page) => new(page.Name, page.Address, [new Crumb("Older for you")], page.Who);

    /// <summary>The Tangents the reader may view, in the rail.</summary>
    [Slot(Slot.Rail), Newtonsoft.Json.JsonIgnore]
    public TangentList Listed => new("Tangents", Tangents, AllTangents, NewTangent);

    [Slot(Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Earlier Column => Older;

    /// <summary>One page of what reached the reader and is no longer new: each thing as its <see cref="Item.Kind"/> says it —
    /// <c>reply</c>, <c>taken</c>, <c>mention</c>, <c>report</c>, <c>edited</c> or <c>removed</c> — with its Post, newest
    /// first; <see cref="Next"/>, the door's address of the page before it, absent where nothing lies behind it; and
    /// <see cref="Newest"/>, the door, where what is new is read.</summary>
    public sealed record Earlier(IReadOnlyList<Item> Items, string? Next, string Newest) : IPart;

    /// <summary>One thing that reached the reader: what it is, and its Post as the door's news draws one.</summary>
    public sealed record Item(string Kind, Door.NewPost Post);
}
