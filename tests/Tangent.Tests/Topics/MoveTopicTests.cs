using System.Net;
using System.Text.Json;
using Koan.Data.Core.Commands;
using Koan.Identity.Access;
using Koan.Identity.Roles;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Events;
using Tangent.Space;
using Tangent.Tangents;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Posts;

/// <summary>Moving a Topic: whoever moderates both Tangents moves it from its root's footer, as a page and as a tool, and its
/// replies, its images' uses and its rows of the log go with it; the Tangents count it where it stands, its address where it
/// stood forwards, a Post's address under it too, for whoever may read it there, and the moderators' Tangent neither gives nor
/// takes a Topic.</summary>
public sealed class MoveTopicTests
{
    private const string Porch = "porch";
    private const string Backroom = "backroom";

    [Fact]
    public async Task A_moderator_moves_a_topic_with_its_replies_images_and_rows_and_its_old_addresses_forward_to_its_new_ones()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Moving(tangentSpace);
        var leo = Persona.Id("leo4moves");
        await Given.AParticipant(tangentSpace, leo, [Roles.Administrator], ct: Cancellation);
        var image = await Media.Keep(ArticleMediaTests.Jpeg(), Cancellation);
        Assert.IsType<Outcome<MediaUse>.Done>(await new BringMedia(image.Id, Ada).Send(Cancellation));
        var stays = await Started(Bea, "Kilns", "Gas or wood?");
        var root = await Started(Ada, "Lanterns", "Mine hangs by the door. ![Lit](/media/" + image.Id + ")");
        var first = await Replied(root, Bea, "Mine by the window.");
        var second = await Replied(root, Mo, "Both work.");
        var before = (await Tangents.Tangent.Get(Workshop, Cancellation))!;
        using var mo = await SignedIn(tangentSpace, Mo);

        // Mo follows the Topic's way to move it, then the move's form, as an agent does.
        var topic = (await Send(mo.Client, HttpMethod.Get, TopicPath(root), json: true)).Json;
        var page = await Send(mo.Client, HttpMethod.Get, topic.GetProperty("move").GetString()![BaseUrl.Length..], json: true);
        var form = page.Json.GetProperty("move");
        var choices = form.GetProperty("fields")[0].GetProperty("choices").EnumerateArray().Select(choice => choice.GetProperty("value").GetString()).ToList();
        Assert.Contains(Porch, choices);
        Assert.DoesNotContain(Workshop, choices);
        var moved = await Follow(mo, form, new Dictionary<string, string> { ["to"] = Porch });

        var address = BaseUrl + "/t/" + Porch + "/" + root.Slug;
        Assert.Equal((HttpStatusCode.OK, address), (moved.Status, moved.Location));
        foreach (var post in new[] { root, first, second }) Assert.Equal(Porch, (await Post.Get(post.Id, Cancellation))?.TangentId);
        Assert.Equal((Porch, root.Slug), ((await Post.Get(root.Id, Cancellation))?.TangentId, (await Post.Get(root.Id, Cancellation))?.Slug));
        Assert.Equal(Porch, (await MediaUse.Get(MediaUse.Key(image.Id, root.Id), Cancellation))?.TangentId);
        Assert.All(await Event.Query(row => row.TopicId == root.Id, Cancellation), row => Assert.Equal(Porch, row.TangentId));
        var move = Assert.Single(await Event.Query(row => row.Kind == EventKind.PostMoved, Cancellation));
        Assert.Equal((Porch, root.Id, Mo, (string?)null), (move.TangentId, move.PostId, move.ActorId, move.TargetId));
        // The Tangents count it where it stands, in all, by its month and by its author.
        var left = (await Tangents.Tangent.Get(Workshop, Cancellation))!;
        var came = (await Tangents.Tangent.Get(Porch, Cancellation))!;
        Assert.Equal((before.TopicCount - 1, 1), (left.TopicCount, came.TopicCount));
        Assert.True(left.Tallied() && came.Tallied());
        Assert.False(left.Authors.ContainsKey(Ada));
        Assert.Equal(1, came.Authors[Ada]);

        // The old address and a Post's address under it forward to where the Topic stands now, and its page reads it there.
        var visitor = Agent(tangentSpace);
        var old = await Send(visitor, HttpMethod.Get, TopicPath(root));
        var oldPost = await Send(visitor, HttpMethod.Get, PostPath(root, first));
        Assert.Equal((HttpStatusCode.Found, address), (old.Status, old.Location));
        Assert.Equal((HttpStatusCode.Found, address + "/" + first.Id), (oldPost.Status, oldPost.Location));
        var there = (await Send(visitor, HttpMethod.Get, "/t/" + Porch + "/" + root.Slug, json: true)).Json;
        Assert.Equal((Porch, 2), (there.GetProperty("tangent").GetProperty("slug").GetString(), there.GetProperty("replyCount").GetInt32()));
        Assert.Equal(HttpStatusCode.OK, (await Send(visitor, HttpMethod.Get, "/media/" + image.Id)).Status);
        Assert.Equal(HttpStatusCode.OK, (await Send(visitor, HttpMethod.Get, TopicPath(stays))).Status);

