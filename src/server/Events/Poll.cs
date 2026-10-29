using Tangent.Space;

namespace Tangent.Events;

/// <summary>The poll: one bounded page of the log for a reader — the rows past <see cref="Since"/> that reach them,
/// oldest first, at most <see cref="EventsController.PageSize"/> — and where to ask again: <see cref="Through"/> is the
/// newest row the page holds, where it holds one; <see cref="Since"/> again where the page read rows of which none reaches
/// the reader; and the last row read only where a whole scan (<see cref="EventsController.ScanLimit"/>) held none, so a
/// poll never stalls and says of hidden rows only that a whole scan of them passed. <see cref="Next"/> is the poll's
/// address from there. <see cref="More"/> says the page or its scan was full, so asking again at once may answer more.
/// The same rows, one to a frame, are what the stream at the same address sends.</summary>
public sealed record Poll(long Since, long Through, bool More, IReadOnlyList<Poll.Row> Events, string Next)
{
    /// <summary>One row of the log as the stream and the poll send it: its sequence, what happened, the Tangent, Topic and
    /// Post it happened to, who did it and whom it concerns, the Post's version, when, and the audience a Tangent has
    /// after a <see cref="EventKind.TangentChanged"/>.</summary>
    public sealed record Row(long Seq, string Kind, string? Tangent, string? Topic, string? Post, string? Actor, string? Target,
        long? Version, DateTimeOffset At, Audience? Audience)
    {
        public static Row Of(Event row) => new(row.Seq, row.Kind, row.TangentId, row.TopicId, row.PostId, row.ActorId, row.TargetId,
            row.Version, row.At, row.Audience);
    }
}
