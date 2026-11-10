using System.Globalization;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core.Querying;
using Koan.Web.Queries;
using Microsoft.AspNetCore.DataProtection;
using Tangent.Events;
using Tangent.Participants;
using Tangent.Posts;

namespace Tangent.Tangents;

/// <summary>One bounded page of a Tangent's Topics, newest first, in the Tangent's own sort (<see cref="For"/>): a discussions
/// Tangent's by last activity, through <c>ix_post_topics</c> (<c>TangentId</c>, <c>ParentId</c>, <c>LastActivityAt</c>), and an
/// articles Tangent's by when each started, through <c>ix_post_created</c> (<c>TangentId</c>, <c>ParentId</c>,
/// <c>CreatedAt</c>), removed Topics among them. A list narrows to the Topics started in one month, a range of
/// <c>ix_post_created</c>, or by one author, a range of <c>ix_post_topics_by</c>, which holds <c>AuthorId</c> before
/// <c>CreatedAt</c> (<see cref="Narrowing"/>), the ways an articles Tangent's rail offers, so by start each range is read in
/// its order, and by last activity it is sorted. A page holds at most <see cref="PageSize"/> Topics and continues through Koan's
/// keyset over the sort, the Id breaking ties (<see cref="Order"/>), from the key of the last Topic a page held rather than a
/// position, so a Topic present for the whole read is on exactly one page, narrowed or not; an address carries that key sealed
/// (<see cref="Seal"/>). The whole list leads its first page with the Topic its moderators pinned (<see cref="Pinned"/>), which
/// every page of it leaves out of its own rows. Nothing is counted. Reads only and takes no lock; whom a Topic may be shown to is
/// the caller's to decide.</summary>
public static class TopicList
{
    /// <summary>The most Topics one page holds.</summary>
    public const int PageSize = 30;

    /// <summary>How a list sorts its Topics, newest first: by when each last gained a Post, or by when each started.</summary>
    public enum Sort
    {
        Activity,
        Start
    }

    private static readonly Keyset<Post> ByActivity = Keyset<Post>.By(order => order.OrderByDescending(post => post.LastActivityAt));

    private static readonly Keyset<Post> ByStart = Keyset<Post>.By(order => order.OrderByDescending(post => post.CreatedAt));

    /// <summary>The sort of <paramref name="tangent"/>'s list: an articles Tangent's by when each article started, a
    /// discussions Tangent's by latest activity.</summary>
    public static Sort For(Tangent tangent) => tangent.DefaultKind == PostKind.Article ? Sort.Start : Sort.Activity;

    /// <summary>The keyset a list in <paramref name="sort"/> continues through: its instant, newest first, then the Id.</summary>
    public static Keyset<Post> Order(Sort sort) => sort == Sort.Start ? ByStart : ByActivity;

    /// <summary>A page of Topics and the key the next one continues from; <see cref="Next"/> is null on the last page.</summary>
    public sealed record Page(IReadOnlyList<Post> Topics, KeysetKey? Next);

    /// <summary><paramref name="next"/> as an address carries it, sealed for the list of <paramref name="tangentId"/>'s Topics in
    /// <paramref name="sort"/>.</summary>
    public static string Seal(IDataProtectionProvider protection, string tangentId, Sort sort, KeysetKey next)
        => SealedCursor.Seal(protection, Purpose(tangentId, sort), Order(sort), next);

    /// <summary>The key an address's <paramref name="after"/> holds for the list of <paramref name="tangentId"/>'s Topics in
    /// <paramref name="sort"/>, or null where it holds none: altered, sealed for another Tangent's list or another sort, or by another
    /// key ring.</summary>
    public static KeysetKey? Open(IDataProtectionProvider protection, string tangentId, Sort sort, string after)
        => SealedCursor.Open(protection, Purpose(tangentId, sort), Order(sort), after);

    private static string Purpose(string tangentId, Sort sort) => nameof(TopicList) + "." + sort + "." + tangentId;

