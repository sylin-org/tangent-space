using TangentSpace.Activity;
using TangentSpace.Conversation;
using Xunit;

namespace TangentSpace.Tests;

/// <summary>Rule tests for participant attention: own-message exclusion, watch state, priority, and read independence.</summary>
public sealed class AttentionRulesTests
{
    private const string Watcher = "did:plc:aaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Author = "did:plc:bbbbbbbbbbbbbbbbbbbbbbbb";
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_actors_own_messages_never_count_as_unread_attention()
    {
        var own = new Message { RoomKey = "lounge", AuthorParticipantId = Watcher, Sequence = 2 };
        var theirs = new Message { RoomKey = "lounge", AuthorParticipantId = Author, Sequence = 3 };
        Assert.False(AttentionRules.CountsForAttention(own, Watcher));
        Assert.True(AttentionRules.CountsForAttention(theirs, Watcher));
        Assert.True(AttentionRules.CountsForAttention(own, Author));
    }

    [Fact]
    public void Watch_none_omits_the_channel_and_its_message_markers_without_touching_access()
    {
        Assert.False(AttentionRules.DeliversChannel(WatchMode.None));
        Assert.True(AttentionRules.DeliversChannel(WatchMode.All));
        Assert.True(AttentionRules.DeliversChannel(WatchMode.Replies));

        Assert.False(AttentionRules.DeliversEvent(WatchMode.None, ActivityKind.MessageAccepted));
        // Watch only suppresses conversation noise: administrative markers stay visible with access.
        Assert.True(AttentionRules.DeliversEvent(WatchMode.None, ActivityKind.MembershipChanged));
        Assert.True(AttentionRules.DeliversEvent(WatchMode.None, ActivityKind.RestrictionChanged));
        Assert.True(AttentionRules.DeliversEvent(WatchMode.All, ActivityKind.MessageAccepted));
    }

    [Fact]
    public void Replies_mode_reports_only_direct_reply_attention()
    {
        // All and None keep the full unread count; only Replies filters it to direct replies.
        Assert.Equal(7, AttentionRules.AttentionUnread(WatchMode.All, 7, 2));
        Assert.Equal(2, AttentionRules.AttentionUnread(WatchMode.Replies, 7, 2));
        Assert.Equal(7, AttentionRules.AttentionUnread(WatchMode.None, 7, 2));
    }

    [Fact]
    public void Default_priority_puts_direct_replies_first_then_participated_channels()
    {
        Assert.Equal(0, AttentionRules.PriorityTier(3, 0));
        Assert.Equal(1, AttentionRules.PriorityTier(0, 12));
        Assert.Equal(2, AttentionRules.PriorityTier(0, 0));
        var ordered = new[] { (Tier: 2, Room: "a"), (Tier: 0, Room: "c"), (Tier: 1, Room: "b"), (Tier: 0, Room: "d") }
            .OrderBy(value => value.Tier).ThenBy(value => value.Room, StringComparer.Ordinal).ToArray();
        Assert.Equal(["c", "d", "b", "a"], ordered.Select(value => value.Room).ToArray());
    }

    [Fact]
    public void Watch_state_is_per_participant_and_channel_and_independent_of_read_state()
    {
        var watch = WatchSetting.Choose(Watcher, "lounge", WatchMode.Replies, Watcher, Now);
        Assert.Equal(WatchMode.Replies, watch.Mode);
        Assert.Equal(WatchSetting.Key(Watcher, "lounge"), watch.Id);
        Assert.NotEqual(WatchSetting.Key(Watcher, "lounge"), WatchSetting.Key(Author, "lounge"));
        Assert.NotEqual(WatchSetting.Key(Watcher, "lounge"), WatchSetting.Key(Watcher, "workshop"));
        // Read progress and watch preference are separate records with separate keys.
        Assert.NotEqual(WatchSetting.Key(Watcher, "lounge"), ReadPosition.Key(Watcher, "lounge"));
        Assert.Throws<InvalidOperationException>(() => WatchSetting.Choose(Watcher, "lounge", (WatchMode)99, Watcher, Now));
    }

