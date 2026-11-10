using System.Net;
using Koan.Data.Abstractions.Failures;
using Koan.Data.Core.Commands;
using Koan.Data.Core.Transactions;
using Tangent.Access;
using Tangent.Capture;
using Tangent.Events;
using Tangent.Participants;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Posts;

/// <summary>A mention is a link to <c>/p/</c> and a participant's id: at render a link to the participant's address reading the name
/// the place shows them by now, titled with their handle, or its own words where the DID is no participant's; written with the Post, one <c>mention</c> event per
/// participant newly mentioned who may view the Tangent, in the Post's transaction; and on the door of the participant
/// mentioned.</summary>
public sealed class MentionTests
{
    private static readonly string Leo = Persona.Id("leo4writes");
    private static readonly string Banned = Persona.Id("banned4writes");
    private static readonly string Ghost = Persona.Id("ghost4writes");

    private static string Mention(string did, string words) => $"[@{words}](/p/{did})";

    [Fact]
    public async Task A_mention_renders_as_a_link_to_the_participants_address_with_their_name_now_and_an_unknown_identity_as_text()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var body = $"Hello {Mention(Bea, "bea")} and {Mention(Ghost, "ghost")}, and `{Mention(Mo, "code")}` stays code.";
        var root = await Started(Ada, "Introductions", body);
        using var visitor = Agent(tangentSpace);

        var page = await Send(visitor, HttpMethod.Get, TopicPath(root), json: true);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        var shown = page.Json.GetProperty("root");
        Assert.Equal(body, shown.GetProperty("body").GetString());
        var mention = Assert.Single(shown.GetProperty("mentions").EnumerateArray());
        var handle = Persona.Handle(Bea);
        var address = BaseUrl + "/p/" + Uri.EscapeDataString(handle);
        Assert.Equal((Bea, handle, handle, address),
            (mention.GetProperty("id").GetString(), mention.GetProperty("handle").GetString(), mention.GetProperty("name").GetString(),
                mention.GetProperty("address").GetString()));

        // With no display name, the name shown is the handle.
        var html = (await Send(visitor, HttpMethod.Get, TopicPath(root))).Body;
        Assert.Contains($"<p>Hello <a href=\"{address}\" title=\"@{handle}\">@{handle}</a> and @ghost, and <code>{WebUtility.HtmlEncode(Mention(Mo, "code"))}</code> stays code.</p>", html);
        Assert.DoesNotContain("href=\"at:", html);
        Assert.DoesNotContain("href=\"#\"", html);

        // Bea's profile names her; every render reads it at once, and hovering tells her apart by her handle.
        Assert.IsType<Outcome<Participant>.Done>(await new CaptureProfile(Bea, new ProfileAnswer(Persona.Did(Bea), "Bea <Lantern>", null, null), DateTimeOffset.UtcNow).Send(Cancellation));
        html = (await Send(visitor, HttpMethod.Get, TopicPath(root))).Body;
        Assert.Contains($"<a href=\"{address}\" title=\"@{handle}\">@Bea &lt;Lantern&gt;</a>", html);
        Assert.Equal("Bea <Lantern>", (await Send(visitor, HttpMethod.Get, TopicPath(root), json: true)).Json.GetProperty("root")
            .GetProperty("mentions")[0].GetProperty("name").GetString());

