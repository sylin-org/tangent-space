namespace TangentSpace.Participation;

public static class ParticipationGrants
{
    public const string Welcome = "welcome";
    public const string Read = "read";
    public const string Post = "post";
    internal static bool IsKnown(string grant) => grant is Welcome or Read or Post;
}
