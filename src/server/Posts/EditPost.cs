using System.Globalization;
using Koan.Data.Core;
using Tangent.Access;
using Tangent.Events;
using Tangent.Writer;

namespace Tangent.Posts;

/// <summary>Edits a Post: its body, and its title on a root. The Post exists for the editor, who holds
/// <see cref="Bit.View"/> in its Tangent, or the command answers that no Post has the Id; the editor holds
/// <see cref="Bit.EditOwn"/> on their own Post or <see cref="Bit.Moderate"/> on any, or is refused with the gate word —
/// <c>not yours</c> for someone else's, <c>removed</c> for a tombstone. The command carries the <see cref="Version"/>
/// the editor saw: an edit of another version is a conflict, and the sender reads the current text, never a silent
/// overwrite. In the same commit the Post as it stood goes to the audit partition, the Post takes the new words and
/// its version rises, and <see cref="EventKind.PostEdited"/> is appended with the new version, with one
/// <see cref="EventKind.Mention"/> per participant the new body mentions and the old did not
/// (<see cref="Mentions.Notify"/>); the body is judged where the command is built (<see cref="PostBody"/>), never in
/// the writer. Safe to repeat: when the
/// Post already says what the command says, at any version, it answers with the Post and writes nothing.</summary>
public sealed record EditPost(
    string PostId,
    long Version,
    PostBody Body,
    string? Title,
    string EditorId,
    DateTimeOffset? At = null) : ICommand<Post>
{
    public const string NoPost = "No Post has this Id.";

    /// <summary>The conflict's reason: the version the Post is at now.</summary>
    public static string Stale(long current) =>
        "The Post is at version " + current.ToString(CultureInfo.InvariantCulture) + " now, not the version this edit was made from.";

    public async Task<Outcome<Post>> Run(Sequences sequences, CancellationToken ct)
    {
        if (await Post.Get(PostId, ct) is not { } post) return new Outcome<Post>.Refused(NoPost);
        var capabilities = await Capabilities.Load(EditorId, post.TangentId, post, ct);
        if (!capabilities.Exists) return new Outcome<Post>.Refused(NoPost);
        if (!capabilities.Holds(Bit.EditOwn) && !capabilities.Holds(Bit.Moderate))
            return new Outcome<Post>.Refused(capabilities.Gate(Bit.EditOwn)!);

        if (post.Unchanged(Body.Text, Title)) return new Outcome<Post>.Done(post);
        if (post.Version != Version) return new Outcome<Post>.Conflict(Stale(post.Version));

        var at = At ?? DateTimeOffset.UtcNow;
        await AuditPartition.Snapshot(post, post.Version, ct);
        var before = post.MentionedIds.ToList();
        post.Edit(Body, Title);
        await post.Save(ct);
        await Event.Next(sequences, EventKind.PostEdited, at,
            tangentId: post.TangentId, topicId: post.TopicId, postId: post.Id, actorId: EditorId, version: post.Version).Save(ct);
        await Mentions.Notify(sequences, post, EditorId, before, at, ct);
        return new Outcome<Post>.Done(post);
    }
}
