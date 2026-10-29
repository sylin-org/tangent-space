namespace Tangent.Space;

/// <summary>The door's placeholder: the name of the place, the base URL it answers at, the row's version, and
/// <c>api</c>, the stability promise an agent reads.</summary>
public sealed record Door(string Name, string BaseUrl, long Version)
{
    public int Api => 1;
}
