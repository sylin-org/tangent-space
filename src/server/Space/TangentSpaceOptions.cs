namespace Tangent.Space;

/// <summary>What an operator configures under the <c>Tangent</c> section: the folder that holds the Tangent
/// Space's data, the account that sets it up, and the archive it is retired to. Where it answers is learned
/// (<see cref="PlaceAddress"/>).</summary>
public sealed class TangentSpaceOptions
{
    public const string Configuration = "Tangent";

    /// <summary>The SQLite database inside the data folder.</summary>
    public const string DatabaseFile = "tangent.db";

    /// <summary>The one data folder, resolved against the executable's folder (the content root) unless absolute.</summary>
    public string DataFolder { get; set; } = "data";

    /// <summary>The account that may set up the Tangent Space at first run: its handle, with or without <c>@</c>, or
    /// its DID. Unset, whoever signs in first sets it up. It matters only until the place is set up.</summary>
    public string? Operator { get; set; }

    /// <summary>The archive this Tangent Space answers from, retired, resolved against the executable's folder unless
    /// absolute (<see cref="Lifecycle.Retirement"/>). Unset, it is not retired.</summary>
    public string? Archive { get; set; }

    /// <summary>The configured operator as first run names them: their DID, or their handle without its <c>@</c> in the
    /// lower case handles are written in; null when none is configured.</summary>
    public string? NamedOperator()
        => Operator?.Trim().TrimStart('@') is not { Length: > 0 } named ? null
            : IsDid(named) ? named
            : named.ToLowerInvariant();

    /// <summary>Whether the configured operator is named by DID rather than by handle.</summary>
    public bool NamedByDid() => NamedOperator() is { } named && IsDid(named);

    private static bool IsDid(string named) => named.StartsWith("did:", StringComparison.Ordinal);

    /// <summary>Whether this account may set up the place: anyone when no operator is configured; otherwise the account
    /// whose DID the setting names, or whose verified handle it names in any case.</summary>
    public bool MaySetUp(string did, string handle)
        => NamedOperator() is not { } named
            || (NamedByDid() ? named == did : string.Equals(named, handle, StringComparison.OrdinalIgnoreCase));

    public string DataFolderPath(string contentRoot) => Path.GetFullPath(Path.Combine(contentRoot, DataFolder));

    public string DatabasePath(string contentRoot) => Path.Combine(DataFolderPath(contentRoot), DatabaseFile);
}
