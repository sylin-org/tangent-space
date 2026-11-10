using Koan.Web.Authorization;

namespace Tangent.Posts;

/// <summary>Who writes a Post that a write through the Topic and Post controllers brings: the signed-in participant, whose person id
/// is the principal's, stamped on its author as it is created, so no body names another author. Nothing else: who may reach an
/// address is the Doorway's (<see cref="Access.Doorway"/>), and what an edit or a removal may do the capabilities say, which hold
/// whose a Post is.</summary>
public sealed class PostAccess : EntityAccess<Post>
{
    public override IAccessFilter<Post> Constrain(IAccessFilter<Post> q, AccessAction action)
        => action == AccessAction.Create ? q.Stamp(post => post.AuthorId, CurrentUserId ?? "") : q;
}
