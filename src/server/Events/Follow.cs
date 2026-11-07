using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Core.Sorting;
using Tangent.Posts;

namespace Tangent.Events;

/// <summary>A participant's choice that a Topic's new replies are for them, keyed by participant and Topic. <see cref="On"/>
/// says whether they follow it: a row that says false records that they stopped, so writing in it again does not follow it
/// again. What the writes compile, so the door reads it as one range: <see cref="Count"/>, how many Posts someone else wrote
/// in the Topic since the participant last caught up with it, <see cref="New"/> while there are some, and
/// <see cref="LastLogSeq"/>, the Topic's newest Post's place in the log. Writing in a Topic follows it (<see cref="Wrote"/>);
/// every reply raises its other followers (<see cref="Moved"/>); reading it lowers their own (<see cref="Read"/>), and so
/// does saying the door seen (<see cref="Caught"/>). It is apart from a checkpoint and moves no position. It names no
/// Tangent: the root says where the Topic is.</summary>
public sealed class Follow : Entity<Follow>
{
    /// <summary>The most rows one read of <see cref="Moved"/>, <see cref="Caught"/> or a delete takes.</summary>
    public const int Page = 200;

    public static string Key(string participantId, string topicId) => participantId + "/" + topicId;

    [Index(Name = "ix_follow_participants", Order = 0)]
    public string ParticipantId { get; set; } = "";

    [Index(Name = "ix_follow_topics", Order = 0)]
    public string TopicId { get; set; } = "";

    [Index(Name = "ix_follow_topics", Order = 1)]
    public bool On { get; set; }

    [Index(Name = "ix_follow_participants", Order = 1)]
    public bool New { get; set; }

    [Index(Name = "ix_follow_participants", Order = 2)]
    public long LastLogSeq { get; set; }

    public long Count { get; set; }

    /// <summary>The row that says whether <paramref name="participantId"/> follows the Topic of <paramref name="root"/>,
    /// with nothing new.</summary>
    public static Follow Of(string participantId, Post root, bool on) => new()
    {
        Id = Key(participantId, root.Id),
        ParticipantId = participantId,
        TopicId = root.Id,
        On = on,
        LastLogSeq = root.LastLogSeq
    };

    /// <summary>Inside the command that wrote in the Topic of <paramref name="root"/>: its writer follows it where they never
    /// chose about it, and where <paramref name="caughtUp"/> says their write left them at its end, nothing is new for
    /// them. One keyed read.</summary>
    public static async Task Wrote(string authorId, Post root, bool caughtUp, CancellationToken ct)
    {
        var row = await Get(Key(authorId, root.Id), ct);
        if (row is null) await Of(authorId, root, true).Save(ct);
        else if (caughtUp && row.New) await row.Lowered(0, ct);
    }

    /// <summary>Inside the command that wrote a reply in the Topic of <paramref name="root"/>: each of its followers but its
    /// writer has one more Post new to them, at the Topic's newest place in the log. Reads <see cref="Page"/> followers at a time
    /// through <c>ix_follow_topics</c>, the first page each pass, since a command reads its own writes.</summary>
    public static async Task Moved(Post root, string authorId, CancellationToken ct)
    {
        while (true)
        {
            var rows = await Query(f => f.TopicId == root.Id && f.On && f.LastLogSeq < root.LastLogSeq,
                new QueryDefinition { Page = 1, PageSize = Page }, ct);
            if (rows.Count == 0) return;
            foreach (var row in rows)
            {
                row.LastLogSeq = root.LastLogSeq;
                if (row.ParticipantId == authorId) continue;
                row.Count++;
                row.New = true;
            }

            await rows.Save(ct);
            if (rows.Count < Page) return;
        }
    }

    /// <summary>Inside the command that moved <paramref name="participantId"/>'s mark on the Topic of <paramref name="root"/>
    /// to <paramref name="through"/>: at its end nothing there is new to them, and short of it no more than the Posts
    /// past the mark. One keyed read.</summary>
    public static async Task Read(string participantId, Post root, long through, CancellationToken ct)
    {
        if (await Get(Key(participantId, root.Id), ct) is { New: true } row) await row.Lowered(Math.Max(0, root.LastSequence - through), ct);
    }

    /// <summary>Inside the command that said the door seen through <paramref name="through"/>: nothing is new for
    /// <paramref name="participantId"/> in a followed Topic whose newest Post is at or before it. Reads <see cref="Page"/>
    /// at a time through <c>ix_follow_participants</c>.</summary>
    public static async Task Caught(string participantId, long through, CancellationToken ct)
    {
        while (true)
        {
            var rows = await Query(f => f.ParticipantId == participantId && f.New && f.LastLogSeq <= through,
                new QueryDefinition { Page = 1, PageSize = Page }, ct);
            foreach (var row in rows)
            {
                row.Count = 0;
                row.New = false;
            }

            if (rows.Count > 0) await rows.Save(ct);
            if (rows.Count < Page) return;
        }
    }

    /// <summary>Removes every row that names one of these Topics, <see cref="Page"/> at a time.</summary>
    public static async Task RemoveAll(IReadOnlyCollection<string> topicIds, CancellationToken ct)
    {
        if (topicIds.Count == 0) return;
        while (true)
        {
            var rows = await Query(f => topicIds.Contains(f.TopicId), new QueryDefinition { Page = 1, PageSize = Page }, ct);
            if (rows.Count > 0) await Remove(rows.Select(row => row.Id), ct);
            if (rows.Count < Page) return;
        }
    }

    /// <summary>The followed Topics with something new for <paramref name="participantId"/> whose newest Post is past
    /// <paramref name="floor"/>, newest first, at most <paramref name="size"/>: one range of <c>ix_follow_participants</c>.</summary>
    public static Task<IReadOnlyList<Follow>> NewFor(string participantId, long floor, int size, CancellationToken ct)
        => Query(f => f.ParticipantId == participantId && f.New && f.LastLogSeq > floor,
            new QueryDefinition { Page = 1, PageSize = size }.WithSort<Follow>(sort => sort.OrderByDescending(f => f.LastLogSeq)), ct);

    /// <summary>Saves the row with at most <paramref name="left"/> Posts new to them, where that lowers it.</summary>
    private async Task Lowered(long left, CancellationToken ct)
    {
        if (left >= Count) return;
        Count = left;
        New = left > 0;
        await this.Save(ct);
    }
}
