namespace Tangent.Render;

/// <summary>The frame the shell draws around a page: the place's name and its door in the masthead, who is signed in
/// where someone is, with how many things are for them and new, or where a visitor signs in, and where the
/// page is, as crumbs in the path bar, and the feeds the page advertises. A page names only its own crumbs, past the
/// Tangent and the Topic its address names; the formatter puts those before them (<see cref="Trail"/>), and the place is
/// never a crumb, since the masthead is the root. <see cref="Switcher"/> is the list of Tangents the Tangent's crumb
/// opens, where the page holds one. A resource that
/// names a frame gets the masthead, the path bar and the footer; one that names none gets its component alone in the
/// page. The masthead holds the search box, with <see cref="Searched"/> in it, unless the page is not
/// <see cref="Searchable"/>: a step of signing in or setting up, where nothing is there to find yet; on a Tangent's page
/// it searches that Tangent (<see cref="SearchIn"/>). <see cref="Document"/> is the
/// <c>site.standard.document</c> record of an article published under its author's account, which the head names.</summary>
public sealed record Frame(string Place, string Door, IReadOnlyList<Crumb> Crumbs, Who? Who = null, string? SignIn = null, IReadOnlyList<FeedLink>? Feeds = null,
    bool Searchable = true, string? Searched = null, string? Document = null)
{
    public Switcher? Switcher { get; init; }

    /// <summary>The Tangent the search box searches alone, by its slug and its name, where the page is that Tangent's.</summary>
    public (string Slug, string Name)? SearchIn { get; init; }

    /// <summary>Whether the page's crumbs start at the root whatever its address names, as the saved change that shut its
    /// sender out of the Tangent it was sent to.</summary>
    public bool AtRoot { get; init; }

    /// <summary>Where a page is: the Tangent and the Topic its address names, the Tangent's crumb opening the switcher where
    /// the page holds one, then the page's own crumbs; the last is the page itself and has no address.</summary>
    public static IReadOnlyList<Crumb> Trail(Crumb? tangent, Crumb? topic, IReadOnlyList<Crumb> own)
    {
        var trail = new List<Crumb>();
        if (tangent is not null) trail.Add(tangent);
        if (topic is not null) trail.Add(topic);
        trail.AddRange(own);
        if (trail.Count > 0) trail[^1] = trail[^1] with { Address = null };
        return trail;
    }

    /// <summary>The path bar of a page whose address <paramref name="here"/> names, under <paramref name="baseAddress"/>.</summary>
    public IReadOnlyList<Crumb> Under(Access.Here? here, string baseAddress)
    {
        if (AtRoot) return Trail(null, null, Crumbs);
        var tangent = here?.Tangent is { } row ? new Crumb(row.Name, Posts.PostWrites.TangentAddress(baseAddress, row.Id), Switcher) : null;
        var topic = here?.Tangent is { } inTangent && here.Root is { } root
            ? new Crumb(root.Title ?? "", Posts.PostWrites.TangentAddress(baseAddress, inTangent.Id) + "/" + Uri.EscapeDataString(root.Slug ?? root.Id))
            : null;
        return Trail(tangent, topic, Crumbs);
    }

    /// <summary>Where the search box sends its words.</summary>
    public string Search => Door.TrimEnd('/') + Posts.SearchPage.Path;

    /// <summary>The search box, where the page is searchable, narrowed to <see cref="SearchIn"/> where the page names one.</summary>
    public Form? Searching => !Searchable ? null
        : SearchIn is { } inside
            ? Form.Reading(Search, Words("Search " + inside.Name, "Search " + inside.Name),
                new Form.Field(Posts.SearchController.In, Holds.SearchIn, inside.Slug, Form.Input.Hidden)) with { Tool = "search", Island = "search" }
            : Form.Reading(Search, Words("Search this place", "Search")) with { Tool = "search", Island = "search" };

    private Form.Field Words(string label, string placeholder)
        => new(Posts.SearchController.Words, "", Searched, Form.Input.Search) { Label = label, Placeholder = placeholder, Limit = Posts.PostSearch.TextLimit };

    /// <summary>The mark drawn for a place with no image of its own: the first letter of its name.</summary>
    public string Initial => Place.Length > 0 ? char.ToUpperInvariant(Place[0]).ToString() : "T";
}

/// <summary>Who is signed in, the same on every page and in its JSON, as the account menu shows them: their id, the name
/// to show, their handle, their kind (<c>person</c> or <c>agent</c>), their avatar where their profile has one, their own
/// profile's address, which the menu opens, absent only for someone signed in who is no participant yet, the highest role
/// they hold where they hold one, where they sign out, coming back to the page they are on, the place's settings, for one
/// who holds <c>ManageSpace</c>, <c>ManageStanding</c> or <c>ManageRoles</c>, and how many things are for them and new
/// (<see cref="Events.Unread"/>), which the masthead shows beside the account, linked to the door.</summary>
public sealed record Who(string Id, string Name, string Handle, string Kind, string? Avatar, string? Profile, string? Role, string SignOut,
    string? Settings = null, Events.Unread.Badge? ForYou = null)
{
    /// <summary>The reader as the Person part draws them (<see cref="Person.Of"/>), with the role they hold, where they sign
    /// out, the place's settings where they may change them, and their count.</summary>
    public static Who Of(Person person, string? role, string signOut, string? settings, Events.Unread.Badge? forYou)
        => new(person.Id, person.Name, person.Handle, person.Kind, person.Avatar, person.Profile, role, signOut, settings, forYou);

    /// <summary>Them as the Person part, with the role they hold; their mark opens the account menu, never their card.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public Person Person => new(Id, Handle, Name, Kind, Avatar) { Role = Role };

    /// <summary>Signing out, coming back to the page they are on.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public Form SigningOut => Form.Posting(SignOut) with { Tool = "sign-out", Submit = "Sign out" };

    /// <summary>Using another Bluesky account, coming back to the page they are on.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public Participants.AnotherAccount.Offer Another => Participants.AnotherAccount.For(this);
}

/// <summary>One step of the path bar. The last crumb is the page itself and has no address. A crumb with a
/// <see cref="Switcher"/> is a Tangent's, drawn with its tile and opening the list of Tangents in the page.</summary>
public sealed record Crumb(string Label, string? Address = null, Switcher? Switcher = null);

/// <summary>The Tangent crumb's list: the current Tangent, the Tangents the reader may view, the page's marked, where all the
/// Tangents are, and where a new one is created, for a reader who may create one.</summary>
public sealed record Switcher(TangentEntry Current, IReadOnlyList<TangentEntry> Tangents, string All, string? New = null);

/// <summary>A Tangent's tile: its tone and the first letter of its name, or its image in the letter's place. The tone is
/// the one the Tangent chose, or else comes from the slug, the same wherever the Tangent is drawn.</summary>
public sealed record Tile(string Tone, string Initial, string? Image = null)
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

    /// <summary>The tile a Tangent wears: its own accent and image where it chose them, its image under
    /// <paramref name="door"/>, the base URL ending in a slash, or one brought in here under <paramref name="media"/> where a
    /// page names its images elsewhere (<see cref="Space.Art.Address"/>).</summary>
    public static Tile For(Tangents.Tangent tangent, string door, string? media = null)
    {
        var tile = For(tangent.Id, tangent.Name);
        return tile with { Tone = tangent.Accent.Length > 0 ? tangent.Accent : tile.Tone, Image = Space.Art.Address(tangent.Image, door, media) };
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
