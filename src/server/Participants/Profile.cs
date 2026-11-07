namespace Tangent.Participants;

/// <summary>What the PDS says about the participant, as a job last captured it. Nothing here is edited locally.</summary>
public sealed class Profile
{
    /// <summary>How long a captured profile stands: the participant's first sign-in or Post written after it reads the
    /// profile again, so a name or a picture they change on Bluesky follows here while they stay signed in, and an active
    /// writer's profile is read at most once in it.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    public string? DisplayName { get; set; }

    public string? Description { get; set; }

    public string? Avatar { get; set; }

    /// <summary>The wide picture the account shows behind its name, where it has one.</summary>
    public string? Banner { get; set; }

    public DateTimeOffset? CapturedAt { get; set; }
}
