using System.Net;
using Tangent.Events;
using Tangent.Participants;
using Tangent.Writer;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Posts;

/// <summary>An <c>@</c> and a participant's handle typed in a Post, by a person's form or an agent's JSON, is written as
/// their mention when the Post is written: the body keeps their DID, they hear it as a mention, and it reads as every
/// mention reads, under their handle as it stands. A handle no participant holds stays the words typed, and so does
/// whatever only looks like one: an address, code, a link's words, a URL.</summary>
public sealed class TypedMentionTests
{
    private static string Mention(string did, string words) => $"[{words}](at://{did})";

    [Fact]
    public async Task A_reply_that_types_a_participants_handle_mentions_them_by_form_and_by_an_agents_json()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        var root = await Started(writer, Mo, "Spare parts", "Who has a spare knob for the old radio?");
        using var ada = await SignedIn(tangentSpace, Ada);
        using var bea = await SignedIn(tangentSpace, Bea);
        using var mo = await SignedIn(tangentSpace, Mo);

        // Ada's browser sends the reply form, typing Bea's handle the way a person writes a mention.
        var replyId = Tids.Mint();
        var replied = await Form(ada, TopicPath(root), TopicPath(root),
            new Dictionary<string, string> { ["id"] = replyId, ["body"] = "@bea4writes.example you said you had a spare knob?" });

        Assert.Equal(HttpStatusCode.SeeOther, replied.Status);
        var reply = (await Post.Get(replyId, Cancellation))!;
        Assert.Equal($"{Mention(Bea, "@bea4writes.example")} you said you had a spare knob?", reply.Body);
        Assert.Equal([Bea], reply.MentionedIds);
        // The Post Ada lands on reads it as every mention reads: Bea's handle, linked to her profile.
        var landed = await Send(ada.Client, HttpMethod.Get, new Uri(replied.Location!).AbsolutePath);
        Assert.Contains($"<a href=\"{BaseUrl}/p/bea4writes.example\">@bea4writes.example</a> you said you had a spare knob?", landed.Body);

        // Bea hears it on her door and among her notifications.
        var door = (await Send(bea.Client, HttpMethod.Get, "/", json: true)).Json.GetProperty("news").GetProperty("mentions");
        Assert.Equal((1, replyId), (door.GetProperty("count").GetInt32(), door.GetProperty("posts")[0].GetProperty("postId").GetString()));
        var heard = Assert.Single((await Send(bea.Client, HttpMethod.Get, Notifications.Path, json: true)).Json.GetProperty("entries").EnumerateArray());
        Assert.Equal(("mention", Ada, "Spare parts"),
            (heard.GetProperty("kind").GetString(), heard.GetProperty("who").GetProperty("id").GetString(), heard.GetProperty("topic").GetString()));

        // Ada's agent replies as JSON with her delegate's bearer, typing Mo's handle in another case: Mo hears it.
        var token = await Delegate(tangentSpace, ada);
        using var agent = Agent(tangentSpace);
        var agentReplyId = Tids.Mint();
        var asJson = await Send(agent, HttpMethod.Post, TopicPath(root),
            JsonBody(new { id = agentReplyId, body = "Thanks (@Mo4Writes.Example), I'll bring it Friday." }), json: true, bearer: token);

