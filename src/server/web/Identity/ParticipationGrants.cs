namespace Tangent.Identity;

public static class ParticipationGrants
{
    public const string Welcome = "welcome";
    public const string Read = "read";
    public const string Post = "post";
    /// <summary>Coarse management transport grant. Never issued implicitly; the issuer must separately prove current host management authority.</summary>
    public const string Manage = "manage";
    internal static bool IsKnown(string grant) => grant is Welcome or Read or Post or Manage;
}