    [Fact]
    public void An_absent_watch_record_means_all_and_never_dereferences_null()
    {
        Assert.Equal(WatchMode.All, AttentionRules.Effective((WatchSetting?)null, null));
        var malformedChannel = WatchSetting.Choose(Watcher, "lounge", WatchMode.None, Watcher, Now);
        malformedChannel.Mode = (WatchMode)99;
        Assert.Equal(WatchMode.All, AttentionRules.Effective(malformedChannel, null));
    }

    [Fact]
    public void A_channel_preference_overrides_the_tangent_default_and_absent_means_all()
    {
        Assert.Equal(WatchMode.None, AttentionRules.Effective((WatchMode?)null, WatchMode.None));
        Assert.Equal(WatchMode.Replies, AttentionRules.Effective(WatchMode.Replies, WatchMode.None));
        // An explicit channel All beats an inherited None.
        Assert.Equal(WatchMode.All, AttentionRules.Effective(WatchMode.All, WatchMode.None));
        Assert.Equal(WatchMode.All, AttentionRules.Effective((WatchMode?)null, null));
        var tangentDefault = TangentWatchSetting.Choose(Watcher, "kintsugi", WatchMode.None, Watcher, Now);
        Assert.Equal(WatchMode.None, AttentionRules.Effective(null, tangentDefault));
        Assert.NotEqual(TangentWatchSetting.Key(Watcher, "kintsugi"), WatchSetting.Key(Watcher, "kintsugi"));
        Assert.Throws<InvalidOperationException>(() => TangentWatchSetting.Choose(Watcher, "kintsugi", (WatchMode)99, Watcher, Now));
    }

    [Fact]
    public void Replies_mode_event_delivery_only_answers_the_participants_own_accepted_messages()
    {
        var reply = new Message { RoomKey = "lounge", AuthorParticipantId = Author, Sequence = 4,
            Content = new("", default, new SourceReference("at://x", "cid")) };
        var parent = new SourceDecision { RoomKey = "lounge", AuthorParticipantId = Watcher, Accepted = true };
        var standalone = new Message { RoomKey = "lounge", AuthorParticipantId = Author, Sequence = 5 };
        var ownReply = new Message { RoomKey = "lounge", AuthorParticipantId = Watcher, Sequence = 6,
            Content = new("", default, new SourceReference("at://y", "cid")) };

        Assert.True(AttentionRules.IsDirectReply(reply, parent, Watcher));
        // Not a reply at all, a reply to someone else's message, an unaccepted parent, or the actor's own message.
        Assert.False(AttentionRules.IsDirectReply(standalone, parent, Watcher));
        Assert.False(AttentionRules.IsDirectReply(reply, new SourceDecision { AuthorParticipantId = Author, Accepted = true }, Watcher));
        Assert.False(AttentionRules.IsDirectReply(reply, new SourceDecision { AuthorParticipantId = Watcher, Accepted = false }, Watcher));
        Assert.False(AttentionRules.IsDirectReply(ownReply, new SourceDecision { AuthorParticipantId = Watcher, Accepted = true }, Watcher));
        Assert.False(AttentionRules.IsDirectReply(null, parent, Watcher));
    }

    [Fact]
    public void Restriction_targets_are_administration_metadata_not_public_activity()
    {
        var entry = new ActivityJournal { Sequence = 4, Kind = ActivityKind.RestrictionChanged, RoomKey = "lounge",
            TangentKey = "home", ActorParticipantId = Author, TargetParticipantId = Watcher };
        // The target learns of their own restriction; a third party does not learn who was restricted.
        Assert.Equal(Watcher, ActivityService.EventFor(Watcher, entry).TargetParticipantId);
        var stranger = "did:plc:eeeeeeeeeeeeeeeeeeeeeeee";
        Assert.Null(ActivityService.EventFor(stranger, entry).TargetParticipantId);
    }
}
