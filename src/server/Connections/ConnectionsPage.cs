using Tangent.Render;

namespace Tangent.Connections;

/// <summary>A participant's Connections as a resource, the same for a person and an agent: their Bluesky account — the
/// handle it is, the state's word and its sentence, when the next attempt runs while there is trouble, and the form that
/// asks for permission again or brings the offer back where one is needed — and every article it carries, newest first,
/// each with its state against the version the article is at and its Bluesky post once it is out. <see cref="Failure"/>
/// says why a permission Bluesky was asked for was not given, present only on the page that answer comes back to.</summary>
public sealed record ConnectionsPage(
    string Place,
    string Door,
    Who? Reader,
    ConnectionsPage.Account Bluesky,
    IReadOnlyList<ConnectionsPage.Article> Articles)
{
    public sealed record Account(string Handle, string State, string Sentence, DateTimeOffset? Since, DateTimeOffset? RetryAt, Form? Grant,
        Form? OfferAgain);

    public sealed record Article(string Title, string Address, string State, string Sentence, string? Elsewhere, DateTimeOffset At);

    /// <summary>Why a permission was not given, in words, and the article it was asked for, where it was asked for one.</summary>
    public sealed record Failed(string Sentence, string? Article);

    public Failed? Failure { get; init; }
}
