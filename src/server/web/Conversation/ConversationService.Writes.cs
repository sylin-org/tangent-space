using System.Security.Cryptography;
using Koan.Data.Core;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sorting;
using Tangent.Community;
using Tangent.Stewardship;
using Tangent.Activity;
using Tangent.Access;

namespace Tangent.Conversation;

public sealed partial class ConversationService
{
    private static readonly QueryDefinition LatestPost = new()
    {
        Page = 1, PageSize = 1,
        Sort = [new SortSpec(new MemberPath(typeof(Post), [typeof(Post).GetProperty(nameof(Post.Sequence))!],
            typeof(long), false, -1), true)]
    };

    /// <summary>Creates one Post for the author, Topic and operation identity. Koan may persist
    /// only part of a deferred commit. A matching retry repairs the Topic sequence and journal
    /// before returning the Post; a divergent retry is a conflict. One writer process owns writes.</summary>
    public async Task<Post> CreatePost(string participantId, string topicKey, PostCreateRequest input, CancellationToken ct)
        => await AcceptPost(participantId, participantId, topicKey, input, ct);

    internal Task<Post> ImportSourcePost(string operatorId, string authorId, string topicKey, string operationId, string text, DateTimeOffset createdAt, string? replyTo, string sourceUri, string sourceCid, CancellationToken ct)
        => AcceptPost(operatorId,authorId,topicKey,new PostCreateRequest(operationId,text,replyTo),ct,createdAt,sourceUri,sourceCid);

    private async Task<Post> AcceptPost(string acceptanceActor, string participantId, string topicKey, PostCreateRequest input, CancellationToken ct, DateTimeOffset? sourceCreatedAt=null,string? sourceUri=null,string? sourceCid=null)
    {
        PostContent.CheckText(input.Text);
        var format = PostContent.CheckFormat(input.Format);
        if (format == PostContent.Markdown)
        {
            if (input.Facets is { Count: > 0 }) throw new ArgumentException("Markdown posts cannot carry text facets.");
        }
        WindowPlanner.CheckContent(input.Text, format);
        if (string.IsNullOrWhiteSpace(input.OperationId) || input.OperationId.Length > 128
            || input.OperationId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new ArgumentException("Use an operation ID of 1–128 letters, digits, hyphens or underscores.");
        var facets = format == PostContent.Markdown ? Array.Empty<PostFacet>() : PostFacets.Check(input.Text, input.Facets);
        await writes.WaitAsync(ct);
        try
        {
            Post? result = null;
            var conflict = false;
            await governance.WithinAcceptanceAsync(acceptanceActor, topicKey, async (policy, token) =>
            {
                RequireTopicCapability(policy, sourceCreatedAt is null ? TopicCapability.Reply : TopicCapability.ManageTopic, "This topic's current rules do not allow accepting this post.");
                if (sourceCreatedAt is not null && (await Tangent.Identity.Participant.Get(participantId,token)) is not {IsSuspended:false})
                    throw new UnauthorizedAccessException("The source author is unavailable.");
                var existing = (await Post.Query(m => m.TopicKey == topicKey && m.AuthorParticipantId == participantId && m.OperationId == input.OperationId,
                    Window<Post>(nameof(Post.Sequence), 1, 1), token)).FirstOrDefault();
                if (existing is { } found)
                {
                    // A null facet package means the server derived mentions at first acceptance.
                    // Identity labels may change before a retry, so that accepted package is
                    // immutable and is not re-derived to decide whether this is the same request.
                    if (found.Removed || found.Content.Text != input.Text || found.Content.ReplyTo != input.ReplyTo
                        || found.Content.Format != format
                        || facets is not null && Canonical(found.Facets) != Canonical(facets))
                    {
                        conflict = true;
                        return true;
                    }
                    await media.BindReferences(participantId, topicKey, found.Id, input.Text, format, token);
                    await RepairAcceptedPost(found, participantId, topicKey, token);
                    result = found;
                    return true;
                }
                string? replyTo = null;
                if (input.ReplyTo is { } reply)
                {
                    var parent = await Post.Get(reply, token);
                    if (parent is null || parent.TopicKey != topicKey || parent.Removed)
                        throw new ArgumentException("Reply to a post in this topic.");
                    replyTo = parent.Id;
                }
                var state = await TopicConversation.Get(topicKey, token) ?? new TopicConversation { Id = topicKey };
                var latest = (await Post.Query(m => m.TopicKey == topicKey && m.OfPostId == null,
                    LatestPost, token)).FirstOrDefault();
                state.LastSequence = Math.Max(state.LastSequence, latest?.Sequence ?? 0);
                var now = clock.GetUtcNow();
                var postId = Guid.CreateVersion7().ToString("n");
                await media.ValidateReferences(participantId, topicKey, postId, input.Text, format, token);
                var projected = new Post
                {
                    Id = postId, TopicKey = topicKey, AuthorParticipantId = participantId,
                    Sequence = checked(++state.LastSequence), AcceptedAt = now,
                    Content = new PostContent(input.Text, sourceCreatedAt ?? now, replyTo, format), OperationId = input.OperationId,
                    Facets = facets, SourceUri = sourceUri, SourceCid = sourceCid,
                };
                await projected.Save(token);
                await media.BindReferences(participantId, topicKey, projected.Id, input.Text, format, token);
                await state.Save(token);
                var topic = await Topic.Get(topicKey, token);
                await ActivityJournal.AppendInTransaction(ActivityKind.PostAccepted, topicKey, participantId, null,
                    topic?.TangentKey, projected.Sequence, now, token);
                result = projected;
                return true;
            }, ct);
            if (conflict) throw new OperationConflictException(input.OperationId);
            signals.Pulse(topicKey);
            return result!;
        }
        finally { writes.Release(); }
    }

    private static async Task RepairAcceptedPost(Post post, string participantId, string topicKey, CancellationToken ct)
    {
        var state = await TopicConversation.Get(topicKey, ct) ?? new TopicConversation { Id = topicKey };
        if (state.LastSequence < post.Sequence)
        {
            state.LastSequence = post.Sequence;
            await state.Save(ct);
        }
        var journal = (await ActivityJournal.Query(entry => entry.Kind == ActivityKind.PostAccepted
            && entry.TopicKey == topicKey && entry.ActorParticipantId == participantId
            && entry.PostSequence == post.Sequence, new QueryDefinition { Page = 1, PageSize = 1 }, ct)).FirstOrDefault();
        if (journal is not null) return;
        var topic = await Topic.Get(topicKey, ct);
        await ActivityJournal.AppendInTransaction(ActivityKind.PostAccepted, topicKey, participantId, null,
            topic?.TangentKey, post.Sequence, post.AcceptedAt, ct);
    }

    /// <summary>Canonical facet payload for the conflict check.</summary>
    private static string Canonical(IReadOnlyList<PostFacet>? facets)
        => facets is null ? "<auto>"
            : string.Join("|", facets.OrderBy(facet => facet.Start).ThenBy(facet => facet.Kind).Select(facet => facet.Canonical()));
}
