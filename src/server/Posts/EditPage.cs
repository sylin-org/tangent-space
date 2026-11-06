using Tangent.Render;

namespace Tangent.Posts;

/// <summary>The form that edits a Post, with its inline removal, one resource both ways. It names the Post, its
/// address, its Tangent and its Topic; <see cref="Edit"/> takes <c>body</c>, <c>title</c> on a root, and <c>version</c>,
/// the <see cref="Version"/> the editor read, and <see cref="Remove"/> removes the Post, each present only where the
/// reader may do it. <see cref="Can"/> is what the reader holds on the Post; <see cref="Gate"/> is why they may not edit
/// it and <see cref="RemoveGate"/> why they may not remove it, each null when they may. A tombstone is <see cref="Removed"/> and holds no body. After a refusal the form keeps what was
/// sent and says why; after an edit made from another version it holds the current text and version, and the words
/// that were not saved in <see cref="SentTitle"/> and <see cref="SentBody"/>. <see cref="BodyOptional"/> marks a Topic's
/// root taken on a tangent, which may keep no body of its author's own. <see cref="Edited"/> says who other than its
/// author last edited the words the form holds, where someone did. <see cref="Bring"/> is the form that brings an image
/// in, for an editor who may edit it. On an article, <see cref="CoverId"/> is its cover, <see cref="Author"/> who wrote it
/// and <see cref="CreatedAt"/> when.</summary>
[Render.Page(Render.Layout.Panel)]
public sealed record EditPage(
    string Id,
    string Address,
    string Tangent,
    string TangentName,
    string TangentAddress,
    string TopicTitle,
    string TopicAddress,
    bool Root,
    string? Title,
    string Body,
    long Version,
    bool Removed,
    Form? Edit,
    Form? Remove,
    IReadOnlyList<string> Can,
    string? Gate,
    string? RemoveGate,
    string? SignIn,
    string Place,
    string Door,
    Who? Who,
    string? Failure = null,
    string? FailureGate = null,
    string? TitleFailure = null,
    string? BodyFailure = null,
    string? SentTitle = null,
    string? SentBody = null,
    bool BodyOptional = false,
    Tangents.TangentPage.Edited? Edited = null,
    string Kind = "topic",
    string? CoverId = null,
    Form? Bring = null,
    Render.Person? Author = null,
    DateTimeOffset CreatedAt = default) : Render.ILaidOut
{
    /// <summary>An article is written in the article's own layout; anything else in one panel.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public Render.Layout Layout => Root && !Removed && Kind == PostWrites.Word(PostKind.Article) && Edit is not null && Author is not null ? Render.Layout.Reading : Render.Layout.Panel;

    [Render.Slot(Render.Slot.Rail), Newtonsoft.Json.JsonIgnore]
    public Render.TangentBack? Back => Layout == Render.Layout.Reading ? new(TangentName, TangentAddress, "All articles") : null;

    private string Heading_ => !Root ? "Edit post" : Kind == PostWrites.Word(PostKind.Article) ? "Edit article" : "Edit topic";

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.PanelTitle? Heading => Layout == Render.Layout.Reading ? null : new(Heading_, "In " + Render.Sentence.Link(TopicTitle, TopicAddress));

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Sentence? LastEdited => Layout == Render.Layout.Reading || Edited is not { } edited ? null : new("Last edited by " + Render.Sentence.Plain(edited.By.Name) + ".");

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Notice? Line => Layout == Render.Layout.Reading ? null
        : Failure is { } failure ? new Render.Notice(Render.Sentence.Plain(failure), FailureGate) : Render.Notice.Of(Gate, null, SignIn);

    /// <summary>The editor's props, for an editor who may edit: where the place's images live, where a picture is brought in,
    /// whose draft of this version it keeps, and where the suggestions of this Topic are read.</summary>
    private string? EditorProps => Gate is null && Edit is not null
        ? System.Text.Json.JsonSerializer.Serialize(new
        {
            door = Door,
            bring = Bring?.Action,
            participant = Who?.Id,
            edit = Who is null ? null : "edit:" + Id + ":" + Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            suggest = SuggestionsController.Address(TangentAddress, TopicSlug),
            post = true
        })
        : null;

    /// <summary>The form a Post is edited in: an article in the article's own layout, anything else in one panel with its title
    /// where it is a Topic's root, its body and the words sent that were not saved; nothing for a removed Post.</summary>
    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.IPart? Writing => Layout == Render.Layout.Reading && Edit is { } writing && Author is { } author
        ? new Render.ArticleEditor
        {
            Tool = "edit", Heading = "Edit article", TangentAddress = TangentAddress, Action = writing.Action, Author = author, At = CreatedAt, Door = Door,
            TitleId = "edit-title", BodyId = "edit-body", Island = "submitting", Bring = Bring, Participant = Who?.Id,
            EditScope = "edit:" + Id + ":" + Version.ToString(System.Globalization.CultureInfo.InvariantCulture), Topic = TopicSlug, Title = Title, Body = Body,
            CoverId = CoverId, Minutes = Tangents.TangentPage.ReadTime(Body), Failure = Failure, FailureGate = FailureGate, TitleFailure = TitleFailure,
            BodyFailure = BodyFailure, Hidden = [new(PostWrites.VersionField, "", writing.Value(PostWrites.VersionField), Render.Form.Input.Hidden)],
            EditedBy = Edited?.By.Name, SentTitle = SentTitle, SentBody = SentBody, Cancel = new Render.Onward("Cancel", Address), Submit = "Save", Busy = "Saving…"
        }
        : Removed ? null : new Render.Form(Edit?.Action ?? "", HttpMethods.Post,
        [
            .. Edit is { } edit ? new[] { new Render.Form.Field(PostWrites.VersionField, "", edit.Value(PostWrites.VersionField), Render.Form.Input.Hidden) } : [],
            .. Root ? new[] { new Render.Form.Field(PostWrites.TitleField, "", Title) { Label = "Title", Limit = Post.TitleLimit, Required = true, Failure = TitleFailure, Focus = TitleFailure is not null || BodyFailure is null } } : [],
            new(PostWrites.BodyField, "", Body, Render.Form.Input.Markdown)
            {
                Label = "Post", Rows = 12, Required = !BodyOptional, Failure = BodyFailure, Focus = !Root, Island = Gate is null && Edit is not null ? "article" : null, IslandProps = EditorProps
            },
            .. SentBody is { } sent ? new[] { new Render.Form.Field("sent", "", sent, Render.Form.Input.Area) { Label = "Your words, not saved", Note = SentTitle is { } sentTitle ? Render.Sentence.Plain(sentTitle) : null, Rows = 6, ReadOnly = true } } : []
        ])
        {
            Tool = "edit", ReadOnly = Gate is not null || Edit is null, Island = "submitting", Cancel = new Render.Onward("Cancel", Address), Submit = "Save", Busy = "Saving…"
        };

    /// <summary>Removing the Post, confirmed in the page.</summary>
    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Form? Removing => Removed || Remove is not { } remove ? null : remove with
    {
        Tool = "remove", Submit = "Remove",
        Opens = Layout == Render.Layout.Reading ? "Remove this article" : Root ? "Remove this topic’s opening post" : "Remove this post",
        Confirm = Layout == Render.Layout.Reading ? "Its words leave the page and a tombstone stays in their place; its replies stay as they are."
            : Root ? "Its words leave the page and a tombstone stays in their place; the topic’s replies stay as they are."
            : "Its words leave the page and a tombstone stays in their place."
    };

    /// <summary>The frame the shell draws around the page: the edit as its own crumb past the Post's Tangent and Topic, and who
    /// reads, or where a visitor signs in.</summary>
    public static Render.Frame Frame(EditPage page)
        => new(page.Place, page.Door, [new Render.Crumb(page.Heading_)], page.Who, page.SignIn);

    /// <summary>The Topic's slug, the last segment of its address.</summary>
    public string TopicSlug => Uri.UnescapeDataString(TopicAddress[(TopicAddress.LastIndexOf('/') + 1)..]);
}
