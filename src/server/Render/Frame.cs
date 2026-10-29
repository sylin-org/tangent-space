namespace Tangent.Render;

/// <summary>The frame the shell draws around a page: the place's name and its door in the masthead, who is signed in
/// where someone is or where a visitor signs in, and where the page is, as crumbs in the path bar. A resource that
/// names a frame gets the masthead, the path bar and the footer; one that names none gets its component alone in the
/// page.</summary>
public sealed record Frame(string Place, string Door, IReadOnlyList<Crumb> Crumbs, Who? Who = null, string? SignIn = null)
{
    /// <summary>The mark drawn for a place with no image of its own: the first letter of its name.</summary>
    public string Initial => Place.Length > 0 ? char.ToUpperInvariant(Place[0]).ToString() : "T";
}

/// <summary>Who is signed in, as the account menu shows them: the name to show, their handle, the highest role they
/// hold where they hold one, their avatar where their profile has one, and where they sign out, coming back to the
/// page they are on.</summary>
public sealed record Who(string Name, string Handle, string? Role, string? Avatar, string SignOut)
{
    /// <summary>The mark drawn without an avatar: the first letter of the name.</summary>
    public string Initial => Name.TrimStart('@') is { Length: > 0 } name ? char.ToUpperInvariant(name[0]).ToString() : "?";
}

/// <summary>One step of the path bar. The last crumb is the page itself and has no address. A crumb with a
/// <see cref="Switcher"/> is a Tangent's, drawn with its tile and opening the list of Tangents in the page.</summary>
public sealed record Crumb(string Label, string? Address = null, Switcher? Switcher = null);

/// <summary>The Tangent crumb's list: the current Tangent's tile and the Tangents the reader may view.</summary>
public sealed record Switcher(Tile Tile, IReadOnlyList<SwitcherItem> Tangents);

/// <summary>One Tangent in the switcher: its name, address, kind and count in words, tile, and whether it is the page's.</summary>
public sealed record SwitcherItem(string Name, string Address, string Detail, Tile Tile, bool Current);

/// <summary>A Tangent's letter tile: its tone and the first letter of its name. The tone comes from the slug, the same
/// wherever the Tangent is drawn.</summary>
public sealed record Tile(string Tone, string Initial)
{
    /// <summary>The six tones of the design's Tangent tiles.</summary>
    public static readonly IReadOnlyList<string> Tones = ["amber", "coral", "jade", "sky", "iris", "rose"];

    /// <summary>The tile for a Tangent: FNV-1a over its slug with the low byte folded away, which spreads slugs that
    /// differ only near their end over every tone.</summary>
    public static Tile For(string slug, string name)
    {
        var hash = 2166136261u;
        foreach (var unit in System.Text.Encoding.UTF8.GetBytes(slug)) hash = unchecked((hash ^ unit) * 16777619u);
        return new Tile(Tones[(int)((hash >> 8) % (uint)Tones.Count)], name.Length > 0 ? char.ToUpperInvariant(name[0]).ToString() : "T");
    }
}