    /// <summary>A month in UTC, as a Topic's start falls in it: its key is its year and number, <c>2026-10</c>, and its
    /// name the words a page says it in, <c>October 2026</c>.</summary>
    public sealed record Month(int Year, int Number)
    {
        public DateTimeOffset Start => new(Year, Number, 1, 0, 0, 0, TimeSpan.Zero);

        public DateTimeOffset End => Start.AddMonths(1);

        public string Key => Start.ToString("yyyy-MM", CultureInfo.InvariantCulture);

        public string Name => Start.ToString("MMMM yyyy", CultureInfo.InvariantCulture);

        public static Month Of(DateTimeOffset at)
        {
            var utc = at.UtcDateTime;
            return new Month(utc.Year, utc.Month);
        }

        /// <summary>The month a query carries as its key, or null when the text is not one.</summary>
        public static Month? Parse(string? text)
            => DateTime.TryParseExact(text, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) && at.Year < 9999
                ? new Month(at.Year, at.Month)
                : null;
    }

    /// <summary>What a page is narrowed to: the Topics started in <see cref="Within"/>, by the participant
    /// <see cref="By"/> names, or both; neither is the whole list.</summary>
    public sealed record Narrowing(Month? Within, string? By)
    {
        public static readonly Narrowing None = new(null, null);

        public bool Narrowed => Within is not null || By is not null;
    }

    /// <summary>The page of <paramref name="tangentId"/>'s Topics after <paramref name="after"/>, or the first page, in
    /// <paramref name="sort"/>, narrowed as <paramref name="to"/> says, without the Topic whose root is <paramref name="leaving"/>, where
    /// one is named: one read of the page and one row more, which says whether a next page exists.</summary>
    public static async Task<Page> Read(string tangentId, KeysetKey? after, CancellationToken ct, int size = PageSize, Narrowing? to = null,
        Sort sort = Sort.Activity, string? leaving = null)
    {
        if (size is < 1 or > PageSize) throw new ArgumentOutOfRangeException(nameof(size));
        var terms = Narrow(to ?? Narrowing.None);
        terms.Insert(0, LinqFilterCompiler.Compile<Post>(post => post.TangentId == tangentId && post.ParentId == null));
        if (leaving is not null) terms.Add(Filter.On(FieldPath.Of(nameof(Post.Id)), FilterOperator.Ne, FilterValue.Of(leaving)));
        var page = await Post.Page(Order(sort), after, size, QueryDefinition.All.Where(Filter.All([.. terms])), ct);
        return new Page(page.Items, page.Next);
    }

    /// <summary>The Topic <paramref name="tangent"/>'s moderators pinned, while its root stands there: no read where none is
    /// pinned, otherwise one keyed read; null where the pin names a root removed, gone from the Tangent or deleted, which
    /// nothing writes away.</summary>
    public static async Task<Post?> Pinned(Tangent tangent, CancellationToken ct)
        => tangent.PinnedId is { } id && await Post.Get(id, ct) is { ParentId: null, Removed: false } root && root.TangentId == tangent.Id ? root : null;

