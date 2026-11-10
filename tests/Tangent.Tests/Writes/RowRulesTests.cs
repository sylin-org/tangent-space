using Koan.Data.Core.Commands;
using Tangent.Capture;
using Tangent.Events;
using Tangent.Posts;
using Tangent.Space;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Writes;

/// <summary>What every Post, Tangent and place row holds is judged as it is saved, whatever saves it: a row saved as a seed
/// saves it is refused as a command's is, with the same reason, while a snapshot of a Post and a counter saved on its root
/// pass as they are.</summary>
public sealed class RowRulesTests
{
    [Fact]
    public async Task A_row_saved_outside_any_command_that_writes_it_is_refused_with_the_rule_it_breaks()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var at = DateTimeOffset.UtcNow;
        var root = Given.ARoot(Tids.Mint(), Workshop, "Lanterns", "lanterns", PostKind.Topic, "How do you hang yours?", Ada, at);

        Post Changed(Action<Post> change)
        {
            var row = Given.ARoot(Tids.Mint(), Workshop, "Lanterns", "lanterns-" + Tids.Mint(), PostKind.Topic, "Words.", Ada, at);
            change(row);
            return row;
        }

        async Task<Reason> Refused<TEntity>(TEntity row) where TEntity : Koan.Data.Core.Model.Entity<TEntity>
            => Assert.IsType<Outcome<TEntity>.Refused>(await new Store<TEntity>(row).Send(Cancellation)).Reason;

        Assert.Equal(Post.NeedsTangent, await Refused(Changed(row => row.TangentId = "")));
        Assert.Equal(Post.NeedsAuthor, await Refused(Changed(row => row.AuthorId = " ")));
        Assert.Equal(Post.BodyHasNul, await Refused(Changed(row => row.Body = "Before\0after")));
        Assert.Equal(Post.BodyTooLong, await Refused(Changed(row => row.Body = new string('x', Post.KeptBodyLimit + 1))));
        Assert.Equal(Post.TitleTooLong, await Refused(Changed(row => row.Title = new string('t', Post.TitleLimit + 1))));
        Assert.Equal(Post.NoTitleOnReply, await Refused(Changed(row => (row.ParentId, row.Title) = (root.Id, "A title"))));
        Assert.Equal(Post.CoverOnArticle, await Refused(Changed(row => row.CoverId = "cover")));
        Assert.Equal(Tangents.Tangent.NeedsSlug, await Refused(Tangents.Tangent.Create("Not a slug", "Name", "", PostKind.Topic, 9)));
        Assert.Equal(Tangents.Tangent.SlugReserved, await Refused(Tangents.Tangent.Create(Tangents.Tangent.NewSegment, "Name", "", PostKind.Topic, 9)));
        var workshop = (await Tangents.Tangent.Get(Workshop, Cancellation))!;
        workshop.Accent = "mauve";
        Assert.Equal(Tangents.Tangent.AccentRule, await Refused(workshop));
        var space = (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!;
        space.Appearance = space.Appearance.With(css: new string('a', Appearance.CssLimit + 1));
        Assert.Equal(Appearance.CssRule, await Refused(space));
        Assert.Equal(0, await Post.Count.Exact(Cancellation));
        Assert.Equal("", (await Tangents.Tangent.Get(Workshop, Cancellation))!.Accent);
    }

    [Fact]
    public async Task A_snapshot_a_removal_and_the_counters_a_reply_moves_pass_as_they_are_and_a_title_sent_to_a_reply_is_refused_with_nothing_written()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var root = await Started(Ada, "Lanterns", "How do you hang yours?");
        var reply = await Replied(root, Bea, "On a hook.");
        var events = await Event.Count.Exact(Cancellation);

        var titled = await new Edited(reply.Id, reply.Version, "On two hooks.", "A title", Bea).Send(Cancellation);
        var edited = await new Edited(root.Id, root.Version, "How do you hang them?", null, Ada).Send(Cancellation);
        var removed = await new RemovePost(reply.Id, Bea).Send(Cancellation);

        Assert.Equal(Post.NoTitleOnReply, Assert.IsType<Outcome<Post>.Refused>(titled).Reason);
        Assert.Equal(2, Assert.IsType<Outcome<Post>.Done>(edited).Result.Version);
        Assert.True(Assert.IsType<Outcome<Post>.Done>(removed).Result.Removed);
        Assert.Equal("How do you hang yours?", (await AuditPartition.At<Post>(root.Id, 1, Cancellation))!.Body);
        Assert.Equal("On a hook.", (await AuditPartition.At<Post>(reply.Id, 1, Cancellation))!.Body);
        Assert.Equal((1, 1L), ((await Post.Get(root.Id, Cancellation))!.ReplyCount, (await Post.Get(root.Id, Cancellation))!.LastSequence));
        Assert.Equal(events + 2, await Event.Count.Exact(Cancellation));
    }
}
