using System.Globalization;
using Koan.Data.Abstractions;
using Koan.Data.Core.Sorting;
using Tangent.Participants;
using Tangent.Posts;

namespace Tangent.Events;

/// <summary>What is new to one signed-in reader, by one rule in two units. A row is new to them when it reaches them
/// (<see cref="Readable"/>), someone else made it, and it lies past their position: in the log, past their
/// <see cref="Floor"/>, the later of their arrival (<see cref="Participant.ArrivedSeq"/>) and their place mark; in a
/// Topic, past their mark there. So a Post is new where its <see cref="Post.LogSeq"/> is past the floor and its
/// <see cref="Post.Sequence"/> past their Topic mark, and a Topic's new Posts are always its last ones. What is for
/// them (<see cref="ForYou"/>) is read on the door and counted on every page's masthead (<see cref="Badge"/>). Reads
/// only and takes no lock: a position moves by <see cref="Seen"/> and by the reader's own write.</summary>
public sealed class Unread
{
    /// <summary>The highest count said as it is; past it the count reads "at least" (<c>99+</c>).</summary>
    public const int CountLimit = 99;

    private Unread(Participant reader, long floor)
    {
        Reader = reader;
        Floor = floor;
    }

    public Participant Reader { get; }

    /// <summary>Where "since you were here" begins in the log: the later of the reader's arrival and their place mark.
    /// A reader who never says what they have seen reads news from their arrival, never from the start of the log.</summary>
    public long Floor { get; }

    /// <summary>The reader's position: their participant and their place mark, one keyed read.</summary>
    public static async Task<Unread> Of(Participant reader, CancellationToken ct)
    {
        var place = await Checkpoint.Get(Checkpoint.Key(reader.Id, Scope.Place), ct);
        return new Unread(reader, Math.Max(reader.ArrivedSeq, place?.Seq ?? 0));
    }

    /// <summary>Whether <paramref name="post"/> is new to the reader, whose mark on its Topic is <paramref name="mark"/>,
    /// null where they hold none: someone else wrote it, past the floor and past the mark.</summary>
    public bool New(Post post, long? mark) => post.AuthorId != Reader.Id && post.LogSeq > Floor && post.Sequence > (mark ?? -1);

    /// <summary>What a list of Topics says is new in one, from its root alone: nothing where its newest Post
    /// (<see cref="Post.LastLogSeq"/>) is not past the floor; where the reader holds a mark, the Posts past it, and the
    /// Topic is new while there are some; where they hold none, new, with no count, since they never read it.</summary>
    public (bool New, long? Count) InTopic(Post root, long? mark)
    {
        if (root.LastLogSeq <= Floor) return (false, 0);
        if (mark is not { } read) return (true, null);
        var count = Math.Max(0, root.LastSequence - read);
        return (count > 0, count);
    }

    /// <summary>The reader's marks on these Topics, by Topic Id, in one keyed read; a Topic they hold none on is absent.</summary>
    public async Task<IReadOnlyDictionary<string, long>> Marks(IEnumerable<string> topicIds, CancellationToken ct)
    {
        var ids = topicIds.Distinct(StringComparer.Ordinal).ToList();
        var marks = new Dictionary<string, long>(StringComparer.Ordinal);
        if (ids.Count == 0) return marks;
        var rows = await Checkpoint.Get(ids.Select(id => Checkpoint.Key(Reader.Id, Scope.Topic(id))).ToList(), ct);
        for (var index = 0; index < ids.Count; index++)
            if (rows[index] is { } checkpoint) marks[ids[index]] = checkpoint.Seq;
        return marks;
    }

    /// <summary>Whether <paramref name="row"/> brought a Post to the reader: the Post's arrival in its Topic, or a
    /// mention written with it, so it is new for as long as that Post is. An edit, a removal, and a mention an edit
    /// wrote are about a Post the Topic's mark may have passed long before, so the floor alone decides them.</summary>
    public static bool Arrival(Event row)
        => row.Kind == EventKind.PostCreated || (row.Kind == EventKind.Mention && (row.Version ?? 1) == 1);

