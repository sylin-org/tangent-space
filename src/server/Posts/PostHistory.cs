using Tangent.Render;
using Tangent.Tangents;

namespace Tangent.Posts;

/// <summary>A Post's earlier versions, the same for a person and an agent: each version the audit partition kept before
/// an edit (<see cref="AuditPartition"/>), newest first, at most <see cref="PostHistoryController.Limit"/>, with who wrote
/// it and when; <see cref="More"/> says older ones stand beyond those. A removed Post's words went with it, and it has
/// none. <see cref="Address"/> is the Post's own; <see cref="Topic"/>, <see cref="Tangent"/> and their addresses say where
/// it stands; <see cref="Place"/>, <see cref="Door"/>, <see cref="SignIn"/> and <see cref="Who"/> where the page is and
/// who reads it, <see cref="Day"/> the UTC day its times are written for.</summary>
public sealed record PostHistory(
    string PostId,
    string Address,
    long Current,
    IReadOnlyList<PostHistory.Version> Versions,
    bool More,
    string Topic,
    string TopicAddress,
    string Tangent,
    string TangentAddress,
    string Place,
    string Door,
    string? SignIn,
    Who? Who,
    DateOnly Day)
{
    /// <summary>One earlier version: its number, who wrote these words — the author, or whoever else edited them in —
    /// when, the Markdown as it stood, and the participants it mentions as they are now.</summary>
    public sealed record Version(long Number, TangentPage.Author By, DateTimeOffset At, string Body, IReadOnlyList<TopicPage.Mention> Mentions);
}
