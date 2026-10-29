using Tangent.Render;

namespace Tangent.Events;

/// <summary>The form that moves the reader's checkpoint on a Topic to the sequence the page showed it through: where it
/// posts, that sequence in the <c>through</c> field, and its antiforgery token. A page carries it while what it shows
/// reaches past the reader's checkpoint; an agent posts the same field.</summary>
public sealed record TopicSeen(string Action, long Through, FormToken Token);
