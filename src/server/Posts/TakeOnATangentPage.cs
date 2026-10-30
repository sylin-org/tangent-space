using Tangent.Render;
using Tangent.Tangents;

namespace Tangent.Posts;

/// <summary>The form that takes a Post on a tangent, one resource both ways: the <see cref="Origin"/>, quoted for
/// context, and the <see cref="Tangents"/> the reader may start a Topic in, one of which is <see cref="Chosen"/>.
/// <see cref="Take"/>, present only where the reader may take it on a tangent, takes <c>tangent</c> (the slug of the
/// Tangent chosen), <c>title</c>, <c>body</c> — the reader's own words, which may be none — and <c>id</c>, the new root's
/// TID, with one minted for it when it was rendered, so a form sent twice starts one Topic. <see cref="Can"/>
/// is what the reader holds on the origin, and <see cref="Gate"/> why they may not take it on a tangent at all, null when
/// they may; <see cref="SignIn"/> is where a visitor signs in, coming back here. After a refusal the form keeps what was
/// sent and says why: under the choice of Tangent, under the title, under the body, or above the form.</summary>
public sealed record TakeOnATangentPage(
    TakeOnATangentPage.Quoted Origin,
    IReadOnlyList<TakeOnATangentPage.Choice> Tangents,
    string? Chosen,
    Form? Take,
    IReadOnlyList<string> Can,
    string? Gate,
    string? SignIn,
    string Place,
    string Door,
    Who? Who,
    string? Title = null,
    string? Body = null,
    string? Failure = null,
    string? ChoiceFailure = null,
    string? TitleFailure = null,
    string? BodyFailure = null)
{
    /// <summary>The Post taken on a tangent: its address, who wrote it and when, its words as plain text cut short —
    /// none on a tombstone, which is <see cref="Removed"/> — the Topic and Tangent it is in, and who other than its author
    /// last edited it, where someone did.</summary>
    public sealed record Quoted(
        string Id,
        string Address,
        TangentPage.Author Author,
        DateTimeOffset CreatedAt,
        string? Text,
        bool Removed,
        string Topic,
        string TopicAddress,
        string Tangent,
        string TangentAddress,
        TangentPage.Edited? Edited = null);

    /// <summary>The address of the form with the next page of Tangents to choose from; null on the last.</summary>
    public string? More { get; init; }

    /// <summary>The address of the form with the first page of Tangents, on a later page.</summary>
    public string? First { get; init; }

    /// <summary>A Tangent the reader may start a Topic in: its slug, its name, and the kind a Topic started there takes.</summary>
    public sealed record Choice(string Slug, string Name, string Kind);
}
