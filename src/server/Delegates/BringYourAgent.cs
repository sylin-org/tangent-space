using Tangent.Render;

namespace Tangent.Delegates;

/// <summary>The bring-your-agent page as a resource: the place's name, its door and its address, which the page hands to
/// Companion Lobby where one answers on the reader's machine; <see cref="Tools"/>, the address any MCP client connects to;
/// <see cref="Llms"/> and <see cref="ConsentPage"/>, the way in for an agent that follows the pages instead; and
/// <see cref="Reader"/>, the signed-in person whose agent it is.</summary>
public sealed record BringYourAgent(string Place, string Door, string Address, string Tools, string Llms, string ConsentPage, Who? Reader);
