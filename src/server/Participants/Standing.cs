namespace Tangent.Participants;

/// <summary>A participant's state in the Tangent Space. Place-wide; it precedes every capability. An applicant is
/// <see cref="Applied"/> until someone admits them, or <see cref="Declined"/> once their application was declined, which a
/// later admission still reverses.</summary>
public enum Standing
{
    Admitted,
    Applied,
    TimedOut,
    Banned,
    Declined
}
