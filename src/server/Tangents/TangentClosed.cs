using Tangent.Render;

namespace Tangent.Tangents;

/// <summary>What the sender of a Tangent's settings reads when their own change made the Tangent private to them: it is
/// saved, the Tangent named <see cref="Name"/> no longer shows for them, and the <see cref="Door"/> of the
/// <see cref="Place"/> is where they go on.</summary>
public sealed record TangentClosed(string Name, string Place, string Door, Who? Who)
{
    /// <summary>What the sender reads: it is saved, and no longer shows for them.</summary>
    public string Saved => Name + " is private now, and no longer shows for you.";
}
