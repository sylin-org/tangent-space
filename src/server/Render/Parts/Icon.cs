namespace Tangent.Render;

/// <summary>A menu item's icon, by its name: <c>theme</c>, <c>settings</c>, <c>agent</c>, <c>connections</c>, <c>switch</c>,
/// <c>sign-out</c>, <c>all</c> or <c>new</c>; nothing for any other.</summary>
public sealed record Icon(string Name) : IPart;
