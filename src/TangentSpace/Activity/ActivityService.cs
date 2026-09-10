using System.Security.Cryptography;
using System.Text.Json;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sorting;
using Koan.Data.Core;
using Microsoft.AspNetCore.DataProtection;
using TangentSpace.Conversation;
using TangentSpace.Communities;
using TangentSpace.Participation;
using TangentSpace.Participants;
using TangentSpace.Rooms;

namespace TangentSpace.Activity;

/// <summary>Builds bounded, current-policy participant activity snapshots from the durable journal.</summary>
public sealed class ActivityService(RoomGovernance governance, TangentGovernance tangents, TimeProvider clock, IDataProtectionProvider protection)
{
    private const int MaximumJournalScan = 100;
    private const int MaximumEvents = 25;
    private const int MaximumRooms = 100;
    private const int MaximumUnread = 100;
    private readonly IDataProtector cursors = protection.CreateProtector("Tangent.Activity.Cursor.v1");
    private static readonly QueryDefinition JournalWindow = Window<ActivityJournal>(nameof(ActivityJournal.Sequence), MaximumJournalScan + 1);
    private static readonly QueryDefinition MessagesWindow = Window<Message>(nameof(Message.Sequence), MaximumUnread + 1, descending: true);
    private static readonly QueryDefinition OneMessageWindow = Window<Message>(nameof(Message.Sequence), 1, descending: true);

    public Task<ActivitySnapshot> Snapshot(string did, string? credentialId, string? cursor, CancellationToken ct)
        => Snapshot(did, credentialId, cursor, null, ct);

    public Task<ActivitySnapshot> Wait(string did, string? credentialId, string? cursor, CancellationToken ct)
        => Wait(did, credentialId, cursor, null, ct);

    public async Task<ActivitySnapshot> Snapshot(string did, string? credentialId, string? cursor, string? channelCursor, CancellationToken ct)
    {
        await EnsureParticipantActive(did, credentialId, ct);
        ActivityCursor? requested = Decode(cursor, did);
        var head = await ReadHead(ct);
        if (requested is null)
            return await Bootstrap(did, head.LastSequence, resetRequired: cursor is not null, channelCursor, ct);

        var boundary = requested.Boundary ?? head.LastSequence;
        if (requested.After < 0 || requested.After > boundary || boundary > head.LastSequence)
            return await Bootstrap(did, head.LastSequence, resetRequired: true, channelCursor, ct);

        var entries = (await ActivityJournal.Query(value => value.Sequence > requested.After && value.Sequence <= boundary,
            JournalWindow, ct)).OrderBy(value => value.Sequence).Take(MaximumJournalScan + 1).ToArray();
        var scanned = entries.Take(MaximumJournalScan).ToArray();
        var visibility = new List<(long Sequence, bool Visible)>(scanned.Length);
        foreach (var entry in scanned) visibility.Add((entry.Sequence, await CanReadEvent(did, entry, ct) && await WatchesEvent(did, entry, ct)));
        var plan = ActivityPaging.Plan(requested.After, boundary, visibility, entries.Length > MaximumJournalScan, MaximumEvents);
        var delivered = plan.DeliveredSequences.ToHashSet();
        var events = scanned.Where(entry => delivered.Contains(entry.Sequence)).Select(entry => EventFor(did, entry)).ToArray();
        var after = plan.After;
        var hasMore = plan.HasMore;
        // The checkpoint is also the cursor to use for a reconnect. A non-null boundary fixes paging despite later commits.
        var nextState = new ActivityCursor(did, requested.Consumer, after, hasMore ? boundary : null, requested.ExpiresAt);
        // An unchanged opaque cursor is significant: Wait uses it to distinguish an idle timeout from a committed update.
        var checkpoint = ActivityPaging.Continue(cursor!, requested, nextState, Encode);
        var channels = await Channels(did, channelCursor, ct);
        return new ActivitySnapshot(checkpoint, events, hasMore ? checkpoint : null, hasMore, channels.ResetRequired,
            channels.Values, channels.HasMore || channels.Incomplete, channels.NextCursor, channels.HasMore, channels.Incomplete);
    }

    public async Task<ActivitySnapshot> Wait(string did, string? credentialId, string? cursor, string? channelCursor, CancellationToken ct)
    {
        // Capture before inspecting durable state. A commit between the snapshot and wait is therefore replayed.
        using var notification = ActivityJournal.Capture();
        var first = await Snapshot(did, credentialId, cursor, channelCursor, ct);
        if (first.ResetRequired || !string.Equals(first.Checkpoint, cursor, StringComparison.Ordinal)) return first;
        await notification.WaitAsync(TimeSpan.FromSeconds(15), ct);
        return await Snapshot(did, credentialId, cursor, channelCursor, ct);
    }

