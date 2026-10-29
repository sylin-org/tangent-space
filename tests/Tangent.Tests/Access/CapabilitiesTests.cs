using Koan.Identity.Roles;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Space;
using Tangent.Tangents;
using Xunit;

namespace Tangent.Access;

/// <summary>The capability table, enumerated: Discord's cases on one Tangent, standing, state and authorship, the gate
/// words in their precedence, and the explanation that names the deciding overwrite. Every row is the pure function
/// over values; no Tangent Space runs.</summary>
public sealed class CapabilitiesTests
{
    private const string Workshop = "the-workshop";
    private const string Leo = "leo";
    private const string Ada = "ada";

    private static readonly TangentSpace PublicPlace = new() { Audience = Audience.Public };
    private static readonly TangentSpace PrivatePlace = new() { Audience = Audience.Private };
    private static readonly DateTimeOffset At = new(2026, 10, 29, 10, 0, 0, TimeSpan.Zero);

    /// <summary>A member's bits on a Tangent, where no Post is theirs to edit or remove.</summary>
    private const Bit OnTangent = Roles.MemberBits & ~(Bit.EditOwn | Bit.RemoveOwn);

    private static Participant Participant(string id = Leo, Standing standing = Standing.Admitted) =>
        new() { Id = id, Handle = id + ".example", Standing = standing };

    /// <summary>Koan's bag for an admitted participant with these roles and the bits they confer, with the everyone
    /// role's own View beside it where the place is public.</summary>
    private static List<string> Bag(Bit bits, bool publicPlace = true, params string[] roleIds) =>
        [RoleTokens.Everyone, RoleTokens.Authenticated, .. roleIds, .. Bits.Tokens(bits | (publicPlace ? Bit.View : Bit.None))];

    private static List<string> Member(bool publicPlace = true) => Bag(Roles.MemberBits, publicPlace, Roles.Member);

    /// <summary>A visitor's bag: Koan's anonymous bag with the everyone role's View beside it on a public place.</summary>
    private static List<string> Visitor(bool publicPlace = true) =>
        publicPlace ? [RoleTokens.Everyone, Bits.Token(Bit.View)] : [RoleTokens.Everyone];

    private static Overwrite Over(string subject, Bit allow = Bit.None, Bit deny = Bit.None) => new()
    {
        Id = Overwrite.Key(Workshop, subject),
        TangentId = Workshop,
        SubjectId = subject,
        Allow = [.. Bits.Names(allow)],
        Deny = [.. Bits.Names(deny)]
    };

    private static Post Root(string authorId = Leo, bool locked = false, bool removed = false)
    {
        var root = Post.Root("3jzfcijpj2z2a", Workshop, "A Topic", "a-topic", PostKind.Topic, "Words.", authorId, At);
        root.Locked = locked;
        root.Removed = removed;
        return root;
    }

    /// <summary>Discord's cases for one bit on one Tangent. Each row names the base, the everyone overwrite, the role
    /// overwrite and the participant's own, and expects the bit held or the deciding fact named.</summary>
    [Theory]
    [InlineData("base allow alone", true, false, null, null, null, true, null)]
    [InlineData("everyone deny", true, false, "deny", null, null, false, "everyone denies Topic")]
    [InlineData("everyone deny with a role allow", true, false, "deny", "allow", null, true, null)]
    [InlineData("role deny with the participant's own allow", true, false, null, "deny", "allow", true, null)]
    [InlineData("role allow with the participant's own deny", false, false, null, "allow", "deny", false, "leo denies Topic")]
    [InlineData("everyone allow with no base", false, false, "allow", null, null, true, null)]
    [InlineData("no base and nothing else", false, false, null, null, null, false, "no role grants Topic")]
    [InlineData("Administrator against every deny", false, true, "deny", "deny", "deny", true, null)]
    public void Topic_on_one_tangent_resolves_in_discords_order(string row, bool baseHoldsTopic, bool administrator,
        string? everyone, string? role, string? own, bool held, string? gate)
    {
        var bag = Bag((baseHoldsTopic ? Bit.Topic : Bit.None) | (administrator ? Bit.Administrator : Bit.None), true, Roles.Member);
        var overwrites = new List<Overwrite>();
        if (everyone is not null) overwrites.Add(Over(Overwrite.Everyone, Rule(everyone, Bit.Topic), Rule(everyone, Bit.Topic, deny: true)));
        if (role is not null) overwrites.Add(Over(Roles.Member, Rule(role, Bit.Topic), Rule(role, Bit.Topic, deny: true)));
        if (own is not null) overwrites.Add(Over(Leo, Rule(own, Bit.Topic), Rule(own, Bit.Topic, deny: true)));

        var capabilities = Capabilities.For(Participant(), bag, PublicPlace, overwrites);

        Assert.Equal((row, held, gate), (row, capabilities.Holds(Bit.Topic), capabilities.Gate(Bit.Topic)));

        static Bit Rule(string rule, Bit bit, bool deny = false) => rule == (deny ? "deny" : "allow") ? bit : Bit.None;
    }

