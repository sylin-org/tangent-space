using Tangent.Render;
using Tangent.Space;

namespace Tangent.Participants;

/// <summary>The place's applications, one resource both ways: the <see cref="Sections"/> of the settings, applications
/// marked current, and one bounded page of <see cref="Applications"/>, newest first, each waiting or declined, with
/// <see cref="Next"/>, the address of the older ones, null on the last page, and <see cref="First"/>, the newest page's,
/// for a page that <see cref="Continued"/> from a cursor. <see cref="Admit"/> and <see cref="Decline"/> are the forms that
/// decide, each taking the applicant's id in <c>participant</c>, present only where the reader may decide; a waiting
/// application is admitted or declined, a declined one only admitted. <see cref="Can"/> is what the
/// reader holds in the place, and <see cref="Gate"/> why they may not decide, null when they may, when the list is empty
/// whatever waits; <see cref="SignIn"/> is where a visitor signs in, coming back here. <see cref="Day"/> is the UTC day the
/// times are written for. After a refusal the page says why above the list.</summary>
public sealed record ApplicationsPage(
    IReadOnlyList<SettingsSection> Sections,
    IReadOnlyList<ApplicationsPage.Applicant> Applications,
    string? Next,
    bool Continued,
    string First,
    Form? Admit,
    Form? Decline,
    IReadOnlyList<string> Can,
    string? Gate,
    string? SignIn,
    string Place,
    string Door,
    Who? Who,
    DateOnly Day,
    string? Failure = null)
{
    /// <summary>One applicant: their id, the name to show, their handle, their profile's address, the state of their
    /// application (<c>waiting</c> or <c>declined</c>) and when they applied.</summary>
    public sealed record Applicant(string Id, string Name, string Handle, string Profile, string State, DateTimeOffset AppliedAt);

    public const string Waiting = "waiting";

    public const string Declined = "declined";
}

/// <summary>An application as a decision leaves it, for an agent: the applicant's id, name and handle, their standing
/// now (<c>admitted</c> or <c>declined</c>), the applications' address, and what the decider <see cref="Can"/> do in the
/// place.</summary>
public sealed record DecidedApplication(string Id, string Name, string Handle, string Standing, string Applications, IReadOnlyList<string> Can);
