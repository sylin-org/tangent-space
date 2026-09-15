using System.Security.Cryptography;
using System.Text.Json;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sorting;
using Koan.Data.Core;
using Microsoft.AspNetCore.DataProtection;
using TangentSpace.Activity;
using TangentSpace.Communities;
using TangentSpace.Conversation;
using TangentSpace.Participants;
using TangentSpace.Participation;
using TangentSpace.Application;
using TangentSpace.Rooms;

namespace TangentSpace.Experience;

/// <summary>Assembles the bounded per-participant attention digest from current message
/// projections under current policy. Derived state: edits and deletes withdraw their items,
/// re-ingestion never mints a fresh request, and explicit read acknowledgements resynchronize
/// by removing items from later digests. Directed attention (mentions, direct replies) is kept
/// distinct from mere watched-topic activity.</summary>
public sealed class ExperienceDigest(
    TangentGovernance tangents, RoomGovernance governance, TimeProvider clock,
    IDataProtectionProvider protection, References refs, TangentServer hub)
{
    public const int MaximumRooms = 100;
    public const int MaximumUnread = 100;
    private const int MaximumDirectedCount = 50;
    private const int MaximumActivityCount = 100;
    private static readonly QueryDefinition UnreadWindow = Window<Message>(nameof(Message.Sequence), MaximumUnread + 1, descending: true);

    private readonly IDataProtector cursorProtector = protection.CreateProtector("Tangent.Experience.DigestCursor.v1");

    public sealed record Digest(
        ExperienceAttention Attention, IReadOnlyList<ExperienceAction> FollowUps);

    public sealed record DigestCursor(string ParticipantId, string? ScopeTangent, string? ScopeRoom, int Offset, DateTimeOffset ExpiresAt);

    /// <summary>Computes one digest page for the participant. The checkpoint is the offset-zero
    /// cursor for recovery; the page cursor continues this page sequence only.</summary>
    public async Task<Digest> Page(string did, string? credentialId, DigestCursor? cursor, string? scopeTangent,
        string? scopeRoom, int limit, CancellationToken ct)
    {
        await EnsureActive(did, credentialId, ct);
        var offset = cursor?.Offset ?? 0;
        var directed = new List<ExperienceAttentionItem>();
        var watched = new List<ExperienceAttentionItem>();
        var overflow = false;
        var roomsScanned = 0;
        var scanLimited = false;
        for (var page = 1; page <= 4; page++)
        {
            TangentChannelDirectory directory;
            using (EntityContext.NoCache())
                directory = await tangents.ListAuthorizedChannels(did, page, ct);
            if (directory.ScanLimited) scanLimited = true;
            foreach (var room in directory.Channels)
            {
                if (roomsScanned >= MaximumRooms) { overflow = true; break; }
                roomsScanned++;
                if (scopeTangent is not null && room.TangentKey != scopeTangent) continue;
                if (scopeRoom is not null && room.Key != scopeRoom) continue;
                await CollectRoom(did, room, directed, watched, ct);
            }
            if (directory.NextPage is null || overflow) break;
        }
        directed.Sort(DirectOrder);
        watched.Sort(DirectOrder);
        var waiting = directed.Count;
        var activity = watched.Count;
        var chosen = directed.Concat(watched).Skip(offset).Take(limit).ToList();
        var more = directed.Count + watched.Count > offset + chosen.Count;
        var partial = overflow || scanLimited;
        var revision = await HeadRevision(ct);
        var asOf = Timestamp();
        var attention = new ExperienceAttention(revision, asOf,
            new(Math.Min(waiting, MaximumDirectedCount), waiting > MaximumDirectedCount),
            new(Math.Min(activity, MaximumActivityCount), activity > MaximumActivityCount),
            chosen, more, DetailsIncluded: false);
        var followUps = directed.Skip(offset).Take(3)
            .Select(item => new ExperienceAction(ExperienceActionNames.ReadTopic, item.ScopeRef,
                item.SourceRef, $"Read {item.ActorName ?? "a participant"}'s request"))
            .ToList();
        return new(attention, followUps);
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

    private async Task CollectRoom(string did, RoomDescription room, List<ExperienceAttentionItem> directed,
        List<ExperienceAttentionItem> watched, CancellationToken ct)
    {
        await governance.WithCurrentPolicy(did, room.Key, async (policy, token) =>
        {
            if (!policy.CanRead) return false;
            using var fresh = EntityContext.NoCache();
            var stored = await Room.Get(room.Key, token);
            if (stored is null) return false;
            var mode = AttentionRules.Effective(
                await WatchSetting.Get(WatchSetting.Key(did, room.Key), token),
                await TangentWatchSetting.Get(TangentWatchSetting.Key(did, stored.TangentKey), token));
            if (!AttentionRules.DeliversChannel(mode)) return false;
            var state = await RoomConversation.Get(room.Key, token) ?? new RoomConversation { Id = room.Key };
            var read = await ReadPosition.Get(ReadPosition.Key(did, room.Key), token);
            var readSequence = Math.Min(read?.Sequence ?? 0, state.LastSequence);
            var recipientDid = await hub.Directory.AtprotoDidOf(did, token);
            var unread = (await Message.Query(
                message => message.RoomKey == room.Key && message.Sequence > readSequence, UnreadWindow, token))
                .OrderBy(message => message.Sequence).ToList();
            var handles = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var (author, label) in await hub.Directory.LabelsFor(
                     unread.Select(message => message.AuthorParticipantId).Distinct(StringComparer.Ordinal), token))
                handles[author] = label;
            var addressedPosts = new HashSet<string>(StringComparer.Ordinal);
            foreach (var message in unread)
            {
                if (message.Removed || message.AuthorParticipantId == did) continue;
                var authorHandle = handles.GetValueOrDefault(message.AuthorParticipantId);
                // Direct replies answer this participant's accepted message.
                if (message.Content.ReplyTo is { } parent)
                {
                    var decision = await SourceDecision.Get(SourceDecision.Key(room.Key, parent.Uri, parent.Cid), token);
                    if (AttentionRules.IsDirectReply(message, decision, did))
                    {
                        addressedPosts.Add(message.Id);
                        directed.Add(Item(did, room, stored.TangentKey, message, "direct_reply", "replies_to_you", authorHandle));
                        continue;
                    }
                }
                // Mentions are facets (ADR 0008): minted by the picker or detected when the post is saved,
                // they carry the perennial identity value (atproto DID or internal DID) and need no prose resolution.
                var facets = message.Facets ?? [];
                if (facets.Any(facet => recipientDid is not null && facet.References(recipientDid)
                        || facet.References(ParticipantIdentity.InternalValue(did))))
                {
                    addressedPosts.Add(message.Id);
                    directed.Add(Item(did, room, stored.TangentKey, message, "direct_mention", "addressed_to_you", authorHandle));
                    continue;
                }
                // Group mentions expand to current holders of the scoped roles: the group is
                // resolved at digest time, never stored in anyone's words (ADR 0008).
                var groups = facets.Where(facet => facet.Kind == Conversation.PostFacet.Group)
                    .Select(facet => facet.Value!).Where(value => Conversation.PostFacet.Groups.Contains(value, StringComparer.Ordinal))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (groups.Count > 0 && message.AuthorParticipantId != did && await HoldsAnyRole(did, stored.TangentKey, groups, token))
                {
                    addressedPosts.Add(message.Id);
                    directed.Add(Item(did, room, stored.TangentKey, message, "direct_mention", "addressed_to_you", authorHandle));
                    continue;
                }
                if (mode != WatchMode.Replies)
                    watched.Add(Item(did, room, stored.TangentKey, message, "watched_activity", null, authorHandle));
            }
            return true;
        }, ct);
    }

    /// <summary>Whether the recipient currently holds any of the mentioned role groups in
    /// this Tangent. admins/moderators = owner plus Tangent admins; members = any member.</summary>
    private static async Task<bool> HoldsAnyRole(string participantId, string tangentKey, IReadOnlyList<string> groups, CancellationToken ct)
    {
        using var fresh = EntityContext.NoCache();
        var membership = await TangentMembership.Get(TangentMembership.Key(tangentKey, participantId), ct);
        var site = await TangentSpace.Site.TangentSite.Get(TangentSpace.Infrastructure.TangentConstants.SiteId, ct);
        var owner = site?.IsOwner(participantId) == true;
        var admin = owner || membership?.Role == TangentRole.Admin;
        if (admin && groups.Any(group => group is "admins" or "moderators")) return true;
        if (membership?.Role is TangentRole.Member or TangentRole.Admin or TangentRole.Reader && groups.Contains("members")) return true;
        return false;
    }

    private static int DirectOrder(ExperienceAttentionItem left, ExperienceAttentionItem right)
        => string.CompareOrdinal(RefKey(right), RefKey(left));

    private static string RefKey(ExperienceAttentionItem item) => item.Ref;

    private ExperienceAttentionItem Item(string did, RoomDescription room, string tangentKey, Message message,
        string kind, string? relationship, string? authorHandle)
        => new("att:" + room.Key + ":" + message.Id, kind, message.AuthorParticipantId,
            string.IsNullOrEmpty(authorHandle) ? null : authorHandle, did,
            refs.Topic(tangentKey, room.Key), refs.Post(tangentKey, room.Key, message.Id),
            relationship, Preview(message.Content.Text, 160),
            message.SourceCid ?? "seq:" + message.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "pending");

    internal static string Preview(string value, int limit)
        => value.Length <= limit ? value : value[..(char.IsHighSurrogate(value[limit - 2]) ? limit - 2 : limit - 1)] + "…";

    public async Task<string> HeadRevision(CancellationToken ct)
    {
        using var fresh = EntityContext.NoCache();
        var head = await ActivityHead.Get(ActivityHead.Key, ct) ?? new ActivityHead { Id = ActivityHead.Key };
        return "att:" + head.LastSequence.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    internal string Timestamp() => clock.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

    public string Encode(DigestCursor cursor) => cursorProtector.Protect(JsonSerializer.Serialize(cursor));

    public DigestCursor? Decode(string? value, string participantId, string? scopeTangent, string? scopeRoom)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            if (value.Length > 4096) return null;
            var cursor = JsonSerializer.Deserialize<DigestCursor>(cursorProtector.Unprotect(value));
            return cursor is null || cursor.ParticipantId != participantId || cursor.Offset < 0 || cursor.Offset > 100_000
                || cursor.ExpiresAt <= clock.GetUtcNow()
                || cursor.ScopeTangent != scopeTangent || cursor.ScopeRoom != scopeRoom ? null : cursor;
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
