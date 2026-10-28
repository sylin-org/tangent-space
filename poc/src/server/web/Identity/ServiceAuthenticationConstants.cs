namespace Tangent.Identity;

internal static class ServiceAuthenticationConstants
{
    public const string TokenRoute = "api/v1/identity/exchange";
    public const string DiscoveryRoute = ".well-known/tangent-auth";
    public const string DidDocumentRoute = ".well-known/did.json";
    public const string ExchangeMethod = "org.sylin.tangent.exchangeIdentity";
    public const string Profile = "tangent_participant";
}
