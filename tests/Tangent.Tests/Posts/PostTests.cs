using System.Reflection;
using Koan.Data.Core.Commands;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Events;
using Xunit;

namespace Tangent.Posts;

/// <summary>Topics started and replies made by command: the root as the Topic's state, slugs, sequences, the log rows
/// appended with them, refusals that write nothing, and idempotency by Id.</summary>
public sealed class PostTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private const string Workshop = "the-workshop";

    /// <summary>The workshop and the articles Tangents, public, with leo and ada as members.</summary>
    private static async Task Started(TangentSpaceFixture tangentSpace)
    {
        await Given.ATangent(tangentSpace, Workshop, ct: Cancellation);
        await Given.ATangent(tangentSpace, "articles", ct: Cancellation, kind: PostKind.Article);
        await Given.AParticipant(tangentSpace, "leo", [Roles.Member], ct: Cancellation);
        await Given.AParticipant(tangentSpace, "ada", [Roles.Member], ct: Cancellation);
    }

    [Fact]
    public async Task A_topic_started_by_command_is_a_root_post_with_a_slug_from_its_title_deduplicated_in_its_tangent_and_appends_post_created()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        await Started(tangentSpace);
        var firstId = Tids.Mint();
        var secondId = Tids.Mint();
        var elsewhereId = Tids.Mint();

        var first = Assert.IsType<Outcome<Post>.Done>(await new StartTopic(firstId, Workshop, "  Hello, Wörld! ", "A first word.", "leo").Send(Cancellation)).Result;
        var second = Assert.IsType<Outcome<Post>.Done>(await new StartTopic(secondId, Workshop, "Hello world", "The same title again.", "leo").Send(Cancellation)).Result;
        var elsewhere = Assert.IsType<Outcome<Post>.Done>(await new StartTopic(elsewhereId, "articles", "Hello world", "The title in another Tangent.", "leo").Send(Cancellation)).Result;

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
    public async Task A_title_whose_slug_is_a_tangents_own_page_takes_the_suffix_and_each_topic_started_raises_the_tangents_count()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        await Started(tangentSpace);

        var create = Assert.IsType<Outcome<Post>.Done>(await new StartTopic(Tids.Mint(), Workshop, "New", "Where Topics start.", "leo").Send(Cancellation)).Result;
        var settings = Assert.IsType<Outcome<Post>.Done>(await new StartTopic(Tids.Mint(), Workshop, "Settings!", "Where the Tangent is set.", "leo").Send(Cancellation)).Result;
        var plain = Assert.IsType<Outcome<Post>.Done>(await new StartTopic(Tids.Mint(), Workshop, "New settings", "Neither.", "leo").Send(Cancellation)).Result;
        var leave = Assert.IsType<Outcome<Post>.Done>(await new StartTopic(Tids.Mint(), Workshop, "Leave", "Where a participant goes out.", "leo").Send(Cancellation)).Result;

        Assert.Equal(("new-2", "settings-2", "new-settings", "leave-2"), (create.Slug, settings.Slug, plain.Slug, leave.Slug));
        Assert.Equal(4, (await Tangents.Tangent.Get(Workshop, Cancellation))!.TopicCount);
        Assert.Equal(0, (await Tangents.Tangent.Get("articles", Cancellation))!.TopicCount);
    }

    [Fact]
    public void Every_segment_of_a_tangents_own_routes_that_could_be_a_slug_is_reserved_so_no_topic_answers_at_one()
    {
        var own = typeof(TangentModule).Assembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type))
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .SelectMany(action => action.GetCustomAttributes<HttpMethodAttribute>())
            .Select(route => route.Template?.Split('/', StringSplitOptions.RemoveEmptyEntries) ?? [])
            .Where(segments => segments is ["t", "{tangent}", _, ..] && Slug.IsValid(segments[2]))
            .Select(segments => segments[2])
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(Slug.Reserved.Order(StringComparer.Ordinal), own.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_reply_records_itself_as_the_topics_latest_and_its_author_first_among_at_most_four_recent_authors()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        await Started(tangentSpace);
        foreach (var id in new[] { "mara", "sam", "kim" }) await Given.AParticipant(tangentSpace, id, [Roles.Member], ct: Cancellation);
        var topicId = Tids.Mint();
        var started = Assert.IsType<Outcome<Post>.Done>(await new StartTopic(topicId, Workshop, "Who is here", "Say hello.", "leo").Send(Cancellation)).Result;
        Assert.Equal(["leo"], started.RecentAuthorIds);
        Assert.Null(started.LatestReplyId);

        string last = "";
        foreach (var author in new[] { "ada", "mara", "leo", "sam", "kim", "sam" })
        {
            last = Tids.Mint();
            Assert.IsType<Outcome<Post>.Done>(await new Reply(last, topicId, "Hello from " + author + ".", author).Send(Cancellation));
        }

        var root = (await Post.Get(topicId, Cancellation))!;
        Assert.Equal((last, "sam"), (root.LatestReplyId, root.LatestReplyAuthorId));
        Assert.Equal(["sam", "kim", "leo", "mara"], root.RecentAuthorIds);
    }

    [Fact]
    public async Task Past_fifty_topics_with_one_slug_the_posts_own_id_is_the_suffix_and_the_slug_stays_within_the_limit()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        await Started(tangentSpace);
        var title = new string('a', Slug.Limit + 10);
        var slugs = new List<string>();
        string lastId = "";

        for (var topic = 1; topic <= StartTopic.SuffixBound + 1; topic++)
        {
            lastId = Tids.Mint();
            slugs.Add(Assert.IsType<Outcome<Post>.Done>(await new StartTopic(lastId, Workshop, title, "Again.", "leo").Send(Cancellation)).Result.Slug!);
        }

        Assert.Equal(new string('a', Slug.Limit), slugs[0]);
        Assert.Equal(new string('a', Slug.Limit - 2) + "-2", slugs[1]);
        Assert.Equal(new string('a', Slug.Limit - 3) + "-50", slugs[StartTopic.SuffixBound - 1]);
        Assert.Equal(new string('a', Slug.Limit - 1 - Tid.Length) + "-" + lastId, slugs[StartTopic.SuffixBound]);
        Assert.Equal(StartTopic.SuffixBound + 1, slugs.Distinct().Count());
        Assert.All(slugs, slug => Assert.InRange(slug.Length, 1, Slug.Limit));
    }

    [Fact]
    public async Task A_reply_by_command_carries_the_roots_tangent_and_topic_takes_the_next_sequence_moves_the_root_and_appends_post_created()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        await Started(tangentSpace);
        var topicId = Tids.Mint();
        var startedAt = new DateTimeOffset(2026, 10, 29, 9, 0, 0, TimeSpan.Zero);
        var repliedAt = startedAt.AddMinutes(5);
        Assert.IsType<Outcome<Post>.Done>(await new StartTopic(topicId, "articles", "A question", "What is this?", "leo", startedAt).Send(Cancellation));
        var firstId = Tids.Mint();
        var secondId = Tids.Mint();

        var first = Assert.IsType<Outcome<Post>.Done>(await new Reply(firstId, topicId, "An answer.", "ada", At: repliedAt).Send(Cancellation)).Result;
        var second = Assert.IsType<Outcome<Post>.Done>(await new Reply(secondId, topicId, "Another.", "leo", ParentId: firstId, At: repliedAt.AddMinutes(1)).Send(Cancellation)).Result;

        Assert.Equal(("articles", topicId, topicId, 1L, "ada", repliedAt), (first.TangentId, first.TopicId, first.ParentId, first.Sequence, first.AuthorId, first.CreatedAt));
        Assert.Equal((topicId, firstId, 2L), (second.TopicId, second.ParentId, second.Sequence));
        // The kind lives on the root; a reply stays at the default.
        Assert.Equal((PostKind.Topic, PostKind.Topic, null, null), (first.Kind, second.Kind, first.Title, first.Slug));
        var root = await Post.Get(topicId, Cancellation);
        Assert.NotNull(root);
        Assert.Equal((PostKind.Article, 2L, 2, repliedAt.AddMinutes(1)), (root.Kind, root.LastSequence, root.ReplyCount, root.LastActivityAt));
        var stored = await Post.Get(firstId, Cancellation);
        Assert.NotNull(stored);
        Assert.Equal(("articles", topicId, 1L, "An answer.", PostKind.Topic), (stored.TangentId, stored.TopicId, stored.Sequence, stored.Body, stored.Kind));

        var logged = (await Event.All(Cancellation)).OrderBy(e => e.Seq).ToList();
        Assert.Equal(3, logged.Count);
        Assert.Equal((2L, EventKind.PostCreated, "articles", topicId, firstId, "ada", repliedAt), (logged[1].Seq, logged[1].Kind, logged[1].TangentId, logged[1].TopicId, logged[1].PostId, logged[1].ActorId, logged[1].At));
        Assert.Equal((3L, secondId), (logged[2].Seq, logged[2].PostId));
    }

    [Fact]
    public async Task A_reply_whose_parent_is_missing_or_in_another_topic_is_refused_and_writes_nothing()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        await Started(tangentSpace);
        var topicId = Tids.Mint();
        var otherTopicId = Tids.Mint();
        var otherReplyId = Tids.Mint();
        Assert.IsType<Outcome<Post>.Done>(await new StartTopic(topicId, Workshop, "Here", "This one.", "leo").Send(Cancellation));
        Assert.IsType<Outcome<Post>.Done>(await new StartTopic(otherTopicId, Workshop, "There", "That one.", "leo").Send(Cancellation));
        Assert.IsType<Outcome<Post>.Done>(await new Reply(otherReplyId, otherTopicId, "Over there.", "ada").Send(Cancellation));
        var before = (await Post.Count.Exact(Cancellation), await Event.Count.Exact(Cancellation));

        var noParent = await new Reply(Tids.Mint(), topicId, "To whom?", "ada", ParentId: "nope").Send(Cancellation);
        var missingParent = await new Reply(Tids.Mint(), topicId, "To whom?", "ada", ParentId: Tids.Mint()).Send(Cancellation);
        var parentElsewhere = await new Reply(Tids.Mint(), topicId, "To whom?", "ada", ParentId: otherReplyId).Send(Cancellation);
        var rootElsewhere = await new Reply(Tids.Mint(), topicId, "To whom?", "ada", ParentId: otherTopicId).Send(Cancellation);

        Assert.Equal(Reply.NoParent, Assert.IsType<Outcome<Post>.Refused>(noParent).Reason);
        Assert.Equal(Reply.NoParent, Assert.IsType<Outcome<Post>.Refused>(missingParent).Reason);
        Assert.Equal(Reply.NoParent, Assert.IsType<Outcome<Post>.Refused>(parentElsewhere).Reason);
        Assert.Equal(Reply.NoParent, Assert.IsType<Outcome<Post>.Refused>(rootElsewhere).Reason);
        Assert.Equal(before, (await Post.Count.Exact(Cancellation), await Event.Count.Exact(Cancellation)));
        Assert.Equal((0L, 0), ((await Post.Get(topicId, Cancellation))!.LastSequence, (await Post.Get(topicId, Cancellation))!.ReplyCount));
    }

    [Fact]
    public async Task A_reply_on_a_removed_topic_is_refused_with_the_gate_word_and_writes_nothing_the_log_included()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        await Started(tangentSpace);
        var removed = Post.Root(Tids.Mint(), Workshop, "Removed", "removed", PostKind.Topic, "Gone.", "leo", DateTimeOffset.UtcNow);
        removed.Removed = true;
        Assert.IsType<Outcome<Post>.Done>(await new Store<Post>(removed).Send(Cancellation));
        var before = (await Post.Count.Exact(Cancellation), await Event.Count.Exact(Cancellation));

        var onRemoved = await new Reply(Tids.Mint(), removed.Id, "Still?", "ada").Send(Cancellation);
        var onNothing = await new Reply(Tids.Mint(), Tids.Mint(), "Anyone?", "ada").Send(Cancellation);

        Assert.Equal(Capabilities.Closed, Assert.IsType<Outcome<Post>.Refused>(onRemoved).Reason);
        Assert.Equal(Reply.NoTopic, Assert.IsType<Outcome<Post>.Refused>(onNothing).Reason);
        Assert.Equal(before, (await Post.Count.Exact(Cancellation), await Event.Count.Exact(Cancellation)));
        Assert.Equal((0L, 0), ((await Post.Get(removed.Id, Cancellation))!.LastSequence, (await Post.Get(removed.Id, Cancellation))!.ReplyCount));
    }

    [Fact]
    public async Task A_post_command_whose_id_exists_answers_with_the_existing_row_for_the_same_request_a_conflict_for_a_different_one_in_the_same_place_and_one_refusal_for_an_id_held_elsewhere_and_none_writes()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        await Started(tangentSpace);
        var topicId = Tids.Mint();
        var otherTopicId = Tids.Mint();
        var replyId = Tids.Mint();
        var topic = Assert.IsType<Outcome<Post>.Done>(await new StartTopic(topicId, Workshop, "Once", "Said once.", "leo").Send(Cancellation)).Result;
        Assert.IsType<Outcome<Post>.Done>(await new StartTopic(otherTopicId, Workshop, "Elsewhere", "Said elsewhere.", "leo").Send(Cancellation));
        var reply = Assert.IsType<Outcome<Post>.Done>(await new Reply(replyId, topicId, "Heard.", "ada").Send(Cancellation)).Result;
        var before = (await Post.Count.Exact(Cancellation), await Event.Count.Exact(Cancellation));

        var topicAgain = await new StartTopic(topicId, Workshop, " Once ", "Said once.", "leo", At: DateTimeOffset.UtcNow.AddDays(1)).Send(Cancellation);
        var topicDiffers = new[]
        {
            await new StartTopic(topicId, Workshop, "Once", "Said twice.", "leo").Send(Cancellation),
            await new StartTopic(topicId, Workshop, "Twice", "Said once.", "leo").Send(Cancellation),
            await new StartTopic(topicId, Workshop, "Once", "Said once.", "ada").Send(Cancellation),
            await new StartTopic(replyId, Workshop, "Once", "Heard.", "ada").Send(Cancellation)
        };
        var replyAgain = await new Reply(replyId, topicId, "Heard.", "ada", ParentId: topicId, At: DateTimeOffset.UtcNow.AddDays(1)).Send(Cancellation);
        var replyDiffers = new[]
        {
            await new Reply(replyId, topicId, "Unheard.", "ada").Send(Cancellation),
            await new Reply(replyId, topicId, "Heard.", "leo").Send(Cancellation),
            await new Reply(replyId, topicId, "Heard.", "ada", ParentId: otherTopicId).Send(Cancellation),
            await new Reply(topicId, topicId, "Said once.", "leo").Send(Cancellation)
        };
        // An id a Post of another Tangent or another Topic holds: one refusal, which names nothing of that Post.
        var heldElsewhere = new[]
        {
            await new StartTopic(topicId, "articles", "Once", "Said once.", "leo").Send(Cancellation),
            await new Reply(replyId, otherTopicId, "Heard.", "ada").Send(Cancellation)
        };

        Assert.Equal((topic.Id, topic.Slug, topic.Body), Select(Assert.IsType<Outcome<Post>.Done>(topicAgain).Result));
        Assert.All(topicDiffers, differs => Assert.Equal(Post.IdTaken, Assert.IsType<Outcome<Post>.Refused>(differs).Reason));
        Assert.Equal((reply.Id, reply.Slug, reply.Body), Select(Assert.IsType<Outcome<Post>.Done>(replyAgain).Result));
        Assert.All(replyDiffers, differs => Assert.Equal(Post.IdTaken, Assert.IsType<Outcome<Post>.Refused>(differs).Reason));
        Assert.All(heldElsewhere, refused => Assert.Equal(Post.IdTaken, Assert.IsType<Outcome<Post>.Refused>(refused).Reason));
        Assert.Equal(before, (await Post.Count.Exact(Cancellation), await Event.Count.Exact(Cancellation)));
        Assert.Equal("Said once.", (await Post.Get(topicId, Cancellation))?.Body);
        Assert.Equal((1L, 1), ((await Post.Get(topicId, Cancellation))!.LastSequence, (await Post.Get(topicId, Cancellation))!.ReplyCount));

        static (string, string?, string) Select(Post post) => (post.Id, post.Slug, post.Body);
    }

    [Fact]
    public async Task A_body_over_64_KiB_with_a_NUL_or_empty_is_refused()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        await Started(tangentSpace);
        var topicId = Tids.Mint();
        Assert.IsType<Outcome<Post>.Done>(await new StartTopic(topicId, Workshop, "Limits", "Within them.", "leo").Send(Cancellation));

        var tooLong = await new StartTopic(Tids.Mint(), Workshop, "Long", new string('x', Post.BodyLimit + 1), "leo").Send(Cancellation);
        var multibyte = await new StartTopic(Tids.Mint(), Workshop, "Long", new string('é', Post.BodyLimit / 2 + 1), "leo").Send(Cancellation);
        var atTheLimit = await new StartTopic(Tids.Mint(), Workshop, "Just", new string('x', Post.BodyLimit), "leo").Send(Cancellation);
        var withNul = await new Reply(Tids.Mint(), topicId, "Before\0after", "leo").Send(Cancellation);
        var empty = await new Reply(Tids.Mint(), topicId, "", "leo").Send(Cancellation);
        var blank = await new Reply(Tids.Mint(), topicId, " \n\t", "leo").Send(Cancellation);

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
        await Started(tangentSpace);

        var refused = await new StartTopic("not-a-tid", Workshop, "Shape", "Any.", "leo").Send(Cancellation);

        Assert.Equal(Post.NeedsTid, Assert.IsType<Outcome<Post>.Refused>(refused).Reason);
        Assert.Equal(0, await Post.Count.Exact(Cancellation));
    }
}
