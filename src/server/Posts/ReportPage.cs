using Tangent.Render;

namespace Tangent.Posts;

/// <summary>The report of one Post, one resource both ways: the <see cref="Reported"/> Post, quoted for context, and
/// either the form that reports it or, once the reader's report was received (<see cref="Received"/>), only that.
/// <see cref="Report"/>, present only where the reader may report it, takes <c>body</c>, the reason, and <c>id</c>, the
/// report's TID, with one minted for it when it was rendered, so a form sent twice is one report. <see cref="Gate"/> is
/// why the reader may not report it, null when they may; <see cref="SignIn"/>
/// is where a visitor signs in, coming back here. After a refusal the form keeps the reason that was sent and says why,
/// under the reason or above the form. Nothing here names the moderators' Topic a report starts.</summary>
[Render.Page(Render.Layout.Panel)]
public sealed record ReportPage(
    QuotedPost Reported,
    bool Received,
    Form? Report,
    string? Gate,
    string? SignIn,
    string Place,
    string Door,
    Who? Who,
    string? Body = null,
    string? Failure = null,
    string? BodyFailure = null)
{
    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.PanelTitle Heading => new(Received ? "Report received" : "Report this post",
        "From " + Render.Sentence.Link(Reported.Topic, Reported.TopicAddress) + " in " + Render.Sentence.Link(Reported.Tangent, Reported.TangentAddress));

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public QuotedPost Quoted => Reported;

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Sentence? Thanks => Received ? new("The moderators have your report. Only they can read it.") : null;

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Onward? Back => Received ? new("Back to the post", Reported.Address) { Back = true } : null;

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Notice? Line => Received ? null : Render.Notice.Of(Gate, Failure, SignIn);

    /// <summary>The report's form: its reason, under which a refusal of it shows, sent with the TID minted for it; read only for
    /// a reader who may not report.</summary>
    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Form? Reporting => Received || (Report is null && Gate is null) ? null
        : (Report ?? (PostWrites.ReportForm("", "") with { ReadOnly = true }))
            .Changing(PostWrites.BodyField, item => item with { Value = Body, Failure = BodyFailure, Rows = 3, Focus = Gate is null })
            with { Key = "report", By = Who is { } who ? new Render.Signed("Reporting as", "@" + who.Handle) : null };

    /// <summary>The frame the shell draws around the page: the report as its own crumb past the Post's Tangent and Topic, and
    /// who reads, or where a visitor signs in.</summary>
    public static Render.Frame Frame(ReportPage page)
        => new(page.Place, page.Door,
            [new Render.Crumb("Report")],
            page.Who, page.SignIn);
}