    /// <summary>What is for the reader and new to them: each row of the log that names them as its target, each mention of
    /// a role they hold, and, while they may view the moderators' Tangent, each report, that someone else made past the
    /// floor and that reaches them, a Post that mentions them more than once counting once;
    /// and, while they hold <c>ManageStanding</c>, the applications that wait past it. A row that brought a Post
    /// (<see cref="Arrival"/>) stays while that Post and its Topic stand and the Post is new in its Topic, so reading the
    /// Topic clears it here too; someone else's edits of one Post are its newest, a change of the reader's standing is
    /// its newest, and a role given or taken is the newest change of that role. Newest first. One read of at most
    /// <see cref="CountLimit"/> and one more rows of each kind, the mentions of their roles one range of
    /// <c>ix_event_roles</c> for each, what capability reads for the Tangents they name, and
    /// one keyed read each of the Posts they name and of the reader's marks on their Topics.</summary>
    public async Task<ForYou> ForYouNow(CancellationToken ct, TimeProvider? clock = null)
    {
        var floor = Floor;
        var readerId = Reader.Id;
        var page = new QueryDefinition { Page = 1, PageSize = CountLimit + 1 };
        var targeted = await Event.Query(e => e.TargetId == readerId && e.Seq > floor,
            page.WithSort<Event>(sort => sort.OrderByDescending(e => e.Seq)), ct);
        var readable = new Readable(readerId, clock);
        IReadOnlyList<Event> reports = await readable.Reports(ct)
            ? await Event.Query(e => e.Report == true && e.Seq > floor,
                new QueryDefinition { Page = 1, PageSize = CountLimit + 1 }.WithSort<Event>(sort => sort.OrderByDescending(e => e.Seq)), ct)
            : [];
        var held = await readable.Held(ct);
        IReadOnlyList<Event> named = held.Count > 0
            ? await Event.Query(e => held.Contains(e.Role!) && e.Kind == EventKind.Mention && e.Seq > floor,
                new QueryDefinition { Page = 1, PageSize = CountLimit + 1 }.WithSort<Event>(sort => sort.OrderByDescending(e => e.Seq)), ct)
            : [];
        await readable.Prepare([.. targeted, .. reports, .. named], ct);
        var decides = await readable.Decides(ct);
        var waiting = decides ? await Applications.Waiting(floor, long.MaxValue, CountLimit + 1, ct) : [];

        // A Post that mentions the reader and a role of theirs, or two of their roles, is one mention of them.
        var mentioning = targeted.Where(row => row.Kind == EventKind.Mention).Select(Mentioned).ToHashSet(StringComparer.Ordinal);
        var reached = targeted.Concat(reports).Concat(named.Where(row => mentioning.Add(Mentioned(row))))
            .Where(row => row.ActorId != readerId && readable.Reaches(row))
            .DistinctBy(row => row.Seq).OrderByDescending(row => row.Seq).ToList();
        var posts = await Posts(reached, ct);
        var marks = await Marks(reached.Where(Arrival).Select(row => row.TopicId).OfType<string>(), ct);

        var kept = new List<Event>(reached.Count);
        var edits = new HashSet<string>(StringComparer.Ordinal);
        var roles = new HashSet<string>(StringComparer.Ordinal);
        var standing = false;
        foreach (var row in reached)
        {
            var post = row.PostId is { } postId ? posts.GetValueOrDefault(postId) : null;
            var root = row.TopicId is { } topicId ? posts.GetValueOrDefault(topicId) : null;
            var holds = row.Kind switch
            {
                _ when Arrival(row) => post is { Removed: false } && root is { Removed: false }
                                       && post.Sequence > (marks.TryGetValue(root.Id, out var mark) ? mark : -1),
                EventKind.PostEdited => post is { Removed: false } && root is not null && edits.Add(post.Id),
                EventKind.PostRemoved or EventKind.Mention => post is not null && root is not null && (row.Kind == EventKind.PostRemoved || !post.Removed),
                EventKind.StandingChanged => !standing && (standing = true),
                EventKind.RoleChanged => row.Role is { } role && roles.Add(role),
                _ => false
            };
            if (holds) kept.Add(row);
        }

        var through = new[] { floor, kept.Count > 0 ? kept[0].Seq : floor, waiting.Count > 0 ? waiting[0].WaitingSeq!.Value : floor }.Max();
        var atLeast = targeted.Count > CountLimit || reports.Count > CountLimit || named.Count > CountLimit || waiting.Count > CountLimit;
        return new ForYou(kept, waiting.Count, atLeast, through, decides, posts);
    }

