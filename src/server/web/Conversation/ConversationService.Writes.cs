using System.Security.Cryptography;
using Koan.Data.Core;
using Tangent.Community;
using Tangent.Stewardship;
using Tangent.Activity;
using Tangent.Access;

namespace Tangent.Conversation;

public sealed partial class ConversationService
{
    /// <summary>An atomic upsert at the client-minted operation identity (ADR 0007). First delivery
    /// creates the projection and its sequence in one transaction; a re-delivery of the same package
    /// returns the same row unchanged; the same key with different content is a conflict. The
    /// returned projection is the receipt: creating with a used id replays its row.</summary>
    public async Task<Post> CreatePost(string participantId, string roomKey, PostCreateRequest input, CancellationToken ct)
    {
        PostContent.CheckText(input.Text);
        if (string.IsNullOrWhiteSpace(input.OperationId) || input.OperationId.Length > 128
            || input.OperationId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new ArgumentException("Use an operation ID of 1–128 letters, digits, hyphens or underscores.");
        var facets = PostFacets.Check(input.Text, input.Facets);
        await writes.WaitAsync(ct);
        try
        {
            Post? result = null;
            var conflict = false;
            await governance.WithCurrentPolicy(participantId, roomKey, async (policy, token) =>
            {
                RequireTopicCapability(policy, TopicCapability.Reply, "This topic's current rules do not allow posting.");
                var existing = (await Post.Query(m => m.TopicKey == roomKey && m.AuthorParticipantId == participantId && m.OperationId == input.OperationId,
                    Window<Post>(nameof(Post.Sequence), 1, 1), token)).FirstOrDefault();
                if (existing is { } found)
                {
                    if (found.Removed || found.Content.Text != input.Text || found.Content.ReplyTo != input.ReplyTo
                        || Canonical(found.Facets) != Canonical(await PostFacets.Effective(input.Text, facets, token)))
                    {
                        conflict = true;
                        return true;
                    }
                    result = found;
                    return true;
                }
                string? replyTo = null;
                if (input.ReplyTo is { } reply)
                {
                    var parent = await Post.Get(reply, token);
                    if (parent is null || parent.TopicKey != roomKey || parent.Removed)
                        throw new ArgumentException("Reply to a post in this topic.");
                    replyTo = parent.Id;
                }
                var state = await TopicConversation.Get(roomKey, token) ?? new TopicConversation { Id = roomKey };
                var now = clock.GetUtcNow();
                var projected = new Post
                {
                    Id = Guid.CreateVersion7().ToString("n"), TopicKey = roomKey, AuthorParticipantId = participantId,
                    Sequence = checked(++state.LastSequence), AcceptedAt = now,
                    Content = new PostContent(input.Text, now, replyTo), OperationId = input.OperationId,
                };
                await projected.Save(token);
                await state.Save(token);
                var topic = await Topic.Get(roomKey, token);
                await ActivityJournal.AppendInTransaction(ActivityKind.PostAccepted, roomKey, participantId, null,
                    topic?.TangentKey, projected.Sequence, now, token);
                result = projected;
                return true;
            }, ct);
            if (conflict) throw new OperationConflictException(input.OperationId);
            signals.Pulse(roomKey);
            return result!;
        }
        finally { writes.Release(); }
    }

    /// <summary>Canonical facet payload for the conflict check.</summary>
    private static string Canonical(IReadOnlyList<PostFacet>? facets)
        => facets is null ? "<auto>"
            : string.Join("|", facets.OrderBy(facet => facet.Start).ThenBy(facet => facet.Kind).Select(facet => facet.Canonical()));
}
