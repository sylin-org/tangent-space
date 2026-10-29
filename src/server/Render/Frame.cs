namespace Tangent.Render;

/// <summary>The frame the shell draws around a page: the place's name and its door in the masthead, and where the
/// page is, as crumbs in the path bar. A resource that names a frame gets the masthead, the path bar and the footer;
/// one that names none gets its component alone in the page.</summary>
public sealed record Frame(string Place, string Door, IReadOnlyList<Crumb> Crumbs)
{
    /// <summary>The mark drawn for a place with no image of its own: the first letter of its name.</summary>
    public string Initial => Place.Length > 0 ? char.ToUpperInvariant(Place[0]).ToString() : "T";
}

/// <summary>One step of the path bar. The last crumb is the page itself and has no address.</summary>
public sealed record Crumb(string Label, string? Address = null);
