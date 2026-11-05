using Koan.Data.Core.Commands;
using Tangent.Posts;

namespace Tangent.Space;

/// <summary>What fills the base layer, the plain elements and the fonts: Tangent's own; one of the classless stylesheets
/// Tangent bundles, each with its adapter onto the <c>--ts-*</c> tokens; or Concord, Tangent's own arrangement of the
/// place in a chat app's look, which also arranges Tangent's parts in the tangent layer, scoped by
/// <c>data-base</c>.</summary>
public enum Base
{
    Tangent,
    Simple,
    Mvp,
    Marx,
    Sakura,
    Water,
    New,
    Concord
}

/// <summary>The place's colours, one choice: the base's own, which under Tangent's own base is Tangent's palette in gold, or
/// Tangent's palette in one of its four accents over whichever base the place wears.</summary>
public enum Palette
{
    Base,
    Gold,
    Jade,
    Iris,
    Copper
}

/// <summary>How the place looks: the five layers of its CSS, stated once in <c>layers.css</c>, as whoever holds
/// <c>ManageSpace</c> fills them — its <see cref="Base"/>, its <see cref="Palette"/> and its own <see cref="Css"/>; the shapes and
/// tangent layers are always Tangent's — with its <see cref="Image"/> behind the door's head and its words in Markdown: the
/// <see cref="Masthead"/> line, the <see cref="Footer"/> line, the door's <see cref="Intro"/> and the
/// <see cref="ArticleHeader"/>. Light or dark is each reader's own, kept by their browser. Each part at its default adds
/// nothing, and the place wears Tangent's own design.</summary>
public sealed class Appearance
{
    /// <summary>The most characters the place's own CSS holds.</summary>
    public const int CssLimit = 50_000;

    /// <summary>The most characters the masthead line and the footer line each hold.</summary>
    public const int LineLimit = 2000;

    /// <summary>The most characters the door's introduction and the article header each hold.</summary>
    public const int ProseLimit = 8000;

    public const string MastheadPart = "masthead";
    public const string FooterPart = "footer";
    public const string IntroPart = "intro";
    public const string ArticleHeaderPart = "article-header";
    public const string ImagePart = "image";

    public static readonly Reason BaseRule = Reason.Invalid("The base is Tangent's own or a bundled stylesheet: choose tangent, simple, mvp, marx, sakura, water, new or concord.");
    public static readonly Reason PaletteRule = Reason.Invalid("The colours are the base's own or Tangent's in an accent: choose base, gold, jade, iris or copper.");
    public static readonly Reason CssRule = Reason.Invalid("The place's own CSS holds at most 50,000 characters.");
    public static readonly Reason ImageRule = Reason.Invalid("The place's image is one of the bundled covers, by its name, or an image brought in here, as /media/ and its id; empty takes it away.");
    public static readonly Reason MastheadRule = Reason.Invalid("The masthead line holds at most 2,000 characters of Markdown that renders within its bound.");
    public static readonly Reason FooterRule = Reason.Invalid("The footer line holds at most 2,000 characters of Markdown that renders within its bound.");
    public static readonly Reason IntroRule = Reason.Invalid("The door's introduction holds at most 8,000 characters of Markdown that renders within its bound.");
    public static readonly Reason ArticleHeaderRule = Reason.Invalid("The article header holds at most 8,000 characters of Markdown that renders within its bound.");

    /// <summary>The bases a form offers, in their order: Tangent's own first.</summary>
    public static readonly IReadOnlyList<string> Bases = [.. Enum.GetValues<Base>().Select(Word)];

    /// <summary>The palettes a form offers, in their order: the base's own first.</summary>
    public static readonly IReadOnlyList<string> Palettes = [.. Enum.GetValues<Palette>().Select(Word)];

    public Base Base { get; set; } = Base.Tangent;

    public Palette Palette { get; set; } = Palette.Base;

    /// <summary>The place's own CSS, the owner layer, as its writer wrote it; empty adds nothing.</summary>
    public string Css { get; set; } = "";

    /// <summary>The image behind the door's head and the icon of every page: a bundled cover's name (<see cref="Art"/>) or an
    /// image brought in here, as <c>/media/{id}</c>; empty for none.</summary>
    public string Image { get; set; } = "";

    /// <summary>Markdown beside the place's name on every page.</summary>
    public string Masthead { get; set; } = "";

    /// <summary>Markdown first in every page's footer.</summary>
    public string Footer { get; set; } = "";

    /// <summary>Markdown under the door's head: the place's welcome.</summary>
    public string Intro { get; set; } = "";

    /// <summary>Markdown above every article's title.</summary>
    public string ArticleHeader { get; set; } = "";

    /// <summary>The base layer's stylesheet, under the base URL.</summary>
    public string BaseSheet => Base == Base.Tangent ? "base.css" : "bases/" + Word(Base) + ".css";

    /// <summary>Whether the palette layer holds Tangent's palette, in <see cref="Accent"/>, rather than the base's own.</summary>
    private bool Tangents => Palette != Palette.Base || Base == Base.Tangent;

    /// <summary>The palette layer's stylesheet, under the base URL: the base's own, or Tangent's.</summary>
    public string PaletteSheet => Tangents ? "palette.css" : "bases/" + Word(Base) + ".palette.css";

    /// <summary>The accent Tangent's palette wears, as <c>data-accent</c> on <c>&lt;html&gt;</c> says it; none under a base's own
    /// palette.</summary>
    public string? Accent => !Tangents ? null : Palette == Palette.Base ? Word(Palette.Gold) : Word(Palette);

