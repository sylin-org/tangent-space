namespace Tangent.Posts;

/// <summary>A Post as a write answers a JSON request: its Id and address, where it is — Tangent, Topic and the Post it
/// replies to — and, on a root, its slug, kind and title; its body, version and place in its Topic; who wrote it and
/// when; whether it is a tombstone, removed by whom and when; who other than its author last edited it and when; and
/// the verbs the writer holds on it now.</summary>
public sealed record WrittenPost(
    string Id,
    string Address,
    string Tangent,
    string TopicId,
    string? ParentId,
    string? Slug,
    string? Kind,
    string? Title,
    string Body,
    long Version,
    long Sequence,
    string AuthorId,
    DateTimeOffset CreatedAt,
    bool Removed,
    string? RemovedBy,
    DateTimeOffset? RemovedAt,
    string? EditedBy,
    DateTimeOffset? EditedAt,
    IReadOnlyList<string> Can)
{
    public static WrittenPost Of(Post post, string address, IReadOnlyList<string> can)
    {
        var root = post.ParentId is null;
        return new WrittenPost(post.Id, address, post.TangentId, post.TopicId, post.ParentId,
            root ? post.Slug : null, root ? PostWrites.Word(post.Kind) : null, root ? post.Title : null,
            post.Body, post.Version, post.Sequence, post.AuthorId, post.CreatedAt, post.Removed, post.RemovedBy, post.RemovedAt, post.EditedBy, post.EditedAt, can);
    }
}

/// <summary>What an edit made from another version answers a JSON request: RFC 9457's members, and the Post as it
/// stands now, whose text and version a new edit starts from.</summary>
public sealed record StaleEdit(int Status, string Title, string Detail, WrittenPost Current);
