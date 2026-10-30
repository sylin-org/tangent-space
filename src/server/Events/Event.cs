using System.Globalization;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Tangent.Space;
using Tangent.Writer;

namespace Tangent.Events;

/// <summary>One row of the log: something happened, with a sequence number. Only the writer appends, inside the
/// command that made the change; <see cref="Seq"/> comes from the log's high-water mark and the Id is that number,
/// zero-padded so it sorts as it counts.</summary>
public sealed class Event : Entity<Event>
{
    /// <summary>The name of the log's high-water mark.</summary>
    public const string Log = "log";

    /// <summary>The log's mark: the highest sequence stored, read by one bounded query, or zero.</summary>
    public static readonly HighWaterMark HighWaterMark = new(Log, async ct =>
        (await FirstPage(1, sort => sort.OrderByDescending(e => e.Seq), ct)).FirstOrDefault()?.Seq ?? 0);

    public static string Key(long seq) => seq.ToString("D19", CultureInfo.InvariantCulture);

    [Index(Name = "ix_event_seq", Unique = true)]
    [Index(Name = "ix_event_targets", Order = 1)]
    public long Seq { get; set; }

    /// <summary>One of <see cref="EventKind"/>.</summary>
    public string Kind { get; set; } = "";

    public string? TangentId { get; set; }

    public string? TopicId { get; set; }

    public string? PostId { get; set; }

    /// <summary>Who did it.</summary>
    public string? ActorId { get; set; }

    /// <summary>Whom it concerns: the author of the Post a reply answers, the participant mentioned, admitted, timed out,
    /// banned, lifted, or given or taken a role, or the one who decides an Offer.</summary>
    [Index(Name = "ix_event_targets", Order = 0)]
    public string? TargetId { get; set; }

    /// <summary>The Post's version after the change, on a Post event.</summary>
    public long? Version { get; set; }

    public DateTimeOffset At { get; set; }

    /// <summary>The audience a Tangent has after the change, on <see cref="EventKind.TangentChanged"/>.</summary>
    public Audience? Audience { get; set; }

    /// <summary>The Offer made, on <see cref="EventKind.OfferMade"/>.</summary>
    public string? OfferId { get; set; }

    /// <summary>The standing a change left its target with, on <see cref="EventKind.StandingChanged"/> when someone
    /// else changed it: admitted them, declined their application, timed them out, banned them or lifted either.</summary>
    public Participants.Standing? Standing { get; set; }

    /// <summary>The standing the target held before that change, beside <see cref="Standing"/>: what tells a lift from an
    /// admission.</summary>
    public Participants.Standing? Before { get; set; }

    /// <summary>The role given or taken, by its key, on <see cref="EventKind.RoleChanged"/>.</summary>
    public string? Role { get; set; }

    /// <summary>Whether the role was given (true) or taken (false), on <see cref="EventKind.RoleChanged"/>.</summary>
    public bool? Given { get; set; }

    /// <summary>The next row of the log, numbered from the mark the writer holds for the running command.</summary>
    public static Event Next(Sequences sequences, string kind, DateTimeOffset at,
        string? tangentId = null, string? topicId = null, string? postId = null, string? actorId = null,
        string? targetId = null, long? version = null, Audience? audience = null, string? offerId = null,
        Participants.Standing? standing = null, Participants.Standing? before = null, string? role = null, bool? given = null)
    {
        var seq = sequences.Next(Log);
        return new Event
        {
            Id = Key(seq),
            Seq = seq,
            Kind = kind,
            TangentId = tangentId,
            TopicId = topicId,
            PostId = postId,
            ActorId = actorId,
            TargetId = targetId,
            Version = version,
            At = at,
            Audience = audience,
            OfferId = offerId,
            Standing = standing,
            Before = before,
            Role = role,
            Given = given
        };
    }
}
