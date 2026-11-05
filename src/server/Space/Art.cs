using Tangent.Posts;

namespace Tangent.Space;

/// <summary>The covers Tangent bundles, served at <c>/art/{name}.webp</c>, which a place or a Tangent may wear as its image
/// as well as one brought in here. An image is written as a cover's name or as <c>/media/{id}</c>.</summary>
public static class Art
{
    /// <summary>One cover: its name as an image is written, and its title.</summary>
    public sealed record Cover(string Name, string Title);

    /// <summary>The covers, in the order a picker offers them.</summary>
    public static readonly IReadOnlyList<Cover> Covers =
    [
        new("the-commons", "The Commons"),
        new("midnight-workshop", "Midnight Workshop"),
        new("universe-within", "A Universe Within"),
        new("quiet-geometry", "Quiet Geometry"),
        new("paper-current", "Paper Current"),
        new("prism-study", "Prism Study"),
        new("blueprint-exchange", "Blueprint Exchange"),
        new("greenhouse", "Greenhouse"),
        new("moss-and-signal", "Moss & Signal"),
        new("coastal-hours", "Coastal Hours"),
        new("orbital-dawn", "Orbital Dawn"),
        new("neural-loom", "Neural Loom"),
        new("companion-circle", "Companion Circle"),
        new("afterlight", "Afterlight"),
        new("arcade-bloom", "Arcade Bloom"),
        new("electric-ink", "Electric Ink")
    ];

    /// <summary>A cover's file under the base URL.</summary>
    public static string File(string name) => "art/" + name + ".webp";

    /// <summary>The image as it is kept, trimmed: empty, a cover's name or <c>/media/{id}</c>; null where it is none of them.</summary>
    public static string? Judged(string sent)
    {
        var image = sent.Trim();
        return image.Length == 0 || Covers.Any(cover => cover.Name == image) || Media.IdIn(image) is not null ? image : null;
    }

    /// <summary>The address of a kept image: a cover's under <paramref name="door"/>, the base URL ending in a slash, and an
    /// image brought in here under <paramref name="media"/>, the door unless a page names its images elsewhere; null for none.</summary>
    public static string? Address(string image, string door, string? media = null)
        => image.Length == 0 ? null : Media.IdIn(image) is not null ? (media ?? door) + image.TrimStart('/') : door + File(image);
}
