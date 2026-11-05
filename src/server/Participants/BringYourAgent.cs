using Tangent.Render;

namespace Tangent.Participants;

/// <summary>The bring-your-agent page as a resource: the place's name, its door and its address, which any agent is told;
/// <see cref="Lobby"/>, the page of Companion Lobby on the reader's own machine that receives that address; and
/// <see cref="Reader"/>, whoever reads it, or null for a visitor, who signs in at <see cref="SignIn"/>.</summary>
public sealed record BringYourAgent(string Place, string Door, string Address, string Lobby, Who? Reader, string? SignIn);
