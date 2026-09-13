using TangentSpace.Authorization;
using TangentSpace.Rooms;
using Xunit;

namespace TangentSpace.Tests;

public sealed class TopicPermissionEvaluatorTests
{
    private const string Author = "did:plc:author";
    private const string Other = "did:plc:other";
    private const long Revision = 42;

    private static RoomPolicy Policy(
        bool canRead = true, bool canWrite = true, bool canManage = false, bool canAppointManagers = false,
        bool editingAllowed = true, bool locked = false, string reason = "allowed",
        string? actor = Author, RoomRole? role = RoomRole.Member, long revision = Revision) =>
        new("workshop", actor, revision, 3, RoomAdmission.SignedIn, RoomSpaceState.Ready, null,
            role, canAppointManagers, canRead, canWrite, canManage, canAppointManagers, reason, editingAllowed, locked);

    private static TopicPermissionDecision Evaluate(RoomPolicy policy, TopicCapability capability)
        => TopicPermissionEvaluator.Evaluate(policy, capability);

    private static TopicPermissionDecision EvaluatePost(RoomPolicy policy, TopicCapability capability,
        string? author = Author, bool removed = false)
        => TopicPermissionEvaluator.EvaluatePost(policy, capability, author, removed);

    public static TheoryData<bool, bool, bool, bool> Roles => new()
    {
        { true, false, false, false },
        { true, true, false, false },
        { true, true, true, false },
        { true, true, true, true }
    };

    [Theory]
    [MemberData(nameof(Roles))]
    public void Topic_decisions_follow_the_current_permission_projection(bool canRead, bool canWrite, bool canManage, bool canAppoint)
    {
        var policy = Policy(canRead: canRead, canWrite: canWrite, canManage: canManage, canAppointManagers: canAppoint,
            role: canAppoint ? null : canManage ? RoomRole.Manager : canWrite ? RoomRole.Member : RoomRole.Reader);
        Assert.Equal(canRead, Evaluate(policy, TopicCapability.Read).Allowed);
        Assert.Equal(canWrite, Evaluate(policy, TopicCapability.Reply).Allowed);
        Assert.Equal(canManage, Evaluate(policy, TopicCapability.ManageTopic).Allowed);
        Assert.Equal(canManage, Evaluate(policy, TopicCapability.ManageParticipants).Allowed);
        Assert.Equal(canAppoint, Evaluate(policy, TopicCapability.AppointManagers).Allowed);
        Assert.Equal(canWrite, Evaluate(policy, TopicCapability.EditOwnPost).Allowed);
        Assert.Equal(canWrite, Evaluate(policy, TopicCapability.DeleteOwnPost).Allowed);
        Assert.Equal(canManage && canRead, Evaluate(policy, TopicCapability.RemovePost).Allowed);
    }

    public static TheoryData<RoomPolicy> ConsistentPolicies => new()
    {
        Policy(canWrite: false, role: RoomRole.Reader),
        Policy(role: RoomRole.Member),
        Policy(canManage: true, role: RoomRole.Manager),
        Policy(canAppointManagers: true, role: null),
        Policy(locked: true),
        Policy(editingAllowed: false),
        Policy(canManage: true, editingAllowed: false, locked: true, role: RoomRole.Manager),
        Policy(canRead: false, canWrite: false, reason: "invitation-required"),
        Policy(canManage: true, canRead: false, canWrite: false, reason: "space-pending", role: RoomRole.Manager)
    };

    [Theory]
    [MemberData(nameof(ConsistentPolicies))]
    public void Topic_decisions_agree_with_the_existing_permissions_baseline(RoomPolicy policy)
    {
        var baseline = Permissions.Topic(policy).AllowedActions.ToHashSet();
        foreach (var (capability, action) in CapabilityActions())
            Assert.Equal(baseline.Contains(action), Evaluate(policy, capability).Allowed);
    }

