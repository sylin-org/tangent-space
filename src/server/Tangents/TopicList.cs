using System.Globalization;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Core.Sorting;
using Tangent.Posts;

namespace Tangent.Tangents;

/// <summary>One bounded page of a Tangent's Topics, newest activity first, read through the topic-list index
/// <c>ix_post_topics</c> (<c>TangentId</c>, <c>ParentId</c>, <c>LastActivityAt</c>). A page holds at most
/// <see cref="PageSize"/> Topics and continues from a <see cref="Cursor"/>, which names the last Topic a page held by
/// its place in that order rather than by a position, so a Topic present for the whole read is on exactly one page.
/// Nothing is counted. Reads only and takes no lock; whom a Topic may be shown to is the caller's to decide.</summary>
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

    /// <summary>The page of <paramref name="tangentId"/>'s Topics after <paramref name="after"/>, or the first page:
    /// one read, limited to the page and one more, which says whether a next page exists.</summary>
    public static async Task<Page> Read(string tangentId, Cursor? after, CancellationToken ct, int size = PageSize)
    {
        if (size is < 1 or > PageSize) throw new ArgumentOutOfRangeException(nameof(size));
        var query = new QueryDefinition { Filter = after is null ? null : Seek(after), Page = 1, PageSize = size + 1 }
            .WithSort<Post>(sort => sort.OrderByDescending(post => post.LastActivityAt).ThenByDescending(post => post.Id));
        var topics = await Post.Query(post => post.TangentId == tangentId && post.ParentId == null, query, ct);
        return topics.Count > size
            ? new Page([.. topics.Take(size)], Cursor.After(topics[size - 1]))
            : new Page(topics, null);
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
