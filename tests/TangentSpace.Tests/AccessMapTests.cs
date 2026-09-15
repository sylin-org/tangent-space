using TangentSpace.Authorization;
using TangentSpace.Communities;
using TangentSpace.Rooms;
using TangentSpace.Site;
using Xunit;

namespace TangentSpace.Tests;

public sealed class AccessMapTests
{
    private const string OwnerDid = "did:plc:aaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly string Owner = TangentSpace.Participants.Participant.NewIdentifier();
    private static readonly string Other = TangentSpace.Participants.Participant.NewIdentifier();
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Criteria_normalize_to_stable_distinct_tokens_and_reject_policy_syntax()
    {
        Assert.Equal(["role:gardeners", "global:post_create"],
            AccessCriteria.Normalize([" Role:Gardeners ", "role:gardeners", "GLOBAL:POST_CREATE"]));
        Assert.Throws<ArgumentException>(() => AccessCriteria.Normalize(["subject:alice"]));
        Assert.Throws<ArgumentException>(() => AccessCriteria.Normalize(["role:*"]));
    }

    [Fact]
    public void Topic_inheritance_is_resolved_before_matching_and_global_authority_is_automatic()
    {
        var tangent = new AccessMap
        {
            See = ["role:member"],
            Post = ["role:contributors"],
            Manage = ["role:stewards"],
            CreateTopics = ["role:member"]
        };
        var topic = new AccessMap { See = ["role:secret_club"] };

        var effective = topic.ResolveTopic(tangent);

        Assert.Equal(["role:secret_club", TangentPermissions.ReadTopics], effective.See);
        Assert.Equal(["role:contributors", TangentPermissions.CreatePosts], effective.Post);
        Assert.Equal(["role:stewards", TangentPermissions.ManageTopics], effective.Manage);
    }

    [Fact]
    public void An_explicit_empty_topic_decision_does_not_inherit()
    {
        var effective = new AccessMap { Post = [] }.ResolveTopic(new AccessMap { Post = ["role:member"] });

        Assert.Equal([TangentPermissions.CreatePosts], effective.Post);
    }

    [Fact]
    public void Each_scope_exposes_only_its_meaningful_decisions()
    {
        var server = new AccessMap { See = ["everyone"], CreateTangents = ["role:builders"], Manage = [] }.ResolveServer();
        var tangent = new AccessMap { See = ["role:member"], Post = ["role:member"], CreateTopics = ["role:authors"], Manage = [] }.ResolveTangent();

        Assert.Contains(TangentPermissions.ReadHost, server.See!);
        Assert.Contains(TangentPermissions.CreateTangents, server.CreateTangents!);
        Assert.Contains(TangentPermissions.CreateTopics, tangent.CreateTopics!);
        Assert.Throws<ArgumentException>(() => new AccessMap { CreateTangents = [] }.NormalizeForTangent());
        Assert.Throws<ArgumentException>(() => new AccessMap { CreateTopics = [] }.NormalizeForTopic());
    }

    [Fact]
    public void Access_changes_are_owned_domain_mutations_with_policy_revisions()
    {
        var site = Site();
        var tangent = TangentCommunity.Create(site, Owner, "garden", "Garden", "", "", "", "", Now);
        var room = Room.Create(site, Owner, "garden-notes", "Notes", RoomAdmission.InvitationOnly, Now,
            tangent);

        Assert.Throws<UnauthorizedAccessException>(() => site.ChangeAccess(Other, AccessMap.ServerDefaults()));
        Assert.Equal(TangentDenial.Forbidden, Assert.Throws<TangentRuleViolation>(() =>
            tangent.ChangeAccess(Other, AccessMap.TangentDefaults(), Now)).Denial);
        Assert.Equal(RoomDenial.Forbidden, Assert.Throws<RoomRuleViolation>(() =>
            room.ChangeAccess(site, Other, null, AccessMap.TopicDefaults(), Now, tangent)).Denial);

        var tangentRevision = tangent.PolicyRevision;
        tangent.ChangeAccess(Owner, new AccessMap
        {
            See = ["role:member"], Post = ["role:writers"], Manage = [], CreateTopics = ["role:writers"]
        }, Now);
        Assert.Equal(tangentRevision + 1, tangent.PolicyRevision);

        var roomRevision = room.PolicyRevision;
        room.ChangeAccess(site, Owner, null, new AccessMap { See = ["role:admins"] }, Now, tangent);
        Assert.Equal(roomRevision + 1, room.PolicyRevision);
        Assert.Equal(["role:admins"], room.Access.See);
        Assert.Null(room.Access.Post);
    }

    private static TangentSite Site()
        => TangentSite.Establish(new SiteOptions { Name = "Host", OwnerDid = OwnerDid }, OwnerDid, Owner, Now);
}
