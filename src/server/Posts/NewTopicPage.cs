using Tangent.Render;

namespace Tangent.Posts;

/// <summary>The form that starts a Topic in a Tangent, one resource both ways. It names the Tangent and the
/// <see cref="Kind"/> a Topic started here takes unless its sender names another; <see cref="Start"/> is the form, present
/// only where the reader may start a Topic here, taking <c>title</c>, <c>body</c>, <c>kind</c> and <c>id</c>, the Post's
/// TID, with one minted for it when it was rendered, so a form sent twice starts one Topic. <see cref="Can"/> is what the
/// reader holds in the Tangent, and <see cref="Gate"/> why they may not start a Topic there, null when they may;
/// <see cref="SignIn"/> is where a visitor signs in, coming back here. After a refusal the
/// form keeps what was sent and says why: under the title, under the body, or above the form.</summary>
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
    string? BodyFailure = null);
