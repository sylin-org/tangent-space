using Tangent.Render;

namespace Tangent.Connections;

/// <summary>A participant's Connections as a resource, the same for a person and an agent: their Bluesky account — the
/// handle it is, the state's word and its sentence, when the next attempt runs while there is trouble, and the form that
/// asks for permission again or brings the offer back where one is needed — and every article it carries, newest first,
/// each with its state against the version the article is at and its Bluesky post once it is out — and the way to use
/// another Bluesky account (<see cref="Another"/>). <see cref="Failure"/> says why a permission Bluesky was asked for was
/// not given, present only on the page that answer comes back to.</summary>
[Render.Page(Render.Layout.Panel)]
public sealed record ConnectionsPage(
    string Place,
    string Door,
    Who? Reader,
    ConnectionsPage.Account Bluesky,
    IReadOnlyList<ConnectionsPage.Article> Articles,
    Participants.AnotherAccount.Offer Another)
{
    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.PanelTitle Heading => new("Connections", "Yours, at " + Render.Sentence.Link(Place, Door));

    /// <summary>Why a permission Bluesky was asked for was not given, with the way back to the article, where that brought them.</summary>
    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Notice? Line => Failure is { } failure
        ? new(Render.Sentence.Plain(failure.Sentence) + (failure.Article is { } article ? " " + Render.Sentence.Link("Back to the article", article) : ""))
        : null;

    /// <summary>Their Bluesky account, where it stands and the one thing they may do about it.</summary>
    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.EntryList Accounts => new(
    [
        new Render.Entry(Render.Sentence.Strong("Bluesky"),
            Bluesky.Grant is { } grant ? [grant with { Tool = "grant", Submit = "Grant it again" }]
            : Bluesky.OfferAgain is { } again ? [again with { Tool = "offer-again", Submit = "Offer it again", Quiet = true }]
            : null)
        {
            Detail = Render.Sentence.Plain(Bluesky.Handle), More = Render.Sentence.Plain(Bluesky.Says), State = Bluesky.State
        }
    ]);

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Participants.AnotherAccount.Offer Switching => Another;

    /// <summary>Each article the account carries, with its state and its Bluesky post once it is out; one gone with its
    /// Tangent has no page to link.</summary>
    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.EntryList? Carried => Articles.Count == 0 ? null : new(Articles.Select(article =>
        new Render.Entry(article.Address is { } at ? Render.Sentence.Link(article.Title, at) : Render.Sentence.Plain(article.Title),
            article.Elsewhere is { } elsewhere ? [new Render.LinkButton("View on Bluesky", elsewhere) { Outside = true }] : null)
        {
            Detail = Render.Sentence.Plain(article.Sentence), State = article.State
        }).ToList(), "Articles");

    /// <summary>The frame the shell draws around the page: the page as the one crumb, and the account of whoever reads it.</summary>
    public static Render.Frame Frame(ConnectionsPage page) => new(page.Place, page.Door, [new Render.Crumb("Connections")], page.Reader);

    public sealed record Account(string Handle, string State, string Sentence, DateTimeOffset? Since, DateTimeOffset? RetryAt, Form? Grant,
        Form? OfferAgain)
    {
        /// <summary>Its sentence, then when the next attempt runs while there is trouble, and since when it stands.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public string Says => Sentence
            + (RetryAt is { } retry ? " Next attempt " + retry.ToUniversalTime().ToString("HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture) + "." : "")
            + (Since is { } since ? " Since " + since.ToUniversalTime().ToString("MMMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture) + "." : "");
    }

    public sealed record Article(string Title, string? Address, string State, string Sentence, string? Elsewhere, DateTimeOffset At);

    /// <summary>Why a permission was not given, in words, and the article it was asked for, where it was asked for one.</summary>
    public sealed record Failed(string Sentence, string? Article);

    public Failed? Failure { get; init; }
}