    [Fact]
    public void An_administrator_holds_every_bit_and_an_overwrite_grants_administrator_to_no_one()
    {
        var administrator = Capabilities.For(Participant(), Bag(Bit.Administrator, true, Roles.Administrator), PublicPlace,
            [Over(Overwrite.Everyone, deny: Bits.All), Over(Roles.Administrator, deny: Bits.All), Over(Leo, deny: Bits.All)], Root());
        var granted = Capabilities.For(Participant(), Member(), PublicPlace, [Over(Overwrite.Everyone, allow: Bit.Administrator)]);

        Assert.Equal(Bits.All, administrator.Held);
        Assert.Equal(Bits.Names(Bits.All), administrator.Can);
        Assert.False(granted.Holds(Bit.Administrator));
        Assert.False(granted.Holds(Bit.ManageSpace));
    }

    [Fact]
    public void A_role_overwrite_applies_only_to_a_role_in_the_bag_and_the_participants_own_only_to_them()
    {
        var overwrites = new List<Overwrite> { Over(Roles.Moderator, allow: Bit.Moderate), Over(Ada, allow: Bit.ManageTangent) };

        var member = Capabilities.For(Participant(), Member(), PublicPlace, overwrites);
        var moderator = Capabilities.For(Participant(), Bag(Roles.MemberBits, true, Roles.Member, Roles.Moderator), PublicPlace, overwrites);
        var ada = Capabilities.For(Participant(Ada), Member(), PublicPlace, overwrites);

        // On a Tangent no Post is in question, so EditOwn and RemoveOwn are not held there.
        Assert.Equal(OnTangent, member.Held);
        Assert.Equal(OnTangent | Bit.Moderate, moderator.Held);
        Assert.Equal(OnTangent | Bit.ManageTangent, ada.Held);
    }

    [Fact]
    public void Standing_precedes_everything()
    {
        var banned = Capabilities.For(Participant(standing: Standing.Banned), Member(), PublicPlace, [], Root());
        var timedOut = Capabilities.For(Participant(standing: Standing.TimedOut), Member(), PublicPlace, [], Root());
        var appliedPublic = Capabilities.For(Participant(standing: Standing.Applied), Member(), PublicPlace, []);
        var appliedPrivate = Capabilities.For(Participant(standing: Standing.Applied), Member(false), PrivatePlace, []);
        var visitorPublic = Capabilities.For(null, Visitor(), PublicPlace, []);
        var visitorPrivate = Capabilities.For(null, Visitor(false), PrivatePlace, []);
        var visitorWidened = Capabilities.For(null, [.. Visitor(), .. Bits.Tokens(Roles.MemberBits)], PublicPlace, [Over(Overwrite.Everyone, allow: Bits.All)]);

        Assert.Equal((Bit.None, true, Capabilities.Banned), (banned.Held, banned.Absent, banned.Gate(Bit.View)));
        Assert.Equal((Bit.View, false, Capabilities.TimedOut), (timedOut.Held, timedOut.Absent, timedOut.Gate(Bit.Post)));
        Assert.Equal((Bit.View, Capabilities.Apply), (appliedPublic.Held, appliedPublic.Gate(Bit.Post)));
        Assert.Equal((Bit.None, Capabilities.Apply), (appliedPrivate.Held, appliedPrivate.Gate(Bit.View)));
        Assert.Equal((Bit.View, Capabilities.SignIn), (visitorPublic.Held, visitorPublic.Gate(Bit.Post)));
        Assert.Equal((Bit.None, Capabilities.SignIn), (visitorPrivate.Held, visitorPrivate.Gate(Bit.View)));
        Assert.Equal(Bit.View, visitorWidened.Held);
        Assert.Equal(["View"], visitorPublic.Can);
    }

    [Fact]
    public void View_is_the_audience_and_the_prerequisite_of_every_other_bit_on_a_tangent()
    {
        var open = Capabilities.For(null, Visitor(), PublicPlace, [Overwrite.ForAudience(Workshop, Audience.Public)]);
        var closed = Capabilities.For(null, Visitor(), PublicPlace, [Overwrite.ForAudience(Workshop, Audience.Private)]);
        var member = Capabilities.For(Participant(), Member(), PublicPlace, [Overwrite.ForAudience(Workshop, Audience.Private), Over(Roles.Member, allow: Bit.View)]);
        var outside = Capabilities.For(Participant(), Member(), PublicPlace, [Overwrite.ForAudience(Workshop, Audience.Private)]);

        Assert.True(open.Holds(Bit.View));
        Assert.Equal(Capabilities.Denied(Overwrite.Everyone, Bit.View), closed.Gate(Bit.View));
        Assert.Equal(OnTangent, member.Held);
        // Without View nothing else applies: a member who cannot see the Tangent cannot post in it either.
        Assert.Equal((Bit.None, Capabilities.Denied(Overwrite.Everyone, Bit.View)), (outside.Held, outside.Gate(Bit.Topic)));
    }

