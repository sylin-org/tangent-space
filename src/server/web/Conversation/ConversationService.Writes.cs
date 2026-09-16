using Koan.Data.Core;
using TangentSpace.Rooms;
using TangentSpace.Activity;
using TangentSpace.Authorization;

namespace TangentSpace.Conversation;

public sealed partial class ConversationService
{
    /// <summary>An atomic upsert at the client-minted operation identity (ADR 0007). First delivery
    /// creates the durable decision, its projection and the sequence in one transaction; a
    /// re-delivery of the same package returns the same row unchanged; the same key with different
    /// content is a conflict. The returned projection is the receipt.</summary>
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
            var uri = $"local://{roomKey}/{input.OperationId}";
            var cid = "local-" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(uri)))[..24];
            Post? result = null;
            var conflict = false;
            await governance.WithCurrentPolicy(participantId, roomKey, async (policy, token) =>
            {
                RequireTopicCapability(policy, TopicCapability.Reply, "This topic's current rules do not allow posting.");
                var existing = (await Post.Query(m => m.RoomKey == roomKey && m.AuthorParticipantId == participantId && m.OperationId == input.OperationId,
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
                if (input.ReplyTo is { } reply)
                {
                    var target = await SourceDecision.Get(SourceDecision.Key(roomKey, reply.Uri, reply.Cid), token);
                    if (target?.Accepted != true) throw new ArgumentException("Reply to an accepted post in this topic.");
                }
                var state = await TopicConversation.Get(roomKey, token) ?? new TopicConversation { Id = roomKey };
                var content = new PostContent(input.Text, clock.GetUtcNow(), input.ReplyTo);
                var decision = new SourceDecision
                {
                    Id = SourceDecision.Key(roomKey, uri, cid), RoomKey = roomKey, AuthorParticipantId = participantId, SourceUri = uri, SourceCid = cid,
                    Accepted = true, Reason = "accepted", DecidedAt = clock.GetUtcNow(),
                    PolicyRevision = policy.SelectedPolicyRevision, SpacePolicyRevision = policy.SpacePolicyRevision,
                    Content = content, Sequence = checked(++state.LastSequence),
                };
                await decision.Save(token);
                var projected = Post.Project(decision);
                projected.OperationId = input.OperationId;
                projected.Facets = facets;
                await projected.Save(token);
                await state.Save(token);
                var topic = await Topic.Get(roomKey, token);
                await ActivityJournal.AppendInTransaction(ActivityKind.MessageAccepted, roomKey, participantId, null,
                    topic?.TangentKey, decision.Sequence, decision.DecidedAt, token);
                result = projected;
                return true;
            }, ct);
            if (conflict) throw new ArgumentException("That operation ID was already used with different content.");
            updates.Pulse(roomKey);
            ActivityJournal.SignalAfterCommit();
            return result!;
        }
        finally { writes.Release(); }
    }

    /// <summary>Canonical facet payload for the idempotency conflict check.</summary>
    private static string Canonical(IReadOnlyList<PostFacet>? facets)
        => facets is null ? "<auto>"
            : string.Join("|", facets.OrderBy(facet => facet.Start).ThenBy(facet => facet.Kind).Select(facet => facet.Canonical()));
}
