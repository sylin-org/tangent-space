using Tangent.Render;

namespace Tangent.Events;

/// <summary>The form that moves the reader's checkpoint on a Topic to the sequence the page showed it through: where it
/// posts, that sequence in the <c>through</c> field, and its antiforgery token. A page carries it while its window
/// continues from the reader's checkpoint and reaches past it, so it never offers to skip a reply the page did not
/// show; an agent posts the same field, and may name any sequence of the Topic.</summary>
public sealed record TopicSeen(string Action, long Through, FormToken Token);
