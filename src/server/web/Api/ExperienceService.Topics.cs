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

/// <summary>The member-ring writes the connector's grammar has been waiting for. Authority is
/// the domain's own: topic creation answers to the Tangent's access map, post changes to the
/// Topic's own-post and moderator rules; these methods route the job and report what happened.
/// requestId stays the idempotency key; a replay answers the recorded outcome, a different
/// action under the same id a conflict.</summary>
public sealed partial class ExperienceService
{
    /// <summary>Start a thread: create the Topic and its opening Post in one job. The topic key
    /// is minted from the title plus a short hash of the requestId, so a retry after a lost
    /// response lands on the same Topic instead of a duplicate.</summary>
    public async Task<ExperienceResponse> StartThread(ClaimsPrincipal principal, string tangentKey,
        string requestId, string title, string text, CancellationToken ct)
    {
        var participantId = ParticipationAccess.Require(principal, ParticipationGrants.Post);
        var credential = CredentialOf(principal);
        var identity = await IdentityOf(participantId, ct);
        TangentDescription? tangent;
        using (EntityContext.NoCache())
            tangent = await tangents.Describe(participantId, tangentKey, ct);
        if (tangent is null)
            return Problem("start_thread", identity, ServerPlace(principal, await SiteLabel(ct)),
                ExperienceProblem.Of(ExperienceProblemCodes.PermissionDenied, "That Tangent is not visible to your account."),
                participantId: participantId, credential: credential, ct: ct);
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > Community.TopicConstants.MaximumTitleLength)
            throw new ArgumentException("A thread title must contain 1–120 characters.", nameof(title));
        PostContent.CheckText(text);
        var cleanTitle = title.Trim();
        // The topic key is minted from the title plus a hash of the requestId, so a retry after
        // a lost response lands on the same Topic; the opening post carries the requestId as its
        // operation id, so the same job replays its post instead of doubling it.
        var topicKey = MintTopicKey(cleanTitle, requestId);
        try
        {
            await tangents.CreateTopic(participantId, tangentKey, topicKey, cleanTitle, TopicAdmission.SignedIn, null, ct);
        }
        catch (TangentRuleViolation violated) when (violated.Denial == TangentDenial.AlreadyExists)
        {
            // The deterministic key exists because this job's earlier attempt created it; the
            // opening post below replays through its operation id.
        }
        catch (TangentRuleViolation violated) when (violated.Denial is TangentDenial.Forbidden or TangentDenial.NotFound)
        {
            throw new UnauthorizedAccessException(violated.Message);
        }
        var post = await conversation.CreatePost(participantId, topicKey, new PostCreateRequest(requestId, text), ct);
        var topicRef = refs.Topic(tangentKey, topicKey);
        var data = new ExperienceThreadData(topicRef, refs.Tangent(tangentKey),
            refs.Post(tangentKey, topicKey, post.Id), cleanTitle,
            refs.Origin + "/tangents/" + tangentKey + "/posts/" + post.Id + "/");
        var place = await TopicPlaceOf(principal, participantId, tangentKey, topicKey, ct);
        return await Assemble("start_thread", ExperienceStatus.Ok, identity, place,
            new ExperienceResult(data, new ExperienceReceipt(requestId, "completed", topicRef, null), null),
            (await digest.Page(participantId, credential, null, tangentKey, topicKey, 3, ct)).Attention,
            Empty(), [new(ExperienceActionNames.ReadTopic, topicRef, refs.Post(tangentKey, topicKey, post.Id), "Open the new thread")],
            null, CapabilitiesOf(place), participantId, credential, ct);
    }

    public Task<ExperienceResponse> EditPost(ClaimsPrincipal principal, string postRef, string text, CancellationToken ct)
        => ChangePost(principal, postRef, text, delete: false, "edit_post", ct);

    public Task<ExperienceResponse> DeletePost(ClaimsPrincipal principal, string postRef, CancellationToken ct)
        => ChangePost(principal, postRef, text: null, delete: true, "delete_post", ct);

    /// <summary>The connector sends a bare Post id; full references from this server's own
    /// envelopes resolve the same way. An edit converges on its text; a removal of a removed
    /// post answers "already removed" — no request protocol is needed to make that safe.</summary>
    private async Task<ExperienceResponse> ChangePost(ClaimsPrincipal principal, string postRef, string? text,
        bool delete, string operation, CancellationToken ct)
    {
        var participantId = ParticipationAccess.Require(principal, ParticipationGrants.Post);
        var credential = CredentialOf(principal);
        var identity = await IdentityOf(participantId, ct);
        if (!delete) PostContent.CheckText(text!);
        var target = await ResolvePostRef(postRef, ct);
        await conversation.ChangePost(participantId, target.TopicKey, target.PostId, text, delete, ct);
        var fullRef = refs.Post(target.TangentKey, target.TopicKey, target.PostId);
        var data = new ExperiencePostData(fullRef, null,
            refs.Origin + "/tangents/" + target.TangentKey + "/posts/" + target.PostId + "/");
        var place = await TopicPlaceOf(principal, participantId, target.TangentKey, target.TopicKey, ct);
        return await Assemble(operation, ExperienceStatus.Ok, identity, place,
            new ExperienceResult(data, null, null),
            (await digest.Page(participantId, credential, null, target.TangentKey, target.TopicKey, 3, ct)).Attention,
            Empty(), [new(ExperienceActionNames.ReadTopic, refs.Topic(target.TangentKey, target.TopicKey), fullRef, "Open the Topic")],
            null, CapabilitiesOf(place), participantId, credential, ct);
    }

    private async Task<(string TangentKey, string TopicKey, string PostId)> ResolvePostRef(string value, CancellationToken ct)
    {
        if (refs.ParsePost(value) is { } parsed) return parsed;
        // A bare Post id: look the row up and derive its Topic.
        if (value.Length is >= 1 and <= 64 && value.All(character =>
                char.IsAsciiLetterOrDigit(character) || character == '-'))
        {
            Post? post;
            using (EntityContext.NoCache())
                post = await Post.Get(value, ct);
            if (post is not null)
            {
                Topic? topic;
                using (EntityContext.NoCache())
                    topic = await Topic.Get(post.TopicKey, ct);
                if (topic is not null) return (topic.TangentKey, topic.Id, post.Id);
            }
        }
        throw new RequestArgumentException("postRef", "Copy a Post reference returned by this server.");
    }

    /// <summary>A stable key minted from the title and the request id: lowercase run of the
    /// title's letters and digits, bounded, suffixed with a short hash so two threads with one
    /// title stay distinct and one job replays onto its own Topic.</summary>
    private static string MintTopicKey(string title, string requestId)
    {
        var slug = new StringBuilder(title.Length);
        foreach (var raw in title.ToLowerInvariant())
        {
            var character = char.IsAsciiLetterOrDigit(raw) ? raw : '-';
            if (character == '-' && (slug.Length == 0 || slug[^1] == '-')) continue;
            slug.Append(character);
        }
        while (slug.Length > 0 && slug[^1] == '-') slug.Length--;
        if (slug.Length == 0) slug.Append("thread");
        var suffix = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(requestId)))[..8];
        var body = slug.Length > Community.TopicConstants.MaximumKeyLength - suffix.Length - 1
            ? slug.ToString(0, Community.TopicConstants.MaximumKeyLength - suffix.Length - 1).TrimEnd('-')
            : slug.ToString();
        return $"{body}-{suffix}";
    }
}
