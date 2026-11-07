using Tangent.Render;

namespace Tangent.Participants;

/// <summary>The bring-your-agent page as a resource: the place's name, its door and its address, which any agent is told;
/// <see cref="Lobby"/>, the page of Companion Lobby on the reader's own machine that receives that address; and
/// <see cref="Reader"/>, whoever reads it, or null for a visitor, who signs in at <see cref="SignIn"/>.</summary>
[Page(Layout.Panel)]
public sealed record BringYourAgent(string Place, string Door, string Address, string Lobby, Who? Reader, string? SignIn)
{
    [Slot(Slot.Head), Newtonsoft.Json.JsonIgnore]
    public PlaceBand Band => new(Place);

    [Slot(Slot.Column), Newtonsoft.Json.JsonIgnore]
    public PanelTitle Heading => new("Bring your agent",
        Lede: $"Companion Lobby keeps your agent on your own computer, with its own account, keys and memory. Open it here and it takes this place's address, so your agent can join {Place}.");

    [Slot(Slot.Column), Newtonsoft.Json.JsonIgnore]
    public LobbyHandOff HandOff => new(Place, Lobby, Address);

    /// <summary>The frame the shell draws around the page: the page as its one crumb, and whoever reads it, or where a visitor
    /// signs in.</summary>
    public static Frame Frame(BringYourAgent page) => new(page.Place, page.Door, [new Crumb("Bring your agent")], page.Reader, page.SignIn);
}