    [Fact]
    public void Edit_own_and_remove_own_apply_to_my_post_nothing_applies_to_a_removed_post_and_post_is_absent_on_a_locked_topic()
    {
        var mine = Capabilities.For(Participant(), Member(), PublicPlace, [], Root(Leo));
        var another = Capabilities.For(Participant(), Member(), PublicPlace, [], Root(Ada));
        var noPost = Capabilities.For(Participant(), Member(), PublicPlace, []);
        var removed = Capabilities.For(Participant(), Bag(Roles.ModeratorBits, true, Roles.Moderator), PublicPlace, [], Root(Leo, removed: true));
        var locked = Capabilities.For(Participant(), Member(), PublicPlace, [], Root(Leo, locked: true));

        Assert.True(mine.Holds(Bit.EditOwn | Bit.RemoveOwn));
        Assert.Equal((Capabilities.NotYours, Capabilities.NotYours), (another.Gate(Bit.EditOwn), another.Gate(Bit.RemoveOwn)));
        Assert.Equal(OnTangent, another.Held);
        Assert.Equal(Capabilities.NotYours, noPost.Gate(Bit.EditOwn));
        Assert.Equal((Bit.None, false, Capabilities.Removed), (removed.Held, removed.Absent, removed.Gate(Bit.Moderate)));
        Assert.Equal((Roles.MemberBits & ~Bit.Post, Capabilities.Locked), (locked.Held, locked.Gate(Bit.Post)));
    }

    [Fact]
    public void The_gate_word_is_the_first_reason_in_precedence_sign_in_apply_timed_out_locked_removed()
    {
        var lockedRoot = Root(Leo, locked: true);
        var removedRoot = Root(Leo, removed: true);

        var timedOutOnLocked = Capabilities.For(Participant(standing: Standing.TimedOut), Member(), PublicPlace, [], lockedRoot);
        var visitorOnLocked = Capabilities.For(null, Visitor(), PublicPlace, [], lockedRoot);
        var appliedOnLocked = Capabilities.For(Participant(standing: Standing.Applied), Member(false), PrivatePlace, [], lockedRoot);
        var memberOnLocked = Capabilities.For(Participant(), Member(), PublicPlace, [], lockedRoot);
        var visitorOnRemoved = Capabilities.For(null, Visitor(), PublicPlace, [], removedRoot);
        var memberOnRemoved = Capabilities.For(Participant(), Member(), PublicPlace, [], removedRoot);
        var moderatorOnRemoved = Capabilities.For(Participant(), Bag(Roles.ModeratorBits, true, Roles.Moderator), PublicPlace, [], removedRoot);
        var deniedOnLocked = Capabilities.For(Participant(), Member(), PublicPlace, [Over(Overwrite.Everyone, deny: Bit.Post)], lockedRoot);

        Assert.Equal(Capabilities.TimedOut, timedOutOnLocked.Gate(Bit.Post));
        Assert.Equal(Capabilities.SignIn, visitorOnLocked.Gate(Bit.Post));
        Assert.Equal(Capabilities.Apply, appliedOnLocked.Gate(Bit.Post));
        Assert.Equal(Capabilities.Locked, memberOnLocked.Gate(Bit.Post));
        Assert.Equal(Capabilities.Removed, visitorOnRemoved.Gate(Bit.View));
        Assert.Equal(Capabilities.Removed, memberOnRemoved.Gate(Bit.EditOwn));
        Assert.Equal(Capabilities.Removed, moderatorOnRemoved.Gate(Bit.Moderate));
        Assert.Equal(Capabilities.Locked, deniedOnLocked.Gate(Bit.Post));
        Assert.Null(memberOnLocked.Gate(Bit.View));
    }

    [Fact]
    public void For_a_verb_the_bag_denies_the_answer_names_the_deciding_role_or_overwrite()
    {
        var everyone = Capabilities.For(Participant(), Member(), PublicPlace, [Over(Overwrite.Everyone, deny: Bit.Topic)]);
        var role = Capabilities.For(Participant(), Bag(Roles.MemberBits, true, Roles.Member, Roles.Moderator), PublicPlace,
            [Over(Overwrite.Everyone, allow: Bit.Post), Over(Roles.Moderator, deny: Bit.Post)]);
        var own = Capabilities.For(Participant(), Member(), PublicPlace, [Over(Roles.Member, allow: Bit.Post), Over(Leo, deny: Bit.Post)]);
        var noRole = Capabilities.For(Participant(), Member(), PublicPlace, []);
        var overruled = Capabilities.For(Participant(), Member(), PublicPlace, [Over(Overwrite.Everyone, deny: Bit.Topic), Over(Leo, allow: Bit.Topic)]);

        Assert.Equal("everyone denies Topic", everyone.Gate(Bit.Topic));
        Assert.Equal("role:moderator denies Post", role.Gate(Bit.Post));
        Assert.Equal("leo denies Post", own.Gate(Bit.Post));
        Assert.Equal("no role grants ManageSpace", noRole.Gate(Bit.ManageSpace));
        Assert.Null(overruled.Gate(Bit.Topic));
        Assert.Throws<ArgumentException>(() => noRole.Gate(Bit.Post | Bit.Topic));
    }
}
