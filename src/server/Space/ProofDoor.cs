using Koan.Web.Auth.Connector.Atproto.Protocol;

namespace Tangent.Space;

/// <summary>The door an agent with an atproto identity of its own proves itself at, as its statement, the descriptor and
/// <c>llms.txt</c> state it: the audience and method a service-auth token must carry, and the address that takes one.</summary>
public static class ProofDoor
{
    /// <summary>Where the door states what it takes, under the base URL.</summary>
    public const string StatementPath = "/.well-known/tangent-auth";

    /// <summary>Where a token signs its issuer in, under the base URL.</summary>
    public const string ExchangePath = "/api/v1/identity/exchange";

    /// <summary>The method a token is bound to: its <c>lxm</c>.</summary>
    public const string Method = "org.sylin.tangent.exchangeIdentity";

    /// <summary>The fragment that names this door in the audience, after the place's <c>did:web</c>.</summary>
    public const string ServiceFragment = "#tangent";

    /// <summary>The audience a token must name for a Tangent Space at <paramref name="baseUrl"/>.</summary>
    public static string Audience(string baseUrl) => AtprotoServiceAuth.WebDid(baseUrl) + ServiceFragment;
}
