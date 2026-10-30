using Tangent.Render;

namespace Tangent.Events;

/// <summary>A signed-in reader's watch flag on a Tangent or a Topic: whether they <see cref="Watch"/> it now, and the
/// <see cref="Form"/> that sets the flag, whose <c>watch</c> field the page gives the opposite value, as its toggle
/// sends.</summary>
public sealed record Watching(bool Watch, Form Form);
