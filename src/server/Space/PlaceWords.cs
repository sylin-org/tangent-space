using Tangent.Posts;

namespace Tangent.Space;

/// <summary>The place's words — its description and the Markdown parts of its <see cref="Appearance"/> — parsed as a Post's body
/// is, through the same sanitizer, and kept parsed by part in <see cref="RenderedBodies"/>, so a page parses each at most once
/// while it stays the same. No HTML of the proprietor's own reaches a page.</summary>
public static class PlaceWords
{
    public const string DescriptionPart = "description";

    /// <summary>One parse of a part's Markdown, kept while the part holds the same text.</summary>
    public static MarkdownHtml.Rendered Rendered(string part, string markdown) => RenderedBodies.For("place:" + part, 0, markdown);

    /// <summary>A part as a page draws it, its images placed under <paramref name="door"/>, the base URL ending in a slash;
    /// null while it is empty.</summary>
    public static string? Html(string part, string markdown, string door)
        => markdown.Length == 0 ? null : Media.Placed(Rendered(part, markdown).Html(), door);

    /// <summary>A part's words on one line, with no syntax: what a card or a feed says of it.</summary>
    public static string Plain(string part, string markdown) => markdown.Length == 0 ? "" : Rendered(part, markdown).PlainText();
}
