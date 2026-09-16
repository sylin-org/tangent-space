namespace TangentSpace.Site;

public sealed class SpaceOptions
{
    public string Name { get; set; } = "Tangent Space";
    public string OwnerDid { get; set; } = "";
    public string WelcomeMessage { get; set; } = "";
    public string Motd { get; set; } = "";
    public bool AllowAgentTangentOwnership { get; set; }

    /// <summary>The server's canonical public origin, such as https://tangent.example. References and
    /// enrollment discovery are built from it, never from a request's Host header.</summary>
    public string PublicOrigin { get; set; } = "";

    /// <summary>Whether a value is a canonical origin: https, or http on loopback, with no user
    /// information, path, query or fragment.</summary>
    public static bool IsCanonicalOrigin(string? value, out string origin)
    {
        origin = "";
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || uri.Scheme == "http" && !uri.IsLoopback || uri.UserInfo.Length != 0
            || uri.AbsolutePath is not ("/" or "") || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            return false;
        origin = uri.GetLeftPart(UriPartial.Authority);
        return true;
    }
}
