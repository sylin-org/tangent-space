using Tangent.Render;

namespace Tangent.Connections;

/// <summary>A participant's Connections as a resource, the same for a person and an agent: their Bluesky account — the
/// handle it is, the state's word and its sentence, when the next attempt runs while there is trouble, the form that
/// asks for permission again or brings the offer back where one is needed, and the one that stops their new articles
/// going out on their own or has them go out on their own again — and one page of the articles it carries, newest first,
/// each with its state against the version the article is at, its Bluesky post once it is out and the form that takes
/// that down — with <see cref="Next"/> the page after this one, null on the last, and <see cref="First"/> the first,
/// on a page after it — and the way to use another Bluesky account (<see cref="Another"/>). <see cref="Failure"/> says
/// why a permission Bluesky was asked for was not given, present only on the page that answer comes back to.</summary>
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
            [
                .. Bluesky.Grant is { } grant ? new IPart[] { grant with { Tool = "grant", Submit = "Grant it again" } } : [],
                .. Bluesky.OfferAgain is { } again ? new IPart[] { again with { Tool = "offer-again", Submit = "Offer it again", Quiet = true } } : [],
                .. Bluesky.Stop is { } stop
                    ? new IPart[]
                    {
                        stop with
                        {
                            Tool = "stop-publishing", Opens = "Stop publishing new articles", Submit = "Stop",
                            Confirm = "Each new article then goes out only once you publish it from its page. What is out already stays on Bluesky and follows your edits and removals."
                        }
                    }
                    : [],
                .. Bluesky.Resume is { } resume ? new IPart[] { resume with { Tool = "resume-publishing", Submit = "Publish new articles on their own", Quiet = true } } : []
            ])
        {
            Detail = Render.Sentence.Plain(Bluesky.Handle), More = Render.Sentence.Plain(Bluesky.Says), State = Bluesky.State
        }
    ]);

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Participants.AnotherAccount.Offer Switching => Another;

    /// <summary>Each article of the page the account carries, with its state, its Bluesky post once it is out and the
    /// press that takes that down; one gone with its Tangent has no page to link. A page after the first says where the
    /// list continued if it holds nothing.</summary>
    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.EntryList? Carried => Articles.Count == 0 && First is null ? null : new(Articles.Select((article, index) =>
        new Render.Entry(article.Address is { } at ? Render.Sentence.Link(article.Title, at) : Render.Sentence.Plain(article.Title),
        [
            .. article.Elsewhere is { } elsewhere ? new IPart[] { new Render.LinkButton("View on Bluesky", elsewhere) { Outside = true } } : [],
            .. article.Retract is { } retract ? new IPart[] { Publishing.Retracting(retract) with { Key = "retract-" + index } } : []
        ])
        {
            Detail = Render.Sentence.Plain(article.Sentence), State = article.State
        }).ToList(), "Articles", "Nothing further.");

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Pager Pages => new("More articles", First is { } first ? new Render.Onward("First page", first) : null,
        Next is { } next ? new Render.Onward("Next page", next) : null);

    /// <summary>The frame the shell draws around the page: the page as the one crumb, and the account of whoever reads it.</summary>
    public static Render.Frame Frame(ConnectionsPage page) => new(page.Place, page.Door, [new Render.Crumb("Connections")], page.Reader);

    public sealed record Account(string Handle, string State, string Sentence, DateTimeOffset? Since, DateTimeOffset? RetryAt, Form? Grant,
        Form? OfferAgain)
    {
        /// <summary>The form that stops new articles going out on their own, where they do.</summary>
        public Form? Stop { get; init; }

        /// <summary>The form that has new articles go out on their own again, where only the chosen ones do.</summary>
        public Form? Resume { get; init; }

        /// <summary>Its sentence, then when the next attempt runs while there is trouble, and since when it stands.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public string Says => Sentence
            + (RetryAt is { } retry ? " Next attempt " + retry.ToUniversalTime().ToString("HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture) + "." : "")
            + (Since is { } since ? " Since " + since.ToUniversalTime().ToString("MMMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture) + "." : "");
    }

    public sealed record Article(string Title, string? Address, string State, string Sentence, string? Elsewhere, DateTimeOffset At)
    {
        /// <summary>The form that takes its copy down from Bluesky while it stays here, where its copy is out and its author
        /// may.</summary>
        public Form? Retract { get; init; }
    }

    /// <summary>Why a permission was not given, in words, and the article it was asked for, where it was asked for one.</summary>
    public sealed record Failed(string Sentence, string? Article);

    public Failed? Failure { get; init; }

    /// <summary>The address of the page of articles after this one, null on the last.</summary>
    public string? Next { get; init; }

    /// <summary>The address of the first page, on a page after it.</summary>
    public string? First { get; init; }
}
