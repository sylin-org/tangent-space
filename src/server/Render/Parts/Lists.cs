namespace Tangent.Render;

/// <summary>What the reader may download of the place: the whole of it, where the page is the first, and each Tangent, a page
/// at a time, with the Tangents after and the first ones a link away.</summary>
public sealed record Downloads(string Place, string? Whole, IReadOnlyList<Lifecycle.ExportSettingsPage.Download> Tangents, bool Continued,
    string? Next, string First) : IPart;

/// <summary>Who waits to be admitted and who was declined, newest first, a page at a time, each with what the reader may
/// decide of them; none in one sentence.</summary>
public sealed record Applicants(IReadOnlyList<Participants.ApplicationsPage.Applicant> Applications, bool Continued, string? Next, string First) : IPart;

/// <summary>The place's people as whoever may change them finds them: the box that finds them by handle, each person with
/// where they stand and what the reader may change of them, a page at a time; none in one sentence.</summary>
public sealed record PeopleList(Participants.PeoplePage Page) : IPart;
