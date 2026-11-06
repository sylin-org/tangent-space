using System.Globalization;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core.Sorting;
using Tangent.Posts;

namespace Tangent.Tangents;

/// <summary>One bounded page of a Tangent's Topics, newest activity first, read through the topic-list index
/// <c>ix_post_topics</c> (<c>TangentId</c>, <c>ParentId</c>, <c>LastActivityAt</c>), or narrowed to the Topics started in
/// one month, a range of <c>ix_post_started</c>, or by one author, a range of <c>ix_post_topics_by</c>, which holds
/// <c>AuthorId</c> before <c>LastActivityAt</c> (<see cref="Narrowing"/>). A page holds at most <see cref="PageSize"/> Topics and continues from a <see cref="Cursor"/>,
/// which names the last Topic a page held by its place in that order rather than by a position, so a Topic present for the
/// whole read is on exactly one page, narrowed or not. Nothing is counted. Reads only and takes no lock; whom a Topic may
/// be shown to is the caller's to decide.</summary>
public static class TopicList
{
    /// <summary>The most Topics one page holds.</summary>
    public const int PageSize = 30;

    /// <summary>A page of Topics and where the next one starts; <see cref="Next"/> is null on the last page.</summary>
    public sealed record Page(IReadOnlyList<Post> Topics, Cursor? Next);

    /// <summary>The place of the last Topic a page held: its last activity and its Id, which orders Topics whose last
    /// activity is the same instant.</summary>
    public sealed record Cursor(DateTimeOffset At, string Id)
    {
        public static Cursor After(Post topic) => new(topic.LastActivityAt, topic.Id);

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

    /// <summary>The page of <paramref name="tangentId"/>'s Topics after <paramref name="after"/>, or the first page,
    /// narrowed as <paramref name="to"/> says: one read, limited to the page and one more, which says whether a next page
    /// exists.</summary>
    public static async Task<Page> Read(string tangentId, Cursor? after, CancellationToken ct, int size = PageSize, Narrowing? to = null)
    {
        if (size is < 1 or > PageSize) throw new ArgumentOutOfRangeException(nameof(size));
        var terms = Narrow(to ?? Narrowing.None);
        if (after is not null) terms.Add(Seek(after));
        var query = new QueryDefinition { Filter = terms.Count == 0 ? null : Filter.All([.. terms]), Page = 1, PageSize = size + 1 }
            .WithSort<Post>(sort => sort.OrderByDescending(post => post.LastActivityAt).ThenByDescending(post => post.Id));
        var topics = await Post.Query(post => post.TangentId == tangentId && post.ParentId == null, query, ct);
        return topics.Count > size
            ? new Page([.. topics.Take(size)], Cursor.After(topics[size - 1]))
            : new Page(topics, null);
    }

    /// <summary>The terms a narrowing adds: a start within the month, which names both values of <c>Removed</c> so the
    /// month is one range of <c>ix_post_started</c> for each, and the author.</summary>
    private static List<Filter> Narrow(Narrowing to)
    {
        var terms = new List<Filter>();
        if (to.Within is { } month)
        {
            var at = FieldPath.Of(nameof(Post.CreatedAt));
            terms.Add(Filter.On(FieldPath.Of(nameof(Post.Removed)), FilterOperator.In, FilterValue.Many([false, true])));
            terms.Add(Filter.On(at, FilterOperator.Gte, FilterValue.Of(month.Start)));
            terms.Add(Filter.On(at, FilterOperator.Lt, FilterValue.Of(month.End)));
        }

        if (to.By is { } author) terms.Add(Filter.On(FieldPath.Of(nameof(Post.AuthorId)), FilterOperator.Eq, FilterValue.Of(author)));
        return terms;
    }

    /// <summary>The Topics after the cursor in the order a page reads them: last activity no later than the cursor's,
    /// which the index answers as a range, and, at the cursor's own instant, an Id before the cursor's.</summary>
    private static Filter Seek(Cursor after)
    {
        var at = FieldPath.Of(nameof(Post.LastActivityAt));
        var id = FieldPath.Of(nameof(Post.Id));
        return Filter.All(
            Filter.On(at, FilterOperator.Lte, FilterValue.Of(after.At)),
            Filter.Any(
                Filter.On(at, FilterOperator.Lt, FilterValue.Of(after.At)),
                Filter.On(id, FilterOperator.Lt, FilterValue.Of(after.Id))));
    }
}
