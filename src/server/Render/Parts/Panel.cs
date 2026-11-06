namespace Tangent.Render;

/// <summary>A panel's title: whose it is, as a line linking to it (<see cref="Within"/>, the words before the link, and
/// <see cref="Place"/> at <see cref="Address"/>), its heading, and the sentence under it where it has one.</summary>
public sealed record PanelTitle(string Title, string? Within = null, string? Place = null, string? Address = null, string? Lede = null) : IPart;

/// <summary>The Posts a search found, best first, or the one sentence where it found none, and the pages either side.</summary>
public sealed record SearchHits(IReadOnlyList<Posts.SearchPage.Hit> Hits, string Empty, string? Previous, string? Next) : IPart;

/// <summary>Every Tangent the reader may view, a page at a time in the Tangents' order — those they are in as rows, the
/// members-only ones they are outside of as cards with the way in — where a new one is created for whoever may, and the
/// Tangents after and the first ones a link away; none in one sentence.</summary>
public sealed record TangentDirectory(IReadOnlyList<TangentEntry> Tangents, bool Continued, string? Next, string First, string? New) : IPart;
