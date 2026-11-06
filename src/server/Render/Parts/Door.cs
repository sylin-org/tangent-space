namespace Tangent.Render;

/// <summary>The place's head on its door, which names it no more than the masthead does: the line that describes it, whose
/// place it is, what it holds — its Tangents and Topics, "at least" where more Tangents follow those listed — over its image
/// where it wears one. <see cref="Name"/> labels the head for whoever cannot see it.</summary>
public sealed record PlaceHead(string Name, string? Description, Person? Proprietor, int Tangents, int Topics, bool AtLeast, string? Image) : IPart;

/// <summary>The place's welcome under its door's head, its Markdown drawn as a Post's body is.</summary>
public sealed record Intro(string Html) : IPart;

/// <summary>A list of Tangents under its heading, with where all of them are where more follow, and where a new one is
/// created for a reader who may.</summary>
public sealed record TangentList(string Heading, IReadOnlyList<TangentEntry> Tangents, string? All = null, string? New = null) : IPart;

/// <summary>Why the reader may not write in the place, in one line: their application waits, was declined, or they are
/// timed out until <see cref="Until"/>.</summary>
public sealed record StandingLine(string Gate, DateTimeOffset? Until) : IPart;

/// <summary>Where a signed-in reader who left a Topic mid-way continues, which their browser kept.</summary>
public sealed record ContinueReading(string Participant) : IPart;

/// <summary>A page's one sentence where it has nothing to show.</summary>
public sealed record Note(string Title) : IPart;

/// <summary>Topics under a heading, as rows of the table, newest activity first.</summary>
public sealed record TopicShelf(string Id, string Title, IReadOnlyList<TopicEntry> Topics) : IPart;

/// <summary>The live updater of a page: which page it is (<see cref="Door"/>, <see cref="TangentPage"/> or
/// <see cref="TopicPage"/>), the Tangent and the Topic where the page is one's, the Topic's address and whether the page shows
/// its newest window.</summary>
public sealed record LiveUpdates(string Page, string? Tangent = null, string? Topic = null, string? Address = null, bool Newest = false) : IPart
{
    public const string Door = "door";
    public const string TangentPage = "tangent";
    public const string TopicPage = "topic";
}