    [Theory]
    [MemberData(nameof(ConsistentPolicies))]
    public void Post_decisions_agree_with_the_existing_permissions_baseline(RoomPolicy policy)
    {
        foreach (var author in new[] { Author, Other })
            foreach (var removed in new[] { false, true })
            {
                var baseline = Permissions.Post(policy, author, removed).AllowedActions.ToHashSet();
                foreach (var (capability, action) in CapabilityActions())
                    Assert.Equal(baseline.Contains(action), EvaluatePost(policy, capability, author, removed).Allowed);
            }
    }

    private static IEnumerable<(TopicCapability Capability, string Action)> CapabilityActions() => new[]
    {
        (TopicCapability.Read, "read"),
        (TopicCapability.Reply, "reply"),
        (TopicCapability.ManageTopic, "manageTopic"),
        (TopicCapability.ManageParticipants, "manageParticipants"),
        (TopicCapability.EditOwnPost, "editOwnPost"),
        (TopicCapability.DeleteOwnPost, "deleteOwnPost"),
        (TopicCapability.RemovePost, "removePost")
    };

    public static TheoryData<RoomPolicy, TopicCapability, string> DeniedReasons => new()
    {
        { Policy(locked: true), TopicCapability.Reply, TopicPermissionEvaluator.TopicLockedReason },
        { Policy(locked: true), TopicCapability.DeleteOwnPost, TopicPermissionEvaluator.TopicLockedReason },
        { Policy(editingAllowed: false), TopicCapability.EditOwnPost, TopicPermissionEvaluator.EditingDisabledReason },
        { Policy(), TopicCapability.ManageTopic, TopicPermissionEvaluator.ManageRequiredReason },
        { Policy(canManage: true, role: RoomRole.Manager), TopicCapability.AppointManagers, TopicPermissionEvaluator.OwnerRequiredReason },
        { Policy(canWrite: false, role: RoomRole.Reader), TopicCapability.Reply, TopicPermissionEvaluator.PermissionDeniedReason },
        { Policy(canRead: false, canWrite: false, reason: "invitation-required"), TopicCapability.Read, "invitation-required" },
        { Policy(canRead: false, canWrite: false, reason: "space-pending"), TopicCapability.Read, "space-pending" },
        { Policy(canManage: true, canRead: false, canWrite: false, reason: "space-pending", role: RoomRole.Manager), TopicCapability.RemovePost, "space-pending" },
        { Policy(), TopicCapability.RemovePost, TopicPermissionEvaluator.ManageRequiredReason },
        { Policy(), TopicCapability.ManageParticipants, TopicPermissionEvaluator.ManageRequiredReason }
    };

    [Theory]
    [MemberData(nameof(DeniedReasons))]
    public void Denied_topic_decisions_carry_stable_reasons(RoomPolicy policy, TopicCapability capability, string reason)
    {
        var decision = Evaluate(policy, capability);
        Assert.False(decision.Allowed);
        Assert.Equal(reason, decision.Reason);
        Assert.NotEqual(TopicPermissionEvaluator.AllowedReason, decision.Reason);
    }

    [Fact]
    public void Locked_and_editing_disabled_topics_keep_reading_and_management()
    {
        var policy = Policy(canManage: true, editingAllowed: false, locked: true, role: RoomRole.Manager);
        Assert.True(Evaluate(policy, TopicCapability.Read).Allowed);
        Assert.True(Evaluate(policy, TopicCapability.ManageTopic).Allowed);
        Assert.True(Evaluate(policy, TopicCapability.RemovePost).Allowed);
        Assert.False(Evaluate(policy, TopicCapability.EditOwnPost).Allowed);
        Assert.Equal(TopicPermissionEvaluator.TopicLockedReason, Evaluate(policy, TopicCapability.Reply).Reason);
        var editingEnabled = Policy(canManage: true, editingAllowed: true, locked: false, role: RoomRole.Manager);
        Assert.True(Evaluate(editingEnabled, TopicCapability.EditOwnPost).Allowed);
        Assert.True(Evaluate(Policy(editingAllowed: false), TopicCapability.DeleteOwnPost).Allowed);
        Assert.True(EvaluatePost(Policy(editingAllowed: false), TopicCapability.DeleteOwnPost, Author).Allowed);
        Assert.False(EvaluatePost(Policy(editingAllowed: false, locked: true), TopicCapability.EditOwnPost, Author).Allowed);
    }