        // The workshop's count is its own again, so deleting it with what it holds now lands.
        Assert.IsType<Outcome<Tangents.Tangent>.Done>(await new DeleteTangent(Workshop, leo, Topics: left.TopicCount).Send(Cancellation));
        Assert.Equal(Porch, (await Post.Get(root.Id, Cancellation))?.TangentId);
    }

    [Fact]
    public async Task A_topic_moved_where_its_slug_is_taken_takes_the_next_and_its_old_address_still_forwards()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Moving(tangentSpace);
        var there = await Started(Bea, "Lanterns", "Paper or glass?", Porch);
        var root = await Started(Ada, "Lanterns", "Mine hangs by the door.");
        using var mo = await SignedIn(tangentSpace, Mo);

        var moved = await Form(mo, TopicPath(root) + "/" + TopicPageController.MoveSegment, TopicPath(root) + "/" + TopicPageController.MoveSegment,
            new Dictionary<string, string> { ["to"] = Porch });

        var slug = (await Post.Get(root.Id, Cancellation))!.Slug;
        Assert.Equal((HttpStatusCode.SeeOther, Slug.WithSuffix(there.Slug!, 2)), (moved.Status, slug));
        Assert.Equal(BaseUrl + "/t/" + Porch + "/" + slug, moved.Location);
        Assert.Equal(BaseUrl + "/t/" + Porch + "/" + slug, (await Send(Agent(tangentSpace), HttpMethod.Get, TopicPath(root))).Location);
        Assert.Equal(there.Id, (await Send(Agent(tangentSpace), HttpMethod.Get, "/t/" + Porch + "/" + there.Slug, json: true)).Json.GetProperty("id").GetString());
    }

    [Fact]
    public async Task A_move_is_refused_without_moderate_on_both_tangents_into_or_out_of_the_moderators_tangent_and_to_nowhere_and_sent_again_changes_nothing()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Moving(tangentSpace);
        Assert.IsType<Outcome<Tangents.Tangent>.Done>(await new CreateModeratorsTangent().Send(Cancellation));
        var roles = tangentSpace.Services.GetRequiredService<RoleCollection>();
        await roles.Overwrite(Overwrites.Resource(Articles), OverwriteSubjectKind.Role, Roles.Moderator, [], [Bits.Token(Bit.Moderate)], ct: Cancellation);
        var root = await Started(Ada, "Lanterns", "Mine hangs by the door.");
        var reply = await Replied(root, Bea, "Mine by the window.");
        var report = Assert.IsType<Outcome<Post>.Done>(await new ReportPost(Tids.Mint(), reply.Id, PostBody.Of("Unkind."), Ada).Send(Cancellation)).Result;
        var events = (await Counted()).Events;
        using var mo = await SignedIn(tangentSpace, Mo);
        using var ada = await SignedIn(tangentSpace, Ada);
        var move = TopicPath(root) + "/" + TopicPageController.MoveSegment;

        var member = await Json(ada, TopicPath(root), move, new { to = Porch });
        var unmoderated = await Json(mo, TopicPath(root), move, new { to = Articles });
        var intoReports = await Json(mo, TopicPath(root), move, new { to = ModeratorsTangent.Slug });
        var outOfReports = await Json(mo, TopicPath(report), TopicPath(report) + "/" + TopicPageController.MoveSegment, new { to = Workshop });
        var nowhere = await Json(mo, TopicPath(root), move, new { to = "nowhere" });
        var hidden = await Json(mo, TopicPath(root), move, new { to = Studio });
        var again = await Json(mo, TopicPath(root), move, new { to = Workshop });

        Assert.Equal((HttpStatusCode.Forbidden, "gated"), (member.Status, member.Kind));
        Assert.Equal((HttpStatusCode.Forbidden, "gated"), (unmoderated.Status, unmoderated.Kind));
        Assert.Equal((HttpStatusCode.BadRequest, MoveTopic.KeepsReports.Text), (intoReports.Status, intoReports.Json.GetProperty("detail").GetString()));
        Assert.Equal((HttpStatusCode.BadRequest, MoveTopic.KeepsReports.Text), (outOfReports.Status, outOfReports.Json.GetProperty("detail").GetString()));
        foreach (var absent in new[] { nowhere, hidden })
        {
            Assert.Equal((HttpStatusCode.BadRequest, MoveTopic.NoDestination.Text), (absent.Status, absent.Json.GetProperty("detail").GetString()));
            Assert.True(absent.Json.GetProperty("errors").TryGetProperty(MoveTopic.ToField, out _));
        }

        Assert.Equal((HttpStatusCode.OK, BaseUrl + TopicPath(root)), (again.Status, again.Location));
        Assert.Equal(Workshop, (await Post.Get(root.Id, Cancellation))?.TangentId);
        Assert.Equal(events, (await Counted()).Events);
        // A member's page offers no move, and the moderators' Tangent offers none to its moderators.
        Assert.False((await Send(ada.Client, HttpMethod.Get, TopicPath(root), json: true)).Json.TryGetProperty("move", out var none) && none.ValueKind != JsonValueKind.Null);
        var reported = (await Send(mo.Client, HttpMethod.Get, TopicPath(report), json: true)).Json;
        Assert.True(!reported.TryGetProperty("move", out var offered) || offered.ValueKind == JsonValueKind.Null);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(mo.Client, HttpMethod.Get, TopicPath(report) + "/" + TopicPageController.MoveSegment)).Status);
    }

    [Fact]
    public async Task A_topic_moved_where_a_reader_may_not_read_leaves_them_its_old_address_absent_its_reply_gone_from_their_door_and_search()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Moving(tangentSpace);
        var root = await Started(Bea, "Lanterns", "Mine hangs by the porch light.");
        var reply = await Replied(root, Ada, "Mine by the quinceberry window.");
        var bea = (await Participants.Participant.Get(Bea, Cancellation))!;
        Assert.Contains((await (await Unread.Of(bea, Cancellation)).ForYouNow(Cancellation)).Rows, row => row.PostId == reply.Id);
        using var reader = await SignedIn(tangentSpace, Bea);
        Assert.Contains(reply.Id, (await Send(reader.Client, HttpMethod.Get, "/search?q=quinceberry", json: true)).Body);

        Assert.IsType<Outcome<Post>.Done>(await new MoveTopic(root.Id, Backroom, Mo).Send(Cancellation));

        Assert.Equal(HttpStatusCode.NotFound, (await Send(reader.Client, HttpMethod.Get, TopicPath(root))).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(reader.Client, HttpMethod.Get, PostPath(root, reply))).Status);
        Assert.DoesNotContain((await (await Unread.Of(bea, Cancellation)).ForYouNow(Cancellation)).Rows, row => row.PostId == reply.Id);
        Assert.DoesNotContain(reply.Id, (await Send(reader.Client, HttpMethod.Get, "/search?q=quinceberry", json: true)).Body);
        // Its moderator reads it where it stands.
        using var mo = await SignedIn(tangentSpace, Mo);
        Assert.Equal(BaseUrl + "/t/" + Backroom + "/" + root.Slug, (await Send(mo.Client, HttpMethod.Get, TopicPath(root))).Location);
    }

    [Fact]
    public async Task A_topic_moved_into_a_tangent_made_after_it_keeps_what_reached_its_readers_new_and_older()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Moving(tangentSpace);
        var leo = Persona.Id("leo4moves");
        await Given.AParticipant(tangentSpace, leo, [Roles.Administrator], ct: Cancellation);
        var root = await Started(Ada, "Lanterns", "Mine hangs by the door.");
        var read = await Replied(root, Bea, "Mine by the window.");
        var unread = await Replied(root, Bea, "And one by the gate.");
        Assert.IsType<Outcome<Checkpoint>.Done>(await new Seen(Ada, read.Sequence, root.Id).Send(Cancellation));
        // The archive begins in the log after every row of the Topic.
        var archive = Assert.IsType<Outcome<Tangents.Tangent>.Done>(
            await new CreateTangent("archive", "Archive", "", PostKind.Topic, 3, Audience.Public, leo).Send(Cancellation)).Result;
        Assert.All(await Event.Query(row => row.TopicId == root.Id, Cancellation), row => Assert.True(row.Seq < archive.LogSeq));

        Assert.IsType<Outcome<Post>.Done>(await new MoveTopic(root.Id, archive.Id, Mo).Send(Cancellation));

        var ada = await Unread.Of((await Participants.Participant.Get(Ada, Cancellation))!, Cancellation);
        Assert.Contains((await ada.ForYouNow(Cancellation)).Rows, row => row.PostId == unread.Id);
        Assert.Contains((await ada.Earlier(long.MaxValue, Cancellation)).Rows, row => row.PostId == read.Id);
        // Deleted with the Topic, the archive leaves its rows to no one, and none to a Tangent that takes its slug later.
        Assert.IsType<Outcome<Tangents.Tangent>.Done>(await new DeleteTangent(archive.Id, leo, Topics: 1).Send(Cancellation));
        var again = Assert.IsType<Outcome<Tangents.Tangent>.Done>(
            await new CreateTangent("archive", "Archive", "", PostKind.Topic, 3, Audience.Public, leo).Send(Cancellation)).Result;
        var anew = (await Capabilities.LoadEach(Ada, [again.Id], Cancellation))[again.Id];
        Assert.All(await Event.Query(row => row.TopicId == root.Id, Cancellation), row => Assert.False(anew.Owns(row)));
    }

    /// <summary>The place of the write verbs, with the porch, a public Tangent of discussions, and the backroom, a private one
    /// its moderators read.</summary>
    private static async Task Moving(TangentSpaceFixture tangentSpace)
    {
        await Place(tangentSpace);
        await Given.ATangent(tangentSpace, Porch, ct: Cancellation);
        await Given.ATangent(tangentSpace, Backroom, Audience.Private, ct: Cancellation);
        await Overwrites.SetAccess(tangentSpace.Services.GetRequiredService<RoleCollection>(), Backroom, Roles.Moderator, RoleAccess.Post, Cancellation);
    }
}
