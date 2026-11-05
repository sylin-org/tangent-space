using System.Text.RegularExpressions;
using Koan.Data.Core.Commands;
using Koan.Media;
using Koan.Media.Abstractions.Recipes;
using Koan.Media.Core.Pipeline;
using Koan.Storage;

namespace Tangent.Posts;

/// <summary>A file a Post references, stored once by content hash on Koan's media. What is stored is never what was
/// sent: <see cref="Keep"/> decodes it, turns it upright, fits it within <see cref="LongestEdge"/>, removes its metadata
/// and encodes it again, so nothing its sender's camera or tools wrote reaches a reader. A colour profile stays, so a
/// phone's photo keeps its colours. Who may see it is <see cref="MediaUse"/>'s to say.</summary>
[StorageBinding(Container = "media")]
public sealed partial class Media : MediaEntity<Media>
{
    /// <summary>The most bytes an image holds as it is sent.</summary>
    public const int SentLimit = 20 * 1024 * 1024;

    /// <summary>The longest edge of an image as it is kept, in pixels.</summary>
    public const int LongestEdge = 2400;

    /// <summary>The most megapixels an image is decoded at.</summary>
    public const int SourceMegapixels = 64;

    /// <summary>The quality a photograph is encoded at.</summary>
    public const int KeptQuality = 85;

    public static readonly Reason NotAnImage = Reason.Invalid("Choose a PNG, JPEG or WebP image.");
    public static readonly Reason TooLarge = Reason.Invalid("Choose an image smaller than 20 MiB.");
    public static readonly Reason TooManyPixels = Reason.Invalid("Choose an image of at most 64 megapixels.");

    /// <summary>Where a Post's body names an image held here, whatever address the place answers at.</summary>
    public static string Reference(string id) => "/media/" + id;

    /// <summary>The Id of the media a body's image names, or null where it names anything else.</summary>
    public static string? IdIn(string? target) => target is not null && Referenced().Match(target) is { Success: true } match ? match.Groups[1].Value : null;

    /// <summary>A body's or one of the place's words' HTML with each image held here, as an image or a link's target, at its address under
    /// the base URL, which ends in a slash.</summary>
    public static string Placed(string html, string door) => html
        .Replace("src=\"/media/", "src=\"" + door + "media/", StringComparison.Ordinal)
        .Replace("href=\"/media/", "href=\"" + door + "media/", StringComparison.Ordinal);

    /// <summary>The media these sent bytes are, or a refusal: anything but a PNG, a JPEG or a WebP of a size this keeps.
    /// The same picture sent twice is kept once.</summary>
    public static async Task<Media> Keep(byte[] sent, CancellationToken ct)
    {
        if (sent.Length > SentLimit) throw new Refusal(TooLarge);
        if (!Sniffed(sent)) throw new Refusal(NotAnImage);
        using var kept = new MemoryStream();
        MediaOutput output;
        try
        {
            output = await new MemoryStream(sent).AsMedia(limits: new MediaPipelineLimits { MaxSourceMegapixels = SourceMegapixels, MaxFrameCount = MediaPipelineLimits.Defaults.MaxFrameCount })
                .AutoOrient()
                .ResizeFit(LongestEdge, LongestEdge)
                .Strip(MetadataKinds.All & ~MetadataKinds.Icc)
                .PreserveFormat(KeptQuality)
                .WriteToAsync(kept, ct);
        }
        catch (MediaSourceLimitException)
        {
            throw new Refusal(TooManyPixels);
        }
        catch (MediaDecodeException)
        {
            throw new Refusal(NotAnImage);
        }

        return await Store(kept.ToArray(), null, output.ContentType, null, ct);
    }

    /// <summary>Whether the bytes open as a PNG, a JPEG or a WebP.</summary>
    private static bool Sniffed(ReadOnlySpan<byte> sent)
        => sent.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])
           || sent.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF])
           || (sent.Length > 12 && sent.StartsWith("RIFF"u8) && sent[8..].StartsWith("WEBP"u8));

    [GeneratedRegex("^/media/([0-9a-f-]{32,36})$", RegexOptions.CultureInvariant)]
    private static partial Regex Referenced();
}

/// <summary>The renders of an image Koan serves beside what is kept.</summary>
public static class MediaRecipes
{
    /// <summary>The recipe a list's cover reads.</summary>
    public const string CardName = "card";

    /// <summary>The width of a cover on a list, wide enough for its lead on a dense screen.</summary>
    public const int CardWidth = 960;

    [MediaRecipe(CardName, Description = "An article's cover on a list", Version = 1)]
    public static MediaRecipe Card() => MediaRecipe.New().Resize(width: CardWidth).Build();
}
