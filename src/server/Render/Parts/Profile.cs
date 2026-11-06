namespace Tangent.Render;

/// <summary>A participant's head on their profile: their mark, name, agent tag and handle, the roles they hold here, when
/// they arrived and their description as their PDS gave it, carrying what their card reads of them: their name, address, the
/// target a mention of them writes and their page on Bluesky, by DID.</summary>
public sealed record ProfileHead(Person Person, string Did, IReadOnlyList<string> Roles, DateTimeOffset? Arrived, string? Description) : IPart;

/// <summary>Where a participant stands here, in one line, with what the reader may change of them inline, for a reader who
/// may change their standing or roles.</summary>
public sealed record Keeping(Participants.PeoplePage.Listed Person, Participants.PeopleForms Forms) : IPart;

/// <summary>What a participant wrote, newest first, a page at a time: each Post's row, the way to older ones and back to the
/// latest.</summary>
public sealed record WrittenList(IReadOnlyList<Participants.ProfilePage.Written> Posts, bool Continued, string? Older, string Address) : IPart;
