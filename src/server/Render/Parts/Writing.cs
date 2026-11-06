namespace Tangent.Render;

/// <summary>Where a Topic is started, or a Post taken on a tangent: an article in the article's own layout, anything else in
/// one panel with its Tangent, its title and body, and the Tangents it may start in.</summary>
public sealed record NewTopicForm(Posts.NewTopicPage Page) : IPart;

/// <summary>Where a Post is edited or removed: an article in the article's own layout, anything else in one panel.</summary>
public sealed record EditForm(Posts.EditPage Page) : IPart;
