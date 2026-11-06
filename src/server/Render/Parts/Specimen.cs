namespace Tangent.Render;

/// <summary>A part shown under the name of the state it stands in, as the gallery of parts shows every part, in the shape
/// <see cref="As"/> names. The part stays out of the JSON: a member typed by an interface would have Koan answer the page as
/// guarded JSON whatever the request prefers.</summary>
public sealed record Specimen(string Title, [property: Newtonsoft.Json.JsonIgnore] IPart Part, Shape As = Shape.Default) : IPart;
