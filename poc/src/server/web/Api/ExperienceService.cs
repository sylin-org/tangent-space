using System.Security.Claims;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sorting;
using Koan.Data.Core;
using Tangent.Access;
using Tangent.Community;
using Tangent.Conversation;
using Tangent.Activity;
using Tangent.Application;
using Tangent.Stewardship;
using Tangent.Identity;
using Tangent.Spaces;
using Tangent.Infrastructure;

namespace Tangent.Api;

/// <summary>Domain resource reads and writes over the TangentServer hub. The acting
/// participant always derives from the verified credential.</summary>
public sealed partial class ExperienceService(
    TangentServer hub, References refs, TimeProvider clock, ExperienceDigest digest,
    ModerationCaseService moderation, UserManagementService users)
{
    private TangentGovernance tangents => hub.Tangents;
    private TopicGovernance topics => hub.Topics;
    private ParticipantGovernance participants => hub.Participants;
    private ConversationService conversation => hub.Posts;

    public const int DefaultDigestLimit = 5;
    public const int MaximumDigestLimit = 25;
    public const int DirectoryLimit = 10;

    // ---------- Arrival and orientation ----------

    public async Task<ResourceResult<ExperienceArrivalData>> Arrive(ClaimsPrincipal principal, string? scopeRef, CancellationToken ct)
    {
        var participantId = ParticipationAccess.Require(principal, ParticipationGrants.Read);
        var credential = CredentialOf(principal);
        var (scopeTangent, scopeTopic) = await ScopeOf(participantId, scopeRef, ct);
        var identity = await IdentityOf(participantId, ct);
        Space? spaceRow;
        using (EntityContext.NoCache())
            spaceRow = await Space.Get(TangentConstants.SpaceId, ct);
        var space = spaceRow?.Name is { Length: > 0 } name ? name : "This Tangent server";
        var page = await digest.Page(participantId, credential, null, scopeTangent, scopeTopic, 3, ct);
        var (directory, continuation, _) = await TangentPage(participantId, 1, 0, DirectoryLimit, ct);
        var management = await ManagementAuthorityOf(principal, participantId, ct);
        var hasManageGrant = TransportGrant(principal, ParticipationGrants.Manage);
        var canStewardScope = false;
        if (hasManageGrant && scopeTopic is { } topicKey)
        {
            var policy = await conversation.ReadPolicy(participantId, topicKey, ct);
            canStewardScope = AccessEvaluator.Evaluate(policy, TopicCapability.Read).Allowed
                && AccessEvaluator.Evaluate(policy, TopicCapability.ManageTopic).Allowed;
        }
        var place = new ExperiencePlace(refs.ServerRef, space,
            spaceRow?.IsOwner(participantId) == true ? "owner" : "participant");
        var authority = new ExperienceAuthority(true,
            TransportGrant(principal, ParticipationGrants.Post),
            hasManageGrant, canStewardScope, management);
        return ResourceResult<ExperienceArrivalData>.Success(
            new ExperienceArrivalData(directory, continuation, identity, place, authority, page));
    }

    // ---------- Directories ----------

    public async Task<ResourceResult<ExperienceTangentsData>> ListTangents(ClaimsPrincipal principal, string? cursor, CancellationToken ct)
    {
        var participantId = ParticipationAccess.Require(principal, ParticipationGrants.Read);
        var selected = DecodeDirectoryCursor(cursor, "experience:tangents", participantId);
        var page = selected?.Page ?? 1;
        var offset = InnerOffset(selected);
        var (tangents, continuation, _) = await TangentPage(participantId, page, offset, DirectoryLimit, ct);
        return ResourceResult<ExperienceTangentsData>.Success(new ExperienceTangentsData(tangents, continuation));
    }

    public async Task<ResourceResult<ExperienceTopicsData>> ListTopics(ClaimsPrincipal principal, string tangentKey, string? cursor, CancellationToken ct)
    {
        var participantId = ParticipationAccess.Require(principal, ParticipationGrants.Read);
        TangentDescription? selected;
        using (EntityContext.NoCache())
            selected = await tangents.Describe(participantId, tangentKey, ct);
        if (selected is null)
            return ResourceResult<ExperienceTopicsData>.Failure(ExperienceProblem.Of(ExperienceProblemCodes.PermissionDenied, "That Tangent is not visible to your account."));
        var decoded = DecodeDirectoryCursor(cursor, "experience:topics:" + tangentKey, participantId);
        var page = decoded?.Page ?? 1;
        var offset = InnerOffset(decoded);
        TangentTopicDirectory listing;
        using (EntityContext.NoCache())
            listing = await topics.ListForTangent(participantId, tangentKey, page, ct);
        var visible = listing.Topics.Skip(offset).ToList();
        var slice = visible.Take(DirectoryLimit).ToList();
        string? continuation = null;
        if (visible.Count > DirectoryLimit)
            continuation = refs.EncodeListCursor("experience:topics:" + tangentKey, participantId, page, (offset + DirectoryLimit).ToString());
        else if (listing.NextPage is { } next)
            continuation = refs.EncodeListCursor("experience:topics:" + tangentKey, participantId, next, "0");
        var listed = slice.Select(topic => TopicDto(tangentKey, topic)).ToList();
        return ResourceResult<ExperienceTopicsData>.Success(new ExperienceTopicsData(listed, continuation));
    }

    // ---------- Topic reading ----------

    public async Task<ResourceResult<ExperienceTopicData>> ReadTopic(ClaimsPrincipal principal, string topicKey, string? cursor,
        string? aroundPostRef, int limit, CancellationToken ct)
    {
        var participantId = ParticipationAccess.Require(principal, ParticipationGrants.Read);
        Topic? stored;
        using (EntityContext.NoCache())
            stored = await Topic.Get(topicKey, ct);
        if (stored is null)
            return ResourceResult<ExperienceTopicData>.Failure(ExperienceProblem.Of(ExperienceProblemCodes.PermissionDenied, "That Topic is not visible to your account."));
        var tangentKey = stored.TangentKey;
        string? anchorId = null;
        if (aroundPostRef is { } anchorRef)
        {
            var anchor = refs.ParsePost(anchorRef);
            if (anchor is null || anchor.Value.TopicKey != topicKey || anchor.Value.TangentKey != tangentKey)
                throw new RequestArgumentException("aroundPostRef", "Choose a Post in this Topic.");
            anchorId = anchor.Value.PostId;
        }
        TopicWindow window;
        try
        {
            window = await conversation.ReadWindow(participantId, topicKey, cursor, anchorId, limit, ct);
        }
        catch (ArgumentException error)
        {
            return ResourceResult<ExperienceTopicData>.Failure(ExperienceProblem.Of(ExperienceProblemCodes.CursorExpired,
                    error.Message.Contains("cursor", StringComparison.OrdinalIgnoreCase)
                        ? "This history cursor expired or no longer matches. Open a fresh window explicitly."
                        : error.Message));
        }
        var posts = Posts(tangentKey, topicKey, window);
        var policy = await conversation.ReadPolicy(participantId, topicKey, ct);
        var canWrite = AccessEvaluator.Evaluate(policy, TopicCapability.Reply).Allowed;
        var manage = AccessEvaluator.Evaluate(policy, TopicCapability.Read).Allowed
            && AccessEvaluator.Evaluate(policy, TopicCapability.ManageTopic).Allowed;
        var stewardship = TransportGrant(principal, ParticipationGrants.Manage) && manage;
        return ResourceResult<ExperienceTopicData>.Success(new ExperienceTopicData(stored.Title, ExperienceDigest.Preview(stored.Description, 480),
                posts, window.Position, window.Resolved is null ? null
                    : window.Resolved.ToDictionary(pair => pair.Key, pair => new ExperienceResolution(
                        pair.Value.Handle, pair.Value.DisplayName, pair.Value.Classification,
                        pair.Value.Avatar, pair.Value.ProfileUrl)), stored.IsLocked, stored.AllowPostEditing, window.OlderCursor, window.NewerCursor, window.ReadCursor,
                refs.ServerRef, refs.Tangent(tangentKey), refs.Topic(tangentKey, topicKey),
                canWrite, stewardship, TangentKind.TopicPresentation((await Community.Tangent.Get(tangentKey, ct))?.Kind ?? TangentKind.Discussions),
                window.OpeningPost is null ? null : Posts(tangentKey, topicKey,
                    window with { Messages = [window.OpeningPost] })[0]));
    }

    // ---------- Digest, updates and wait ----------

    public async Task<ResourceResult<ExperienceUpdatesData>> GetUpdates(ClaimsPrincipal principal, string? checkpoint, string? pageCursor,
        string? scopeRef, int limit, CancellationToken ct)
    {
        var participantId = ParticipationAccess.Require(principal, ParticipationGrants.Read);
        var credential = CredentialOf(principal);
        var (scopeTangent, scopeTopic) = await ScopeOf(participantId, scopeRef, ct);
        var decoded = digest.Decode(pageCursor ?? checkpoint, participantId, scopeTangent, scopeTopic);
        if ((pageCursor is not null || checkpoint is not null) && decoded is null)
            return ResourceResult<ExperienceUpdatesData>.Failure(ExperienceProblem.Of(ExperienceProblemCodes.CursorExpired, "This digest cursor expired or no longer matches. Start a new read without a cursor."));
        ExperienceAttention page;
        try { page = await digest.Page(participantId, credential, decoded, scopeTangent, scopeTopic, limit, ct); }
        catch (ArgumentException error)
        {
            return ResourceResult<ExperienceUpdatesData>.Failure(ExperienceProblem.Of(ExperienceProblemCodes.CursorExpired, error.Message));
        }
        var recovery = digest.Encode(new ExperienceDigest.DigestCursor(participantId, scopeTangent, scopeTopic, 0, clock.GetUtcNow().AddDays(7)));
        var continuationCursor = page.More
            ? digest.Encode(new ExperienceDigest.DigestCursor(participantId, scopeTangent, scopeTopic, (decoded?.Offset ?? 0) + page.Items.Count, clock.GetUtcNow().AddDays(7), Selection: page.Selection))
            : null;
        return ResourceResult<ExperienceUpdatesData>.Success(new ExperienceUpdatesData(scopeRef ?? refs.ServerRef, page, recovery, continuationCursor));
    }

    // ---------- Participation mutations ----------

    public async Task<ResourceResult<ExperiencePostData>> CreatePost(ClaimsPrincipal principal, string topicKey, string requestId,
        string text, string? replyTo, IReadOnlyList<Conversation.PostFacet>? facets, CancellationToken ct, string? format = null)
    {
        var participantId = ParticipationAccess.Require(principal, ParticipationGrants.Post);
        Topic? stored;
        using (EntityContext.NoCache())
            stored = await Topic.Get(topicKey, ct);
        if (stored is null)
            return ResourceResult<ExperiencePostData>.Failure(ExperienceProblem.Of(ExperienceProblemCodes.PermissionDenied, "That Topic is not visible to your account."));
        var tangentKey = stored.TangentKey;
        PostContent.CheckText(text);
        var policy = await conversation.ReadPolicy(participantId, topicKey, ct);
        if (!AccessEvaluator.Evaluate(policy, TopicCapability.Reply).Allowed) throw new UnauthorizedAccessException();
        string? replyParent = null;
        if (replyTo is { } reference)
        {
            var target = refs.ParsePost(reference)
                ?? throw new RequestArgumentException("replyTo", "Copy a Post reference returned by this server.");
            if (target.TangentKey != tangentKey || target.TopicKey != topicKey
                || reference != refs.Post(target.TangentKey, target.TopicKey, target.PostId))
                throw new RequestArgumentException("replyTo", "Reply to a Post in this Topic.");
            using var fresh = EntityContext.NoCache();
            var anchor = await Post.Get(target.PostId, ct);
            if (anchor is null || anchor.TopicKey != topicKey || anchor.Removed)
                throw new RequestArgumentException("replyTo", "Reply to a Post in this Topic.");
            replyParent = anchor.Id;
        }
        // The requestId is the post's operation id: CreatePost returns the post that id already
        // created (ADR 0007), so a retry heals a lost response instead of doubling the post.
        var operationId = requestId ?? "post-" + Guid.CreateVersion7().ToString("n");
        var post = await conversation.CreatePost(participantId, topicKey, new PostCreateRequest(operationId, text, replyParent, facets, format), ct);
        var postRef = refs.Post(tangentKey, topicKey, post.Id);
        var data = new ExperiencePostData(postRef, null,
            refs.Origin + "/tangents/" + tangentKey + "/posts/" + post.Id + "/");
        return ResourceResult<ExperiencePostData>.Success(data with { RequestId = requestId });
    }

    public async Task<ResourceResult<ExperienceReadPositionData>> ReadPosition(ClaimsPrincipal principal, string topicKey, string readCursor, CancellationToken ct)
    {
        var participantId = ParticipationAccess.Require(principal, ParticipationGrants.Read);
        Topic? stored;
        using (EntityContext.NoCache())
            stored = await Topic.Get(topicKey, ct);
        if (stored is null)
            return ResourceResult<ExperienceReadPositionData>.Failure(ExperienceProblem.Of(ExperienceProblemCodes.PermissionDenied, "That Topic is not visible to your account."));
        var tangentKey = stored.TangentKey;
        long sequence;
        try
        {
            sequence = await conversation.Acknowledge(participantId, topicKey, readCursor, ct);
        }
        catch (ArgumentException error)
        {
            var invalid = error.Message.Contains("resume", StringComparison.Ordinal) || error.Message.Contains("Acknowledge", StringComparison.Ordinal);
            return ResourceResult<ExperienceReadPositionData>.Failure(ExperienceProblem.Of(invalid ? ExperienceProblemCodes.InvalidArguments : ExperienceProblemCodes.CursorExpired,
                    invalid ? "Acknowledge a read cursor returned with a history page."
                        : "This read cursor expired or belongs to another participant. Open a fresh window.", "readCursor"));
        }
        string? throughRef = null;
        using (EntityContext.NoCache())
        {
            var through = (await Post.Query(post => post.TopicKey == topicKey && post.Sequence == sequence, One(), ct))
                .FirstOrDefault();
            if (through is not null) throughRef = refs.Post(tangentKey, topicKey, through.Id);
        }
        var data = new ExperienceReadPositionData(throughRef, refs.Topic(tangentKey, topicKey));
        return ResourceResult<ExperienceReadPositionData>.Success(data);
    }

    public async Task<ResourceResult<ExperienceMembershipData>> Join(ClaimsPrincipal principal, string tangentKey, CancellationToken ct)
    {
        var participantId = ParticipationAccess.Require(principal, ParticipationGrants.Read);
        JoinResult join;
        try
        {
            join = await participants.Join(participantId, tangentKey, ct);
        }
        catch (TangentRuleViolation denied) when (denied.Denial == TangentDenial.Forbidden)
        {
            return ResourceResult<ExperienceMembershipData>.Failure(ExperienceProblem.Of(ExperienceProblemCodes.NotAdmitted, denied.Message));
        }
        if (join.Outcome == JoinOutcome.PendingApproval)
        {
            return ResourceResult<ExperienceMembershipData>.Accepted(new ExperienceMembershipData("pending", "Your request is with the Tangent's administrators."));
        }
        var membership = join.Membership is { } result ? result.Role switch
        {
            TangentRole.Admin => "admin", TangentRole.Reader => "reader", _ => "member"
        } : "owner";
        return ResourceResult<ExperienceMembershipData>.Success(new ExperienceMembershipData(membership,
                join.Outcome == JoinOutcome.AlreadyMember ? "You already belong to this Tangent." : "Welcome to your Tangent."));
    }

    public async Task<ResourceResult<ExperienceMembershipData>> Leave(ClaimsPrincipal principal, string tangentKey, CancellationToken ct)
    {
        var participantId = ParticipationAccess.Require(principal, ParticipationGrants.Read);
        await participants.Leave(participantId, tangentKey, ct);
        return ResourceResult<ExperienceMembershipData>.Success(new ExperienceMembershipData("visitor", "You left this Tangent. Authorship is never erased."));
    }

    public async Task<ResourceResult<ExperienceWatchData>> SetWatch(ClaimsPrincipal principal, string scopeRef, string mode, CancellationToken ct)
    {
        var participantId = ParticipationAccess.Require(principal, ParticipationGrants.Read);
        var (tangentKey, topicKey) = ScopeDestination(scopeRef);
        if (await tangents.Describe(participantId, tangentKey, ct) is null) throw new UnauthorizedAccessException();
        var parsed = mode switch
        {
            "all" => (WatchMode?)WatchMode.All, "replies" => WatchMode.Replies, "none" => WatchMode.None, _ => null
        } ?? throw new RequestArgumentException("mode", "Choose all, replies, or none.");
        if (topicKey is { } watchedTopic) await participants.SetWatch(participantId, watchedTopic, parsed, ct);
        else await participants.SetTangentWatch(participantId, tangentKey, parsed, ct);
        return ResourceResult<ExperienceWatchData>.Success(new ExperienceWatchData(scopeRef, mode));
    }

    internal async Task<ExperienceIdentity> IdentityOf(string participantId, CancellationToken ct)
    {
        using var fresh = EntityContext.NoCache();
        // A principal's participant always exists; the hollow fallback only renders an id.
        var did = await hub.Directory.AtprotoDidOf(participantId, ct);
        var label = await hub.Directory.LabelOf(participantId, ct);
        return new ExperienceIdentity(participantId, did, DisplayName(label ?? participantId), label);
    }

    private static string DisplayName(string label)
    {
        var at = label.IndexOf('.');
        var acting = at > 0 ? label[..at] : label;
        return acting.Length <= 80 ? acting : label[..80];
    }

    internal static string RoleOf(TangentDescription tangent) => tangent.IsOwner ? "owner"
        : tangent.MembershipPending ? "pending"
        : tangent.MembershipRole switch
        {
            TangentRole.Member => "member", TangentRole.Admin => "admin", TangentRole.Reader => "reader", _ => "visitor"
        };

    private async Task<(string? Tangent, string? Topic)> ScopeOf(string participantId, string? scopeRef, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(scopeRef) || scopeRef == refs.ServerRef) return (null, null);
        if (refs.ParseTopic(scopeRef) is { } topic)
        {
            await conversation.ReadPolicy(participantId, topic.TopicKey, ct);
            if ((await topics.Describe(participantId, topic.TopicKey, ct))?.TangentKey != topic.TangentKey)
                throw new RequestArgumentException("scopeRef", "The Topic does not belong to that Tangent.");
            return (topic.TangentKey, topic.TopicKey);
        }
        if (refs.ParseTangent(scopeRef) is { } tangent)
        {
            if (!await tangents.CanAccess(participantId, tangent, ct)) throw new UnauthorizedAccessException();
            return (tangent, null);
        }
        throw new RequestArgumentException("scopeRef", "Copy a server, Topic, or Tangent reference returned by this server.");
    }

    private (string Tangent, string? Topic) ScopeDestination(string? reference)
    {
        if (refs.ParseTopic(reference) is { } topic) return (topic.TangentKey, topic.TopicKey);
        if (refs.ParseTangent(reference) is { } tangent) return (tangent, null);
        throw new RequestArgumentException("scopeRef", "Copy a Topic or Tangent reference returned by this server.");
    }

    private async Task<bool> RefReadable(string participantId, string? reference, CancellationToken ct)
    {
        if (reference is null) return true;
        try
        {
            (string Tangent, string Topic)? scope = refs.ParseTopic(reference);
            if (refs.ParsePost(reference) is { } post) scope = (post.TangentKey, post.TopicKey);
            if (scope is { } topic)
            {
                var description = await topics.Describe(participantId, topic.Topic, ct);
                return description?.TangentKey == topic.Tangent && description.CanRead;
            }
            if (refs.ParseCase(reference) is { } moderationCase)
            {
                var description = await topics.Describe(participantId, moderationCase.TopicKey, ct);
                return description?.TangentKey == moderationCase.TangentKey && description.CanRead;
            }
            if (refs.ParseTangent(reference) is { } tangent) return await tangents.CanAccess(participantId, tangent, ct);
            return false;
        }
        catch (Exception error) when (error is UnauthorizedAccessException or TopicRuleViolation or TangentRuleViolation)
        { return false; }
    }

    internal async Task<(IReadOnlyList<ExperienceTangentDto> Tangents, string? Continuation, bool Incomplete)> TangentPage(
        string participantId, int page, int offset, int limit, CancellationToken ct)
    {
        TangentsResponse directory;
        using (EntityContext.NoCache())
            directory = await tangents.List(participantId, page, ct);
        var visible = directory.Tangents.Skip(offset).ToList();
        var slice = visible.Take(limit).ToList();
        string? continuation = null;
        if (visible.Count > limit)
            continuation = refs.EncodeListCursor("experience:tangents", participantId, page, (offset + limit).ToString());
        else if (directory.NextPage is { } next)
            continuation = refs.EncodeListCursor("experience:tangents", participantId, next, "0");
        return (slice.Select(ToDto).ToList(), continuation, directory.DirectoryIncomplete);
    }

    private ExperienceTangentDto ToDto(TangentDescription tangent) => new(refs.Tangent(tangent.Key),
        ExperienceDigest.Preview(tangent.Name, 80), ExperienceDigest.Preview(tangent.Description, 240), RoleOf(tangent),
        tangent.Admission switch
        {
            TangentAdmission.Open => "open", _ => "approval"
        },
        tangent.MembershipRole != TangentRole.Removed && RoleOf(tangent) == "visitor",
        tangent.Topics.Any(topic => topic.CanRead), tangent.Topics.Any(topic => topic.CanWrite), tangent.Kind);

    internal ExperienceTopicDto TopicDto(string tangentKey, TopicDescription topic)
        => new(refs.Topic(tangentKey, topic.Key), ExperienceDigest.Preview(topic.Title, 80),
            ExperienceDigest.Preview(topic.Topic, 240), topic.CanRead, topic.CanWrite, topic.IsLocked, topic.AllowPostEditing,
            topic.Presentation);

    private List<ExperiencePostDto> Posts(string tangentKey, string topicKey, TopicWindow window)
    {
        var posts = new List<ExperiencePostDto>(window.Messages.Count);
        foreach (var post in window.Messages)
        {
            string? replyTo = null;
            if (post.Content.ReplyTo is { } parentId)
            {
                var projection = (from m in window.Messages where m.Id == parentId select m).FirstOrDefault();
                if (projection is not null) replyTo = refs.Post(tangentKey, topicKey, projection.Id);
            }
            posts.Add(new ExperiencePostDto(refs.Post(tangentKey, topicKey, post.Id), post.AuthorParticipantId,
                window.AuthorHandles.GetValueOrDefault(post.AuthorParticipantId, post.AuthorParticipantId),
                post.Removed ? "" : post.Content.Text, replyTo,
                refs.Origin + "/tangents/" + tangentKey + "/posts/" + post.Id + "/",
                Format(post.AcceptedAt), post.EditedAt is { } edited ? Format(edited) : null, post.Removed,
                post.Facets, new ExperiencePostSource("tangent",
                    refs.Origin + "/tangents/" + tangentKey + "/posts/" + post.Id + "/", post.Id),
                post.Content.Format, post.Removed ? null : PostMarkup.Html(post.Content)));
        }
        return posts;
    }

    internal string Timestamp() => clock.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

    internal string Format(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

    internal static string? CredentialOf(ClaimsPrincipal principal)
        => principal.FindFirst(ParticipationConstants.CredentialClaim)?.Value;

    internal static bool TransportGrant(ClaimsPrincipal principal, string grant)
        => !ParticipationAccess.UsesCredential(principal)
            || principal.HasClaim(ParticipationConstants.GrantClaim, grant);


    internal static QueryDefinition One()
    {
        var member = typeof(Post).GetProperty(nameof(Post.Id))!;
        return new QueryDefinition { Page = 1, PageSize = 1,
            Sort = [new SortSpec(new MemberPath(typeof(Post), [member], member.PropertyType, false, -1), false)] };
    }

    private ListCursor? DecodeDirectoryCursor(string? value, string scope, string participantId)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try { return refs.DecodeListCursor(value, scope, participantId); }
        catch (ArgumentException) { throw new RequestArgumentException("cursor", "This directory cursor is invalid. Open the directory again."); }
    }

    private static int InnerOffset(ListCursor? cursor)
        => cursor is not null && int.TryParse(cursor.Inner, out var parsed) && parsed > 0 ? parsed : 0;
}
