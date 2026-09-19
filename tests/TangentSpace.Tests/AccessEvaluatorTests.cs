using Tangent.Access;
using Tangent.Community;
using Tangent.Identity;
using Tangent.Stewardship;
using Xunit;

namespace Tangent.Tests;

/// <summary>Table-driven tests over the one evaluator (ADR 0013): the category table decides what
/// an absent decision means, the scope chain narrows, the bag grants, and restrictions and
/// ownership bound. Inputs are plain rows — no host, no database — because the evaluator is pure.
/// The bags carry the derived tokens a real Koan bag always holds: everyone, plus authenticated.</summary>
public sealed class AccessEvaluatorTests
{
    private static readonly string[] SignedOut = ["everyone"];
    private static readonly string[] SignedIn = ["everyone", "authenticated"];

    private static Topic Topic(string creator = "creator-1", bool locked = false, bool editing = false) => new()
    {
        Id = "topic-1", TangentKey = "tangent-1", CreatorParticipantId = creator,
        Access = AccessMap.TopicDefaults(), IsLocked = locked, AllowPostEditing = editing,
    };

    private static Community.Tangent Tangent(AccessMap? access = null) => new()
    {
        Id = "tangent-1", Access = access ?? AccessMap.TangentDefaults(),
        ParticipationPreset = ParticipationPreset.Everyone,
    };

    /// <summary>The topic's own state rides the draft: editing and locked are facts of the row,
    /// not of the actor.</summary>
    private static TopicPolicy Draft(Topic topic, string? actor, bool isOwner = false) => new(
        topic.Id, actor, SelectedPolicyRevision: 3, SpacePolicyRevision: 1, TopicAdmission.SignedIn,
        Role: null, IsOwner: isOwner, CanRead: false, CanWrite: false, CanManage: false,
        Reason: "", EditingAllowed: topic.AllowPostEditing, Locked: topic.IsLocked);

    private static TopicPolicy Decide(Topic topic, Community.Tangent? tangent, string[] bag,
        bool suspended = false, EffectiveRestriction? restriction = null,
        ParticipantClassification classification = ParticipantClassification.Human, bool isOwner = false)
        => AccessEvaluator.Decide(topic, tangent, Draft(topic, isOwner ? "owner-actor" : "actor-1", isOwner),
            bag, suspended, restriction, classification);

    [Fact]
    public void The_default_tangent_reads_to_everyone_and_writes_to_nobody_but_authority()
    {
        var tangent = Tangent();
        var signedOut = Decide(Topic(), tangent, SignedOut);
        Assert.True(signedOut.CanRead, "reading is the default: an absent See decision is everyone's");
        Assert.False(signedOut.CanWrite);

        var roleless = Decide(Topic(), tangent, SignedIn);
        Assert.True(roleless.CanRead, "a signed-in participant never sees less than a signed-out one");
        Assert.False(roleless.CanWrite, "posting is not the default");

        var poster = Decide(Topic(), tangent, [.. SignedIn, "global:post_create"]);
        Assert.True(poster.CanWrite, "the category's always-on permission posts anywhere");
    }

    [Fact]
    public void A_named_role_in_the_list_changes_what_its_holder_may_do()
    {
        var tangent = Tangent(new AccessMap { See = ["role:member"], Post = ["role:member"], Manage = ["role:steward"] });
        var member = Decide(Topic(), tangent, [.. SignedIn, "role:member"]);
        Assert.True(member.CanRead);
        Assert.True(member.CanWrite);
        Assert.False(member.CanManage);

        var steward = Decide(Topic(), tangent, [.. SignedIn, "role:steward"]);
        Assert.True(steward.CanManage, "the map grants manage to the role it names");
        Assert.False(steward.CanWrite, "the lists grant independently: manage is not post");
    }

    [Fact]
    public void The_child_narrows_and_never_widens()
    {
        var tangent = Tangent(new AccessMap { Post = ["role:member"], Manage = ["role:steward"] });
        var openTopic = Topic();
        openTopic.Access = new AccessMap { Post = ["everyone"], Manage = [] };
        var outsider = Decide(openTopic, tangent, SignedOut);
        Assert.False(outsider.CanWrite, "a topic's everyone cannot widen a members-only Tangent");
        Assert.False(outsider.CanManage, "an empty topic manage narrows to the always-on permission");
    }

    [Fact]
    public void An_empty_decision_denies_everyone_except_the_always_on_permission()
    {
        var tangent = Tangent(new AccessMap { See = [], Post = [] });
        var nobody = Decide(Topic(), tangent, [.. SignedIn, "role:member"]);
        Assert.False(nobody.CanRead, "an explicitly empty See is not everyone's");
        Assert.False(nobody.CanWrite);
        var seer = Decide(Topic(), tangent, ["global:topic_read"]);
        Assert.True(seer.CanRead, "the always-on permission reads through an empty decision");
    }

    [Fact]
    public void Ownership_is_derived_and_manages_without_a_list()
    {
        var tangent = Tangent(new AccessMap { Manage = [] });
        var owner = Decide(Topic(), tangent, SignedOut, isOwner: true);
        Assert.True(owner.CanManage, "the Space's owner manages without any list naming them");
        Assert.True(owner.CanRead);

        var creatorTopic = Topic(creator: "actor-1");
        var creator = Decide(creatorTopic, tangent, SignedOut);
        Assert.True(creator.CanManage, "the topic's author manages their own topic");
    }

