using System.Globalization;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core.Sorting;
using Tangent.Posts;

namespace Tangent.Tangents;

/// <summary>One bounded page of a Tangent's Topics, newest first, in the Tangent's own sort (<see cref="For"/>): a discussions
/// Tangent's by last activity, through <c>ix_post_topics</c> (<c>TangentId</c>, <c>ParentId</c>, <c>LastActivityAt</c>), and an
/// articles Tangent's by when each started, through <c>ix_post_created</c> (<c>TangentId</c>, <c>ParentId</c>,
/// <c>CreatedAt</c>), removed Topics among them. A list narrows to the Topics started in one month, a range of
/// <c>ix_post_created</c>, or by one author, a range of <c>ix_post_topics_by</c>, which holds <c>AuthorId</c> before
/// <c>CreatedAt</c> (<see cref="Narrowing"/>), the ways an articles Tangent's rail offers, so by start each range is read in
/// its order, and by last activity it is sorted. A page holds at most <see cref="PageSize"/> Topics and continues from a
/// <see cref="Cursor"/>, which names the last Topic a page held by its place in that sort rather than by a position, so a Topic present for the
/// whole read is on exactly one page, narrowed or not. The whole list leads its first page with the Topic its moderators
/// pinned (<see cref="Pinned"/>), which every page of it leaves out of its own rows. Nothing is counted. Reads only and takes no lock; whom a Topic may
/// be shown to is the caller's to decide.</summary>
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

    /// <summary>The sort of <paramref name="tangent"/>'s list: an articles Tangent's by when each article started, a
    /// discussions Tangent's by latest activity.</summary>
    public static Sort For(Tangent tangent) => tangent.DefaultKind == PostKind.Article ? Sort.Start : Sort.Activity;

    /// <summary>A page of Topics and where the next one starts; <see cref="Next"/> is null on the last page.</summary>
    public sealed record Page(IReadOnlyList<Post> Topics, Cursor? Next);

    /// <summary>The place of the last Topic a page held: its last activity or its start, as the list sorts, and its Id,
    /// which orders Topics at the same instant.</summary>
    public sealed record Cursor(DateTimeOffset At, string Id)
    {
        public static Cursor After(Post topic, Sort sort) => new(sort == Sort.Start ? topic.CreatedAt : topic.LastActivityAt, topic.Id);

        /// <summary>The cursor a query carries, or null when the text is not one.</summary>
        public static Cursor? Parse(string? text)
        {
            var parts = text?.Split('.') ?? [];
            if (parts.Length != 2) return null;
            if (!long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                || ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks) return null;
            if (!Tid.IsValid(parts[1])) return null;
            return new Cursor(new DateTimeOffset(ticks, TimeSpan.Zero), parts[1]);
        }

        public override string ToString() => At.UtcTicks.ToString(CultureInfo.InvariantCulture) + "." + Id;
    }

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
    /// <paramref name="sort"/>, narrowed as <paramref name="to"/> says, without the Topic whose root is <paramref name="leaving"/>, where one is named:
    /// one read, limited to the page, the one it leaves and one more, which says whether a next page exists.</summary>
    public static async Task<Page> Read(string tangentId, Cursor? after, CancellationToken ct, int size = PageSize, Narrowing? to = null,
        Sort sort = Sort.Activity, string? leaving = null)
    {
        if (size is < 1 or > PageSize) throw new ArgumentOutOfRangeException(nameof(size));
        var terms = Narrow(to ?? Narrowing.None);
        if (after is not null) terms.Add(Seek(after, sort));
        var query = new QueryDefinition { Filter = terms.Count == 0 ? null : Filter.All([.. terms]), Page = 1, PageSize = size + (leaving is null ? 1 : 2) }
            .WithSort<Post>(by => (sort == Sort.Start ? by.OrderByDescending(post => post.CreatedAt) : by.OrderByDescending(post => post.LastActivityAt))
                .ThenByDescending(post => post.Id));
        var topics = (await Post.Query(post => post.TangentId == tangentId && post.ParentId == null, query, ct))
            .Where(topic => topic.Id != leaving).ToList();
        return topics.Count > size
            ? new Page([.. topics.Take(size)], Cursor.After(topics[size - 1], sort))
            : new Page(topics, null);
    }

    /// <summary>The Topic <paramref name="tangent"/>'s moderators pinned, while its root stands there: no read where none is
    /// pinned, otherwise one keyed read; null where the pin names a root removed, gone from the Tangent or deleted, which
    /// nothing writes away.</summary>
    public static async Task<Post?> Pinned(Tangent tangent, CancellationToken ct)
        => tangent.PinnedId is { } id && await Post.Get(id, ct) is { ParentId: null, Removed: false } root && root.TangentId == tangent.Id ? root : null;

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

    /// <summary>The Topics after the cursor in the sort a page reads them: last activity or start no later than the
    /// cursor's, which the index answers as a range, and, at the cursor's own instant, an Id before the cursor's.</summary>
    private static Filter Seek(Cursor after, Sort sort)
    {
        var at = FieldPath.Of(sort == Sort.Start ? nameof(Post.CreatedAt) : nameof(Post.LastActivityAt));
        var id = FieldPath.Of(nameof(Post.Id));
        return Filter.All(
            Filter.On(at, FilterOperator.Lte, FilterValue.Of(after.At)),
            Filter.Any(
                Filter.On(at, FilterOperator.Lt, FilterValue.Of(after.At)),
                Filter.On(id, FilterOperator.Lt, FilterValue.Of(after.Id))));
    }
}
