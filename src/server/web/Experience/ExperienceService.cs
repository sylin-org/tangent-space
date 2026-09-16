using System.Security.Claims;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sorting;
using Koan.Data.Core;
using TangentSpace.Activity;
using TangentSpace.Authorization;
using TangentSpace.Communities;
using TangentSpace.Conversation;
using TangentSpace.Application;
using TangentSpace.Moderation;
using TangentSpace.Participation;
using TangentSpace.Participants;
using TangentSpace.Rooms;
using TangentSpace.Site;

namespace TangentSpace.Experience;

/// <summary>The application experience assembler over the shared TangentServer hub. Every
/// operation invokes existing domain services and their policy boundaries; equivalent authorized
/// actors observe the same domain outcomes regardless of browser or connector transport. The
/// actor always derives from the verified credential: submitted identifiers are never authority.</summary>
public sealed partial class ExperienceService(
    TangentServer hub, OperationReceipts receipts, References refs, TimeProvider clock, ExperienceDigest digest,
    ModerationCaseService moderation)
{
    private TangentGovernance tangents => hub.Tangents;
    private RoomGovernance rooms => hub.Topics;
    private CompanionGovernance companions => hub.Participants;
    private ConversationService conversation => hub.Posts;
    private ActivityService activity => hub.Activity;
    private ServerGovernance server => hub.Space;

    public const int DefaultDigestLimit = 5;
    public const int MaximumDigestLimit = 25;
    public const int DirectoryLimit = 10;

    /// <summary>Receipt-registry scope for browser-session mutations, which carry no participant
    /// credential claim. Connector callers always use their real credential id.</summary>
    private const string WebSessionScope = "web-session";

    // ---------- Arrival and orientation ----------

    public async Task<ExperienceResponse> Arrive(ClaimsPrincipal principal, string? scopeRef, CancellationToken ct)
    {
        var participantId = ParticipationAccess.Require(principal, ParticipationGrants.Read);
        var credential = CredentialOf(principal);
        await activity.EnsureParticipantActive(participantId, credential, ct);
        var (scopeTangent, scopeRoom) = await ScopeOf(principal, participantId, credential, scopeRef, ct);
        var identity = await IdentityOf(participantId, ct);
        var space = await SiteLabel(ct);
        var page = await digest.Page(participantId, credential, null, scopeTangent, scopeRoom, 3, ct);
        var (directory, continuation, _) = await TangentPage(participantId, 1, 0, DirectoryLimit, ct);
        var orientation = new ExperienceOrientation(
            $"{space} is a shared conversation space for people and agents.",
            [],
            page.Attention.WaitingCount.Value is > 0
                ? $"{page.Attention.WaitingCount.Value} request(s) are waiting for you; {page.Attention.NewActivityCount.Value ?? 0} new posts in watched Topics."
                : directory.Count == 0 ? "No Tangents are visible to you yet."
                : $"{directory.Count} Tangent(s) are visible. Quiet reading is always fine.");
        var actions = new List<ExperienceAction>(page.FollowUps);
        if (actions.Count == 0 && directory.Count > 0)
            actions.Add(new(ExperienceActionNames.ListTopics, directory[0].TangentRef, null, $"Browse Topics in {directory[0].Name}"));
        return await Assemble("arrive", ExperienceStatus.Ok, identity,
            ServerPlace(principal, space),
            new ExperienceResult(new ExperienceArrivalData(directory, continuation), null, null),
            page.Attention, new ExperienceContinuation(null, null, null, null, null, null),
            actions, orientation,
            new ExperienceCapabilities(Attention: true, Coordination: false), participantId, credential, ct);
    }

    // ---------- Directories ----------

    public async Task<ExperienceResponse> ListTangents(ClaimsPrincipal principal, string? cursor, CancellationToken ct)
    {
        var participantId = ParticipationAccess.Require(principal, ParticipationGrants.Read);
        var credential = CredentialOf(principal);
        await activity.EnsureParticipantActive(participantId, credential, ct);
        var identity = await IdentityOf(participantId, ct);
        var selected = DecodeDirectoryCursor(cursor, "experience:tangents", participantId);
        var page = selected?.Page ?? 1;
        var offset = InnerOffset(selected);
        var (tangents, continuation, _) = await TangentPage(participantId, page, offset, DirectoryLimit, ct);
        return await Assemble("list_tangents", ExperienceStatus.Ok, identity,
            ServerPlace(principal, await SiteLabel(ct)),
            new ExperienceResult(new ExperienceTangentsData(tangents, continuation), null, null),
            (await digest.Page(participantId, credential, null, null, null, 3, ct)).Attention,
            new ExperienceContinuation(null, null, null, null, null, continuation), [], null, null, participantId, credential, ct);
    }

    public async Task<ExperienceResponse> ListTopics(ClaimsPrincipal principal, string tangentKey, string? cursor, CancellationToken ct)
    {
        var participantId = ParticipationAccess.Require(principal, ParticipationGrants.Read);
        var credential = CredentialOf(principal);
        await activity.EnsureParticipantActive(participantId, credential, ct);
        var identity = await IdentityOf(participantId, ct);
        TangentDescription? selected;
        using (EntityContext.NoCache())
            selected = await tangents.Describe(participantId, tangentKey, ct);
        if (selected is null)
            return Problem("list_topics", identity, ServerPlace(principal, await SiteLabel(ct)),
                ExperienceProblem.Of(ExperienceProblemCodes.PermissionDenied, "That Tangent is not visible to your account."),
                participantId: participantId, credential: credential, ct: ct);
        var decoded = DecodeDirectoryCursor(cursor, "experience:topics:" + tangentKey, participantId);
        var page = decoded?.Page ?? 1;
        var offset = InnerOffset(decoded);
        TangentChannelDirectory listing;
        using (EntityContext.NoCache())
            listing = await rooms.ListForTangent(participantId, tangentKey, page, ct);
        var visible = listing.Channels.Skip(offset).ToList();
        var slice = visible.Take(DirectoryLimit).ToList();
        string? continuation = null;
        if (visible.Count > DirectoryLimit)
            continuation = refs.EncodeListCursor("experience:topics:" + tangentKey, participantId, page, (offset + DirectoryLimit).ToString());
        else if (listing.NextPage is { } next)
            continuation = refs.EncodeListCursor("experience:topics:" + tangentKey, participantId, next, "0");
        var topics = slice.Select(room => TopicDto(tangentKey, room)).ToList();
        var actions = topics.Count > 0
            ? new List<ExperienceAction> { new(ExperienceActionNames.ReadTopic, topics[0].TopicRef, null, $"Read {topics[0].Title}") }
            : new List<ExperienceAction>();
        return await Assemble("list_topics", ExperienceStatus.Ok, identity,
            TangentPlace(principal, selected),
            new ExperienceResult(new ExperienceTopicsData(topics, continuation), null, null),
            (await digest.Page(participantId, credential, null, tangentKey, null, 3, ct)).Attention,
            new ExperienceContinuation(null, null, null, null, null, continuation), actions, null, null, participantId, credential, ct);
    }

    // ---------- Topic reading ----------

    public async Task<ExperienceResponse> ReadTopic(ClaimsPrincipal principal, string topicKey, string? cursor,
        string? aroundPostRef, int limit, CancellationToken ct)
    {
        var participantId = ParticipationAccess.Require(principal, ParticipationGrants.Read);
        var credential = CredentialOf(principal);
        await activity.EnsureParticipantActive(participantId, credential, ct);
        var identity = await IdentityOf(participantId, ct);
        Rooms.Room? stored;
        using (EntityContext.NoCache())
            stored = await Room.Get(topicKey, ct);
        if (stored is null)
            return Problem("read_topic", identity, ServerPlace(principal, await SiteLabel(ct)),
                ExperienceProblem.Of(ExperienceProblemCodes.PermissionDenied, "That Topic is not visible to your account."),
                participantId: participantId, credential: credential, ct: ct);
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
            return Problem("read_topic", identity, ServerPlace(principal, await SiteLabel(ct)),
                ExperienceProblem.Of(ExperienceProblemCodes.CursorExpired,
                    error.Message.Contains("cursor", StringComparison.OrdinalIgnoreCase)
                        ? "This history cursor expired or no longer matches. Open a fresh window explicitly."
                        : error.Message),
                participantId: participantId, credential: credential, ct: ct);
        }
        var posts = Posts(tangentKey, topicKey, window);
        var policy = await conversation.ReadPolicy(participantId, topicKey, ct);
        var allowed = new List<string> { ExperienceActionNames.ReadTopic, ExperienceActionNames.MarkRead,
            ExperienceActionNames.SetWatch, ExperienceActionNames.ReportPost };
        if (TopicPermissionEvaluator.Evaluate(policy, TopicCapability.Reply).Allowed) allowed.Add(ExperienceActionNames.CreatePost);
        var stewardship = TopicPermissionEvaluator.Evaluate(policy, TopicCapability.Read).Allowed
            && TopicPermissionEvaluator.Evaluate(policy, TopicCapability.ManageTopic).Allowed;
        if (stewardship) allowed.Add(ExperienceActionNames.ListModerationCases);
        var place = new ExperiencePlace(refs.ServerRef, refs.Tangent(tangentKey), refs.Topic(tangentKey, topicKey),
            $"{await TangentName(tangentKey, ct)} / {stored.Title}", await RoleOf(participantId, tangentKey, ct), allowed);
        var actions = new List<ExperienceAction>();
        if (posts.Count > 0)
            actions.Add(new(ExperienceActionNames.MarkRead, refs.Topic(tangentKey, topicKey), null, "Acknowledge reading through the newest Post"));
        if (stewardship)
            actions.Add(new(ExperienceActionNames.ListModerationCases, refs.Topic(tangentKey, topicKey), null, "Review this Topic's moderation cases"));
        return await Assemble("read_topic", ExperienceStatus.Ok, identity, place,
            new ExperienceResult(new ExperienceTopicData(stored.Title, ExperienceDigest.Preview(stored.Topic, 480),
                posts, window.Position, window.Resolved is null ? null
                    : window.Resolved.ToDictionary(pair => pair.Key, pair => new ExperienceResolution(
                        pair.Value.Handle, pair.Value.DisplayName, pair.Value.Classification,
                        pair.Value.Avatar, pair.Value.ProfileUrl))), null, null),
            (await digest.Page(participantId, credential, null, tangentKey, topicKey, 3, ct)).Attention,
            new ExperienceContinuation(null, null, window.OlderCursor, window.NewerCursor, window.ReadCursor, null),
            actions,
            new ExperienceOrientation(null, [],
                $"{stored.Title}: {ExperienceDigest.Preview(stored.Topic, 240)}"),
            new ExperienceCapabilities(Attention: true, Coordination: false, Stewardship: stewardship),
            participantId, credential, ct);
    }

    // ---------- Digest, updates and wait ----------

    public async Task<ExperienceResponse> GetUpdates(ClaimsPrincipal principal, string? checkpoint, string? pageCursor,
        string? scopeRef, int limit, CancellationToken ct)
    {
        var participantId = ParticipationAccess.Require(principal, ParticipationGrants.Read);
        var credential = CredentialOf(principal);
        var (scopeTangent, scopeRoom) = await ScopeOf(principal, participantId, credential, scopeRef, ct);
        var identity = await IdentityOf(participantId, ct);
        var decoded = digest.Decode(pageCursor ?? checkpoint, participantId, scopeTangent, scopeRoom);
        if (pageCursor is not null && decoded is null && checkpoint is null)
            return Problem("get_updates", identity, ServerPlace(principal, await SiteLabel(ct)),
                ExperienceProblem.Of(ExperienceProblemCodes.CursorExpired, "This digest cursor expired or no longer matches. Restart from the checkpoint."),
                participantId: participantId, credential: credential, ct: ct);
        var page = await digest.Page(participantId, credential, decoded, scopeTangent, scopeRoom, limit, ct);
        var recovery = digest.Encode(new ExperienceDigest.DigestCursor(participantId, scopeTangent, scopeRoom, 0, clock.GetUtcNow().AddDays(7)));
        var continuationCursor = page.Attention.More
            ? digest.Encode(new ExperienceDigest.DigestCursor(participantId, scopeTangent, scopeRoom, (decoded?.Offset ?? 0) + limit, clock.GetUtcNow().AddDays(7)))
            : null;
        var actions = page.FollowUps.Take(3).ToList();
        return await Assemble("get_updates", ExperienceStatus.Ok, identity,
            ServerPlace(principal, await SiteLabel(ct)),
            new ExperienceResult(new ExperienceUpdatesData(scopeRef ?? refs.ServerRef), null, null),
            page.Attention, new ExperienceContinuation(recovery, continuationCursor, null, null, null, null),
            actions, null, null, participantId, credential, ct);
    }

    public async Task<ExperienceResponse> Wait(ClaimsPrincipal principal, string? checkpoint, string? sinceRevision,
        string? scopeRef, CancellationToken ct)
    {
        var participantId = ParticipationAccess.Require(principal, ParticipationGrants.Read);
        var credential = CredentialOf(principal);
        var (scopeTangent, scopeRoom) = await ScopeOf(principal, participantId, credential, scopeRef, ct);
        var identity = await IdentityOf(participantId, ct);
        // Capture before inspecting state so a commit between snapshot and wait is replayed.
        using var notification = ActivityJournal.Capture();
        var current = await digest.Page(participantId, credential, digest.Decode(checkpoint, participantId, scopeTangent, scopeRoom), scopeTangent, scopeRoom, DefaultDigestLimit, ct);
        if (sinceRevision is { Length: > 0 } && string.Equals(current.Attention.Revision, sinceRevision, StringComparison.Ordinal))
        {
            await notification.WaitAsync(TimeSpan.FromSeconds(15), ct);
            current = await digest.Page(participantId, credential, null, scopeTangent, scopeRoom, DefaultDigestLimit, ct);
        }
        var recovery = digest.Encode(new ExperienceDigest.DigestCursor(participantId, scopeTangent, scopeRoom, 0, clock.GetUtcNow().AddDays(7)));
        return await Assemble("wait", ExperienceStatus.Ok, identity,
            ServerPlace(principal, await SiteLabel(ct)),
            new ExperienceResult(new ExperienceUpdatesData(scopeRef ?? refs.ServerRef), null, null),
            current.Attention, new ExperienceContinuation(recovery, null, null, null, null, null),
            current.FollowUps.Take(3).ToList(), null, null, participantId, credential, ct);
    }

    // ---------- Participation mutations ----------

    public async Task<ExperienceResponse> CreatePost(ClaimsPrincipal principal, string topicKey, string requestId,
        string text, string? replyTo, IReadOnlyList<Conversation.PostFacet>? facets, CancellationToken ct)
    {
        var participantId = ParticipationAccess.Require(principal, ParticipationGrants.Post);
        var credential = CredentialOf(principal);
        await activity.EnsureParticipantActive(participantId, credential, ct);
        var identity = await IdentityOf(participantId, ct);
        Rooms.Room? stored;
        using (EntityContext.NoCache())
            stored = await Room.Get(topicKey, ct);
        if (stored is null)
            return Problem("create_post", identity, ServerPlace(principal, await SiteLabel(ct)),
                ExperienceProblem.Of(ExperienceProblemCodes.PermissionDenied, "That Topic is not visible to your account."),
                participantId: participantId, credential: credential, ct: ct);
        var tangentKey = stored.TangentKey;
        PostContent.CheckText(text);
        var policy = await conversation.ReadPolicy(participantId, topicKey, ct);
        if (!TopicPermissionEvaluator.Evaluate(policy, TopicCapability.Reply).Allowed) throw new UnauthorizedAccessException();
        SourceReference? replySource = null;
        if (replyTo is { } reference)
        {
            var target = refs.ParsePost(reference)
                ?? throw new RequestArgumentException("replyTo", "Copy a Post reference returned by this server.");
            if (target.TangentKey != tangentKey || target.TopicKey != topicKey
                || reference != refs.Post(target.TangentKey, target.TopicKey, target.PostId))
                throw new RequestArgumentException("replyTo", "Reply to a Post in this Topic.");
            using var fresh = EntityContext.NoCache();
            var anchor = await Post.Get(target.PostId, ct);
            if (anchor is null || anchor.RoomKey != topicKey)
                throw new RequestArgumentException("replyTo", "Reply to a Post in this Topic.");
            replySource = new SourceReference(anchor.SourceUri, anchor.SourceCid);
        }
        var payload = new Dictionary<string, string?> { ["text"] = text, ["replyTo"] = replyTo,
            ["facets"] = facets is null ? "<auto>"
                : string.Join("|", facets.OrderBy(facet => facet.Start).Select(facet => facet.Canonical())) };
        return await receipts.Run(RegistryCredential(principal), participantId, requestId, async () =>
        {
            var registration = await receipts.Register(RegistryCredential(principal), participantId, requestId, "CreatePost", topicKey, payload, ct);
            if (registration.Reused && registration.Record.State == "completed" && registration.Record.ResultData is not null)
                return await Replay("create_post", principal, identity, registration.Record, participantId, credential, ct);
            var operationId = registration.Record.NamespacedOperationId
                ?? OperationReceipt.BuildOperationId(RegistryCredential(principal), participantId, requestId);
            var post = await conversation.CreatePost(participantId, topicKey, new PostCreateRequest(operationId, text, replySource, facets), ct);
            var place = await TopicPlaceOf(principal, participantId, tangentKey, topicKey, ct);
            var postRef = refs.Post(tangentKey, topicKey, post.Id);
            var data = new ExperiencePostData(postRef, new ExperienceSource(post.SourceUri, post.SourceCid),
                refs.Origin + "/tangents/" + tangentKey + "/posts/" + post.Id + "/");
            await receipts.Complete(registration.Record, "completed", postRef, Serialize(data), ct);
            return await Assemble("create_post", ExperienceStatus.Ok, identity, place,
                new ExperienceResult(data, new ExperienceReceipt(requestId, "completed", postRef, null), null),
                (await digest.Page(participantId, credential, null, tangentKey, topicKey, 3, ct)).Attention,
                Empty(), [new(ExperienceActionNames.ReadTopic, refs.Topic(tangentKey, topicKey), null, "Open the Topic")], null, null, participantId, credential, ct);
        }, ct);
    }

    public async Task<ExperienceResponse> ReadPosition(ClaimsPrincipal principal, string topicKey, string readCursor,
        string requestId, CancellationToken ct)
    {
        var participantId = ParticipationAccess.Require(principal, ParticipationGrants.Read);
        var credential = CredentialOf(principal);
        await activity.EnsureParticipantActive(participantId, credential, ct);
        var identity = await IdentityOf(participantId, ct);
        Rooms.Room? stored;
        using (EntityContext.NoCache())
            stored = await Room.Get(topicKey, ct);
        if (stored is null)
            return Problem("read_position", identity, ServerPlace(principal, await SiteLabel(ct)),
                ExperienceProblem.Of(ExperienceProblemCodes.PermissionDenied, "That Topic is not visible to your account."),
                participantId: participantId, credential: credential, ct: ct);
        var tangentKey = stored.TangentKey;
        Func<Task<ExperienceResponse>> acknowledge = async () =>
        {
            long sequence;
            try
            {
                sequence = await conversation.Acknowledge(participantId, topicKey, readCursor, ct);
            }
            catch (ArgumentException error)
            {
                var invalid = error.Message.Contains("resume", StringComparison.Ordinal) || error.Message.Contains("Acknowledge", StringComparison.Ordinal);
                return Problem("read_position", identity, ServerPlace(principal, await SiteLabel(ct)),
                    ExperienceProblem.Of(invalid ? ExperienceProblemCodes.InvalidArguments : ExperienceProblemCodes.CursorExpired,
                        invalid ? "Acknowledge a read cursor returned with a history page."
                            : "This read cursor expired or belongs to another participant. Open a fresh window.", "readCursor"),
                    participantId: participantId, credential: credential, ct: ct);
            }
            string? throughRef = null;
            using (EntityContext.NoCache())
            {
                var through = (await Post.Query(post => post.RoomKey == topicKey && post.Sequence == sequence, One(), ct))
                    .FirstOrDefault();
                if (through is not null) throughRef = refs.Post(tangentKey, topicKey, through.Id);
            }
            var data = new ExperienceReadPositionData(throughRef, refs.Topic(tangentKey, topicKey));
            var receipt = requestId is null ? null : new ExperienceReceipt(requestId, "completed", null, null);
            return await Assemble("read_position", ExperienceStatus.Ok, identity,
                await TopicPlaceOf(principal, participantId, tangentKey, topicKey, ct),
                new ExperienceResult(data, receipt, null),
                (await digest.Page(participantId, credential, null, tangentKey, topicKey, 3, ct)).Attention,
                Empty(), [new(ExperienceActionNames.ReadTopic, refs.Topic(tangentKey, topicKey), null, "Open the Topic")], null, null, participantId, credential, ct);
        };
        if (requestId is null) return await acknowledge();
        return await receipts.Run(RegistryCredential(principal), participantId, requestId, async () =>
        {
            var registration = await receipts.Register(RegistryCredential(principal), participantId, requestId, "ReadPosition", topicKey,
                new Dictionary<string, string?> { ["readCursor"] = readCursor }, ct);
            if (registration.Reused && registration.Record.State == "completed" && registration.Record.ResultData is not null)
                return await Replay("read_position", principal, identity, registration.Record, participantId, credential, ct);
            var outcome = await acknowledge();
            if (outcome.Status == ExperienceStatus.Ok)
                await receipts.Complete(registration.Record, "completed", null,
                    Serialize(outcome.Result.Data), ct);
            return outcome;
        }, ct);
    }

    public async Task<ExperienceResponse> Join(ClaimsPrincipal principal, string tangentKey, string? inviteRef,
        string requestId, CancellationToken ct)
    {
        var participantId = ParticipationAccess.Require(principal, ParticipationGrants.Read);
        var credential = CredentialOf(principal);
        await activity.EnsureParticipantActive(participantId, credential, ct);
        var identity = await IdentityOf(participantId, ct);
        string? invitationId = null;
        if (inviteRef is { } invite)
        {
            var parsed = refs.ParseInvite(invite)
                ?? throw new RequestArgumentException("inviteRef", "Copy an invitation reference returned by this server.");
            if (parsed.TangentKey != tangentKey)
                throw new RequestArgumentException("inviteRef", "That invitation belongs to another Tangent.");
            invitationId = parsed.InvitationId;
        }
        return await receipts.Run(RegistryCredential(principal), participantId, requestId, async () =>
        {
            var registration = await receipts.Register(RegistryCredential(principal), participantId, requestId, "JoinTangent",
                tangentKey, new Dictionary<string, string?> { ["inviteRef"] = inviteRef }, ct);
            if (registration.Reused && registration.Record.State == "completed" && registration.Record.ResultData is not null)
                return await Replay("join", principal, identity, registration.Record, participantId, credential, ct);
            receipts.CompleteWithDomain(registration.Record, raw =>
            {
                var joined = (CompanionJoinResult)raw!;
                var pending = joined.Outcome == CompanionJoinOutcome.PendingApproval;
                return (pending ? "pending" : "completed", pending ? null : refs.Tangent(tangentKey), "{}");
            });
            CompanionJoinResult join;
            try
            {
                join = await companions.Join(participantId, tangentKey, invitationId, ct);
            }
            catch (TangentRuleViolation denied) when (denied.Denial == TangentDenial.Forbidden)
            {
                return Problem("join", identity, ServerPlace(principal, await SiteLabel(ct)),
                    ExperienceProblem.Of(ExperienceProblemCodes.NotAdmitted, denied.Message),
                    participantId: participantId, credential: credential, ct: ct);
            }
            var key = requestId;
            var place = await TangentPlaceOf(principal, participantId, tangentKey, ct);
            if (join.Outcome == CompanionJoinOutcome.PendingApproval)
            {
                await receipts.Complete(registration.Record, "pending", null, null, ct);
                return await Assemble("join", ExperienceStatus.Pending, identity, place,
                    new ExperienceResult(new ExperienceMembershipData("pending", "Your request is with the Tangent's administrators."),
                        new ExperienceReceipt(key, "pending", null, 15), null),
                    (await digest.Page(participantId, credential, null, null, null, 3, ct)).Attention, Empty(),
                    [new("get_operation", refs.ServerRef, null, "Check the saved action")], null, null, participantId, credential, ct);
            }
            var membership = join.Membership is { } result ? result.Role switch
            {
                TangentRole.Admin => "admin", TangentRole.Reader => "reader", _ => "member"
            } : "owner";
            await receipts.Complete(registration.Record, "completed", refs.Tangent(tangentKey), "{}", ct);
            return await Assemble("join", ExperienceStatus.Ok, identity, place,
                new ExperienceResult(new ExperienceMembershipData(membership,
                    join.Outcome == CompanionJoinOutcome.AlreadyMember ? "You already belong to this Tangent." : "Welcome to your Tangent."),
                    new ExperienceReceipt(key, "completed", refs.Tangent(tangentKey), null), null),
                (await digest.Page(participantId, credential, null, tangentKey, null, 3, ct)).Attention, Empty(),
                [new(ExperienceActionNames.ListTopics, refs.Tangent(tangentKey), null, "Browse Topics")], null, null, participantId, credential, ct);
        }, ct);
    }

    public async Task<ExperienceResponse> Leave(ClaimsPrincipal principal, string tangentKey, string requestId, CancellationToken ct)
    {
        var participantId = ParticipationAccess.Require(principal, ParticipationGrants.Read);
        var credential = CredentialOf(principal);
        await activity.EnsureParticipantActive(participantId, credential, ct);
        var identity = await IdentityOf(participantId, ct);
        return await receipts.Run(RegistryCredential(principal), participantId, requestId, async () =>
        {
            var registration = await receipts.Register(RegistryCredential(principal), participantId, requestId, "LeaveTangent",
                tangentKey, new Dictionary<string, string?>(), ct);
            if (registration.Reused && registration.Record.State == "completed" && registration.Record.ResultData is not null)
                return await Replay("leave", principal, identity, registration.Record, participantId, credential, ct);
            receipts.CompleteWithDomain(registration.Record, _ => ("completed", null, "{}"));
            await companions.Leave(participantId, tangentKey, ct);
            await receipts.Complete(registration.Record, "completed", null, "{}", ct);
            return await Assemble("leave", ExperienceStatus.Ok, identity,
                new ExperiencePlace(refs.ServerRef, refs.Tangent(tangentKey), null, await TangentName(tangentKey, ct), "visitor",
                    [ExperienceActionNames.ListTangents]),
                new ExperienceResult(new ExperienceMembershipData("visitor", "You left this Tangent. Authorship is never erased."),
                    new ExperienceReceipt(requestId, "completed", null, null), null),
                (await digest.Page(participantId, credential, null, null, null, 3, ct)).Attention, Empty(),
                [new(ExperienceActionNames.ListTangents, refs.ServerRef, null, "Browse Tangents")], null, null, participantId, credential, ct);
        }, ct);
    }

    public async Task<ExperienceResponse> SetWatch(ClaimsPrincipal principal, string scopeRef, string mode,
        string requestId, CancellationToken ct)
    {
        var participantId = ParticipationAccess.Require(principal, ParticipationGrants.Read);
        var credential = CredentialOf(principal);
        await activity.EnsureParticipantActive(participantId, credential, ct);
        var identity = await IdentityOf(participantId, ct);
        var (tangentKey, roomKey) = ScopeDestination(scopeRef);
        if (!await tangents.CanAccess(participantId, tangentKey, ct)) throw new UnauthorizedAccessException();
        var parsed = mode switch
        {
            "all" => (WatchMode?)WatchMode.All, "replies" => WatchMode.Replies, "none" => WatchMode.None, _ => null
        } ?? throw new RequestArgumentException("mode", "Choose all, replies, or none.");
        return await receipts.Run(RegistryCredential(principal), participantId, requestId, async () =>
        {
            var registration = await receipts.Register(RegistryCredential(principal), participantId, requestId, "SetWatch",
                roomKey ?? tangentKey, new Dictionary<string, string?> { ["scopeRef"] = scopeRef, ["mode"] = mode }, ct);
            if (registration.Reused && registration.Record.State == "completed" && registration.Record.ResultData is not null)
                return await Replay("set_watch", principal, identity, registration.Record, participantId, credential, ct);
            receipts.CompleteWithDomain(registration.Record, _ => ("completed", null, "{}"));
            if (roomKey is { } watchedRoom) await companions.SetWatch(participantId, watchedRoom, parsed, ct);
            else await companions.SetTangentWatch(participantId, tangentKey, parsed, ct);
            await receipts.Complete(registration.Record, "completed", null, "{}", ct);
            return await Assemble("set_watch", ExperienceStatus.Ok, identity,
                roomKey is null ? await TangentPlaceOf(principal, participantId, tangentKey, ct)
                    : await TopicPlaceOf(principal, participantId, tangentKey, roomKey, ct),
                new ExperienceResult(new ExperienceWatchData(scopeRef, mode),
                    new ExperienceReceipt(requestId, "completed", null, null), null),
                (await digest.Page(participantId, credential, null, tangentKey, roomKey, 3, ct)).Attention, Empty(), [], null, null, participantId, credential, ct);
        }, ct);
    }

    // ---------- Receipt recovery ----------

    public async Task<ExperienceResponse> GetOperation(ClaimsPrincipal principal, string requestId, CancellationToken ct)
    {
        var participantId = ParticipationAccess.Require(principal, ParticipationGrants.Read);
        var credential = CredentialOf(principal);
        var identity = await IdentityOf(participantId, ct);
        // Lookup never re-executes; the durable registry and the Post rows are the only truth.
        var record = await receipts.Find(RegistryCredential(principal), participantId, requestId, ct);
        if (record is null)
            return Problem("get_operation", identity, ServerPlace(principal, await SiteLabel(ct)),
                ExperienceProblem.Of(ExperienceProblemCodes.ReceiptExpired, "No receipt is available for that request ID.", "requestId"),
                participantId: participantId, credential: credential, ct: ct);
        // A receipt is not an authority token. Stewardship operations remain private to a
        // current Topic manager even after their domain mutation has committed.
        if (record.Operation == "ApplyModerationAction")
            await moderation.Read(participantId, record.TargetKey, 0, 1, 1, ct);
        var state = record.State;
        string? resultRef = record.ResultRef;
        if (record.Operation is "CreatePost" or "PostMessage")
        {
            using var fresh = EntityContext.NoCache();
            var operationId = record.NamespacedOperationId
                ?? OperationReceipt.BuildOperationId(RegistryCredential(principal), participantId, requestId);
            // ADR 0007: the projection row is the receipt.
            var projected = (await Post.Query(m => m.AuthorParticipantId == participantId && m.OperationId == operationId, One(), ct)).FirstOrDefault();
            if (projected is not null && await Room.Get(projected.RoomKey, ct) is { } rowRoom)
            {
                state = projected.Removed ? "rejected" : "completed";
                resultRef = refs.Post(rowRoom.TangentKey, projected.RoomKey, projected.Id);
            }
        }
        if (record.Operation == "JoinTangent" && state == "pending")
        {
            using var fresh = EntityContext.NoCache();
            var admission = await TangentJoinRequest.Get(TangentJoinRequest.Key(record.TargetKey, participantId), ct);
            if (admission is { Pending: false })
            {
                state = admission.Accepted == true ? "completed" : "rejected";
                resultRef = admission.Accepted == true ? refs.Tangent(record.TargetKey) : null;
                await receipts.Complete(record, state, resultRef, admission.Accepted == true ? "{}" : null, ct);
            }
        }
        // Current access governs cached receipts: a revoked target's references are not re-disclosed.
        resultRef = await RefReadable(participantId, resultRef, ct) ? resultRef : null;
        var receipt = new ExperienceReceipt(record.RequestId, state is "completed" ? "completed" : state is "rejected" ? "rejected" : "pending",
            resultRef, state == "pending" ? 15 : null);
        var actions = new List<ExperienceAction>();
        if (resultRef is not null && refs.ParsePost(resultRef) is { } post)
            actions.Add(new(ExperienceActionNames.ReadTopic, refs.Topic(post.TangentKey, post.TopicKey), resultRef, "Open the Topic"));
        return await Assemble("get_operation", ExperienceStatus.Ok, identity,
            ServerPlace(principal, await SiteLabel(ct)),
            new ExperienceResult(new ExperienceOperationData(record.Operation, receipt), null, null),
            (await digest.Page(participantId, credential, null, null, null, 3, ct)).Attention, Empty(), actions, null, null, participantId, credential, ct);
    }

    // ---------- Shared assembly ----------

    private async Task<ExperienceResponse> Assemble(string operation, string status, ExperienceIdentity identity,
        ExperiencePlace place, ExperienceResult result, ExperienceAttention attention,
        ExperienceContinuation continuation, IReadOnlyList<ExperienceAction> actions,
        ExperienceOrientation? orientation, ExperienceCapabilities? capabilities,
        string participantId, string credential, CancellationToken ct)
    {
        await activity.EnsureParticipantActive(participantId, credential, ct);
        return new ExperienceResponse(ExperienceResponse.Version, operation, status,
            new ExperienceSnapshot(await digest.HeadRevision(ct), Timestamp(), "current"),
            identity, place, result, attention, continuation, actions, orientation, capabilities);
    }

    internal ExperienceResponse Problem(string operation, ExperienceIdentity? identity, ExperiencePlace place,
        ExperienceProblem problem, IReadOnlyList<ExperienceAction>? actions = null,
        string? participantId = null, string? credential = null, CancellationToken ct = default)
        => new(ExperienceResponse.Version, operation, ExperienceStatus.Blocked,
            new ExperienceSnapshot("unknown", Timestamp(), "unavailable"), identity, place,
            new ExperienceResult(null, null, problem),
            new ExperienceAttention("unknown", Timestamp(), new ExperienceCount(null, false), new ExperienceCount(null, false), [], false, false),
            Empty(), actions ?? [], null, null);

    private async Task<ExperienceResponse> Replay(string operation, ClaimsPrincipal principal, ExperienceIdentity identity,
        OperationReceipt record, string participantId, string credential, CancellationToken ct)
    {
        // Retries of a committed action return the recorded result; no side effect repeats.
        var receipt = new ExperienceReceipt(record.RequestId, record.State == "completed" ? "completed" : record.State,
            await RefReadable(participantId, record.ResultRef, ct) ? record.ResultRef : null, null);
        return await Assemble(operation, ExperienceStatus.Ok, identity,
            ServerPlace(principal, await SiteLabel(ct)),
            new ExperienceResult(record.ResultData is null ? null : System.Text.Json.JsonSerializer.Deserialize<object>(record.ResultData, ExperienceJson.Options),
                receipt, null),
            (await digest.Page(participantId, credential, null, null, null, 3, ct)).Attention, Empty(), [], null, null, participantId, credential, ct);
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

    internal ExperiencePlace ServerPlace(ClaimsPrincipal principal, string label)
        => new(refs.ServerRef, null, null, label, "participant",
            principal.HasClaim(ParticipationConstants.GrantClaim, ParticipationGrants.Read) || !ParticipationAccess.UsesCredential(principal)
                ? [ExperienceActionNames.ListTangents, "get_updates"] : []);

    private async Task<ExperiencePlace> TangentPlaceOf(ClaimsPrincipal principal, string participantId, string tangentKey, CancellationToken ct)
    {
        using var fresh = EntityContext.NoCache();
        var tangent = await Tangent.Get(tangentKey, ct);
        var allowed = new List<string> { ExperienceActionNames.ListTopics, ExperienceActionNames.SetWatch };
        if (await tangents.CanAccess(participantId, tangentKey, ct)) allowed.Add(ExperienceActionNames.LeaveTangent);
        else allowed.Add(ExperienceActionNames.JoinTangent);
        return new ExperiencePlace(refs.ServerRef, refs.Tangent(tangentKey), null,
            tangent?.Name is { Length: > 0 } name ? name : tangentKey, await RoleOf(participantId, tangentKey, ct), allowed);
    }

    private ExperiencePlace TangentPlace(ClaimsPrincipal principal, TangentDescription tangent)
        => new(refs.ServerRef, refs.Tangent(tangent.Key), null, tangent.Name, RoleOf(tangent),
            tangent.MembershipRole != TangentRole.Removed && !tangent.IsMember && !tangent.MembershipPending
                ? [ExperienceActionNames.ListTopics, ExperienceActionNames.JoinTangent, ExperienceActionNames.SetWatch]
                : [ExperienceActionNames.ListTopics, ExperienceActionNames.LeaveTangent, ExperienceActionNames.SetWatch]);

    private async Task<ExperiencePlace> TopicPlaceOf(ClaimsPrincipal principal, string participantId, string tangentKey, string topicKey, CancellationToken ct)
    {
        var policy = await conversation.ReadPolicy(participantId, topicKey, ct);
        using var fresh = EntityContext.NoCache();
        var room = await Room.Get(topicKey, ct);
        var allowed = new List<string> { ExperienceActionNames.ReadTopic, ExperienceActionNames.MarkRead,
            ExperienceActionNames.SetWatch, ExperienceActionNames.ReportPost };
        if (TopicPermissionEvaluator.Evaluate(policy, TopicCapability.Reply).Allowed) allowed.Add(ExperienceActionNames.CreatePost);
        if (TopicPermissionEvaluator.Evaluate(policy, TopicCapability.Read).Allowed
            && TopicPermissionEvaluator.Evaluate(policy, TopicCapability.ManageTopic).Allowed)
            allowed.Add(ExperienceActionNames.ListModerationCases);
        return new ExperiencePlace(refs.ServerRef, refs.Tangent(tangentKey), refs.Topic(tangentKey, topicKey),
            $"{await TangentName(tangentKey, ct)} / {room?.Title ?? topicKey}", await RoleOf(participantId, tangentKey, ct), allowed);
    }

    internal static string RoleOf(TangentDescription tangent) => tangent.IsOwner ? "owner"
        : tangent.MembershipPending ? "pending"
        : tangent.MembershipRole switch
        {
            TangentRole.Member => "member", TangentRole.Admin => "admin", TangentRole.Reader => "reader", _ => "visitor"
        };

    private async Task<string> RoleOf(string participantId, string tangentKey, CancellationToken ct)
    {
        using var fresh = EntityContext.NoCache();
        return RoleOf(await tangents.Describe(participantId, tangentKey, ct) ?? new TangentDescription(tangentKey, tangentKey, "", "", "", "",
            "", false, false, [], IsMember: false));
    }

    private async Task<string> TangentName(string tangentKey, CancellationToken ct)
    {
        using var fresh = EntityContext.NoCache();
        return (await Tangent.Get(tangentKey, ct))?.Name is { Length: > 0 } name ? name : tangentKey;
    }

    internal async Task<string> SiteLabel(CancellationToken ct)
    {
        using var fresh = EntityContext.NoCache();
        var space = await Site.Space.Get(TangentSpace.Infrastructure.TangentConstants.SpaceId, ct);
        return space?.Name.Length > 0 ? space.Name : "This Tangent server";
    }

    private async Task<(string? Tangent, string? Topic)> ScopeOf(ClaimsPrincipal principal, string participantId, string credential,
        string? scopeRef, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(scopeRef) || scopeRef == refs.ServerRef) return (null, null);
        if (refs.ParseTopic(scopeRef) is { } topic)
        {
            await conversation.ReadPolicy(participantId, topic.TopicKey, ct);
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
                var description = await rooms.Describe(participantId, topic.Topic, ct);
                return description?.TangentKey == topic.Tangent && description.CanRead;
            }
            if (refs.ParseCase(reference) is { } moderationCase)
            {
                var description = await rooms.Describe(participantId, moderationCase.TopicKey, ct);
                return description?.TangentKey == moderationCase.TangentKey && description.CanRead;
            }
            if (refs.ParseTangent(reference) is { } tangent) return await tangents.CanAccess(participantId, tangent, ct);
            return false;
        }
        catch (Exception error) when (error is UnauthorizedAccessException or RoomRuleViolation or TangentRuleViolation)
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
            TangentAdmission.Open => "open", TangentAdmission.Approval => "approval", _ => "invite"
        },
        tangent.MembershipRole != TangentRole.Removed && RoleOf(tangent) == "visitor"
            && tangent.Admission is TangentAdmission.Open or TangentAdmission.Approval,
        tangent.Channels.Any(channel => channel.CanRead), tangent.Channels.Any(channel => channel.CanWrite));

    internal ExperienceTopicDto TopicDto(string tangentKey, RoomDescription room)
        => new(refs.Topic(tangentKey, room.Key), ExperienceDigest.Preview(room.Title, 80),
            ExperienceDigest.Preview(room.Topic, 240), room.CanRead, room.CanWrite, room.IsLocked, room.AllowPostEditing);

    private List<ExperiencePostDto> Posts(string tangentKey, string topicKey, TopicWindow window)
    {
        var posts = new List<ExperiencePostDto>(window.Messages.Count);
        foreach (var post in window.Messages)
        {
            string? replyTo = null;
            if (post.Content.ReplyTo is { } parent)
            {
                var projection = (from m in window.Messages where m.SourceUri == parent.Uri select m).FirstOrDefault();
                if (projection is not null) replyTo = refs.Post(tangentKey, topicKey, projection.Id);
            }
            posts.Add(new ExperiencePostDto(refs.Post(tangentKey, topicKey, post.Id), post.AuthorParticipantId,
                window.AuthorHandles.GetValueOrDefault(post.AuthorParticipantId, post.AuthorParticipantId),
                post.Removed ? "" : post.Content.Text, replyTo,
                refs.Origin + "/tangents/" + tangentKey + "/posts/" + post.Id + "/",
                Format(post.AcceptedAt), post.EditedAt is { } edited ? Format(edited) : null, post.Removed,
                post.Facets));
        }
        return posts;
    }

    private static string Serialize(object data) => System.Text.Json.JsonSerializer.Serialize(data, ExperienceJson.Options);

    internal string Timestamp() => clock.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

    internal string Format(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

    internal static string? CredentialOf(ClaimsPrincipal principal)
        => principal.FindFirst(ParticipationConstants.CredentialClaim)?.Value;

    private static string RegistryCredential(ClaimsPrincipal principal)
        => CredentialOf(principal) ?? WebSessionScope;

    private static ExperienceContinuation Empty() => new(null, null, null, null, null, null);

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
