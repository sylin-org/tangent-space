using System.Security.Cryptography;
using System.Text.Json;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sorting;
using Koan.Data.Core;
using Microsoft.AspNetCore.DataProtection;
using Tangent.Community;
using Tangent.Conversation;
using Tangent.Identity;
using Tangent.Application;
using Tangent.Stewardship;
using Tangent.Api;
using Tangent.Spaces;
using Tangent.Infrastructure;

namespace Tangent.Activity;

/// <summary>Assembles the bounded per-participant attention digest from the compiled
/// <see cref="TopicAttention"/> projection and current post projections. Writes signal, a
/// background job compiles, reads consume the compiled state: edits and deletes withdraw their
/// items when the item window is rebuilt, re-ingestion never mints a fresh request, and explicit
/// read acknowledgements resynchronize by removing items from later digests. Directed attention
/// (mentions, direct replies) is kept distinct from mere watched-topic activity.</summary>
public sealed class ExperienceDigest(
    TangentGovernance tangents, TimeProvider clock,
    IDataProtectionProvider protection, References refs, TangentServer hub)
{
    public const int MaximumTopics = 100;
    public const int MaximumUnread = 100;
    private const int MaximumDirectedCount = 50;
    private const int MaximumActivityCount = 100;
    private static readonly QueryDefinition UnreadWindow = Window<Post>(nameof(Post.Sequence), MaximumUnread + 1, descending: true);

    private readonly IDataProtector cursorProtector = protection.CreateProtector("Community.Tangent.Experience.DigestCursor.v1");

    public sealed record DigestCursor(string ParticipantId, string? ScopeTangent, string? ScopeTopic, int Offset, DateTimeOffset ExpiresAt, string? Filter = null, string? Selection = null, string? Arrivals = null);

    public sealed record NotificationFilter(bool Replies, bool Mentions, bool Following);

    /// <summary>Computes one digest page for the participant. The checkpoint is the offset-zero
    /// cursor for recovery; the page cursor continues this page sequence only.</summary>
    public async Task<ExperienceAttention> Page(string did, string? credentialId, DigestCursor? cursor, string? scopeTangent,
        string? scopeTopic, int limit, CancellationToken ct, NotificationFilter? notifications = null)
    {
        await EnsureActive(did, credentialId, ct);
        var offset = cursor?.Offset ?? 0;
        var directed = new List<ExperienceAttentionItem>();
        var watched = new List<ExperienceAttentionItem>();
        var incomplete = false;
        if (scopeTopic is not null)
        {
            using var fresh = EntityContext.NoCache();
            var topic = await hub.Topics.Describe(did, scopeTopic, ct);
            if (topic?.CanRead == true && (scopeTangent is null || topic.TangentKey == scopeTangent))
                incomplete = await CollectTopic(did, topic, directed, watched, ct, notifications);
        }
        else
        {
            var topicsScanned = 0;
            for (var page = 1; page <= 4; page++)
            {
                TangentTopicDirectory directory;
                using (EntityContext.NoCache())
                    directory = scopeTangent is null
                        ? await tangents.ListAuthorizedTopics(did, page, ct)
                        : await hub.Topics.ListForTangent(did, scopeTangent, page, ct);
                incomplete |= directory.ScanLimited;
                foreach (var topic in directory.Topics)
                {
                    if (topicsScanned >= MaximumTopics) { incomplete = true; break; }
                    topicsScanned++;
                    incomplete |= await CollectTopic(did, topic, directed, watched, ct, notifications);
                }
                if (directory.NextPage is null) break;
                if (topicsScanned >= MaximumTopics || page == 4) { incomplete = true; break; }
            }
        }
        directed.Sort(DirectOrder);
        watched.Sort(DirectOrder);
        var selection = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(directed.Concat(watched).Select(item => new { item.Ref, item.Kind,
                item.ActorRef, item.ScopeRef, item.SourceRef, item.SourceRevision, item.Relationship, item.Excerpt, item.State })))));
        if (cursor?.Selection is { } expected && expected != selection)
            throw new ArgumentException("This attention selection changed. Start a new read without a cursor.");
        if (cursor is { Offset: > 0, Selection: null })
            throw new ArgumentException("This attention page has expired. Start a new read without a cursor.");
        var waiting = directed.Count;
        var activity = watched.Count;
        var chosen = directed.Concat(watched).Skip(offset).Take(limit).ToList();
        var more = directed.Count + watched.Count > offset + chosen.Count;
        var revision = await HeadRevision(ct);
        var asOf = Timestamp();
        var attention = new ExperienceAttention(revision, asOf,
            new(incomplete ? null : Math.Min(waiting, MaximumDirectedCount), incomplete || waiting > MaximumDirectedCount),
            new(incomplete ? null : Math.Min(activity, MaximumActivityCount), incomplete || activity > MaximumActivityCount),
            chosen, more, DetailsIncluded: false, Incomplete: incomplete) { Selection = selection };
        return attention;
    }

    private async Task EnsureActive(string participantId, string? credentialId, CancellationToken ct)
    {
        using var fresh = EntityContext.NoCache();
        var participant = await Participant.Get(participantId, ct);
        if (participant is null || participant.IsSuspended)
            throw new UnauthorizedAccessException("This participant is no longer active.");
        if (credentialId is null) return;
        var credential = await ParticipantCredential.Get(credentialId, ct);
        if (credential is null || credential.ParticipantId != participantId || !credential.IsActive(clock.GetUtcNow()))
            throw new UnauthorizedAccessException("This participant credential is expired or revoked.");
    }

    /// <summary>Adds one topic's attention items for the viewer by reading the compiled
    /// projection. The directory walk already scoped the topic to what this viewer can read;
    /// the compiled row carries the viewer's attention numbers, and the watch filter applies at
    /// read time from two point reads. Items rebuild from a bounded unread window only when the
    /// compiled window is non-empty, so cost follows item count.</summary>
    private async Task<bool> CollectTopic(string did, TopicDescription topic, List<ExperienceAttentionItem> directed,
        List<ExperienceAttentionItem> watched, CancellationToken ct, NotificationFilter? notifications)
    {
        if (!topic.CanRead) return false;
        var attention = await TopicAttention.Get(TopicAttention.IdOf(topic.Key, did), ct);
        if (attention is null) return false;
        var watch = await WatchSetting.Get(WatchSetting.Key(did, topic.Key), ct);
        var tangentWatch = await TangentWatchSetting.Get(TangentWatchSetting.Key(did, topic.TangentKey), ct);
        var mode = AttentionRules.Effective(watch, tangentWatch);
        var explicitlyFollowing = watch is { Mode: not WatchMode.None }
            || watch is null && tangentWatch is { Mode: not WatchMode.None };
        if (!AttentionRules.DeliversTopic(mode)) return false;
        if (attention.LastSequence <= attention.ReadSequence) return false;
        {
            var recipientDid = await hub.Directory.AtprotoDidOf(did, ct);
            var window = await Post.Query(
                post => post.TopicKey == topic.Key && post.Sequence > attention.ReadSequence, UnreadWindow, ct);
            var incomplete = window.Count > MaximumUnread;
            var unread = window.Take(MaximumUnread).OrderBy(post => post.Sequence).ToList();
            var handles = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var (author, label) in await hub.Directory.LabelsFor(
                 unread.Select(post => post.AuthorParticipantId).Distinct(StringComparer.Ordinal), ct))
            handles[author] = label;
            foreach (var post in unread)
            {
            // The actor's own writes settle into their inbox â€” read mail about what you
            // posted, so the author sees the double post and can reconcile it with the
            // delete key it already holds. Settlement rides the Topic's watch: a
            // watched-out Topic delivers nothing.
            if (post.AuthorParticipantId == did)
            {
                if (!post.Removed && notifications is null)
                    watched.Add(Item(did, topic, topic.TangentKey, post, "write_settlement", null,
                        handles.GetValueOrDefault(did)));
                continue;
            }
            if (post.Removed) continue;
            var authorHandle = handles.GetValueOrDefault(post.AuthorParticipantId);
            // Direct replies answer this participant's accepted post.
            if (post.Content.ReplyTo is { } parentId)
            {
                var parent = await Post.Get(parentId, ct);
                if (parent is not null && parent.TopicKey == topic.Key
                    && AttentionRules.IsDirectReply(post, parent, did))
                {
                    if (notifications?.Replies != false) directed.Add(Item(did, topic, topic.TangentKey, post, "direct_reply", "replies_to_you", authorHandle));
                    continue;
                }
            }
            // Mentions are facets (ADR 0008): minted by the picker or detected when the post is saved,
            // they carry the perennial identity value (atproto DID or internal DID) and need no prose resolution.
            var facets = post.Facets ?? [];
            if (facets.Any(facet => recipientDid is not null && facet.References(recipientDid)
                    || facet.References(ParticipantIdentity.InternalValue(did))))
            {
                if (notifications?.Mentions != false) directed.Add(Item(did, topic, topic.TangentKey, post, "direct_mention", "addressed_to_you", authorHandle));
                continue;
            }
            // Group mentions expand to current holders of the scoped roles: the group is
            // resolved at digest time, never stored in anyone's words (ADR 0008).
            var groups = facets.Where(facet => facet.Kind == Conversation.PostFacet.Group)
                .Select(facet => facet.Value!).Where(value => Conversation.PostFacet.Groups.Contains(value, StringComparer.Ordinal))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (groups.Count > 0 && post.AuthorParticipantId != did && await HoldsAnyRole(did, topic.TangentKey, groups, ct))
            {
                if (notifications?.Mentions != false) directed.Add(Item(did, topic, topic.TangentKey, post, "direct_mention", "addressed_to_you", authorHandle));
                continue;
            }
            if (mode != WatchMode.Replies && (notifications is null || notifications.Following && explicitlyFollowing))
                watched.Add(Item(did, topic, topic.TangentKey, post, "watched_activity", null, authorHandle));
        }
            using var fresh = EntityContext.NoCache();
            var current = await hub.Topics.Describe(did, topic.Key, ct);
            if (current?.CanRead != true || current.TangentKey != topic.TangentKey)
            {
                var scope = refs.Topic(topic.TangentKey, topic.Key);
                directed.RemoveAll(item => item.ScopeRef == scope);
                watched.RemoveAll(item => item.ScopeRef == scope);
                return false;
            }
            return incomplete;
        }
    }

    /// <summary>Whether the recipient currently holds any of the mentioned role groups in
    /// this Tangent. admins/moderators = owner plus Tangent admins; members = any member.</summary>
    private static async Task<bool> HoldsAnyRole(string participantId, string tangentKey, IReadOnlyList<string> groups, CancellationToken ct)
    {
        using var fresh = EntityContext.NoCache();
        var membership = await TangentMembership.Get(TangentMembership.Key(tangentKey, participantId), ct);
        var space = await Space.Get(TangentConstants.SpaceId, ct);
        var owner = space?.IsOwner(participantId) == true;
        var admin = owner || membership?.Role == TangentRole.Admin;
        if (admin && groups.Any(group => group is "admins" or "moderators")) return true;
        if (membership?.Role is TangentRole.Member or TangentRole.Admin or TangentRole.Reader && groups.Contains("members")) return true;
        return false;
    }

    private static int DirectOrder(ExperienceAttentionItem left, ExperienceAttentionItem right)
        => string.CompareOrdinal(RefKey(right), RefKey(left));

    private static string RefKey(ExperienceAttentionItem item) => item.Ref;

    private ExperienceAttentionItem Item(string did, TopicDescription topic, string tangentKey, Post post,
        string kind, string? relationship, string? authorHandle)
        => new("att:" + topic.Key + ":" + post.Id, kind, post.AuthorParticipantId,
            string.IsNullOrEmpty(authorHandle) ? null : authorHandle, did,
            refs.Topic(tangentKey, topic.Key), refs.Post(tangentKey, topic.Key, post.Id),
            relationship, Preview(PostMarkup.PlainText(post.Content), 160),
            "seq:" + post.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "pending");

    internal static string Preview(string value, int limit)
        => value.Length <= limit ? value : value[..(char.IsHighSurrogate(value[limit - 2]) ? limit - 2 : limit - 1)] + "â€¦";

    public async Task<string> HeadRevision(CancellationToken ct)
        => "att:" + (await ActivityJournal.LatestSequence(ct)).ToString(System.Globalization.CultureInfo.InvariantCulture);

    internal string Timestamp() => clock.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

    public string Encode(DigestCursor cursor) => cursorProtector.Protect(JsonSerializer.Serialize(cursor));

    public DigestCursor? Decode(string? value, string participantId, string? scopeTangent, string? scopeTopic, string? filter = null)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            if (value.Length > 4096) return null;
            var cursor = JsonSerializer.Deserialize<DigestCursor>(cursorProtector.Unprotect(value));
            return cursor is null || cursor.ParticipantId != participantId || cursor.Offset < 0 || cursor.Offset > 100_000
                || cursor.ExpiresAt <= clock.GetUtcNow()
                || cursor.ScopeTangent != scopeTangent || cursor.ScopeTopic != scopeTopic || cursor.Filter != filter ? null : cursor;
        }
        catch (Exception error) when (error is CryptographicException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    private static QueryDefinition Window<T>(string property, int size, bool descending = false)
    {
        var member = typeof(T).GetProperty(property)!;
        return new QueryDefinition
        {
            Page = 1, PageSize = size,
            Sort = [new SortSpec(new MemberPath(typeof(T), [member], member.PropertyType, false, -1), descending)]
        };
    }
}
