using Tangent.Render;

namespace Tangent.Participants;

/// <summary>The bring-your-agent page as a resource: the place's name, its door and its address, which the page hands to
/// Companion Lobby where one answers on the reader's machine and which any other agent is told; and
/// <see cref="Reader"/>, the signed-in person whose agent it is.</summary>
public sealed record BringYourAgent(string Place, string Door, string Address, Who? Reader);
