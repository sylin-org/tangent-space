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
/// them (<see cref="ForYou"/>) is read on the door and counted on every page's masthead (<see cref="Badge"/>), and what
/// reached them and is no longer new is paged back (<see cref="Earlier"/>). Reads only and takes no lock: a position moves
/// by <see cref="Seen"/> and by the reader's own write.</summary>
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
    /// floor and that reaches them, a Post that mentions them more than once counting once, and a Post that reaches them
    /// itself, as a reply to them, a Topic taken from theirs or a report, counting once whatever it mentions;
    /// and, while they hold <c>ManageStanding</c>, the applications that wait past it. A row that brought a Post
    /// (<see cref="Arrival"/>) stays while that Post and its Topic stand and the Post is new in its Topic, so reading the
    /// Topic clears it here too; someone else's edits of one Post are its newest, and their removal of it its newest while it
    /// stays removed, so an undone removal leaves; a change of the reader's standing is
    /// its newest, and a role given or taken is the newest change of that role. Newest first. One read of at most
    /// <see cref="CountLimit"/> and one more rows of each kind, the mentions of their roles one range of
    /// <c>ix_event_roles</c> for each, what capability reads for the Tangents they name, and
    /// one keyed read each of the Posts they name and of the reader's marks on their Topics; and the Topics they follow with
    /// something new past the floor (<see cref="Follow.NewFor"/>), one range of as many, whose roots and marks join
    /// the same keyed reads (<see cref="FollowedOf"/>).</summary>
    public async Task<ForYou> ForYouNow(CancellationToken ct, TimeProvider? clock = null)
    {
        var floor = Floor;
        var readerId = Reader.Id;
        var readable = new Readable(readerId, clock);
        var (targeted, reports, named) = await Reaching(floor, long.MaxValue, CountLimit, readable, ct);
        var following = await Follow.NewFor(readerId, floor, CountLimit + 1, ct);
        await readable.Prepare([.. targeted, .. reports, .. named], ct);
        var decides = await readable.Decides(ct);
        var waiting = decides ? await Applications.Waiting(floor, long.MaxValue, CountLimit + 1, ct) : [];

        // A Post that mentions the reader and a role of theirs, or two of their roles, is one mention of them.
        var mentioning = targeted.Where(row => row.Kind == EventKind.Mention).Select(Mentioned).ToHashSet(StringComparer.Ordinal);
        var reached = targeted.Concat(reports).Concat(named.Where(row => mentioning.Add(Mentioned(row))))
            .Where(row => row.ActorId != readerId && readable.Reaches(row))
            .DistinctBy(row => row.Seq).OrderByDescending(row => row.Seq).ToList();
        var posts = await Posts(reached, ct, following.Select(row => row.TopicId));
        var marks = await Marks(reached.Where(Arrival).Select(row => row.TopicId).OfType<string>().Concat(following.Select(row => row.TopicId)), ct);

        var kept = new List<Event>(reached.Count);
        var edits = new HashSet<string>(StringComparer.Ordinal);
        var removals = new HashSet<string>(StringComparer.Ordinal);
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
                EventKind.PostRemoved => post is { Removed: true } && root is not null && removals.Add(post.Id),
                EventKind.Mention => post is { Removed: false } && root is not null,
                EventKind.StandingChanged => !standing && (standing = true),
                EventKind.RoleChanged => row.Role is { } role && roles.Add(role),
                EventKind.TangentLet => true,
                _ => false
            };
            if (holds) kept.Add(row);
        }

        // A Post that reaches the reader as a reply, a Topic taken from theirs or a report is one thing for them, whatever
        // it mentions.
        var arrived = kept.Where(row => row.Kind == EventKind.PostCreated).Select(row => row.PostId).OfType<string>().ToHashSet(StringComparer.Ordinal);
        kept.RemoveAll(row => row.Kind == EventKind.Mention && row.PostId is { } postId && arrived.Contains(postId));
        var followed = await FollowedOf(following, kept, posts, marks, readable, ct);
        var through = new[]
        {
            floor, kept.Count > 0 ? kept[0].Seq : floor, waiting.Count > 0 ? waiting[0].WaitingSeq!.Value : floor,
            followed.Count > 0 ? followed.Max(topic => topic.Root.LastLogSeq) : floor
        }.Max();
        var atLeast = targeted.Count > CountLimit || reports.Count > CountLimit || named.Count > CountLimit || waiting.Count > CountLimit
                      || following.Count > CountLimit;
        return new ForYou(kept, waiting.Count, atLeast, through, decides, posts) { Following = followed };
    }

    /// <summary>The three ranges of the log that reach the reader by what they are, each newest first, past
    /// <paramref name="above"/> and before <paramref name="below"/>, at most <paramref name="size"/> and one more: each row
    /// that names them as its target, each report while <paramref name="readable"/> says reports reach them, and each mention
    /// of a role they hold, one range of <c>ix_event_roles</c> for each.</summary>
    private async Task<(IReadOnlyList<Event> Targeted, IReadOnlyList<Event> Reports, IReadOnlyList<Event> Named)> Reaching(long above, long below, int size,
        Readable readable, CancellationToken ct)
    {
        var readerId = Reader.Id;
        QueryDefinition Newest() => new QueryDefinition { Page = 1, PageSize = size + 1 }.WithSort<Event>(sort => sort.OrderByDescending(e => e.Seq));
        var targeted = await Event.Query(e => e.TargetId == readerId && e.Seq > above && e.Seq < below, Newest(), ct);
        IReadOnlyList<Event> reports = await readable.Reports(ct)
            ? await Event.Query(e => e.Report == true && e.Seq > above && e.Seq < below, Newest(), ct)
            : [];
        var held = await readable.Held(ct);
        IReadOnlyList<Event> named = held.Count > 0
            ? await Event.Query(e => held.Contains(e.Role!) && e.Kind == EventKind.Mention && e.Seq > above && e.Seq < below, Newest(), ct)
            : [];
        return (targeted, reports, named);
    }

    /// <summary>The Topics the reader follows that hold Posts new to them that nothing else for them holds, newest first: each
    /// root that stands in a Tangent they may view and is new to them (<see cref="InTopic"/>), with how many of its new Posts
    /// are not already among <paramref name="kept"/>, or no count where they never read it; one whose every new Post is
    /// already for them adds nothing, so each Post counts once. What capability reads for the Tangents it names.</summary>
    private async Task<IReadOnlyList<Followed>> FollowedOf(IReadOnlyList<Follow> following, IReadOnlyList<Event> kept,
        IReadOnlyDictionary<string, Post> posts, IReadOnlyDictionary<string, long> marks, Readable readable, CancellationToken ct)
    {
        var roots = following.Take(CountLimit).Select(row => (Row: row, Root: posts.GetValueOrDefault(row.TopicId)))
            .Where(each => each.Root is { ParentId: null, Removed: false }).ToList();
        if (roots.Count == 0) return [];
        await readable.Include(roots.Select(each => each.Root!.TangentId), ct);
        var already = kept.Where(Arrival).GroupBy(row => row.TopicId ?? "", StringComparer.Ordinal)
            .ToDictionary(topic => topic.Key, topic => topic.Select(row => row.PostId).Distinct(StringComparer.Ordinal).Count(), StringComparer.Ordinal);
        var shown = new List<Followed>(roots.Count);
        foreach (var (row, root) in roots)
        {
            if (!readable.Views(root!.TangentId)) continue;
            // Someone else's Posts past the mark: no more than the follow counted since the reader last caught up, which
            // leaves out their own.
            var (isNew, count) = InTopic(root, marks.TryGetValue(root.Id, out var mark) ? mark : null);
            long? left = count is { } all ? Math.Min(all, row.Count) - already.GetValueOrDefault(root.Id) : null;
            if (isNew && left is null or > 0) shown.Add(new Followed(root, left));
        }

        return shown;
    }

    /// <summary>The most things one page of what reached the reader and is no longer new holds (<see cref="Earlier"/>).</summary>
    public const int OlderLimit = 20;

    /// <summary>What reached the reader before the log's sequence <paramref name="before"/> and is no longer new to them,
    /// newest first: a reply to them, a Topic taken from their Post, a mention of them, a report, their Post someone else
    /// edited or removed, read from the ranges <see cref="Reaching"/> reads, at most <see cref="OlderLimit"/> of each and one
    /// more. Where one came back full, the page holds only what lies past the last row of the fullest, and
    /// <see cref="Past.Next"/> is where the page before it begins; what is still new, which the door shows, is left out, so a
    /// page may hold less, or nothing, while more lies behind it. A row someone else made that reaches them, whose Post and
    /// root stand, or a removal; a mention once, and not where its Post arrived for them on the same page. What capability
    /// reads for the Tangents the rows name, and one keyed read each of their Posts and of the reader's marks on the Topics
    /// of those past the floor.</summary>
    public async Task<Past> Earlier(long before, CancellationToken ct)
    {
        var readable = new Readable(Reader.Id);
        var (targeted, reports, named) = await Reaching(0, before, OlderLimit, readable, ct);
        var cutoff = new[] { targeted, reports, named }.Where(range => range.Count > OlderLimit).Select(range => range[^1].Seq).DefaultIfEmpty(0).Max();
        var rows = targeted.Concat(reports).Concat(named)
            .Where(row => row.Seq > cutoff && row.Kind is EventKind.PostCreated or EventKind.Mention or EventKind.PostEdited or EventKind.PostRemoved)
            .ToList();
        await readable.Prepare(rows, ct);
        rows = rows.Where(row => row.ActorId != Reader.Id && readable.Reaches(row)).DistinctBy(row => row.Seq).OrderByDescending(row => row.Seq).ToList();
        var posts = await Posts(rows, ct);
        var marks = await Marks(rows.Where(row => row.Seq > Floor && Arrival(row)).Select(row => row.TopicId).OfType<string>(), ct);

        var mentioned = new HashSet<string>(StringComparer.Ordinal);
        var kept = rows.Where(row => posts.TryGetValue(row.PostId ?? "", out var post) && posts.TryGetValue(row.TopicId ?? "", out var root)
                                     && (row.Kind == EventKind.PostRemoved || !post.Removed)
                                     && !(row.Seq > Floor && (!Arrival(row) || post.Sequence > (marks.TryGetValue(root.Id, out var mark) ? mark : -1)))
                                     && (row.Kind != EventKind.Mention || mentioned.Add(Mentioned(row))))
            .ToList();
        var arrived = kept.Where(row => row.Kind == EventKind.PostCreated).Select(row => row.PostId).OfType<string>().ToHashSet(StringComparer.Ordinal);
        kept.RemoveAll(row => row.Kind == EventKind.Mention && arrived.Contains(row.PostId!));
        return new Past(kept, cutoff > 0 ? cutoff + 1 : null, posts);
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

    /// <summary>The Posts the rows name and the roots of their Topics, and the Posts <paramref name="also"/> names, by Id, in
    /// one keyed read.</summary>
    public static async Task<IReadOnlyDictionary<string, Post>> Posts(IEnumerable<Event> rows, CancellationToken ct, IEnumerable<string>? also = null)
    {
        var ids = rows.SelectMany(row => new[] { row.PostId, row.TopicId }).OfType<string>().Concat(also ?? []).Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0) return new Dictionary<string, Post>(StringComparer.Ordinal);
        return (await Post.Get(ids, ct)).OfType<Post>().ToDictionary(post => post.Id, StringComparer.Ordinal);
    }

    /// <summary>What is for the reader and new to them: the <see cref="Rows"/> of the log, newest first, how many
    /// applications wait past their floor (<see cref="Waiting"/>), and the Topics they follow that hold Posts new to them
    /// that nothing else here holds (<see cref="Following"/>), counted together (<see cref="Count"/>), a lower bound where
    /// <see cref="AtLeast"/>; <see cref="Through"/>, the newest of them, or the floor where there is none; whether the reader
    /// decides applications (<see cref="Applications"/>); and the Posts the rows and the followed Topics name, by Id.</summary>
    public sealed record ForYou(IReadOnlyList<Event> Rows, int Waiting, bool AtLeast, long Through, bool Applications,
        IReadOnlyDictionary<string, Post> Posts)
    {
        public IReadOnlyList<Followed> Following { get; init; } = [];

        public int Count => Rows.Count + Waiting + Following.Count;
    }

    /// <summary>A Topic the reader follows that holds Posts new to them: its root, and how many of its new Posts nothing else
    /// for them holds, null where they never read it.</summary>
    public sealed record Followed(Post Root, long? New);

    /// <summary>One page of what reached the reader and is no longer new to them (<see cref="Earlier"/>): its rows, newest
    /// first, where the page before it begins, null where nothing lies behind it, and the Posts the rows name, by Id.</summary>
    public sealed record Past(IReadOnlyList<Event> Rows, long? Next, IReadOnlyDictionary<string, Post> Posts);

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
