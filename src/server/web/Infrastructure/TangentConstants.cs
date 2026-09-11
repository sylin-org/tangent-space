namespace TangentSpace.Infrastructure;

internal static class TangentConstants
{
    public const string SiteId = "site";
    public const string SiteConfiguration = "Tangent:Site";
    public const string ArrivalTransaction = "tangent-arrival";
    public const string WelcomeRoute = "api/site";
    public const string AtprotoProvider = "atproto";
    public const string SignInPath = "/auth/atproto/challenge";
    public const string SignOutPath = "/auth/logout";
    public const string KeyDirectory = "data/keys";
}