    public async Task EnsureParticipantActive(string did, string? credentialId, CancellationToken ct)
    {
        using var fresh = EntityContext.NoCache();
        var participant = await Participant.Get(did, ct);
        if (participant is null || participant.IsSuspended)
            throw new UnauthorizedAccessException("This participant is no longer active.");
        if (credentialId is null) return;
        var credential = await ParticipantCredential.Get(credentialId, ct);
        if (credential is null || credential.ParticipantDid != did || !credential.IsActive(clock.GetUtcNow()))
            throw new UnauthorizedAccessException("This participant credential is expired or revoked.");
    }

    /// <summary>Last-moment authorization gate used immediately before a protected HTTP or SSE payload is written.</summary>
    public async Task EnsureSnapshotCurrent(string did, string? credentialId, ActivitySnapshot snapshot, CancellationToken ct)
    {
        await EnsureParticipantActive(did, credentialId, ct);
        foreach (var channel in snapshot.Channels)
        {
            var allowed = await governance.WithCurrentPolicy(did, channel.RoomKey, (policy, _) => Task.FromResult(policy.CanRead), ct);
            if (!allowed) throw new UnauthorizedAccessException("Activity access changed while the snapshot was being prepared.");
        }
        foreach (var activityEvent in snapshot.Events)
        {
            if (!Enum.TryParse<ActivityKind>(activityEvent.Kind, out var kind)
                || !await CanReadEvent(did, new ActivityJournal { Kind = kind, RoomKey = activityEvent.RoomKey,
                    TangentKey = activityEvent.TangentKey, ActorDid = activityEvent.ActorDid }, ct))
                throw new UnauthorizedAccessException("Activity access changed while the snapshot was being prepared.");
        }
    }

    private async Task<ActivitySnapshot> Bootstrap(string did, long sequence, bool resetRequired, string? channelCursor, CancellationToken ct)
    {
        var checkpoint = Encode(new ActivityCursor(did, Guid.CreateVersion7().ToString("N"), sequence, null, clock.GetUtcNow().AddDays(7)));
        var channels = await Channels(did, channelCursor, ct);
        return new ActivitySnapshot(checkpoint, [], null, false, resetRequired || channels.ResetRequired,
            channels.Values, channels.HasMore || channels.Incomplete, channels.NextCursor, channels.HasMore, channels.Incomplete);
    }

    private async Task<ActivityHead> ReadHead(CancellationToken ct)
    {
        using var fresh = EntityContext.NoCache();
        return await ActivityHead.Get(ActivityHead.Key, ct) ?? new ActivityHead { Id = ActivityHead.Key };
    }

    private async Task<ChannelOverview> Channels(string did, string? cursor, CancellationToken ct)
    {
        var selected = DecodeChannel(cursor, did);
        var reset = cursor is not null && selected is null;
        selected ??= new ActivityChannelCursor(did, Guid.CreateVersion7().ToString("N"), 1, clock.GetUtcNow().AddDays(7));
        var directory = await tangents.ListAuthorizedChannels(did, selected.Page, ct);
        var channels = new List<ActivityChannel>(directory.Channels.Count);
        foreach (var room in directory.Channels)
        {
            var channel = await Channel(did, room.Key, ct);
            if (channel is not null) channels.Add(channel);
        }
        var next = directory.NextPage is null ? null : EncodeChannel(selected with { Page = directory.NextPage.Value });
        // Default priority: channels with waiting direct replies, then channels the participant engaged with.
        var ordered = channels
            .OrderBy(value => AttentionRules.PriorityTier(value.DirectReplies, value.ReadSequence))
            .ThenBy(value => value.TangentKey, StringComparer.Ordinal)
            .ThenBy(value => value.RoomKey, StringComparer.Ordinal)
            .ToArray();
        return new(ordered, next, next is not null, reset, directory.ScanLimited);
    }

    private Task<ActivityChannel?> Channel(string did, string roomKey, CancellationToken ct)
        => governance.WithCurrentPolicy(did, roomKey, async (policy, token) =>
        {
            if (!policy.CanRead) return null;
            var room = await Room.Get(roomKey, token);
            if (room is null) return null;
            // Null-safe: an absent watch record (the common case) simply means All.
            var mode = AttentionRules.Effective(await WatchSetting.Get(WatchSetting.Key(did, roomKey), token),
                await TangentWatchSetting.Get(TangentWatchSetting.Key(did, room.TangentKey), token));
            if (!AttentionRules.DeliversChannel(mode)) return null;
            var state = await RoomConversation.Get(roomKey, token) ?? new RoomConversation { Id = roomKey };
            var read = await ReadPosition.Get(ReadPosition.Key(did, roomKey), token);
            var readSequence = Math.Min(read?.Sequence ?? 0, state.LastSequence);
            // The window stays source-order; attention excludes the actor's own contributions.
            var unreadWindow = await Message.Query(message => message.RoomKey == roomKey && message.Sequence > readSequence,
                MessagesWindow, token);
            var unread = unreadWindow.Where(message => AttentionRules.CountsForAttention(message, did)).ToArray();
            var unreadCount = Math.Min(unread.Length, MaximumUnread);
            var directReplies = 0;
            foreach (var message in unread.Take(MaximumUnread))
            {
                if (message.Content.ReplyTo is not { } reply) continue;
                var parent = await SourceDecision.Get(SourceDecision.Key(roomKey, reply.Uri, reply.Cid), token);
                if (parent?.Accepted == true && parent.AuthorDid == did) directReplies++;
            }
            var last = state.LastSequence == 0 ? null : (await Message.Query(message => message.RoomKey == roomKey && message.Sequence == state.LastSequence,
                OneMessageWindow, token)).FirstOrDefault();
            var attention = AttentionRules.AttentionUnread(mode, unreadCount, Math.Min(directReplies, MaximumUnread));
            // The window itself overflowed: any count derived from it may be clipped at the cap.
            return new ActivityChannel(roomKey, room.TangentKey, attention, unreadWindow.Count > MaximumUnread,
                Math.Min(directReplies, MaximumUnread), state.LastSequence, readSequence, state.Freshness, last?.AcceptedAt);
        }, ct);