    [Fact]
    public void Effective_writes_require_read_even_on_inconsistent_policies()
    {
        var synthetic = Policy(canRead: false, canWrite: true, reason: "allowed");
        Assert.False(Evaluate(synthetic, TopicCapability.Reply).Allowed);
        Assert.False(Evaluate(synthetic, TopicCapability.EditOwnPost).Allowed);
        Assert.False(Evaluate(synthetic, TopicCapability.DeleteOwnPost).Allowed);
        Assert.Equal(TopicPermissionEvaluator.PermissionDeniedReason, Evaluate(synthetic, TopicCapability.Reply).Reason);
        Assert.False(EvaluatePost(synthetic, TopicCapability.EditOwnPost, Author).Allowed);
        var suspended = Policy(canRead: false, canWrite: true, reason: "suspended");
        Assert.Equal("suspended", Evaluate(suspended, TopicCapability.Reply).Reason);
    }

    [Fact]
    public void Management_survives_without_read_but_post_removal_still_needs_it()
    {
        var pending = Policy(canRead: false, canWrite: false, canManage: true, reason: "space-pending", role: RoomRole.Manager);
        Assert.True(Evaluate(pending, TopicCapability.ManageTopic).Allowed);
        Assert.True(Evaluate(pending, TopicCapability.ManageParticipants).Allowed);
        Assert.False(Evaluate(pending, TopicCapability.RemovePost).Allowed);
        Assert.False(EvaluatePost(pending, TopicCapability.RemovePost, Other).Allowed);
    }

    [Fact]
    public void Post_read_follows_the_topic_and_own_actions_require_the_matching_author()
    {
        var member = Policy();
        Assert.True(EvaluatePost(member, TopicCapability.Read, Other).Allowed);
        Assert.True(EvaluatePost(member, TopicCapability.EditOwnPost, Author).Allowed);
        Assert.True(EvaluatePost(member, TopicCapability.DeleteOwnPost, Author).Allowed);
        Assert.False(EvaluatePost(member, TopicCapability.RemovePost, Author).Allowed);
        Assert.Equal(TopicPermissionEvaluator.OwnPostRemovalReason, EvaluatePost(member, TopicCapability.RemovePost, Author).Reason);

        Assert.False(EvaluatePost(member, TopicCapability.EditOwnPost, Other).Allowed);
        Assert.False(EvaluatePost(member, TopicCapability.DeleteOwnPost, Other).Allowed);
        Assert.Equal(TopicPermissionEvaluator.NotAuthorReason, EvaluatePost(member, TopicCapability.EditOwnPost, Other).Reason);
        Assert.Equal(TopicPermissionEvaluator.NotAuthorReason, EvaluatePost(member, TopicCapability.DeleteOwnPost, Other).Reason);

        var manager = Policy(canManage: true, role: RoomRole.Manager);
        Assert.True(EvaluatePost(manager, TopicCapability.RemovePost, Other).Allowed);
        Assert.False(EvaluatePost(manager, TopicCapability.EditOwnPost, Other).Allowed);
        Assert.True(EvaluatePost(manager, TopicCapability.DeleteOwnPost, Author).Allowed);
    }

