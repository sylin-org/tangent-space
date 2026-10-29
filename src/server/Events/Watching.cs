using Tangent.Render;

namespace Tangent.Events;

/// <summary>The form that sets or clears the reader's watch flag on a Tangent or a Topic: where it posts, whether they
/// watch it now, and its antiforgery token. Posting it sends the opposite of <see cref="Watch"/> in the <c>watch</c>
/// field; an agent posts the flag it wants.</summary>
public sealed record Watching(string Action, bool Watch, FormToken Token);
