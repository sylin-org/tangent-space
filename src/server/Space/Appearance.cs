namespace Tangent.Space;

/// <summary>The place's colour, one of the design's four: links, buttons and what is new take it on every page, for every
/// reader, in their own theme, while the palette is Tangent's.</summary>
public enum Accent
{
    Gold,
    Jade,
    Iris,
    Copper
}

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

/// <summary>What fills the palette layer: the base's own colours, Tangent's palette under Tangent's own base, or
/// Tangent's palette in the place's accent over whichever base the place wears.</summary>
public enum Palette
{
    Base,
    Accent
}

/// <summary>How the place looks: the five layers of its CSS (<c>@layer base, shapes, tangent, palette, owner</c>) as whoever
/// holds <c>ManageSpace</c> fills them — its <see cref="Base"/>, its <see cref="Palette"/> with its <see cref="Accent"/>, which every
/// page carries as <c>data-accent</c>, and the place's own <see cref="Css"/>; the shapes and tangent layers are always Tangent's.
/// Light or dark is each reader's own, kept by their browser. Each layer at its default adds nothing, and the place
/// wears Tangent's own design.</summary>
public sealed class Appearance
{
    /// <summary>The most characters the place's own CSS holds.</summary>
    public const int CssLimit = 50_000;

    /// <summary>The accents a form offers, in their order.</summary>
    public static readonly IReadOnlyList<string> Accents = [.. Enum.GetValues<Accent>().Select(Word)];

    /// <summary>The bases a form offers, in their order: Tangent's own first.</summary>
    public static readonly IReadOnlyList<string> Bases = [.. Enum.GetValues<Base>().Select(Word)];

    /// <summary>The palettes a form offers, in their order.</summary>
    public static readonly IReadOnlyList<string> Palettes = [.. Enum.GetValues<Palette>().Select(Word)];

    public Accent Accent { get; set; } = Accent.Gold;

    public Base Base { get; set; } = Base.Tangent;

    public Palette Palette { get; set; } = Palette.Base;

    /// <summary>The place's own CSS, the owner layer, as its writer wrote it; empty adds nothing.</summary>
    public string Css { get; set; } = "";

    /// <summary>The base layer's stylesheet, under the base URL.</summary>
    public string BaseSheet => Base == Base.Tangent ? "base.css" : "bases/" + Word(Base) + ".css";

    /// <summary>The palette layer's stylesheet, under the base URL: the base's own, or Tangent's.</summary>
    public string PaletteSheet => Palette == Palette.Base && Base != Base.Tangent ? "bases/" + Word(Base) + ".palette.css" : "palette.css";

    /// <summary>The licence that travels with a bundled base, under the base URL; none for Tangent's own and Concord,
    /// which are Tangent's.</summary>
    public string? Licence => Base is Base.Tangent or Base.Concord ? null : "bases/" + Word(Base) + "/LICENSE";

    /// <summary>The base as <c>data-base</c> on <c>&lt;html&gt;</c> says it, for a stylesheet to read; none for Tangent's own.</summary>
    public string? BaseAttribute => Base == Base.Tangent ? null : Word(Base);

    /// <summary>The owner layer as a page carries it in its head: the place's CSS inside <c>@layer owner</c>, kept CSS
    /// throughout, since a <c>&lt;/</c> in it never closes the element; nothing while it is empty.</summary>
    public string? CssLayer => Css.Length == 0 ? null : "@layer owner {\n" + Css.Replace("</", "<\\/", StringComparison.Ordinal) + "\n}";

    /// <summary>A copy with the same choices.</summary>
    public Appearance Copy() => new() { Accent = Accent, Base = Base, Palette = Palette, Css = Css };

    /// <summary>Whether <paramref name="other"/> makes the same choices.</summary>
    public bool Same(Appearance other) => Accent == other.Accent && Base == other.Base && Palette == other.Palette && Css == other.Css;

    /// <summary>This appearance with what a write sends in place of what it holds; a choice left null stays.</summary>
    public Appearance With(Accent? accent = null, Base? @base = null, Palette? palette = null, string? css = null)
        => new() { Accent = accent ?? Accent, Base = @base ?? Base, Palette = palette ?? Palette, Css = css ?? Css };

    /// <summary>The word an accent is written as, in a form, in JSON and on a page.</summary>
    public static string Word(Accent accent) => accent.ToString().ToLowerInvariant();

    /// <summary>The word a base is written as, in a form, in JSON and in its stylesheet's name.</summary>
    public static string Word(Base value) => value.ToString().ToLowerInvariant();

    /// <summary>The word a palette is written as, in a form and in JSON.</summary>
    public static string Word(Palette palette) => palette.ToString().ToLowerInvariant();
}
