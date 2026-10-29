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

/// <summary>Who is signed in, as the account menu shows them: their id, the name to show, their handle, the highest role they
/// hold where they hold one, their avatar where their profile has one, and where they sign out, coming back to the
/// page they are on.</summary>
public sealed record Who(string Id, string Name, string Handle, string? Role, string? Avatar, string SignOut);

/// <summary>One step of the path bar. The last crumb is the page itself and has no address. A crumb with a
/// <see cref="Switcher"/> is a Tangent's, drawn with its tile and opening the list of Tangents in the page.</summary>
public sealed record Crumb(string Label, string? Address = null, Switcher? Switcher = null);

/// <summary>The Tangent crumb's list: the current Tangent's tile, the Tangents the reader may view, and where all the
/// Tangents are, the door.</summary>
public sealed record Switcher(Tile Tile, IReadOnlyList<SwitcherItem> Tangents, string All);

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

/// <summary>A participant's mark without an avatar: two letters of their handle on a tint of their own, one of the
/// Tangent tones chosen by a hash of their id, so two people whose names share a first letter still differ. A
/// profile avatar is shown instead where it is an https address.</summary>
public static class Monogram
{
    /// <summary>The first two letters or digits of the handle's first label; a DID's last segment where the handle is
    /// the DID.</summary>
    public static string Letters(string handle)
    {
        var name = handle.TrimStart('@');
        if (name.StartsWith("did:", StringComparison.Ordinal)) name = name[(name.LastIndexOf(':') + 1)..];
        var label = name.Split('.')[0];
        var letters = new string(label.Where(char.IsLetterOrDigit).Take(2).ToArray()).ToUpperInvariant();
        return letters.Length > 0 ? letters : "?";
    }

    /// <summary>The tone of a participant's tint, the same for their id wherever it is drawn.</summary>
    public static string Tone(string id)
    {
        var hash = 2166136261u;
        foreach (var unit in System.Text.Encoding.UTF8.GetBytes(id)) hash = unchecked((hash ^ unit) * 16777619u);
        return Tile.Tones[(int)((hash >> 8) % (uint)Tile.Tones.Count)];
    }

    /// <summary>The avatar to show: the profile's, when it is an https address.</summary>
    public static string? Avatar(string? avatar)
        => avatar is { Length: > 0 } && avatar.StartsWith("https://", StringComparison.Ordinal) ? avatar : null;
}
