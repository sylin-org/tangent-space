namespace Tangent.Participants;

/// <summary>What the PDS says about the participant, as a job last captured it. Nothing here is edited locally.</summary>
public sealed class Profile
{
    public string? DisplayName { get; set; }

    public string? Description { get; set; }

    public string? Avatar { get; set; }

    public DateTimeOffset? CapturedAt { get; set; }
}
