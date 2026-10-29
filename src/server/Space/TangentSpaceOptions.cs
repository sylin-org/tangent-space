namespace Tangent.Space;

/// <summary>What an operator configures under the <c>Tangent</c> section: the folder that holds the Tangent
/// Space's data, and the DID of the one person who may set it up. The base URL it answers at is Koan's
/// <c>Koan:Web:ApplicationUrl</c>.</summary>
public sealed class TangentSpaceOptions
{
    public const string Configuration = "Tangent";

    /// <summary>The SQLite database inside the data folder.</summary>
    public const string DatabaseFile = "tangent.db";

    /// <summary>The one data folder, resolved against the executable's folder (the content root) unless absolute.</summary>
    public string DataFolder { get; set; } = "data";

    /// <summary>The DID of the person who may set up the Tangent Space at first run and is its operator from then on;
    /// unset, first run asks for the setup code each start writes to the log. Changing it never transfers the place.</summary>
    public string? Operator { get; set; }

    public string DataFolderPath(string contentRoot) => Path.GetFullPath(Path.Combine(contentRoot, DataFolder));

    public string DatabasePath(string contentRoot) => Path.Combine(DataFolderPath(contentRoot), DatabaseFile);
}
