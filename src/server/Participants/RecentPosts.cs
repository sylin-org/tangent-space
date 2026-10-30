using System.Globalization;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core.Sorting;
using Tangent.Posts;

namespace Tangent.Participants;

/// <summary>One bounded page of a participant's Posts, newest first, read through <c>ix_post_authors</c>
/// (<c>AuthorId</c>, <c>CreatedAt</c>): only Posts in the Tangents the caller names and none removed, so a Post the reader
/// may not view is never read and nothing past it is said. A page holds at most <see cref="PageSize"/> Posts and continues
/// from a <see cref="Cursor"/>, the place of the last Post a page held, so a Post present for the whole read is on
/// exactly one page. Nothing is counted. Reads only and takes no lock.</summary>
public static class RecentPosts
{
    /// <summary>The most Posts one page holds.</summary>
    public const int PageSize = 20;

    /// <summary>A page of Posts and where the next one starts; <see cref="Next"/> is null on the last page.</summary>
    public sealed record Page(IReadOnlyList<Post> Posts, Cursor? Next);

    /// <summary>The place of the last Post a page held: when it was written and its Id, which orders Posts written at
    /// the same instant.</summary>
    public sealed record Cursor(DateTimeOffset At, string Id)
    {
        public static Cursor After(Post post) => new(post.CreatedAt, post.Id);

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

    /// <summary>The page of <paramref name="authorId"/>'s Posts in <paramref name="tangentIds"/> after
    /// <paramref name="after"/>, or the first page: one read, limited to one more than the page, which says whether a
    /// next page exists. No Tangent named reads nothing.</summary>
    public static async Task<Page> Read(string authorId, IReadOnlyList<string> tangentIds, Cursor? after, CancellationToken ct)
    {
        if (tangentIds.Count == 0) return new Page([], null);
        var filters = new List<Filter> { Filter.On(FieldPath.Of(nameof(Post.TangentId)), FilterOperator.In, FilterValue.Many([.. tangentIds])) };
        if (after is not null) filters.Add(Seek(after));
        var query = new QueryDefinition { Filter = Filter.All([.. filters]), Page = 1, PageSize = PageSize + 1 }
            .WithSort<Post>(sort => sort.OrderByDescending(post => post.CreatedAt).ThenByDescending(post => post.Id));
        var rows = await Post.Query(post => post.AuthorId == authorId && !post.Removed, query, ct);
        return rows.Count > PageSize
            ? new Page(rows.Take(PageSize).ToList(), Cursor.After(rows[PageSize - 1]))
            : new Page(rows, null);
    }

    /// <summary>The Posts after the cursor in the order a page reads them: written no later than the cursor's Post,
    /// which the index answers as a range, and, at its own instant, with an Id before its Id.</summary>
    private static Filter Seek(Cursor after)
    {
        var at = FieldPath.Of(nameof(Post.CreatedAt));
        var id = FieldPath.Of(nameof(Post.Id));
        return Filter.All(
            Filter.On(at, FilterOperator.Lte, FilterValue.Of(after.At)),
            Filter.Any(
                Filter.On(at, FilterOperator.Lt, FilterValue.Of(after.At)),
                Filter.On(id, FilterOperator.Lt, FilterValue.Of(after.Id))));
    }
}
