using Microsoft.Extensions.DependencyInjection;
using Tangent.Events;
using Tangent.Writer;
using Xunit;

namespace Tangent.Posts;

/// <summary>Topics started and replies made by command: the root as the Topic's state, slugs, sequences, the log rows
/// appended with them, refusals that write nothing, and idempotency by Id.</summary>
public sealed class PostTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private const string Workshop = "the-workshop";

    [Fact]
    public async Task A_topic_started_by_command_is_a_root_post_with_a_slug_from_its_title_deduplicated_in_its_tangent_and_appends_post_created()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        var firstId = Tids.Mint();
        var secondId = Tids.Mint();
        var elsewhereId = Tids.Mint();

        var first = Assert.IsType<Outcome<Post>.Done>(await writer.Send(new StartTopic(firstId, Workshop, "  Hello, Wörld! ", "A first word.", "leo"), Cancellation)).Result;
        var second = Assert.IsType<Outcome<Post>.Done>(await writer.Send(new StartTopic(secondId, Workshop, "Hello world", "The same title again.", "leo"), Cancellation)).Result;
        var elsewhere = Assert.IsType<Outcome<Post>.Done>(await writer.Send(new StartTopic(elsewhereId, "articles", "Hello world", "The title in another Tangent.", "leo", PostKind.Article), Cancellation)).Result;

        Assert.Equal((firstId, firstId, null, Workshop, "hello-world", "Hello, Wörld!", PostKind.Topic), (first.Id, first.TopicId, first.ParentId, first.TangentId, first.Slug, first.Title, first.Kind));
        Assert.Equal((0L, 0L, 0, 1L), (first.Sequence, first.LastSequence, first.ReplyCount, first.Version));
        Assert.Equal("hello-world-2", second.Slug);
        Assert.Equal(("hello-world", PostKind.Article), (elsewhere.Slug, elsewhere.Kind));

        var stored = await Post.Get(firstId, Cancellation);
        Assert.NotNull(stored);
        Assert.Equal((firstId, "hello-world", "A first word."), (stored.TopicId, stored.Slug, stored.Body));

        var logged = (await Event.All(Cancellation)).OrderBy(e => e.Seq).ToList();
        Assert.Equal([1L, 2L, 3L], logged.Select(e => e.Seq));
        Assert.All(logged, e => Assert.Equal(EventKind.PostCreated, e.Kind));
        Assert.Equal((Workshop, firstId, firstId, "leo", 1L), (logged[0].TangentId, logged[0].TopicId, logged[0].PostId, logged[0].ActorId, logged[0].Version));
        Assert.Equal(secondId, logged[1].PostId);
    }

    [Fact]
    public async Task A_reply_by_command_carries_the_roots_tangent_and_topic_takes_the_next_sequence_moves_the_root_and_appends_post_created()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        var topicId = Tids.Mint();
        var startedAt = new DateTimeOffset(2026, 10, 29, 9, 0, 0, TimeSpan.Zero);
        var repliedAt = startedAt.AddMinutes(5);
        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new StartTopic(topicId, Workshop, "A question", "What is this?", "leo", At: startedAt), Cancellation));
        var firstId = Tids.Mint();
        var secondId = Tids.Mint();

        var first = Assert.IsType<Outcome<Post>.Done>(await writer.Send(new Reply(firstId, topicId, "An answer.", "ada", At: repliedAt), Cancellation)).Result;
        var second = Assert.IsType<Outcome<Post>.Done>(await writer.Send(new Reply(secondId, topicId, "Another.", "leo", ParentId: firstId, At: repliedAt.AddMinutes(1)), Cancellation)).Result;

        Assert.Equal((Workshop, topicId, topicId, 1L, "ada", repliedAt), (first.TangentId, first.TopicId, first.ParentId, first.Sequence, first.AuthorId, first.CreatedAt));
        Assert.Equal((topicId, firstId, 2L), (second.TopicId, second.ParentId, second.Sequence));
        var root = await Post.Get(topicId, Cancellation);
        Assert.NotNull(root);
        Assert.Equal((2L, 2, repliedAt.AddMinutes(1)), (root.LastSequence, root.ReplyCount, root.LastActivityAt));
        var stored = await Post.Get(firstId, Cancellation);
        Assert.NotNull(stored);
        Assert.Equal((Workshop, topicId, 1L, "An answer."), (stored.TangentId, stored.TopicId, stored.Sequence, stored.Body));

        var logged = (await Event.All(Cancellation)).OrderBy(e => e.Seq).ToList();
        Assert.Equal(3, logged.Count);
        Assert.Equal((2L, EventKind.PostCreated, Workshop, topicId, firstId, "ada", repliedAt), (logged[1].Seq, logged[1].Kind, logged[1].TangentId, logged[1].TopicId, logged[1].PostId, logged[1].ActorId, logged[1].At));
        Assert.Equal((3L, secondId), (logged[2].Seq, logged[2].PostId));
    }

    [Fact]
    public async Task A_reply_on_a_locked_or_removed_topic_is_refused_with_its_reason_and_writes_nothing_the_log_included()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        var announced = 0;
        writer.Committed += _ => announced++;
        var at = DateTimeOffset.UtcNow;
        var locked = Post.Root(Tids.Mint(), Workshop, "Locked", "locked", PostKind.Topic, "No more.", "leo", at);
        locked.Locked = true;
        var removed = Post.Root(Tids.Mint(), Workshop, "Removed", "removed", PostKind.Topic, "Gone.", "leo", at);
        removed.Removed = true;
        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new Store<Post>(locked), Cancellation));
        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new Store<Post>(removed), Cancellation));
        var before = (await Post.Count.Exact(Cancellation), await Event.Count.Exact(Cancellation), announced);

        var onLocked = await writer.Send(new Reply(Tids.Mint(), locked.Id, "Still?", "ada"), Cancellation);
        var onRemoved = await writer.Send(new Reply(Tids.Mint(), removed.Id, "Still?", "ada"), Cancellation);
        var onNothing = await writer.Send(new Reply(Tids.Mint(), Tids.Mint(), "Anyone?", "ada"), Cancellation);

        Assert.Equal(Post.TopicLocked, Assert.IsType<Outcome<Post>.Refused>(onLocked).Reason);
        Assert.Equal(Post.TopicRemoved, Assert.IsType<Outcome<Post>.Refused>(onRemoved).Reason);
        Assert.Equal(Reply.NoTopic, Assert.IsType<Outcome<Post>.Refused>(onNothing).Reason);
        Assert.Equal(before, (await Post.Count.Exact(Cancellation), await Event.Count.Exact(Cancellation), announced));
        Assert.Equal((0L, 0), ((await Post.Get(locked.Id, Cancellation))!.LastSequence, (await Post.Get(locked.Id, Cancellation))!.ReplyCount));
    }

    [Fact]
    public async Task A_post_command_whose_id_exists_answers_with_the_existing_row_for_the_same_body_and_a_conflict_for_a_different_one_and_neither_writes()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        var announced = 0;
        writer.Committed += _ => announced++;
        var topicId = Tids.Mint();
        var replyId = Tids.Mint();
        var topic = Assert.IsType<Outcome<Post>.Done>(await writer.Send(new StartTopic(topicId, Workshop, "Once", "Said once.", "leo"), Cancellation)).Result;
        var reply = Assert.IsType<Outcome<Post>.Done>(await writer.Send(new Reply(replyId, topicId, "Heard.", "ada"), Cancellation)).Result;
        var before = (await Post.Count.Exact(Cancellation), await Event.Count.Exact(Cancellation), announced);

        var topicAgain = await writer.Send(new StartTopic(topicId, Workshop, "Once", "Said once.", "leo"), Cancellation);
        var topicDiffers = await writer.Send(new StartTopic(topicId, Workshop, "Once", "Said twice.", "leo"), Cancellation);
        var replyAgain = await writer.Send(new Reply(replyId, topicId, "Heard.", "ada"), Cancellation);
        var replyDiffers = await writer.Send(new Reply(replyId, topicId, "Unheard.", "ada"), Cancellation);

        Assert.Equal((topic.Id, topic.Slug, topic.Body), Select(Assert.IsType<Outcome<Post>.Done>(topicAgain).Result));
        Assert.Contains($"'{topicId}'", Assert.IsType<Outcome<Post>.Conflict>(topicDiffers).Reason);
        Assert.Equal((reply.Id, reply.Slug, reply.Body), Select(Assert.IsType<Outcome<Post>.Done>(replyAgain).Result));
        Assert.Contains($"'{replyId}'", Assert.IsType<Outcome<Post>.Conflict>(replyDiffers).Reason);
        Assert.Equal(before, (await Post.Count.Exact(Cancellation), await Event.Count.Exact(Cancellation), announced));
        Assert.Equal("Said once.", (await Post.Get(topicId, Cancellation))?.Body);
        Assert.Equal((1L, 1), ((await Post.Get(topicId, Cancellation))!.LastSequence, (await Post.Get(topicId, Cancellation))!.ReplyCount));

        static (string, string?, string) Select(Post post) => (post.Id, post.Slug, post.Body);
    }

    [Fact]
    public async Task A_body_over_64_KiB_with_a_NUL_or_empty_is_refused()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        var topicId = Tids.Mint();
        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new StartTopic(topicId, Workshop, "Limits", "Within them.", "leo"), Cancellation));

        var tooLong = await writer.Send(new StartTopic(Tids.Mint(), Workshop, "Long", new string('x', Post.BodyLimit + 1), "leo"), Cancellation);
        var multibyte = await writer.Send(new StartTopic(Tids.Mint(), Workshop, "Long", new string('é', Post.BodyLimit / 2 + 1), "leo"), Cancellation);
        var atTheLimit = await writer.Send(new StartTopic(Tids.Mint(), Workshop, "Just", new string('x', Post.BodyLimit), "leo"), Cancellation);
        var withNul = await writer.Send(new Reply(Tids.Mint(), topicId, "Before\0after", "leo"), Cancellation);
        var empty = await writer.Send(new Reply(Tids.Mint(), topicId, "", "leo"), Cancellation);
        var blank = await writer.Send(new Reply(Tids.Mint(), topicId, " \n\t", "leo"), Cancellation);

        Assert.Equal(Post.BodyTooLong, Assert.IsType<Outcome<Post>.Refused>(tooLong).Reason);
        Assert.Equal(Post.BodyTooLong, Assert.IsType<Outcome<Post>.Refused>(multibyte).Reason);
        Assert.IsType<Outcome<Post>.Done>(atTheLimit);
        Assert.Equal(Post.BodyHasNul, Assert.IsType<Outcome<Post>.Refused>(withNul).Reason);
        Assert.Equal(Post.NeedsBody, Assert.IsType<Outcome<Post>.Refused>(empty).Reason);
        Assert.Equal(Post.NeedsBody, Assert.IsType<Outcome<Post>.Refused>(blank).Reason);
        Assert.Equal(2, await Post.Count.Exact(Cancellation));
        Assert.Equal(2, await Event.Count.Exact(Cancellation));
    }

    [Fact]
    public async Task A_post_whose_id_is_not_a_tid_is_refused()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();

        var refused = await writer.Send(new StartTopic("not-a-tid", Workshop, "Shape", "Any.", "leo"), Cancellation);

        Assert.Equal(Post.NeedsTid, Assert.IsType<Outcome<Post>.Refused>(refused).Reason);
        Assert.Equal(0, await Post.Count.Exact(Cancellation));
    }
}
