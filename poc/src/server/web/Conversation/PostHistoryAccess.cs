using Koan.Web.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Tangent.Conversation;

/// <summary>Request-local scope populated only after asynchronous history authorization.</summary>
public sealed class PostHistoryScope
{
    public string? OriginalPostId { get; internal set; }
}

/// <summary>Koan applies the authorized original Post constraint to every snapshot query.</summary>
public sealed class PostHistoryAccess : EntityAccess<Post>
{
    public override IAccessFilter<Post> Constrain(IAccessFilter<Post> query, AccessAction action)
    {
        if (action != AccessAction.Read)
            return action is AccessAction.Update or AccessAction.Delete ? query.Where(row => false) : query;
        var postId = Services?.GetService<PostHistoryScope>()?.OriginalPostId;
        return postId is null ? query.Where(row => false) : query.Where(row => row.OfPostId == postId);
    }
}
