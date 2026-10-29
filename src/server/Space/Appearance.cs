namespace Tangent.Space;

/// <summary>How the place looks: the theme a visitor gets before choosing one, and the accent.</summary>
public sealed class Appearance
{
    /// <summary><c>system</c>, <c>light</c> or <c>dark</c>.</summary>
    public string Theme { get; set; } = "system";

    public string Accent { get; set; } = "";
}
