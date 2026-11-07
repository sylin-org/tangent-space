using Koan.Data.Core.Commands;
using Koan.Identity.Roles;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Space;
using Tangent.Tangents;
using Xunit;

namespace Tangent.Access;

/// <summary>The capability table, enumerated: Discord's cases on one Tangent, standing, what a reader may not View,
/// state and authorship, the gate words in their precedence, and the explanation that names the deciding overwrite.
/// Every row is the pure function over values, Koan's overwrite rows among them; no Tangent Space runs.</summary>
public sealed class CapabilitiesTests
{
    private const string Workshop = "the-workshop";
    private const string Leo = "leo";
    private const string Ada = "ada";

    private static readonly TangentSpace PublicPlace = new() { Audience = Audience.Public };
    private static readonly TangentSpace PrivatePlace = new() { Audience = Audience.Private };
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 10, 0, 0, TimeSpan.Zero);

    /// <summary>A member's bits on a Tangent, where no Post is theirs to edit or remove.</summary>
    /// <summary>What a member holds on a Tangent: authorship is asked only of a Post.</summary>
    private const Bit OnTangent = Roles.MemberBits;

    /// <summary>What a member holds on someone else's Post.</summary>
    private const Bit OnAnothers = Roles.MemberBits & ~(Bit.EditOwn | Bit.RemoveOwn);

    /// <summary>A visitor's bag: Koan's anonymous bag, which holds everyone alone.</summary>
    private static readonly List<string> Visitor = [RoleTokens.Everyone];

    private static Participant Participant(string id = Leo, Standing standing = Standing.Admitted, DateTimeOffset? until = null) =>
        new() { Id = id, Handle = id + ".example", Standing = standing, StandingUntil = until };

    /// <summary>Koan's bag for a signed-in participant with these roles and the bits they confer.</summary>
    private static List<string> Bag(Bit bits, params string[] roleIds) =>
        [RoleTokens.Everyone, RoleTokens.Authenticated, .. roleIds, .. Bits.Tokens(bits)];

    private static List<string> Member() => Bag(Roles.MemberBits, Roles.Member);

    private static List<string> Moderator() => Bag(Roles.ModeratorBits, Roles.Member, Roles.Moderator);

    /// <summary>Koan's overwrite on the Workshop for a role — everyone among them — or a participant.</summary>
    private static PermissionOverwrite Over(string subject, Bit allow = Bit.None, Bit deny = Bit.None)
    {
        var kind = subject == RoleTokens.Everyone || subject.StartsWith(RoleTokens.RolePrefix, StringComparison.Ordinal)
            ? OverwriteSubjectKind.Role
            : OverwriteSubjectKind.Person;
        return new PermissionOverwrite
        {
            Id = PermissionOverwrite.KeyFor(Overwrites.Resource(Workshop), kind, subject),
            ResourceId = Overwrites.Resource(Workshop),
            SubjectKind = kind,
            SubjectId = subject,
            Allow = [.. Bits.Tokens(allow)],
            Deny = [.. Bits.Tokens(deny)]
        };
    }

    /// <summary>The everyone overwrite that gives the Workshop this audience.</summary>
    private static PermissionOverwrite ForAudience(Audience audience) =>
        audience == Audience.Public ? Over(RoleTokens.Everyone, allow: Bit.View) : Over(RoleTokens.Everyone, deny: Bit.View);

    private static Post Root(string authorId = Leo, bool removed = false)
    {
        var root = Given.ARoot("3jzfcijpj2z2a", Workshop, "A Topic", "a-topic", PostKind.Topic, "Words.", authorId, Now);
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
        var bag = Bag((baseHoldsTopic ? Bit.Topic : Bit.None) | (administrator ? Bit.Administrator : Bit.None), Roles.Member);
        var overwrites = new List<PermissionOverwrite>();
        if (everyone is not null) overwrites.Add(Over(RoleTokens.Everyone, Rule(everyone, Bit.Topic), Rule(everyone, Bit.Topic, deny: true)));
        if (role is not null) overwrites.Add(Over(Roles.Member, Rule(role, Bit.Topic), Rule(role, Bit.Topic, deny: true)));
        if (own is not null) overwrites.Add(Over(Leo, Rule(own, Bit.Topic), Rule(own, Bit.Topic, deny: true)));

        var capabilities = Capabilities.For(Participant(), bag, PublicPlace, Workshop, overwrites, Now);

        Assert.Equal((row, held, gate), (row, capabilities.Holds(Bit.Topic), capabilities.Gate(Bit.Topic)?.Word));

        static Bit Rule(string rule, Bit bit, bool deny = false) => rule == (deny ? "deny" : "allow") ? bit : Bit.None;
    }

    [Fact]
    public void An_administrator_holds_every_bit_and_an_overwrite_grants_administrator_to_no_one()
    {
        var administrator = Capabilities.For(Participant(), Bag(Bit.Administrator, Roles.Administrator), PublicPlace, Workshop,
            [Over(RoleTokens.Everyone, deny: Bits.All), Over(Roles.Administrator, deny: Bits.All), Over(Leo, deny: Bits.All)], Now, Root());
        var granted = Capabilities.For(Participant(), Member(), PublicPlace, Workshop, [Over(RoleTokens.Everyone, allow: Bit.Administrator)], Now);

        Assert.Equal(Bits.All, administrator.Held);
        Assert.Equal(Bits.Names(Bits.All), administrator.Can);
        Assert.False(granted.Holds(Bit.Administrator));
        Assert.False(granted.Holds(Bit.ManageSpace));
    }

    [Fact]
    public void A_role_overwrite_applies_only_to_a_role_in_the_bag_and_the_participants_own_only_to_them()
    {
        var overwrites = new List<PermissionOverwrite> { Over(Roles.Moderator, allow: Bit.Moderate), Over(Ada, allow: Bit.ManageTangent) };

        var member = Capabilities.For(Participant(), Member(), PublicPlace, Workshop, overwrites, Now);
        var moderator = Capabilities.For(Participant(), Bag(Roles.MemberBits, Roles.Member, Roles.Moderator), PublicPlace, Workshop, overwrites, Now);
        var ada = Capabilities.For(Participant(Ada), Member(), PublicPlace, Workshop, overwrites, Now);

        Assert.Equal(OnTangent, member.Held);
        Assert.Equal(OnTangent | Bit.Moderate, moderator.Held);
        Assert.Equal(OnTangent | Bit.ManageTangent, ada.Held);
    }

    [Fact]
    public void Standing_precedes_everything()
    {
        var banned = Capabilities.For(Participant(standing: Standing.Banned), Member(), PublicPlace, Workshop, [], Now, Root());
        var timedOut = Capabilities.For(Participant(standing: Standing.TimedOut), Member(), PublicPlace, Workshop, [], Now, Root());
        var appliedPublic = Capabilities.For(Participant(standing: Standing.Applied), Member(), PublicPlace, Workshop, [], Now);
        var appliedPrivate = Capabilities.For(Participant(standing: Standing.Applied), Member(), PrivatePlace, Workshop, [], Now);
        var visitorPublic = Capabilities.For(null, Visitor, PublicPlace, Workshop, [], Now);
        var visitorPrivate = Capabilities.For(null, Visitor, PrivatePlace, Workshop, [], Now);
        var visitorWidened = Capabilities.For(null, [.. Visitor, .. Bits.Tokens(Roles.MemberBits)], PublicPlace, Workshop, [Over(RoleTokens.Everyone, allow: Bits.All)], Now);

        Assert.Equal((Bit.None, false, Capabilities.Absent), (banned.Held, banned.Exists, banned.Gate(Bit.View)));
        Assert.Equal((Bit.View, true, Capabilities.TimedOut), (timedOut.Held, timedOut.Exists, timedOut.Gate(Bit.Post)));
        Assert.Equal((Bit.View, Capabilities.Apply), (appliedPublic.Held, appliedPublic.Gate(Bit.Post)));
        Assert.Equal((Bit.None, false, Capabilities.ApplyToRead), (appliedPrivate.Held, appliedPrivate.Exists, appliedPrivate.Gate(Bit.View)));
        Assert.Equal((Bit.View, true, Capabilities.SignIn), (visitorPublic.Held, visitorPublic.Exists, visitorPublic.Gate(Bit.Post)));
        Assert.Equal((Bit.None, false, Capabilities.SignInToRead), (visitorPrivate.Held, visitorPrivate.Exists, visitorPrivate.Gate(Bit.View)));
        Assert.Equal(Capabilities.SignInToRead, visitorPrivate.Gate(Bit.Post));
        Assert.All(new[] { Capabilities.SignIn, Capabilities.SignInToRead, Capabilities.Apply, Capabilities.ApplyToRead, Capabilities.Declined,
            Capabilities.DeclinedToRead, Capabilities.TimedOut, Capabilities.TimedOutUntil(Now), Capabilities.RootRemoved, Capabilities.Closed, Capabilities.Removed,
            Capabilities.NotYours },
            reason => Assert.DoesNotContain("write", reason.Text));
        Assert.Equal(Bit.View, visitorWidened.Held);
        Assert.Equal(["View"], visitorPublic.Can);
    }

    [Fact]
    public void A_timeout_that_has_ended_reads_as_admitted_and_one_that_has_not_keeps_view_alone()
    {
        var ended = Capabilities.For(Participant(standing: Standing.TimedOut, until: Now.AddDays(-1)), Member(), PublicPlace, Workshop, [], Now, Root());
        var endsNow = Capabilities.For(Participant(standing: Standing.TimedOut, until: Now), Member(), PublicPlace, Workshop, [], Now, Root());
        var running = Capabilities.For(Participant(standing: Standing.TimedOut, until: Now.AddDays(1)), Member(), PublicPlace, Workshop, [], Now, Root());
        var open = Capabilities.For(Participant(standing: Standing.TimedOut), Member(), PublicPlace, Workshop, [], Now, Root());

        Assert.Equal(Roles.MemberBits, ended.Held);
        Assert.Equal(Roles.MemberBits, endsNow.Held);
        Assert.Equal((Bit.View, Capabilities.TimedOutUntil(Now.AddDays(1))), (running.Held, running.Gate(Bit.Post)));
        Assert.Contains("until Oct 30, 10:00 AM UTC", running.Gate(Bit.Post)!.Text);
        Assert.Equal((Bit.View, Capabilities.TimedOut), (open.Held, open.Gate(Bit.Post)));
    }

    [Fact]
    public void View_on_a_public_place_comes_from_its_audience_and_on_a_tangent_from_its_everyone_overwrite()
    {
        var visitorWithoutRoleView = Capabilities.For(null, Visitor, PublicPlace, Workshop, [], Now);
        var memberOnPrivatePlace = Capabilities.For(Participant(), Member(), PrivatePlace, Workshop, [], Now);
        var roleWithoutView = Capabilities.For(Participant(), Bag(Bit.Post | Bit.Topic, Roles.Member), PrivatePlace, Workshop, [], Now);
        var open = Capabilities.For(null, Visitor, PublicPlace, Workshop, [ForAudience(Audience.Public)], Now);
        var closed = Capabilities.For(null, Visitor, PublicPlace, Workshop, [ForAudience(Audience.Private)], Now);
        var member = Capabilities.For(Participant(), Member(), PublicPlace, Workshop, [ForAudience(Audience.Private), Over(Roles.Member, allow: Bit.View)], Now);

        Assert.True(visitorWithoutRoleView.Holds(Bit.View));
        Assert.Equal(OnTangent, memberOnPrivatePlace.Held);
        Assert.Equal((Bit.None, false, Capabilities.Absent), (roleWithoutView.Held, roleWithoutView.Exists, roleWithoutView.Gate(Bit.View)));
        Assert.True(open.Holds(Bit.View));
        Assert.Equal((Bit.None, false, Capabilities.Absent), (closed.Held, closed.Exists, closed.Gate(Bit.View)));
        Assert.Equal(OnTangent, member.Held);
    }

    /// <summary>Taken out while the Tangent is private, made public, they read it; made private again, they are kept out.
    /// Only their deny of View gives way: their deny of Post stands, and so does standing in the place.</summary>
    [Fact]
    public void A_person_taken_out_reads_a_public_tangent_and_is_kept_out_again_once_it_is_private()
    {
        var takenOut = Over(Leo, deny: Bit.View);
        IReadOnlyList<PermissionOverwrite> members = [ForAudience(Audience.Private), Over(Roles.Member, allow: Bit.View), takenOut];
        IReadOnlyList<PermissionOverwrite> everyone = [ForAudience(Audience.Public), takenOut];

        var whilePrivate = Capabilities.For(Participant(), Member(), PublicPlace, Workshop, members, Now);
        var madePublic = Capabilities.For(Participant(), Member(), PublicPlace, Workshop, everyone, Now);
        var privateAgain = Capabilities.For(Participant(), Member(), PublicPlace, Workshop, members, Now);
        var postDenied = Capabilities.For(Participant(), Member(), PublicPlace, Workshop, [ForAudience(Audience.Public), Over(Leo, deny: Bit.View | Bit.Post)], Now);
        var banned = Capabilities.For(Participant(standing: Standing.Banned), Member(), PublicPlace, Workshop, everyone, Now);
        var applicant = Capabilities.For(Participant(standing: Standing.Applied), Member(), PrivatePlace, Workshop, everyone, Now);
        var timedOut = Capabilities.For(Participant(standing: Standing.TimedOut), Member(), PublicPlace, Workshop, everyone, Now);

        Assert.Equal((false, Capabilities.Absent), (whilePrivate.Exists, whilePrivate.Gate(Bit.View)));
        Assert.Equal((true, OnTangent), (madePublic.Exists, madePublic.Held));
        Assert.Equal((false, Capabilities.Absent), (privateAgain.Exists, privateAgain.Gate(Bit.View)));
        Assert.Equal((true, Capabilities.Denied(Leo, Bit.Post).Word), (postDenied.Holds(Bit.View), postDenied.Gate(Bit.Post)?.Word));
        Assert.Equal((false, Capabilities.Absent), (banned.Exists, banned.Gate(Bit.View)));
        Assert.Equal((false, Capabilities.ApplyToRead), (applicant.Exists, applicant.Gate(Bit.View)));
        Assert.Equal((true, Capabilities.TimedOut.Word), (timedOut.Holds(Bit.View), timedOut.Gate(Bit.Post)?.Word));
    }

    [Fact]
    public void What_a_reader_may_not_view_does_not_exist_for_them_and_nothing_else_is_said()
    {
        IReadOnlyList<PermissionOverwrite> closed = [ForAudience(Audience.Private)];
        var member = Capabilities.For(Participant(), Member(), PublicPlace, Workshop, closed, Now, Root(Leo, removed: true));
        var timedOut = Capabilities.For(Participant(standing: Standing.TimedOut), Member(), PublicPlace, Workshop, closed, Now, Root(Leo, removed: true));
        var moderator = Capabilities.For(Participant(), Moderator(), PublicPlace, Workshop, [Over(Roles.Moderator, deny: Bit.View)], Now, Root(Ada, removed: true));

        Assert.False(member.Exists);
        Assert.All(Bits.Each, verb => Assert.Equal(Capabilities.Absent, member.Gate(verb)));
        Assert.All(Bits.Each, verb => Assert.Equal(Capabilities.Absent, timedOut.Gate(verb)));
        Assert.All(Bits.Each, verb => Assert.Equal(Capabilities.Absent, moderator.Gate(verb)));
        Assert.Empty(member.Can);
    }

    [Fact]
    public void Edit_own_and_remove_own_apply_to_my_post_nothing_applies_to_a_removed_post_and_post_is_absent_on_a_locked_topic()
    {
        var mine = Capabilities.For(Participant(), Member(), PublicPlace, Workshop, [], Now, Root(Leo));
        var another = Capabilities.For(Participant(), Member(), PublicPlace, Workshop, [], Now, Root(Ada));
        var noPost = Capabilities.For(Participant(), Member(), PublicPlace, Workshop, [], Now);
        var removed = Capabilities.For(Participant(), Moderator(), PublicPlace, Workshop, [], Now, Root(Leo, removed: true));

        Assert.True(mine.Holds(Bit.EditOwn | Bit.RemoveOwn));
        Assert.Equal((Capabilities.NotYours, Capabilities.NotYours), (another.Gate(Bit.EditOwn), another.Gate(Bit.RemoveOwn)));
        Assert.Equal(OnAnothers, another.Held);
        Assert.True(noPost.Holds(Bit.EditOwn | Bit.RemoveOwn), "of a Tangent, a bit of authorship says what the reader may do to their own");
        // A removed Post is a tombstone the reader finds, with nothing to do on it.
        Assert.Equal((Bit.RemoveOwn, true, Capabilities.Removed), (removed.Held, removed.Exists, removed.Gate(Bit.Moderate)));
        Assert.Equal(Capabilities.Removed, removed.Gate(Bit.EditOwn));
    }

    [Fact]
    public void Manage_space_implies_every_tangents_manage_tangent_and_moderate_implies_editing_and_removing_any_post()
    {
        var keeper = Capabilities.For(Participant(), Bag(Roles.MemberBits | Bit.ManageSpace, Roles.Member), PublicPlace, Workshop,
            [Over(Roles.Member, deny: Bit.ManageTangent)], Now);
        var moderator = Capabilities.For(Participant(), Moderator(), PublicPlace, Workshop, [], Now, Root(Ada));
        var member = Capabilities.For(Participant(), Member(), PublicPlace, Workshop, [], Now, Root(Ada));

        Assert.Equal((true, (Reason?)null), (keeper.Holds(Bit.ManageTangent), keeper.Gate(Bit.ManageTangent)));
        Assert.Equal((true, (Reason?)null, (Reason?)null), (moderator.Holds(Bit.EditOwn | Bit.RemoveOwn), moderator.Gate(Bit.EditOwn), moderator.Gate(Bit.RemoveOwn)));
        Assert.Equal(Capabilities.NotYours, member.Gate(Bit.EditOwn));
    }

    [Fact]
    public void The_gate_word_is_the_first_reason_in_precedence_sign_in_apply_timed_out_absent_closed_removed()
    {
        var removedRoot = Root(Leo, removed: true);
        IReadOnlyList<PermissionOverwrite> closed = [ForAudience(Audience.Private)];

        var timedOutOnRemoved = Capabilities.For(Participant(standing: Standing.TimedOut), Member(), PublicPlace, Workshop, [], Now, removedRoot);
        var visitorOnRemoved = Capabilities.For(null, Visitor, PublicPlace, Workshop, [], Now, removedRoot);
        var appliedOnRemoved = Capabilities.For(Participant(standing: Standing.Applied), Member(), PrivatePlace, Workshop, [], Now, removedRoot);
        var unseenRemoved = Capabilities.For(Participant(), Member(), PublicPlace, Workshop, closed, Now, removedRoot);
        var memberOnRemoved = Capabilities.For(Participant(), Member(), PublicPlace, Workshop, [], Now, removedRoot);
        var deniedOnRemoved = Capabilities.For(Participant(), Member(), PublicPlace, Workshop, [Over(RoleTokens.Everyone, deny: Bit.Post)], Now, removedRoot);
        var moderatorOnRemoved = Capabilities.For(Participant(), Moderator(), PublicPlace, Workshop, [], Now, removedRoot);

        Assert.Equal(Capabilities.TimedOut, timedOutOnRemoved.Gate(Bit.Post));
        Assert.Equal(Capabilities.SignIn, visitorOnRemoved.Gate(Bit.Post));
        Assert.Equal(Capabilities.ApplyToRead, appliedOnRemoved.Gate(Bit.Post));
        Assert.Equal(Capabilities.Absent, unseenRemoved.Gate(Bit.View));
        Assert.Equal(Capabilities.RootRemoved, memberOnRemoved.Gate(Bit.Post));
        Assert.Equal(Capabilities.RootRemoved, deniedOnRemoved.Gate(Bit.Post));
        Assert.Equal(Capabilities.Removed, visitorOnRemoved.Gate(Bit.View));
        Assert.Equal(Capabilities.Removed, memberOnRemoved.Gate(Bit.EditOwn));
        Assert.Equal(Capabilities.Removed, moderatorOnRemoved.Gate(Bit.Moderate));
    }

    [Fact]
    public void A_closed_topic_takes_replies_from_its_moderators_alone_and_a_closed_one_whose_root_is_removed_from_no_one()
    {
        var closed = Root(Ada);
        closed.Close(Leo, Now);
        var closedAndRemoved = Root(Ada, removed: true);
        closedAndRemoved.Closed = true;
        var reply = Given.ARoot("3jzfcijpj2z2b", Workshop, "A reply", "a-reply", PostKind.Topic, "More words.", Ada, Now);
        reply.ParentId = closed.Id;

        var member = Capabilities.For(Participant(), Member(), PublicPlace, Workshop, [], Now, closed);
        var moderator = Capabilities.For(Participant(), Moderator(), PublicPlace, Workshop, [], Now, closed);
        var onAReply = Capabilities.For(Participant(), Member(), PublicPlace, Workshop, [], Now, reply, closed);
        var visitor = Capabilities.For(null, Visitor, PublicPlace, Workshop, [], Now, closed);
        var moderatorOnRemoved = Capabilities.For(Participant(), Moderator(), PublicPlace, Workshop, [], Now, closedAndRemoved);

        Assert.Equal((Capabilities.Closed, true), (member.Gate(Bit.Post), member.Holds(Bit.View)));
        Assert.Equal(Capabilities.Closed, onAReply.Gate(Bit.Post));
        Assert.True(moderator.Holds(Bit.Post | Bit.Moderate));
        Assert.Equal(Capabilities.SignIn, visitor.Gate(Bit.Post));
        Assert.Equal(Capabilities.RootRemoved, moderatorOnRemoved.Gate(Bit.Post));
        Assert.Equal(Capabilities.Closed.Word, Capabilities.RootRemoved.Word);
    }

    [Fact]
    public void For_a_verb_the_bag_denies_the_answer_names_the_deciding_role_or_overwrite()
    {
        var everyone = Capabilities.For(Participant(), Member(), PublicPlace, Workshop, [Over(RoleTokens.Everyone, deny: Bit.Topic)], Now);
        var role = Capabilities.For(Participant(), Bag(Roles.MemberBits, Roles.Member, Roles.Moderator), PublicPlace, Workshop,
            [Over(RoleTokens.Everyone, allow: Bit.Post), Over(Roles.Moderator, deny: Bit.Post)], Now);
        var own = Capabilities.For(Participant(), Member(), PublicPlace, Workshop, [Over(Roles.Member, allow: Bit.Post), Over(Leo, deny: Bit.Post)], Now);
        var noRole = Capabilities.For(Participant(), Member(), PublicPlace, Workshop, [], Now);
        var overruled = Capabilities.For(Participant(), Member(), PublicPlace, Workshop, [Over(RoleTokens.Everyone, deny: Bit.Topic), Over(Leo, allow: Bit.Topic)], Now);

        Assert.Equal("everyone denies Topic", everyone.Gate(Bit.Topic)?.Word);
        Assert.Equal("role:moderator denies Post", role.Gate(Bit.Post)?.Word);
        Assert.Equal("leo denies Post", own.Gate(Bit.Post)?.Word);
        Assert.Equal("no role grants ManageSpace", noRole.Gate(Bit.ManageSpace)?.Word);
        Assert.Null(overruled.Gate(Bit.Topic));
        Assert.Throws<ArgumentException>(() => noRole.Gate(Bit.Post | Bit.Topic));
    }
}