    /// <summary>The Post version a mention was written with, which one mention of the reader stands for.</summary>
    private static string Mentioned(Event row) => row.PostId + "\n" + row.Version;

    /// <summary>How many things are for the reader and new to them, as every page's masthead shows it, with the door's
    /// <paramref name="door"/> address, where they are read.</summary>
    public async Task<Badge> BadgeNow(string door, CancellationToken ct)
    {
        var forYou = await ForYouNow(ct);
        return new Badge(forYou.Count, forYou.AtLeast, door, forYou.Through, forYou.Applications);
    }

    /// <summary>The masthead's count for the participant <paramref name="participantId"/> under
    /// <paramref name="baseAddress"/>; null for a visitor or one who never arrived.</summary>
    public static async Task<Badge?> BadgeFor(string? participantId, string baseAddress, CancellationToken ct)
        => participantId is not null && await Participant.Get(participantId, ct) is { } reader
            ? await (await Of(reader, ct)).BadgeNow(baseAddress + "/", ct)
            : null;

    /// <summary>The Posts the rows name and the roots of their Topics, by Id, in one keyed read.</summary>
    public static async Task<IReadOnlyDictionary<string, Post>> Posts(IEnumerable<Event> rows, CancellationToken ct)
    {
        var ids = rows.SelectMany(row => new[] { row.PostId, row.TopicId }).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0) return new Dictionary<string, Post>(StringComparer.Ordinal);
        return (await Post.Get(ids, ct)).OfType<Post>().ToDictionary(post => post.Id, StringComparer.Ordinal);
    }

    /// <summary>What is for the reader and new to them: the <see cref="Rows"/> of the log, newest first, and how many
    /// applications wait past their floor (<see cref="Waiting"/>), counted together (<see cref="Count"/>), a lower bound
    /// where <see cref="AtLeast"/>; <see cref="Through"/>, the newest of them, or the floor where there is none;
    /// whether the reader decides applications (<see cref="Applications"/>); and the Posts the rows name, by Id.</summary>
    public sealed record ForYou(IReadOnlyList<Event> Rows, int Waiting, bool AtLeast, long Through, bool Applications,
        IReadOnlyDictionary<string, Post> Posts)
    {
        public int Count => Rows.Count + Waiting;
    }

    /// <summary>The masthead's count: how many things are for the reader and new to them, a lower bound where
    /// <see cref="AtLeast"/>, and the door, where they are read. <see cref="Through"/> is the newest of them, past which
    /// a row the page's stream delivers that is for the reader adds one, as an application does where
    /// <see cref="Applications"/> says they decide them.</summary>
    public sealed record Badge(int Count, bool AtLeast, string Door, long Through, bool Applications = false)
    {
        /// <summary>The count as the masthead shows it: the number, with a plus where it is a lower bound, and
        /// <c>99+</c> past <see cref="CountLimit"/>.</summary>
        public string Shown => Count > CountLimit
            ? CountLimit.ToString(CultureInfo.InvariantCulture) + "+"
            : Count.ToString(CultureInfo.InvariantCulture) + (AtLeast ? "+" : "");

        /// <summary>The count in words, for whoever does not see the badge: "at least" where it is a lower bound.</summary>
        public string Words => (AtLeast ? "At least " : "") + Count.ToString(CultureInfo.InvariantCulture) + " new for you";
    }
}
