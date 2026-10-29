using Koan.Data.Core.Model;

namespace Tangent.Space;

/// <summary>The one settings row: the name of the place and the base URL it answers at.</summary>
public sealed class TangentSpace : Entity<TangentSpace>
{
    /// <summary>The identity of the single row.</summary>
    public const string SingleRow = "tangent-space";

    public TangentSpace() => Id = SingleRow;

    public string Name { get; set; } = "";

    /// <summary>Scheme, host and optional path; every absolute link derives from it.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>Raised by every change to the row.</summary>
    public long Version { get; set; } = 1;
}