        // Her handle changes; the body says nothing of it, and every render reads the new one.
        Assert.IsType<Outcome<Participant>.Done>(await new RecordSignIn(Bea, Persona.Did(Bea), "bea.renamed.example").Send(Cancellation));
        html = (await Send(visitor, HttpMethod.Get, TopicPath(root))).Body;
        Assert.Contains($"<a href=\"{BaseUrl}/p/bea.renamed.example\" title=\"@bea.renamed.example\">@Bea &lt;Lantern&gt;</a>", html);
        Assert.DoesNotContain(handle, html);
        Assert.Equal(body, (await Post.Get(root.Id, Cancellation))?.Body);
    }

    [Fact]
    public void The_name_shown_is_the_display_name_else_the_handle_else_the_did()
    {
        var participant = new Participant { Id = Bea, Did = Persona.Did(Bea) };
        Assert.Equal((Persona.Did(Bea), Persona.Did(Bea)), (participant.ShownName(), participant.ShownHandle()));
        participant.Handle = "bea.example";
        Assert.Equal(("bea.example", "bea.example"), (participant.ShownName(), participant.ShownHandle()));
        participant.Profile.DisplayName = "Bea";
        Assert.Equal(("Bea", "bea.example"), (participant.ShownName(), participant.ShownHandle()));
        participant.Profile.DisplayName = "";
        Assert.Equal("bea.example", participant.ShownName());
    }

    [Fact]
    public async Task Start_reply_and_edit_write_one_mention_event_per_newly_mentioned_participant_who_may_view_the_tangent()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Leo, [Roles.Administrator], ct: Cancellation);
        await Given.AParticipant(tangentSpace, Banned, [Roles.Member], Standing.Banned, ct: Cancellation);

        // Bea twice, Ada herself, Mo, a DID that is no participant's and a banned participant: Bea and Mo are told, once.
        var body = $"{Mention(Bea, "bea")} and {Mention(Bea, "bea again")}, {Mention(Ada, "me")}, {Mention(Mo, "mo")}, "
            + $"{Mention(Ghost, "ghost")} and {Mention(Banned, "banned")}.";
        var id = Tids.Mint();
        var root = Assert.IsType<Outcome<Post>.Done>(await new Started(id, Workshop, "Who is here", body, Ada).Send(Cancellation)).Result;
        var created = Assert.Single(await Event.Query(e => e.Kind == EventKind.PostCreated && e.PostId == root.Id, Cancellation));
        var started = await MentionsOf(root.Id);
        Assert.Equal([(Bea, Ada, 1L, created.Seq + 1), (Mo, Ada, 1L, created.Seq + 2)],
            started.Select(e => (e.TargetId!, e.ActorId!, e.Version ?? 0, e.Seq)).ToList());
        Assert.All(started, e => Assert.Equal((Workshop, root.Id, root.Id), (e.TangentId, e.TopicId, e.PostId)));

        // Bea replies to Ada and mentions her and herself: Ada is told of the mention once, beside the reply's own target.
        var reply = await Replied(root, Bea, $"Thanks {Mention(Ada, "ada")}; cc {Mention(Bea, "me")}.");
        Assert.Equal([(Ada, Bea)], (await MentionsOf(reply.Id)).Select(e => (e.TargetId!, e.ActorId!)).ToList());

        // An edit that adds Leo tells Leo alone; one that drops a mention and adds none tells no one.
        var added = Assert.IsType<Outcome<Post>.Done>(await new Edited(root.Id, 1, body + $" Also {Mention(Leo, "leo")}.", null, Ada).Send(Cancellation)).Result;
        Assert.Equal([(Leo, Ada, 2L)], (await MentionsOf(root.Id)).Where(e => e.Version == 2).Select(e => (e.TargetId!, e.ActorId!, e.Version ?? 0)).ToList());
        Assert.IsType<Outcome<Post>.Done>(await new Edited(root.Id, added.Version, $"Only {Mention(Leo, "leo")} now.", null, Ada).Send(Cancellation));
        Assert.Equal(3, (await MentionsOf(root.Id)).Count);

        // In the studio, which only administrators read, Leo's mention of Ada tells no one.
        var secret = await Started(Leo, "Plans", $"Not yet, {Mention(Ada, "ada")}.", Studio);
        Assert.Empty(await MentionsOf(secret.Id));

        // Removing a Post writes no mention event, and its tombstone shows no body.
        Assert.IsType<Outcome<Post>.Done>(await new RemovePost(reply.Id, Bea).Send(Cancellation));
        Assert.Single(await MentionsOf(reply.Id));
        using var visitor = Agent(tangentSpace);
        var tombstone = (await Send(visitor, HttpMethod.Get, PostPath(root, reply), json: true)).Json.GetProperty("replies")
            .EnumerateArray().Single(post => post.GetProperty("id").GetString() == reply.Id);
        Assert.False(tombstone.TryGetProperty("body", out _));
        Assert.Empty(tombstone.GetProperty("mentions").EnumerateArray());
    }

    [Fact]
    public async Task A_posts_mention_events_commit_in_its_transaction_or_not_at_all()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var before = await Counted();

        // The store refuses the mention's row at commit: a log row already holds the sequence after the Post's own event.
        var id = Tids.Mint();
        var failed = await new ThenThrow(new Started(id, Workshop, "Hello", $"Hello {Mention(Bea, "bea")}.", Ada)).Send(Cancellation);

        Assert.Equal(DataCommitOutcome.NotCommitted, Assert.IsType<Outcome<Post>.Failed>(failed).Landed);
        Assert.Null(await Post.Get(id, Cancellation));
        Assert.Equal((before.Posts, before.Events), await Counted());
    }

    [Fact]
    public async Task The_doors_mentions_group_shows_a_real_mention_to_the_participant_mentioned()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var root = await Started(Ada, "A lantern question", $"What do you think, {Mention(Bea, "bea")}?");
        using var bea = await SignedIn(tangentSpace, Bea);

        var door = await Send(bea.Client, HttpMethod.Get, "/", json: true);
        Assert.Equal(HttpStatusCode.OK, door.Status);
        var mentions = door.Json.GetProperty("news").GetProperty("mentions");
        Assert.Equal(1, mentions.GetProperty("count").GetInt32());
        var item = Assert.Single(mentions.GetProperty("posts").EnumerateArray());
        Assert.Equal((root.Id, Ada, "A lantern question", BaseUrl + TopicPath(root)),
            (item.GetProperty("postId").GetString(), item.GetProperty("who").GetProperty("id").GetString(),
                item.GetProperty("topic").GetString(), item.GetProperty("address").GetString()));
        Assert.Equal("What do you think, @bea4writes.example?", item.GetProperty("line").GetString());

        var html = WebUtility.HtmlDecode((await Send(bea.Client, HttpMethod.Get, "/")).Body);
        Assert.Contains("1 mention of you", html);

        // Ada, who wrote it, has no mention waiting.
        using var ada = await SignedIn(tangentSpace, Ada);
        Assert.Equal(0, (await Send(ada.Client, HttpMethod.Get, "/", json: true)).Json.GetProperty("news").GetProperty("mentions").GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task The_door_and_the_tangent_page_show_a_mention_as_the_participants_name_now()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        using var bea = await SignedIn(tangentSpace, Bea);
        await Started(Ada, "A lantern question", $"{Mention(Bea, "ada.example")} has a question about [@ghost](/p/{Ghost}).");
        using var visitor = Agent(tangentSpace);

        async Task<(string Door, string Opening)> Lines()
        {
            var door = await Send(bea.Client, HttpMethod.Get, "/", json: true);
            var page = await Send(visitor, HttpMethod.Get, "/t/" + Workshop, json: true);
            return (door.Json.GetProperty("news").GetProperty("mentions").GetProperty("posts")[0].GetProperty("line").GetString()!,
                page.Json.GetProperty("topics")[0].GetProperty("opening").GetString()!);
        }

        Assert.Equal(("@bea4writes.example has a question about @ghost.", "@bea4writes.example has a question about @ghost."), await Lines());

        Assert.IsType<Outcome<Participant>.Done>(await new RecordSignIn(Bea, Persona.Did(Bea), "bea.renamed.example").Send(Cancellation));
        Assert.Equal(("@bea.renamed.example has a question about @ghost.", "@bea.renamed.example has a question about @ghost."), await Lines());

        Assert.IsType<Outcome<Participant>.Done>(await new CaptureProfile(Bea, new ProfileAnswer(Persona.Did(Bea), "Bea", null, null), DateTimeOffset.UtcNow).Send(Cancellation));
        Assert.Equal(("@Bea has a question about @ghost.", "@Bea has a question about @ghost."), await Lines());
    }

    [Fact]
    public async Task A_mention_inside_an_image_description_is_only_words_of_its_alt()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var root = await Started(Bea, "A picture", $"![see {Mention(Ada, "ada")}](https://img/x.png)");

        Assert.Empty(root.MentionedIds);
        Assert.Empty((await Post.Get(root.Id, Cancellation))!.MentionedIds);
        Assert.Empty(await MentionsOf(root.Id));
        using var visitor = Agent(tangentSpace);
        var page = await Send(visitor, HttpMethod.Get, TopicPath(root), json: true);
        Assert.Empty(page.Json.GetProperty("root").GetProperty("mentions").EnumerateArray());
        var html = (await Send(visitor, HttpMethod.Get, TopicPath(root))).Body;
        Assert.Contains("alt=\"see @ada\"", html);
        // Bea's name links to her profile; nothing links to Ada's.
        Assert.DoesNotContain("/p/" + Uri.EscapeDataString(Persona.Handle(Ada)), html);
    }

    /// <summary>The mention events of one Post, in the order of the log.</summary>
    private static async Task<List<Event>> MentionsOf(string postId)
        => (await Event.Query(e => e.Kind == EventKind.Mention && e.PostId == postId, Cancellation)).OrderBy(e => e.Seq).ToList();

    /// <summary>Runs a command, then throws, so everything it wrote rolls back.</summary>
    private sealed record ThenThrow(ICommand<Post> Inner) : ICommand<Post>
    {
        public async Task<Outcome<Post>> Run(CancellationToken ct)
        {
            Assert.IsType<Outcome<Post>.Done>(await Inner.Run(ct));
            throw new InvalidOperationException("thrown after the command's saves");
        }
    }
}
