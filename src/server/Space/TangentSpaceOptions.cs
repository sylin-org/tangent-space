using System.Diagnostics.CodeAnalysis;

namespace Tangent.Space;

/// <summary>What an operator configures once, under the <c>Tangent</c> section: the base URL the
/// Tangent Space answers at, the folder that holds its data, and the one reverse proxy it believes.</summary>
public sealed class TangentSpaceOptions
{
    public const string Configuration = "Tangent";

    /// <summary>The SQLite database inside the data folder.</summary>
    public const string DatabaseFile = "tangent.db";

    /// <summary>Scheme, host and optional path. Every absolute link derives from it.</summary>
    public string BaseUrl { get; set; } = "http://localhost:5220";

    /// <summary>The one data folder, resolved against the content root unless absolute.</summary>
    public string DataFolder { get; set; } = "data";

    /// <summary>The IP address of the immediate reverse proxy whose forwarded scheme is honoured; unset when there is none.</summary>
    public string? TrustedProxy { get; set; }

    /// <summary>The base URL's path without its trailing slash; empty when the base URL has no path.</summary>
    public string BasePath() => TryParseBaseUrl(BaseUrl, out var baseUrl)
        ? baseUrl.AbsolutePath.TrimEnd('/')
        : throw new InvalidOperationException("Tangent:BaseUrl is not an absolute http or https URL.");

    public string DataFolderPath(string contentRoot) => Path.GetFullPath(Path.Combine(contentRoot, DataFolder));

    public string DatabasePath(string contentRoot) => Path.Combine(DataFolderPath(contentRoot), DatabaseFile);

    /// <summary>An absolute http or https URL with nothing after its path.</summary>
    public static bool TryParseBaseUrl(string? value, [NotNullWhen(true)] out Uri? baseUrl)
    {
        baseUrl = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed)) return false;
        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) return false;
        if (parsed.Query.Length > 0 || parsed.Fragment.Length > 0) return false;
        baseUrl = parsed;
        return true;
    }
}
