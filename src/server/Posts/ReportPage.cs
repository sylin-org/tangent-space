using Tangent.Render;

namespace Tangent.Posts;

/// <summary>The report of one Post, one resource both ways: the <see cref="Reported"/> Post, quoted for context, and
/// either the form that reports it or, once the reader's report was received (<see cref="Received"/>), only that.
/// <see cref="Report"/>, present only where the reader may report it, takes <c>body</c>, the reason, and <c>id</c>, the
/// report's TID, with one minted for it when it was rendered, so a form sent twice is one report. <see cref="Gate"/> is
/// why the reader may not report it, null when they may; <see cref="SignIn"/>
/// is where a visitor signs in, coming back here. After a refusal the form keeps the reason that was sent and says why,
/// under the reason or above the form. Nothing here names the moderators' Topic a report starts.</summary>
public sealed record ReportPage(
    TakeOnATangentPage.Quoted Reported,
    bool Received,
    Form? Report,
    string? Gate,
    string? SignIn,
    string Place,
    string Door,
    Who? Who,
    string? Body = null,
    string? Failure = null,
    string? BodyFailure = null);
