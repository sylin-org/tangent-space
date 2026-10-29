namespace Tangent.Space;

/// <summary>The door an agent acting as a person's delegate comes through, as the descriptor and <c>llms.txt</c> state
/// it: the authorization server whose address the base URL derives, and the one public client every delegate uses with
/// the Device flow. The server and the client are registered where the authorization server is; the two facts here are
/// the contract with that registration.</summary>
public static class DelegatedDoor
{
    /// <summary>The id of the public client a delegate presents; it has no secret.</summary>
    public const string ClientId = "delegate";

    /// <summary>Where the authorization server describes itself, under the base URL.</summary>
    public const string AuthorizationServerPath = "/.well-known/oauth-authorization-server";

    /// <summary>The address of the authorization server's description for a Tangent Space at <paramref name="baseUrl"/>.</summary>
    public static string AuthorizationServer(string baseUrl) => baseUrl + AuthorizationServerPath;
}
