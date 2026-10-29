namespace Tangent.Space;

/// <summary>The proprietor's own HTML in the five places every page renders it: identical in the live site, the
/// archive and exports.</summary>
public sealed class Slots
{
    public string Head { get; set; } = "";

    public string Masthead { get; set; } = "";

    public string DoorIntro { get; set; } = "";

    public string ArticleHeader { get; set; } = "";

    public string Footer { get; set; } = "";
}
