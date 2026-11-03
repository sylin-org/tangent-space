using System.Globalization;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Koan.Data.Core.Model;
using Tangent.Space;

namespace Tangent.Events;

/// <summary>One row of the log: something happened, with a sequence number. Only a command appends, inside its own
/// hold; <see cref="Seq"/> comes from <see cref="Log"/> and the Id is that number, zero-padded so it sorts as it
/// counts. Once the hold commits, the event stream learns the row is there (<see cref="EventStream.Committed"/>).</summary>
public sealed class Event : Entity<Event>
{
    /// <summary>The log's numbers: the highest stored, and inside a command's hold the next.</summary>
    public static readonly Sequence Log = Sequence.Of<Event>(e => e.Seq);

    public static string Key(long seq) => seq.ToString("D19", CultureInfo.InvariantCulture);

    [Index(Name = "ix_event_seq", Unique = true)]
    [Index(Name = "ix_event_targets", Order = 1)]
    [Index(Name = "ix_event_reports", Order = 1)]
    public long Seq { get; set; }

    /// <summary>One of <see cref="EventKind"/>.</summary>
    public string Kind { get; set; } = "";

    public string? TangentId { get; set; }

    public string? TopicId { get; set; }

    public string? PostId { get; set; }

    /// <summary>Who did it.</summary>
    public string? ActorId { get; set; }

    /// <summary>Whom it concerns: the author of the Post a reply answers or someone else edited or removed, the participant
    /// mentioned, admitted, timed out, banned, lifted, or given or taken a role.</summary>
    [Index(Name = "ix_event_targets", Order = 0)]
    public string? TargetId { get; set; }

    /// <summary>The Post's version after the change, on a Post event.</summary>
    public long? Version { get; set; }

    public DateTimeOffset At { get; set; }

    /// <summary>The audience a Tangent has after the change, on <see cref="EventKind.TangentChanged"/>.</summary>
    public Audience? Audience { get; set; }

    /// <summary>The standing a change left its target with, on <see cref="EventKind.StandingChanged"/>: the one their
    /// arrival gave them, admitted or applied, or the one someone else's decision left: admitted them, declined their
    /// application, timed them out, banned them or lifted either. An arrival that leaves them applied is an application
    /// (<see cref="Readable.Application"/>).</summary>
    public Participants.Standing? Standing { get; set; }

    /// <summary>The standing the target held before that change, beside <see cref="Standing"/>: what tells a lift from an
    /// admission.</summary>
    public Participants.Standing? Before { get; set; }

    /// <summary>The role given or taken, by its key, on <see cref="EventKind.RoleChanged"/>.</summary>
    public string? Role { get; set; }

    /// <summary>Whether the role was given (true) or taken (false), on <see cref="EventKind.RoleChanged"/>.</summary>
    public bool? Given { get; set; }

    /// <summary>True on the <see cref="EventKind.PostCreated"/> of a report (<see cref="Posts.ReportPost"/>), the Topic
    /// or reply it wrote in the moderators' Tangent; null on every other row. The reports are one range of
    /// <c>ix_event_reports</c>, newest first, which is how they reach whoever may view that Tangent.</summary>
    [Index(Name = "ix_event_reports", Order = 0)]
    public bool? Report { get; set; }

    /// <summary>Appends the next row of the log, inside the running command; once it commits, the event stream's head
    /// moves to it.</summary>
    public static async Task<Event> Append(string kind, DateTimeOffset at, CancellationToken ct,
        string? tangentId = null, string? topicId = null, string? postId = null, string? actorId = null,
        string? targetId = null, long? version = null, Audience? audience = null,
        Participants.Standing? standing = null, Participants.Standing? before = null, string? role = null, bool? given = null,
        bool? report = null)
    {
        var seq = await Log.Next(ct);
        EntityContext.UncommittedScope!.AfterCommit(_ =>
        {
            AppHost.GetRequiredService<EventStream>(nameof(Append)).Committed(seq);
            return Task.CompletedTask;
        });
        return await new Event
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
            Standing = standing,
            Before = before,
            Role = role,
            Given = given,
            Report = report
        }.Save(ct);
    }
}
