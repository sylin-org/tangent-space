using System.Globalization;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core.Sorting;
using Tangent.Posts;

namespace Tangent.Participants;

/// <summary>One bounded page of a participant's Posts, newest first, read through <c>ix_post_authors</c>
/// (<c>AuthorId</c>, <c>CreatedAt</c>), none removed: one scan of at most <see cref="ScanSize"/> of their Posts after the
/// cursor, of which the page keeps those in the Tangents the caller says the reader may view, at most
/// <see cref="PageSize"/>. A page continues from a <see cref="Cursor"/>: the last Post it holds where the scan holds more
/// it may keep, otherwise the last Post the scan read, so a Post present for the whole read is on exactly one page, and a
/// page can hold fewer than <see cref="PageSize"/> Posts, or none, while a next page follows. That cursor is all a page
/// says of the Posts the reader may not view — that one was written at its instant — and only where a whole scan held no
/// more than a page of those they may. Nothing is counted. Reads only and takes no lock.</summary>
public static class RecentPosts
{
    /// <summary>The most Posts one page holds.</summary>
    public const int PageSize = 20;

    /// <summary>The most Posts one page reads to find those it holds.</summary>
    public const int ScanSize = 100;

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

    /// <summary>The page of <paramref name="authorId"/>'s Posts after <paramref name="after"/>, or the first page: one
    /// read of at most <see cref="ScanSize"/> Posts, and one question to <paramref name="viewable"/>, which answers which
    /// of the Tangents those Posts are in the reader may view.</summary>
    public static async Task<Page> Read(string authorId, Func<IReadOnlyList<string>, Task<IReadOnlySet<string>>> viewable, Cursor? after,
        CancellationToken ct)
    {
        var query = new QueryDefinition { Filter = after is null ? null : Seek(after), Page = 1, PageSize = ScanSize }
            .WithSort<Post>(sort => sort.OrderByDescending(post => post.CreatedAt).ThenByDescending(post => post.Id));
        var rows = await Post.Query(post => post.AuthorId == authorId && !post.Removed, query, ct);
        if (rows.Count == 0) return new Page([], null);
        var may = await viewable(rows.Select(post => post.TangentId).Distinct(StringComparer.Ordinal).ToList());
        var kept = new List<Post>(PageSize);
        foreach (var post in rows)
        {
            if (!may.Contains(post.TangentId)) continue;
            if (kept.Count == PageSize) return new Page(kept, Cursor.After(kept[^1]));
            kept.Add(post);
        }

        return new Page(kept, rows.Count < ScanSize ? null : Cursor.After(rows[^1]));
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