    /// <summary>The licence that travels with a bundled base, under the base URL; none for Tangent's own and Concord,
    /// which are Tangent's.</summary>
    public string? Licence => Base is Base.Tangent or Base.Concord ? null : "bases/" + Word(Base) + "/LICENSE";

    /// <summary>The base as <c>data-base</c> on <c>&lt;html&gt;</c> says it, for a stylesheet to read; none for Tangent's own.</summary>
    public string? BaseAttribute => Base == Base.Tangent ? null : Word(Base);

    /// <summary>The owner layer as a page carries it in its head: the place's CSS inside <c>@layer owner</c>, kept CSS
    /// throughout, since a <c>&lt;/</c> in it never closes the element; nothing while it is empty.</summary>
    public string? CssLayer => Css.Length == 0 ? null : "@layer owner {\n" + Css.Replace("</", "<\\/", StringComparison.Ordinal) + "\n}";

    /// <summary>The place's image at its address under <paramref name="door"/>, the base URL ending in a slash, one brought in
    /// here under <paramref name="media"/> where a page names its images elsewhere; null for none.</summary>
    public string? ImageAt(string door, string? media = null) => Art.Address(Image, door, media);

    /// <summary>A copy with the same choices.</summary>
    public Appearance Copy() => (Appearance)MemberwiseClone();

    /// <summary>Whether <paramref name="other"/> makes the same choices.</summary>
    public bool Same(Appearance other) => Base == other.Base && Palette == other.Palette && Css == other.Css && Image == other.Image
                                          && Masthead == other.Masthead && Footer == other.Footer && Intro == other.Intro && ArticleHeader == other.ArticleHeader;

    /// <summary>This appearance with what a write sends in place of what it holds, each judged; a part left null stays.
    /// Refuses with the rule of the first part that is not one of its choices or past its bound.</summary>
    public Appearance With(string? @base = null, string? palette = null, string? css = null, string? image = null,
        string? masthead = null, string? footer = null, string? intro = null, string? articleHeader = null)
    {
        var next = Copy();
        if (@base is not null) next.Base = Parse<Base>(@base, Bases, BaseRule);
        if (palette is not null) next.Palette = Parse<Palette>(palette, Palettes, PaletteRule);
        if (css is not null) next.Css = css.Length <= CssLimit ? css : throw new Refusal(CssRule);
        if (image is not null) next.Image = Art.Judged(image) ?? throw new Refusal(ImageRule);
        if (masthead is not null) next.Masthead = Words(masthead, LineLimit, MastheadRule);
        if (footer is not null) next.Footer = Words(footer, LineLimit, FooterRule);
        if (intro is not null) next.Intro = Words(intro, ProseLimit, IntroRule);
        if (articleHeader is not null) next.ArticleHeader = Words(articleHeader, ProseLimit, ArticleHeaderRule);
        return next;
    }

    /// <summary>This appearance with what a preview sends, each part it cannot take left as it is saved.</summary>
    public Appearance Previewed(string? @base, string? palette, string? css, string? image, string? masthead, string? footer, string? intro, string? articleHeader)
    {
        var next = this;
        foreach (var step in new Func<Appearance, Appearance>[]
                 {
                     a => a.With(@base: @base), a => a.With(palette: palette), a => a.With(css: css), a => a.With(image: image),
                     a => a.With(masthead: masthead), a => a.With(footer: footer), a => a.With(intro: intro), a => a.With(articleHeader: articleHeader)
                 })
        {
            try
            {
                next = step(next);
            }
            catch (Refusal)
            {
            }
        }

        return next;
    }

    /// <summary>Each image of the place that a part names, by the part's name and the image's Id.</summary>
    public IReadOnlySet<(string Part, string Media)> Images()
    {
        var named = new HashSet<(string, string)>();
        if (Media.IdIn(Image) is { } image) named.Add((ImagePart, image));
        foreach (var (part, text) in new[] { (MastheadPart, Masthead), (FooterPart, Footer), (IntroPart, Intro), (ArticleHeaderPart, ArticleHeader) })
            if (text.Length > 0)
                foreach (var target in PlaceWords.Rendered(part, text).Images)
                    if (Media.IdIn(target) is { } id) named.Add((part, id));
        return named;
    }

    /// <summary>Whether the part of this name stands on every page, a private place's sign-in among them, so an image it
    /// names reaches everyone.</summary>
    public static bool OnEveryPage(string part) => part is MastheadPart or FooterPart or ImagePart;

    private static T Parse<T>(string sent, IReadOnlyList<string> words, Reason rule) where T : struct, Enum
        => words.Contains(sent, StringComparer.Ordinal) ? Enum.Parse<T>(sent, ignoreCase: true) : throw new Refusal(rule);

    /// <summary>Markdown as a part keeps it, trimmed with <c>\n</c> line endings, or a refusal with <paramref name="rule"/>
    /// past its bound or where it would not render within a Post's bounds.</summary>
    private static string Words(string sent, int limit, Reason rule)
    {
        var kept = sent.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (kept.Length == 0) return "";
        if (kept.Length > limit || kept.Contains('\0') || !MarkdownHtml.Tractable(kept)) throw new Refusal(rule);
        try
        {
            return MarkdownHtml.Render(kept).Fits ? kept : throw new Refusal(rule);
        }
        catch (ArgumentException)
        {
            throw new Refusal(rule);
        }
    }

    /// <summary>The word a base is written as, in a form, in JSON and in its stylesheet's name.</summary>
    public static string Word(Base value) => value.ToString().ToLowerInvariant();

    /// <summary>The word a palette is written as, in a form, in JSON and as <c>data-accent</c>.</summary>
    public static string Word(Palette palette) => palette.ToString().ToLowerInvariant();
}
