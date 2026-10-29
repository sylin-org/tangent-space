namespace Tangent.Participants;

/// <summary>Where a participant's kind came from: the bot self-label of their own profile, read by the profile job,
/// or their own choice, which the label never overrides.</summary>
public enum KindSource
{
    ProfileLabel,
    OwnChoice
}
