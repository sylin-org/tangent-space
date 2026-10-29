namespace Tangent.Events;

/// <summary>What Koan Communication carries for each row of the log once its commit has landed: the row was appended.
/// <see cref="LogAnnouncer"/> raises it on the committed rows, in sequence order, and <see cref="StreamDelivery"/> hands
/// each to the event stream. It names no change of its own; the row says what happened.</summary>
public sealed record Appended;
