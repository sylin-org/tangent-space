using Tangent.Render;

namespace Tangent.Posts;

/// <summary>The form that starts a Topic in a Tangent, one resource both ways. It names the Tangent and the
/// <see cref="Kind"/> a Topic started here takes; <see cref="Start"/> is the form, present only where the reader may
/// start a Topic here, taking <c>title</c>, <c>body</c>, an article's <c>cover</c>, and <c>id</c>, the Post's
/// TID, with one minted for it when it was rendered, so a form sent twice starts one Topic. <see cref="Can"/> is what the
/// reader holds in the Tangent, and <see cref="Gate"/> why they may not start a Topic there, null when they may;
/// <see cref="SignIn"/> is where a visitor signs in, coming back here. After a refusal the
/// form keeps what was sent and says why: under the title, under the body, or above the form. <see cref="Bring"/> is the
/// form that brings an image in, for a writer who may start a Topic here; in an articles Tangent <see cref="CoverId"/> is
/// the cover the form holds, and <see cref="Day"/> is the UTC day the article would carry. A Topic that takes a Post on a
/// tangent quotes it as <see cref="Origin"/>, offers a title from its first words, and lists as <see cref="Choices"/> the
/// Tangents the reader may start it in, each a link to this form there, the next page and the first a link away.</summary>
[Render.Page(Render.Layout.Panel)]
public sealed record NewTopicPage(
    string Tangent,
    string TangentName,
    string TangentAddress,
    string Kind,
    Form? Start,
    IReadOnlyList<string> Can,
    string? Gate,
    string? SignIn,
    string Place,
    string Door,
    Who? Who,
    string? Title = null,
    string? Body = null,
    string? Failure = null,
    string? TitleFailure = null,
    string? BodyFailure = null,
    Form? Bring = null,
    string? CoverId = null,
    DateOnly Day = default,
    QuotedPost? Origin = null,
    IReadOnlyList<NewTopicPage.Choice>? Choices = null) : Render.ILaidOut
{
    /// <summary>An article is written in the article's own layout; anything else in one panel.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public Render.Layout Layout => Kind == PostWrites.Word(PostKind.Article) && Start is not null && Who is not null ? Render.Layout.Reading : Render.Layout.Panel;

    [Render.Slot(Render.Slot.Rail), Newtonsoft.Json.JsonIgnore]
    public Render.TangentBack? Back => Layout == Render.Layout.Reading ? new(TangentName, TangentAddress, "All articles") : null;

    private bool IsArticle => Kind == PostWrites.Word(PostKind.Article);

    /// <summary>Whether the composer island takes the form: for a participant who may start a Topic here.</summary>
    private bool Composing => Start is not null && Who is not null;

    private string Heading_ => Origin is not null ? "Take this on a tangent" : IsArticle ? "New article" : "New topic";

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.PanelTitle? Heading => Layout == Render.Layout.Reading ? null : new(Heading_, Origin is { } origin
        ? "From " + Render.Sentence.Link(origin.Topic, origin.TopicAddress) + " in " + Render.Sentence.Link(origin.Tangent, origin.TangentAddress)
        : "Posting in " + Render.Sentence.Link(TangentName, TangentAddress));

    /// <summary>Taking a Post on a tangent: the Post quoted, then the Tangents to start the Topic in, each a link to this form
    /// there, this one marked, and the rest a page away.</summary>
    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public QuotedPost? Quoted => Origin;

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.TangentList? StartIn => Origin is null || Choices is not { Count: > 0 } choices ? null : new("Start it in", choices.Select(choice =>
        new Render.TangentEntry(choice.Slug, choice.Name, choice.Kind, choice.Address, 0)
        {
            Tile = choice.Tile,
            Current = choice.Current,
            Says = choice.Kind == PostWrites.Word(PostKind.Article) ? "Starts an article" : "Starts a topic"
        }).ToList());

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Pager? MoreTangents => Origin is null ? null : new("More Tangents", First is { } first ? new Render.Onward("First Tangents", first) : null,
        More is { } more ? new Render.Onward("More Tangents →", more) : null);

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Notice? Line => Layout == Render.Layout.Reading ? null : Render.Notice.Of(Gate, null, SignIn);

    /// <summary>The composer's props: whose drafts these are, the Tangent they are kept under, and the prefix of the address that
    /// names a new Topic by its root's TID until its slug is known.</summary>
    private string? ComposerProps => Composing
        ? System.Text.Json.JsonSerializer.Serialize(new
        {
            participant = Who!.Id,
            scope = Origin is { } origin ? "origin:" + origin.Id : "tangent:" + Tangent,
            locate = TangentAddress + "/",
            noun = IsArticle ? "article" : "topic"
        })
        : null;

    /// <summary>The editor's props: where the place's images live, where a picture is brought in, whose words these are and
    /// where the suggestions of this Tangent are read.</summary>
    private string? EditorProps => Composing
        ? System.Text.Json.JsonSerializer.Serialize(new
        {
            door = Door,
            bring = Bring?.Action,
            participant = Who!.Id,
            suggest = SuggestionsController.Address(TangentAddress),
            post = true
        })
        : null;

    private IReadOnlyList<Render.Form.Field> Given => Start is not { } start ? [] :
    [
        new(PostWrites.IdField, "", start.Value(PostWrites.IdField), Render.Form.Input.Hidden),
        .. Origin is { } origin ? new[] { new Render.Form.Field(PostWrites.OriginField, "", origin.Id, Render.Form.Input.Hidden) } : []
    ];

    /// <summary>The form a Topic is started in: an article in the article's own layout, anything else in one panel with its
    /// title and body, read only for a reader who may not start one.</summary>
    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.IPart? Writing => Layout == Render.Layout.Reading && Start is { } writing && Who is { } author
        ? new Render.ArticleEditor
        {
            Tool = "start-topic", Heading = Heading_, TangentAddress = TangentAddress, Action = writing.Action,
            Author = new Render.Person(author.Id, author.Handle, author.Name, "person", author.Avatar),
            At = new DateTimeOffset(Day, TimeOnly.MinValue, TimeSpan.Zero), Door = Door, TitleId = "new-topic-title", BodyId = "new-topic-body",
            Island = "composer", IslandProps = ComposerProps, Bring = Bring, Participant = author.Id, Title = Title, Body = Body, CoverId = CoverId,
            Failure = Failure, TitleFailure = TitleFailure, BodyFailure = BodyFailure, Hidden = Given,
            By = new Render.Signed("Writing in", TangentName, TangentAddress), Submit = "Publish", Busy = "Publishing…"
        }
        : new Render.Form(Start?.Action ?? "", HttpMethods.Post,
        [
            .. Given,
            new(PostWrites.TitleField, "", Title) { Label = "Title", Limit = Post.TitleLimit, Required = true, Failure = TitleFailure, Focus = TitleFailure is null && BodyFailure is null },
            new(PostWrites.BodyField, "", Body, Render.Form.Input.Markdown)
            {
                Label = Origin is not null ? "Your words (optional)" : IsArticle ? "Article" : "Post", Rows = 12, Required = Origin is null, Failure = BodyFailure,
                Island = Composing ? "article" : null, IslandProps = EditorProps
            }
        ])
        {
            Tool = "start-topic", ReadOnly = Gate is not null || Start is null, Failure = Failure, Island = Composing ? "composer" : null, IslandProps = ComposerProps,
            By = Who is { } who ? new Render.Signed("Posting as", "@" + who.Handle) : null,
            Submit = Origin is null ? "Post" : "Start the topic", Busy = Origin is null ? "Posting…" : "Starting…"
        };

    /// <summary>The frame the shell draws around the page: the form as its own crumb past the Tangent it starts in, and who
    /// reads, or where a visitor signs in.</summary>
    public static Render.Frame Frame(NewTopicPage page)
        => new(page.Place, page.Door, [new Render.Crumb(page.Origin is not null ? "Take this on a tangent" : page.IsArticle ? "New article" : "New topic")],
            page.Who, page.SignIn);

    /// <summary>The address of the form with the next page of Tangents to choose from; null on the last.</summary>
    public string? More { get; init; }

    /// <summary>The address of the form with the first page of Tangents, on a later page.</summary>
    public string? First { get; init; }

    /// <summary>A Tangent the reader may start the Topic in: its slug, its name, the kind a Topic started there takes,
    /// the address of this form there, whether it is this one, and the tile it wears.</summary>
    public sealed record Choice(string Slug, string Name, string Kind, string Address, bool Current, Render.Tile Tile);
}