    [Fact]
    public void Restrictions_bound_every_grant_and_ownership_survives_them()
    {
        var tangent = Tangent(new AccessMap { See = ["role:member"], Post = ["role:member"], Manage = ["role:member"] });
        var bag = new[] { "everyone", "role:member" };
        var ownerBag = new[] { "everyone", "role:member" };

        var bannedOwner = Decide(Topic(), tangent, ownerBag, restriction: new EffectiveRestriction(Banned: true), isOwner: true);
        Assert.True(bannedOwner.CanRead, "ownership survives a ban");
        var bannedMember = Decide(Topic(), tangent, bag, restriction: new EffectiveRestriction(Banned: true));
        Assert.False(bannedMember.CanRead);
        Assert.Equal("banned", bannedMember.Reason);

        var timedOut = Decide(Topic(), tangent, bag, restriction: new EffectiveRestriction(Banned: false));
        Assert.True(timedOut.CanRead, "a timeout keeps reading available");
        Assert.False(timedOut.CanWrite);
        Assert.False(timedOut.CanManage);

        var suspended = Decide(Topic(), tangent, bag, suspended: true);
        Assert.False(suspended.CanRead);
        Assert.Equal("suspended", suspended.Reason);
    }

    [Fact]
    public void Topic_state_and_authorship_decode_the_post_capabilities()
    {
        var tangent = Tangent();
        var editable = Topic(editing: true);
        var authorDraft = Draft(editable, "author-1", isOwner: true);
        var draft = AccessEvaluator.Decide(editable, tangent, authorDraft, [.. SignedIn, "global:post_create"],
            suspended: false, restriction: null, ParticipantClassification.Human);

        Assert.True(AccessEvaluator.EvaluatePost(draft, TopicCapability.EditOwnPost, "author-1", removed: false).Allowed);
        Assert.Equal(AccessEvaluator.NotAuthorReason,
            AccessEvaluator.EvaluatePost(draft, TopicCapability.EditOwnPost, "someone-else", removed: false).Reason);
        Assert.Equal(AccessEvaluator.RemovedReason,
            AccessEvaluator.EvaluatePost(draft, TopicCapability.DeleteOwnPost, "author-1", removed: true).Reason);
        Assert.Equal(AccessEvaluator.OwnPostRemovalReason,
            AccessEvaluator.EvaluatePost(draft, TopicCapability.RemovePost, "author-1", removed: false).Reason);

        var lockedTopic = Topic(locked: true);
        var lockedDraft = AccessEvaluator.Decide(lockedTopic, tangent, Draft(lockedTopic, "author-1", isOwner: true),
            [.. SignedIn, "global:post_create"], suspended: false, restriction: null, ParticipantClassification.Human);
        Assert.Equal(AccessEvaluator.TopicLockedReason,
            AccessEvaluator.Evaluate(lockedDraft, TopicCapability.Reply).Reason);

        var closed = Topic();
        var closedDraft = AccessEvaluator.Decide(closed, tangent, Draft(closed, "author-1", isOwner: true),
            [.. SignedIn, "global:post_create"], suspended: false, restriction: null, ParticipantClassification.Human);
        Assert.True(
            AccessEvaluator.EvaluatePost(closedDraft, TopicCapability.EditOwnPost, "author-1", removed: false).Reason
                == AccessEvaluator.EditingDisabledReason,
            "an edit needs the Topic's editing permission, which the owner's authority does not replace");
    }

    [Fact]
    public void The_derived_roles_match_without_storage()
    {
        var tangent = Tangent(new AccessMap
        {
            See = ["role:participant"],
            Post = ["role:member"],
            Manage = ["role:everyone"],
        });
        // role:participant matches the sign-in fact; nobody is a member of anything.
        var anySignIn = Decide(Topic(), tangent, [.. SignedIn]);
        Assert.True(anySignIn.CanRead, "role:participant is derived from being signed in");
        Assert.False(anySignIn.CanWrite, "role:member is stored membership, not sign-in");

        // role:everyone matches the signed-out visitor too.
        var anyone = Decide(Topic(), tangent, SignedOut);
        Assert.True(anyone.CanManage, "role:everyone holds even for a signed-out visitor");

        // The bare tokens the framework mints keep matching.
        var bare = Decide(Topic(), Tangent(new AccessMap { See = ["role:everyone"] }), SignedOut);
        Assert.True(bare.CanRead);
    }

    [Fact]
    public void A_missing_tangent_or_space_admits_no_one()
    {
        var orphanTopic = Topic();
        var orphan = AccessEvaluator.Decide(orphanTopic, null, Draft(orphanTopic, "owner-1", isOwner: true),
            ["global:host_manage"], suspended: false, restriction: null, ParticipantClassification.Human);
        Assert.False(orphan.CanRead);
        Assert.Equal("tangent-not-found", orphan.Reason);

        var spacelessTopic = Topic();
        var draft = Draft(spacelessTopic, "p1", isOwner: true) with { SpacePolicyRevision = 0 };
        var noSpace = AccessEvaluator.Decide(spacelessTopic, Tangent(), draft,
            [.. SignedIn, "role:member"], suspended: false, restriction: null, ParticipantClassification.Human);
        Assert.False(noSpace.CanRead);
        Assert.Equal("site-unavailable", noSpace.Reason);
    }
}
