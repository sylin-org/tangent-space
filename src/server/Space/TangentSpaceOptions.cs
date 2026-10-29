namespace Tangent.Space;

/// <summary>What an operator configures under the <c>Tangent</c> section: the folder that holds the Tangent
/// Space's data. The base URL it answers at is Koan's <c>Koan:Web:ApplicationUrl</c>.</summary>
public sealed class TangentSpaceOptions
{
    public const string Configuration = "Tangent";

    /// <summary>The SQLite database inside the data folder.</summary>
    public const string DatabaseFile = "tangent.db";

    /// <summary>The one data folder, resolved against the executable's folder (the content root) unless absolute.</summary>
    public string DataFolder { get; set; } = "data";

    public string DataFolderPath(string contentRoot) => Path.GetFullPath(Path.Combine(contentRoot, DataFolder));

    public string DatabasePath(string contentRoot) => Path.Combine(DataFolderPath(contentRoot), DatabaseFile);
}
