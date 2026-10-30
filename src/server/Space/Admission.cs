namespace Tangent.Space;

/// <summary>How a newcomer arrives: <see cref="Join"/>, admitted and a member at once; or <see cref="Apply"/>, applied,
/// reading what a visitor reads until whoever holds <c>ManageStanding</c> admits them.</summary>
public enum Admission
{
    Join,
    Apply
}