    /// <summary>Personal watch preference layered after the access check; never widens access.</summary>
    private async Task<bool> WatchesEvent(string did, ActivityJournal entry, CancellationToken ct)
    {
        if (entry.Kind != ActivityKind.MessageAccepted || string.IsNullOrEmpty(entry.RoomKey)) return true;
        var mode = AttentionRules.Effective(await WatchSetting.Get(WatchSetting.Key(did, entry.RoomKey), ct),
            await TangentWatchSetting.Get(TangentWatchSetting.Key(did, entry.TangentKey), ct));
        if (!AttentionRules.DeliversEvent(mode, entry.Kind)) return false;
        if (mode != WatchMode.Replies) return true;
        // Replies mode delivers a message marker only when it answers this participant's accepted message.
        var message = (await Message.Query(value => value.RoomKey == entry.RoomKey
            && value.Sequence == (entry.MessageSequence ?? -1), OneMessageWindow, ct)).FirstOrDefault();
        var parent = message?.Content.ReplyTo is { } reply
            ? await SourceDecision.Get(SourceDecision.Key(entry.RoomKey, reply.Uri, reply.Cid), ct) : null;
        return AttentionRules.IsDirectReply(message, parent, did);
    }

    private async Task<bool> CanReadEvent(string did, ActivityJournal entry, CancellationToken ct)
    {
        // Individual read positions are personal. Never turn a room marker into read-state disclosure.
        if (entry.Kind == ActivityKind.ReadAcknowledged && entry.ActorDid != did) return false;
        if (string.IsNullOrEmpty(entry.RoomKey))
            return await tangents.CanAccess(did, entry.TangentKey, ct);
        return await governance.WithCurrentPolicy(did, entry.RoomKey, (policy, _) => Task.FromResult(policy.CanRead), ct);
    }

    internal static ActivityEvent EventFor(string did, ActivityJournal entry)
    {
        // Membership, moderation and participant targets are administration metadata, not room conversation data.
        var privateTarget = entry.Kind is ActivityKind.MembershipChanged or ActivityKind.ParticipantChanged
            or ActivityKind.InvitationChanged or ActivityKind.RestrictionChanged;
        var target = privateTarget && entry.TargetDid != did && entry.ActorDid != did ? null : entry.TargetDid;
        return new(entry.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture), entry.Kind.ToString(), entry.RoomKey,
            entry.TangentKey, entry.ActorDid, target, entry.MessageSequence, entry.OccurredAt);
    }

    private string Encode(ActivityCursor cursor) => cursors.Protect(JsonSerializer.Serialize(cursor));

    private ActivityCursor? Decode(string? value, string did)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            if (value.Length > 4096) return null;
            var cursor = JsonSerializer.Deserialize<ActivityCursor>(cursors.Unprotect(value));
            return cursor is null || cursor.Did != did || cursor.ExpiresAt <= clock.GetUtcNow() ? null : cursor;
        }
        catch (Exception error) when (error is CryptographicException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    private string EncodeChannel(ActivityChannelCursor cursor) => cursors.Protect(JsonSerializer.Serialize(cursor));

    private ActivityChannelCursor? DecodeChannel(string? value, string did)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            if (value.Length > 4096) return null;
            var cursor = JsonSerializer.Deserialize<ActivityChannelCursor>(cursors.Unprotect(value));
            return cursor is null || cursor.Did != did || cursor.Page is < 1 or > TangentGovernance.MaximumPage
                || cursor.ExpiresAt <= clock.GetUtcNow() ? null : cursor;
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
            Page = 1,
            PageSize = size,
            Sort = [new SortSpec(new MemberPath(typeof(T), [member], member.PropertyType, false, -1), descending)]
        };
    }

    private sealed record ChannelOverview(IReadOnlyList<ActivityChannel> Values, string? NextCursor, bool HasMore, bool ResetRequired,
        bool Incomplete);
}
