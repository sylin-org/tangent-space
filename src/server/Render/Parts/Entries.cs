namespace Tangent.Render;

/// <summary>One line of a list of things the reader may act on: what it is about — a person, a role or a Tangent, drawn as its
/// name — or a sentence, the detail after it, a second line where it has one, and what the reader may do with it, each a form
/// or a link of its own. <see cref="State"/> is how it stands, which its line carries for a stylesheet.</summary>
public sealed record Entry(string? Text, IReadOnlyList<IPart>? Answers = null) : IPart
{
    public IPart? Subject { get; init; }

    public string? Detail { get; init; }

    public string? More { get; init; }

    public string? State { get; init; }
}

/// <summary>Entries under their heading, or the one sentence where there are none.</summary>
public sealed record EntryList(IReadOnlyList<Entry> Entries, string? Heading = null, string? Empty = null) : IPart;

/// <summary>The way to the page before and the page after, where there are any.</summary>
public sealed record Pager(string Label, Onward? Before, Onward? After) : IPart;

/// <summary>A link drawn as a button: quiet beside the page's main one unless <see cref="Main"/>, a download where it says so,
/// another site's where it leads out.</summary>
public sealed record LinkButton(string Label, string Address) : IPart
{
    public bool Main { get; init; }

    public bool Download { get; init; }

    public bool Outside { get; init; }
}