    /// <summary>One Topic as a list shows it, what is new in it marked for a signed-in reader by the one rule
    /// (<see cref="Unread.InTopic"/>), and none for a visitor, who removed its root, where someone did, and whether it is
    /// closed. Whether it is pinned is the list's to say.</summary>
    public static Render.TopicEntry Entry(Post root, string tangentAddress, IReadOnlyDictionary<string, Render.Person> people,
        IReadOnlyDictionary<string, Post> latest, Unread? unread, IReadOnlyDictionary<string, long> marks)
    {
        long? mark = marks.TryGetValue(root.Id, out var read) ? read : null;
        var (isNew, newPosts) = unread?.InTopic(root, mark) ?? (false, 0);
        var address = tangentAddress + "/" + Uri.EscapeDataString(root.Slug ?? root.Id);
        Render.TopicEntry.Reply? reply = null;
        if (root.LatestReplyId is { } replyId && latest.TryGetValue(replyId, out var post))
            reply = new Render.TopicEntry.Reply(post.Id, Person(people, post.AuthorId), TangentPage.Opening(post, people), post.CreatedAt,
                unread is not null && unread.New(post, mark), TangentPage.EditedOf(post, people));
        return new Render.TopicEntry(
            root.Id,
            root.Slug ?? root.Id,
            root.Title ?? "",
            root.Kind.ToString().ToLowerInvariant(),
            address,
            Person(people, root.AuthorId),
            TangentPage.Opening(root, people),
            TangentPage.ReadTime(root.Body),
            root.ReplyCount,
            root.CreatedAt,
            root.LastActivityAt,
            reply,
            root.RecentAuthorIds.Take(Post.RecentAuthorLimit).Select(id => Person(people, id)).ToList(),
            isNew,
            newPosts,
            TangentPage.EditedOf(root, people),
            root.CoverId is { } cover ? tangentAddress[..tangentAddress.LastIndexOf("/t/", StringComparison.Ordinal)] + Media.Reference(cover) : null,
            root.Removed ? new TopicPage.Removal(Person(people, root.RemovedBy ?? root.AuthorId), root.RemovedAt ?? root.CreatedAt) : null)
        {
            Closed = root.Closed
        };
    }

    /// <summary>The participant <paramref name="id"/> names as the list shows them, or one who left.</summary>
    public static Render.Person Person(IReadOnlyDictionary<string, Render.Person> people, string id)
        => people.TryGetValue(id, out var person) ? person : Render.Person.Left(id);

    /// <summary>The latest reply of each Topic on the page that has one, by its Id, in one keyed read.</summary>
    public static async Task<IReadOnlyDictionary<string, Post>> LatestReplies(IReadOnlyList<Post> topics, CancellationToken ct)
    {
        var ids = topics.Select(topic => topic.LatestReplyId).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0) return new Dictionary<string, Post>();
        return (await Post.Get(ids, ct)).OfType<Post>().ToDictionary(post => post.Id, StringComparer.Ordinal);
    }

    /// <summary>Everyone the page shows (the Topics' authors, their recent authors, the authors of their latest
    /// replies, whom those openings and replies mention, who else edited them, who removed a root, and the participants
    /// <paramref name="also"/> names) in one keyed read, by participant Id.</summary>
    public static async Task<IReadOnlyDictionary<string, Render.Person>> People(IReadOnlyList<Post> topics, IEnumerable<Post> latest,
        CancellationToken ct, IEnumerable<string>? also = null)
    {
        var ids = (also ?? []).Concat(topics.SelectMany(topic => topic.RecentAuthorIds.Take(Post.RecentAuthorLimit).Append(topic.AuthorId)))
            .Concat(latest.Select(post => post.AuthorId))
            .Concat(topics.Concat(latest).SelectMany(post => post.MentionedIds))
            .Concat(topics.Concat(latest).Select(post => post.EditedBy).OfType<string>())
            .Concat(topics.Where(topic => topic.Removed).Select(topic => topic.RemovedBy).OfType<string>()).Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0) return new Dictionary<string, Render.Person>();
        var rows = await Participant.Get(ids, ct);
        return rows.OfType<Participant>().ToDictionary(row => row.Id, row => Render.Person.Of(row), StringComparer.Ordinal);
    }

    /// <summary>The terms a narrowing adds: a start within the month, one range of <c>ix_post_created</c>, and the
    /// author.</summary>
    private static List<Filter> Narrow(Narrowing to)
    {
        var terms = new List<Filter>();
        if (to.Within is { } month)
        {
            var at = FieldPath.Of(nameof(Post.CreatedAt));
            terms.Add(Filter.On(at, FilterOperator.Gte, FilterValue.Of(month.Start)));
            terms.Add(Filter.On(at, FilterOperator.Lt, FilterValue.Of(month.End)));
        }

        if (to.By is { } author) terms.Add(Filter.On(FieldPath.Of(nameof(Post.AuthorId)), FilterOperator.Eq, FilterValue.Of(author)));
        return terms;
    }
}
