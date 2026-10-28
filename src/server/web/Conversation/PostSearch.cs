using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Sorting;

namespace Tangent.Conversation;

public static class PostSearch
{
    public static async Task Prepare(CancellationToken ct)
    {
        using var fresh = EntityContext.NoCache();
        var page = new QueryDefinition { Sort = SortSpecParser.ParseStrict<Post>("Id") }.WithPagination(1, 25);
        while (true)
        {
            var posts = await Post.Query(post => post.SearchText == null && post.OfPostId == null, page, ct);
            if (posts.Count == 0) return;
            foreach (var post in posts) await post.Save(ct);
        }
    }
}
