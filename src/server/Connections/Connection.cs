using Koan.Data.Core.Model;
using Koan.Jobs;

namespace Tangent.Connections;

/// <summary>Whether a participant's Connection publishes, or they said not now to the offer that would start it.</summary>
public enum ConnectionState
{
    Publishing,
    NotNow
}

/// <summary>One participant's Connection to the atproto account they are bound to, keyed by their Id. It
/// syndicates only what they wrote: as a Koan job it reads the log from its own <see cref="Cursor"/>, compares each of
/// their articles the rows name with the <see cref="Receipt"/> of what it delivered, and publishes, updates or retracts
/// (<see cref="Syndication"/>). An article they wrote after <see cref="From"/> publishes on its own; an earlier one, and
/// every one while they publish only what they choose, publishes when they choose it, and what is out follows their edits
/// and removals either way. A failure leaves the cursor where it was and says what it could not do in
/// <see cref="Trouble"/>, until an attempt succeeds; <see cref="NeedsGrant"/> says their account no longer lets the
/// place write, so nothing is tried until they grant it again.</summary>
[JobIdempotent(nameof(Id))]
public sealed class Connection : Entity<Connection>, IKoanJob<Connection>
{
    public ConnectionState State { get; set; }

    /// <summary>The log's sequence when the Connection began publishing on its own: their articles that arrived after it
    /// publish on their own. Null while they publish only what they choose (<see cref="StopPublishing"/>).</summary>
    public long? From { get; set; }

    /// <summary>The log's sequence the Connection has read through.</summary>
    public long Cursor { get; set; }

    public DateTimeOffset Since { get; set; }

    /// <summary>What the last attempt could not do, in a sentence for its participant; null while all is well.</summary>
    public string? Trouble { get; set; }

    /// <summary>When the next attempt runs, while there is trouble.</summary>
    public DateTimeOffset? RetryAt { get; set; }

    /// <summary>How many attempts in a row have failed.</summary>
    public int Failures { get; set; }

    public bool NeedsGrant { get; set; }

    public bool Publishes() => State == ConnectionState.Publishing && !NeedsGrant;

    /// <summary>Whether an article that arrived at <paramref name="logSeq"/> publishes on its own, unchosen.</summary>
    public bool OnItsOwn(long logSeq) => From is { } from && logSeq > from;

    public static Task Execute(Connection connection, JobContext context, CancellationToken ct) => Syndication.Run(connection, context, ct);
}
