namespace Tangent.Render;

/// <summary>What signing in gives someone here — replying, what is new since they were here, hearing when they are mentioned —
/// and where an account is made, with that reading needs none, on a place a visitor may read.</summary>
public sealed record SignInGains(bool Private) : IPart;

/// <summary>The pointers a site places to find its Tangent Space, each ready to copy; where they stand as a section of a
/// page, under <see cref="Heading"/>, reached at <see cref="Anchor"/>, with the sentence <see cref="Lede"/> before them.</summary>
public sealed record Pointing(IReadOnlyList<Space.Pointer> Pointers) : IPart
{
    /// <summary>The id of the heading the pointers stand under, which a link to them names after its <c>#</c>.</summary>
    public const string Anchor = "pointers";

    public string? Heading { get; init; }

    public string? Lede { get; init; }
}
