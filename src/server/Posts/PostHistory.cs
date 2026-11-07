using Tangent.Render;
using Tangent.Tangents;

namespace Tangent.Posts;

/// <summary>A Post's earlier versions, the same for a person and an agent: each version the audit partition kept before
/// an edit (<see cref="AuditPartition"/>), newest first, at most <see cref="PostHistoryController.Limit"/>, with who wrote
/// it and when; <see cref="More"/> says older ones stand beyond those. A removed Post shows none: its words are kept in its
/// snapshot only for its removal to be undone. <see cref="Address"/> is the Post's own; <see cref="Topic"/>, <see cref="Tangent"/> and their addresses say where
/// it stands; <see cref="Place"/>, <see cref="Door"/>, <see cref="SignIn"/> and <see cref="Who"/> where the page is and
/// who reads it, <see cref="Day"/> the UTC day its times are written for.</summary>
[Render.Page(Render.Layout.Panel)]
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
    public sealed record Version(long Number, Render.Person By, DateTimeOffset At, string Body, IReadOnlyList<TopicPage.Mention> Mentions);

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Versions Shown => new(this);

    /// <summary>The frame the shell draws around the page: its history as its own crumb past the Post's Tangent and Topic, and
    /// who reads, or where a visitor signs in.</summary>
    public static Render.Frame Frame(PostHistory page) => new(page.Place, page.Door, [new Render.Crumb("Earlier versions")], page.Who, page.SignIn);
}
