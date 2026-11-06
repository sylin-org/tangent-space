namespace Tangent.Render;

/// <summary>A Tangent's head on its page: its tile, name and description, how many Topics were started there and, where the
/// page holds every one, their replies (<see cref="Replies"/>), who can read it, and its settings for a reader who may change
/// them.</summary>
public sealed record TangentHead(TangentEntry Tangent, bool Private, int? Replies, string? Settings) : IPart;

/// <summary>The ways a Tangent's page lists its Topics, in its rail: all of them, current unless the page continues further
/// back, and for articles the months and authors the page's articles can be narrowed to.</summary>
public sealed record TopicViews(TangentEntry Tangent, bool Continued, IReadOnlyList<TopicEntry> Topics) : IPart;

/// <summary>Who can read a Tangent, in its rail: anyone, or only the participants it is open to.</summary>
public sealed record AudienceNote(bool Private) : IPart;

/// <summary>Where a participant who joined a private Tangent leaves it.</summary>
public sealed record LeaveTangent(Form Leave) : IPart;

/// <summary>A page of a Tangent's Topics, latest activity first: discussions as rows of a table, articles as cards under a
/// lead on the first page; where none are, one sentence and one next step — where a Topic is started, for a reader who may,
/// or signing in, for a visitor; and the way to the page after and back to the latest.</summary>
public sealed record TangentTopics(TangentEntry Tangent, IReadOnlyList<TopicEntry> Topics, bool Continued, string? NewTopic, string? SignIn,
    string? Next) : IPart;
