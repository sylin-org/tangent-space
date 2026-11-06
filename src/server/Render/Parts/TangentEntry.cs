namespace Tangent.Render;

/// <summary>A Tangent as a list shows it, the one part for a Tangent wherever one is listed — the door's rail, the list of
/// Tangents, the switcher, a shelf: its slug, name, kind and address, how many Topics were started there, and how many of its
/// Posts are new to the reader (<see cref="New"/>). A members-only Tangent the reader is outside of says what it is about
/// (<see cref="Description"/>), how they come in (<see cref="Joining"/>: <c>join</c>, <c>ask</c>, or <c>asked</c> or
/// <c>declined</c> once they asked) and, while they may, the form that joins or asks (<see cref="Join"/>), or that withdraws a
/// waiting ask (<see cref="Withdraw"/>). <see cref="Tile"/> is the tile it chose; <see cref="Current"/> marks the Tangent the
/// page is in.</summary>
public sealed record TangentEntry(string Slug, string Name, string Kind, string Address, int TopicCount) : IPart
{
    /// <summary>How many of its Posts are new to the reader.</summary>
    public int New { get; init; }

    public string? Description { get; init; }

    public string? Joining { get; init; }

    public Form? Join { get; init; }

    public Form? Withdraw { get; init; }

    /// <summary>The tile it chose: its accent and its image, where it chose them.</summary>
    public Tile? Tile { get; init; }

    /// <summary>The tile it wears: its own, or the one its slug gives it.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public Tile Worn => Tile ?? Render.Tile.For(Slug, Name);

    /// <summary>Whether it is the Tangent the page is in.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public bool Current { get; init; }

    /// <summary>What its line says in place of what it holds, where a list says something else of it.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public string? Says { get; init; }

    /// <summary>Its kind and how many Topics it holds, in words, unless the list says something else of it.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public string Detail => Says ?? (Kind == "article"
        ? "Articles · " + Wording.Count(TopicCount, "article", "articles", "no articles yet")
        : "Discussions · " + Wording.Count(TopicCount, "topic", "topics", "no topics yet"));

    /// <summary>The Tangent's row as the reader may view it, with the tile it chose, from its own row.</summary>
    public static TangentEntry Of(Tangents.Tangent tangent, string address, string door)
        => new(tangent.Id, tangent.Name, tangent.DefaultKind == Posts.PostKind.Article ? "article" : "topic", address, tangent.TopicCount)
        {
            Tile = Render.Tile.For(tangent, door)
        };
}