    [Fact]
    public void Removed_posts_can_be_read_but_not_edited_deleted_or_removed()
    {
        var manager = Policy(canManage: true, role: RoomRole.Manager);
        Assert.True(EvaluatePost(manager, TopicCapability.Read, Author, removed: true).Allowed);
        foreach (var capability in new[] { TopicCapability.EditOwnPost, TopicCapability.DeleteOwnPost, TopicCapability.RemovePost })
        {
            var decision = EvaluatePost(manager, capability, capability == TopicCapability.RemovePost ? Other : Author, removed: true);
            Assert.False(decision.Allowed);
            Assert.Equal(TopicPermissionEvaluator.RemovedReason, decision.Reason);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_or_null_actors_never_count_as_authors(string? actor)
    {
        var policy = Policy(actor: actor, canManage: true, role: RoomRole.Manager);
        Assert.False(EvaluatePost(policy, TopicCapability.EditOwnPost, actor).Allowed);
        Assert.Equal(TopicPermissionEvaluator.NotAuthorReason, EvaluatePost(policy, TopicCapability.DeleteOwnPost, actor).Reason);
        Assert.True(EvaluatePost(policy, TopicCapability.RemovePost, actor).Allowed);
        var missingAuthor = Policy(actor: Author);
        Assert.False(EvaluatePost(missingAuthor, TopicCapability.EditOwnPost, null).Allowed);
        Assert.False(EvaluatePost(missingAuthor, TopicCapability.EditOwnPost, "").Allowed);
    }

    [Fact]
    public void Unknown_capability_values_fail_closed_at_both_levels()
    {
        var unknown = (TopicCapability)999;
        var topic = Evaluate(Policy(), unknown);
        Assert.False(topic.Allowed);
        Assert.Equal(TopicPermissionEvaluator.UnsupportedActionReason, topic.Reason);
        var post = EvaluatePost(Policy(), unknown, Author);
        Assert.False(post.Allowed);
        Assert.Equal(TopicPermissionEvaluator.UnsupportedActionReason, post.Reason);
        Assert.Equal(Revision, post.PolicyRevision);
    }

    [Fact]
    public void Non_post_capabilities_are_unsupported_at_post_level()
    {
        var owner = Policy(canManage: true, canAppointManagers: true, role: null);
        foreach (var capability in new[] { TopicCapability.Reply, TopicCapability.ManageTopic,
            TopicCapability.ManageParticipants, TopicCapability.AppointManagers })
        {
            var decision = EvaluatePost(owner, capability, Author);
            Assert.False(decision.Allowed);
            Assert.Equal(TopicPermissionEvaluator.UnsupportedActionReason, decision.Reason);
            Assert.True(Evaluate(owner, capability).Allowed);
        }
    }

    [Fact]
    public void Every_decision_carries_the_selected_policy_revision()
    {
        const long revision = 1234;
        var policy = Policy(revision: revision, role: RoomRole.Reader);
        Assert.Equal(revision, Evaluate(policy, TopicCapability.Read).PolicyRevision);
        Assert.Equal(revision, Evaluate(policy, TopicCapability.ManageTopic).PolicyRevision);
        Assert.Equal(revision, EvaluatePost(policy, TopicCapability.DeleteOwnPost, Other).PolicyRevision);
        Assert.Equal(revision, Evaluate(Policy(revision: revision, canRead: false, reason: "suspended"), TopicCapability.Read).PolicyRevision);
        Assert.Equal(revision, Evaluate(Policy(revision: revision), (TopicCapability)999).PolicyRevision);
        Assert.Equal(revision, EvaluatePost(Policy(revision: revision), TopicCapability.Reply, Author).PolicyRevision);
    }

    [Fact]
    public void A_null_policy_fails_closed_without_throwing()
    {
        var topic = TopicPermissionEvaluator.Evaluate(null, TopicCapability.Reply);
        Assert.False(topic.Allowed);
        Assert.Equal(TopicPermissionEvaluator.PermissionDeniedReason, topic.Reason);
        Assert.Equal(0, topic.PolicyRevision);
        var post = TopicPermissionEvaluator.EvaluatePost(null, TopicCapability.Read, Author, false);
        Assert.False(post.Allowed);
        Assert.Equal(0, post.PolicyRevision);
    }

    [Fact]
    public void Allowed_decisions_use_the_allowed_reason()
    {
        foreach (var decision in new[]
        {
            Evaluate(Policy(), TopicCapability.Read),
            Evaluate(Policy(), TopicCapability.EditOwnPost),
            Evaluate(Policy(canManage: true, role: RoomRole.Manager), TopicCapability.ManageParticipants),
            EvaluatePost(Policy(), TopicCapability.DeleteOwnPost, Author)
        })
        {
            Assert.True(decision.Allowed);
            Assert.Equal(TopicPermissionEvaluator.AllowedReason, decision.Reason);
        }
    }
}
