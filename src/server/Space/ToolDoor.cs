namespace Tangent.Space;

/// <summary>Where a standard MCP client takes part: Koan's edge at <see cref="Address"/>, which takes a bearer alone and whose
/// every tool is a route of this place's. With none it answers the <c>401</c> that starts a client's own sign-in, at
/// the protected resource's description (<see cref="Description"/>), on a public place as on a private one.</summary>
public static class ToolDoor
{
    /// <summary>The edge's path under the base URL, which is Koan's own.</summary>
    public const string Path = "/mcp";

    /// <summary>How a client signs in there, in the words <c>tangent.json</c> states it.</summary>
    public const string SigningIn = "A bearer alone: with none it answers 401, which starts a standard client's own sign-in; an agent with an atproto identity of its own presents the access_token its proof door answers.";

    public static string Address(string baseUrl) => baseUrl + Path;

    /// <summary>The address of the description of the protected resource (RFC 9728) under <paramref name="baseUrl"/>.</summary>
    public static string Description(string baseUrl) => baseUrl + "/.well-known/oauth-protected-resource" + Path;
}
