using Koan.Communication;

namespace Tangent.Events;

/// <summary>Koan Communication's handler for <see cref="Appended"/>: hands each appended row of the log to the event
/// stream, which queues it for every open stream at once and never waits.</summary>
public sealed class StreamDelivery(EventStream stream) : IHandleEntityEvent<Event, Appended>
{
    public Task Handle(Event row, EventOccurrence<Appended> occurrence, CancellationToken ct)
    {
        stream.Publish(row);
        return Task.CompletedTask;
    }
}
