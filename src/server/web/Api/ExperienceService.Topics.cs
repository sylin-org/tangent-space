using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Koan.Data.Core;
using Tangent.Application;
using Tangent.Community;
using Tangent.Conversation;
using Tangent.Identity;
using Tangent.Stewardship;

namespace Tangent.Api;

/// <summary>Topic and Post resource actions use the domain's current permission decisions.</summary>
public sealed partial class ExperienceService
{
    /// <summary>Start a Topic and its opening Post under a stable request identity. A partial
    /// Topic-only save can resume; a changed title or opening Post under that identity conflicts.</summary>
    public async Task<ResourceResult<ExperienceThreadData>> StartThread(ClaimsPrincipal principal, string tangentKey,
        string requestId, string title, string text, CancellationToken ct, string? format = null, string? audience = null, string? subtitle = null)
    {
        var participantId = ParticipationAccess.Require(principal, ParticipationGrants.Post);
        TangentDescription? tangent;
        using (EntityContext.NoCache())
            tangent = await tangents.Describe(participantId, tangentKey, ct);
        if (tangent is null)
            return ResourceResult<ExperienceThreadData>.Failure(ExperienceProblem.Of(ExperienceProblemCodes.PermissionDenied, "That Tangent is not visible to your account."));
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > Community.TopicConstants.MaximumTitleLength)
            throw new ArgumentException("A thread title must contain 1–120 characters.", nameof(title));
        if (string.IsNullOrWhiteSpace(requestId) || requestId.Length > 128
            || requestId.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
            throw new RequestArgumentException("requestId", "Use 1–128 letters, digits, hyphens or underscores.");
        PostContent.CheckText(text);
        var contentFormat = PostContent.CheckFormat(format);
        WindowPlanner.CheckContent(text, contentFormat);
        TopicReadAudience? selectedAudience = audience switch { null => null, "Public" => TopicReadAudience.Public, "Restricted" => TopicReadAudience.Restricted, _ => throw new ArgumentException("Choose Public or Admitted.") };
        var cleanTitle = title.Trim();
        var cleanSubtitle = subtitle?.Trim() ?? "";
        if (cleanSubtitle.Length > 240) throw new ArgumentException("A subtitle must fit within 240 characters.", nameof(subtitle));
        var topicKey = MintTopicKey(participantId, tangentKey, requestId);
        try
        {
            await tangents.CreateTopic(participantId, tangentKey, topicKey, cleanTitle, cleanSubtitle, ct, selectedAudience);
        }
        catch (TangentRuleViolation violated) when (violated.Denial == TangentDenial.AlreadyExists)
        {
            using var fresh = EntityContext.NoCache();
            var existing = await Topic.Get(topicKey, ct);
            if (existing is null || existing.TangentKey != tangentKey
                || existing.CreatorParticipantId != participantId || existing.Title != cleanTitle || existing.Description != cleanSubtitle
                || selectedAudience is { } expectedAudience && existing.ReadAudience != expectedAudience)
                throw new OperationConflictException(requestId);
        }
        catch (TangentRuleViolation violated) when (violated.Denial is TangentDenial.Forbidden or TangentDenial.NotFound)
        {
            throw new UnauthorizedAccessException(violated.Message);
        }
        var post = await conversation.CreatePost(participantId, topicKey, new PostCreateRequest(requestId, text, Format: format), ct);
        var topicRef = refs.Topic(tangentKey, topicKey);
        var data = new ExperienceThreadData(topicRef, refs.Tangent(tangentKey),
            refs.Post(tangentKey, topicKey, post.Id), cleanTitle,
            refs.Origin + "/tangents/" + tangentKey + "/posts/" + post.Id + "/");
        return ResourceResult<ExperienceThreadData>.Success(data with { RequestId = requestId });
    }

    public Task<ResourceResult<ExperiencePostData>> EditPost(ClaimsPrincipal principal, string postId, string text, CancellationToken ct, string? format = null)
        => ChangePost(principal, postId, text, delete: false, ct, format);

    public Task<ResourceResult<ExperiencePostData>> DeletePost(ClaimsPrincipal principal, string postId, CancellationToken ct)
        => ChangePost(principal, postId, text: null, delete: true, ct, format: null);

    /// <summary>Edits and removals require read reconciliation after an uncertain outcome.</summary>
    private async Task<ResourceResult<ExperiencePostData>> ChangePost(ClaimsPrincipal principal, string postId, string? text,
        bool delete, CancellationToken ct, string? format)
    {
        var participantId = ParticipationAccess.Require(principal, ParticipationGrants.Read);
        if (!delete) PostContent.CheckText(text!);
        if (string.IsNullOrWhiteSpace(postId) || postId.Length > 64
            || postId.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
            throw new RequestArgumentException("postId", "Choose a Post returned by this server.");
        Post stored;
        Topic topic;
        using (EntityContext.NoCache())
        {
            stored = await Post.Get(postId, ct)
                ?? throw new RequestArgumentException("postId", "Choose a Post returned by this server.");
            topic = await Topic.Get(stored.TopicKey, ct)
                ?? throw new RequestArgumentException("postId", "Choose a Post in an existing Topic.");
            ParticipationAccess.Require(principal,
                stored.AuthorParticipantId == participantId ? ParticipationGrants.Post : ParticipationGrants.Manage);
        }
        await conversation.ChangePost(participantId, topic.Id, postId, text, delete, ct, format: format);
        var fullRef = refs.Post(topic.TangentKey, topic.Id, postId);
        var data = new ExperiencePostData(fullRef, null,
            refs.Origin + "/tangents/" + topic.TangentKey + "/posts/" + postId + "/");
        return ResourceResult<ExperiencePostData>.Success(data);
    }

    /// <summary>The key depends on caller, Tangent and request ID only, so a changed title cannot
    /// allocate a second Topic for one request identity.</summary>
    private static string MintTopicKey(string participantId, string tangentKey, string requestId)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes(participantId + "\n" + tangentKey + "\n" + requestId)));
        return "thread-" + hash[..24];
    }
}
