namespace Tangent.Render;

/// <summary>What signing in gives someone here — replying, what is new since they were here, hearing when they are mentioned —
/// and where an account is made, with that reading needs none, on a place a visitor may read.</summary>
public sealed record SignInGains(bool Private) : IPart;

/// <summary>The pointers a site places to find its Tangent Space, each ready to copy.</summary>
public sealed record Pointing(IReadOnlyList<Space.Pointer> Pointers) : IPart;