        Assert.Equal(HttpStatusCode.Created, asJson.Status);
        Assert.Equal($"Thanks ({Mention(Mo, "@Mo4Writes.Example")}), I'll bring it Friday.", asJson.Json.GetProperty("body").GetString());
        var events = await Event.Query(e => e.Kind == EventKind.Mention, Cancellation);
        Assert.Equal([(replyId, Bea, Ada), (agentReplyId, Mo, Ada)],
            events.OrderBy(e => e.Seq).Select(e => (e.PostId!, e.TargetId!, e.ActorId!)).ToList());
        var moHeard = (await Send(mo.Client, HttpMethod.Get, Notifications.Path, json: true)).Json.GetProperty("entries").EnumerateArray()
            .Single(entry => entry.GetProperty("kind").GetString() == "mention");
        Assert.Equal(Ada, moHeard.GetProperty("who").GetProperty("id").GetString());
    }

    [Fact]
    public async Task An_unknown_handle_an_address_code_a_links_words_and_a_url_stay_as_typed_beside_a_typed_mention()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);
        using var bea = await SignedIn(tangentSpace, Bea);
        const string form = "/t/" + Workshop + "/new";
        var untouched = string.Join("\n\n",
            "Ask @nobody.example, or write to bea@bea4writes.example.",
            "`@bea4writes.example` is code, and so is this:",
            "```\n@bea4writes.example\n```",
            "[@bea4writes.example](https://example.org/bea) is a link's words, and <https://example.org/@bea4writes.example> a link.",
            "https://bsky.app/profile/@bea4writes.example and https://example.org/(@bea4writes.example) are URLs, \\@bea4writes.example is escaped");
        var id = MintedId((await Send(ada.Client, HttpMethod.Get, form)).Body);

        var started = await Form(ada, form, form, new Dictionary<string, string>
        {
            ["id"] = id, ["title"] = "Who to ask", ["body"] = untouched + ", and **@bea4writes.example** is Bea."
        });

        Assert.Equal(HttpStatusCode.SeeOther, started.Status);
        var root = (await Post.Get(id, Cancellation))!;
        Assert.Equal(untouched + $", and **{Mention(Bea, "@bea4writes.example")}** is Bea.", root.Body);
        Assert.Equal([Bea], root.MentionedIds);
        Assert.Equal([Bea], (await Event.Query(e => e.Kind == EventKind.Mention, Cancellation)).Select(e => e.TargetId));
        var html = WebUtility.HtmlDecode((await Send(ada.Client, HttpMethod.Get, TopicPath(root))).Body);
        Assert.Contains("<p>Ask @nobody.example, or write to bea@bea4writes.example.</p>", html);
        Assert.Contains("<code>@bea4writes.example</code>", html);
        Assert.Single(html.Split($"href=\"{BaseUrl}/p/bea4writes.example\">@bea4writes.example</a>").Skip(1));
    }

    [Fact]
    public async Task A_typed_mention_keeps_the_participant_whose_handle_changes_and_reads_under_the_new_one()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);
        using var bea = await SignedIn(tangentSpace, Bea);
        var root = await Started(writer, Ada, "A lantern question", "Which wick?");
        var replyId = Tids.Mint();
        Assert.Equal(HttpStatusCode.SeeOther, (await Form(ada, TopicPath(root), TopicPath(root),
            new Dictionary<string, string> { ["id"] = replyId, ["body"] = "What do you think, @bea4writes.example?" })).Status);

        Assert.IsType<Outcome<Participant>.Done>(await writer.Send(new RecordHandle(Bea, "bea.renamed.example"), Cancellation));

        using var visitor = Agent(tangentSpace);
        var html = (await Send(visitor, HttpMethod.Get, TopicPath(root))).Body;
        Assert.Contains($"<a href=\"{BaseUrl}/p/bea.renamed.example\">@bea.renamed.example</a>?", html);
        Assert.DoesNotContain("bea4writes.example", html);
        Assert.Equal([Bea], (await Post.Get(replyId, Cancellation))!.MentionedIds);
        // The old handle names no one now: typed again, it stays the words typed.
        var later = Tids.Mint();
        Assert.Equal(HttpStatusCode.SeeOther, (await Form(ada, TopicPath(root), TopicPath(root),
            new Dictionary<string, string> { ["id"] = later, ["body"] = "Or you, @bea4writes.example?" })).Status);
        var plain = (await Post.Get(later, Cancellation))!;
        Assert.Equal(("Or you, @bea4writes.example?", 0), (plain.Body, plain.MentionedIds.Count));
    }

    [Fact]
    public async Task An_edit_that_adds_a_typed_mention_notifies_once()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);
        using var bea = await SignedIn(tangentSpace, Bea);
        var root = await Started(writer, Ada, "Lanterns", "How do you hang yours?");
        var edit = PostPath(root, root) + "/edit";

        var added = await Form(ada, edit, edit, new Dictionary<string, string> { ["body"] = "How do you hang yours, @bea4writes.example?", ["version"] = "1" });
        // The editor sends the words again with the mention as typed and a sentence more: Bea is already mentioned.
        var again = await Form(ada, edit, edit,
            new Dictionary<string, string> { ["body"] = "How do you hang yours, @bea4writes.example? From a hook, I hope.", ["version"] = "2" });

        Assert.Equal((HttpStatusCode.SeeOther, HttpStatusCode.SeeOther), (added.Status, again.Status));
        var edited = (await Post.Get(root.Id, Cancellation))!;
        Assert.Equal(($"How do you hang yours, {Mention(Bea, "@bea4writes.example")}? From a hook, I hope.", 3L), (edited.Body, edited.Version));
        var heard = Assert.Single(await Event.Query(e => e.Kind == EventKind.Mention, Cancellation));
        Assert.Equal((Bea, Ada, root.Id, 2L), (heard.TargetId, heard.ActorId, heard.PostId, heard.Version));
        Assert.Single((await Send(bea.Client, HttpMethod.Get, Notifications.Path, json: true)).Json.GetProperty("entries").EnumerateArray(),
            entry => entry.GetProperty("kind").GetString() == "mention");
    }
}
