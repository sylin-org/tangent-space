namespace Tangent.Render;

/// <summary>A Tangent's head on its page: its tile, name and description, how many Topics were started there and, where the
/// page holds every one, their replies (<see cref="Replies"/>), who can read it, and its settings for a reader who may change
/// them.</summary>
public sealed record TangentHead(TangentEntry Tangent, bool Private, int? Replies, string? Settings) : IPart;

/// <summary>The ways a Tangent's page lists its Topics, in its rail: all of them, current where the page is their first
/// (<see cref="All"/>), and in an articles Tangent the months they started in and the authors who started the most of
/// them, each with how many, the one the list is narrowed to current.</summary>
public sealed record TopicViews(TangentEntry Tangent, bool All, string? Month, string? Author, IReadOnlyList<TopicViews.Choice> Months,
    IReadOnlyList<TopicViews.Choice> Authors) : IPart
{
    /// <summary>One way the list narrows: a month or a participant by its key, its name, how many Topics started under it,
    /// and the address of the list narrowed to it.</summary>
    public sealed record Choice(string Key, string Name, int Count, string Address);
}

/// <summary>Who can read a Tangent, in its rail: anyone, or only the participants it is open to.</summary>
public sealed record AudienceNote(bool Private) : IPart;

/// <summary>Where a participant who joined a private Tangent leaves it.</summary>
public sealed record LeaveTangent(Form Leave) : IPart;

/// <summary>A page of a Tangent's Topics, latest activity first, whole or narrowed (<see cref="To"/>): discussions as rows
/// of a table, articles as cards under a lead on the first page; where none are, one sentence and, on the whole list, one
/// next step — where a Topic is started, for a reader who may, or signing in, for a visitor; and the way to the page after
/// and back to the latest.</summary>
public sealed record TangentTopics(TangentEntry Tangent, IReadOnlyList<TopicEntry> Topics, bool Continued, string? NewTopic, string? SignIn,
    string? Next, TangentTopics.Narrowed? To = null) : IPart
{
    /// <summary>What the list is narrowed to, in words — <c>from October 2026</c>, <c>by Ada</c>, or both — how many
    /// Topics it holds where one narrowing alone names them, and the address of its first page.</summary>
    public sealed record Narrowed(string Words, int? Count, string First);
}
