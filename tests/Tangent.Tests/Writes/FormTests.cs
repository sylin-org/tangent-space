using System.Net;
using Koan.Data.Core.Commands;
using Tangent.Access;
using Tangent.Participants;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Posts;

/// <summary>Every write a page offers its reader is a form in its JSON, the one its HTML holds, saying how to make it:
/// where, how, and each field with what it holds and the value the page gives it, the TID minted for the form among them.
/// A participant signed in with its own account, with its session and nothing else, follows a form exactly and the write
/// lands as its own; a reader who may not make the write finds no form, as the page's HTML offers none.</summary>
public sealed class FormTests
{
    [Fact]
    public async Task A_participant_follows_a_topics_reply_form_exactly_and_the_reply_lands_as_its_own_answering_the_post_the_address_named()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var root = await Started(Bea, "Lanterns", "How do you hang yours?");
        var hook = await Replied(root, Mo, "From a hook.");
        using var ada = await SignedIn(tangentSpace, Ada);

        var topic = await Send(ada.Client, HttpMethod.Get, TopicPath(root), json: true);
        Assert.Equal(HttpStatusCode.OK, topic.Status);
        var reply = topic.Json.GetProperty("reply");
        Assert.Equal((BaseUrl + TopicPath(root), "POST"), (reply.GetProperty("action").GetString(), reply.GetProperty("method").GetString()));
        Assert.Equal([PostWrites.IdField, PostWrites.BodyField, PostWrites.ParentField], FieldNames(reply));
        Assert.All(reply.GetProperty("fields").EnumerateArray(), field => Assert.NotEmpty(field.GetProperty("holds").GetString()!));
        Assert.Equal("", Valued(reply, PostWrites.ParentField));
        var minted = Valued(reply, PostWrites.IdField);
        Assert.True(Tid.IsValid(minted));

        var replied = await Follow(ada, reply, new Dictionary<string, string> { [PostWrites.BodyField] = "On a nail, says Ada." });

        Assert.Equal((HttpStatusCode.Created, minted), (replied.Status, replied.Json.GetProperty("id").GetString()));
        var landed = await Post.Get(minted!, Cancellation);
        Assert.Equal((root.Id, root.Id, Ada, "On a nail, says Ada."), (landed?.TopicId, landed?.ParentId, landed?.AuthorId, landed?.Body));
        var again = await Follow(ada, reply, new Dictionary<string, string> { [PostWrites.BodyField] = "On a nail, says Ada." });
        Assert.Equal((HttpStatusCode.Created, minted), (again.Status, again.Json.GetProperty("id").GetString()));
        Assert.Equal(2, (await Post.Get(root.Id, Cancellation))!.ReplyCount);

        // At a reply's address, the form answers that reply.
        var atHook = (await Send(ada.Client, HttpMethod.Get, PostPath(root, hook), json: true)).Json.GetProperty("reply");
        Assert.Equal(hook.Id, Valued(atHook, PostWrites.ParentField));
        var answered = await Follow(ada, atHook, new Dictionary<string, string> { [PostWrites.BodyField] = "Which hook?" });
        Assert.Equal(HttpStatusCode.Created, answered.Status);
        Assert.Equal((hook.Id, Ada), ((await Post.Get(Valued(atHook, PostWrites.IdField)!, Cancellation))?.ParentId, (await Post.Get(Valued(atHook, PostWrites.IdField)!, Cancellation))?.AuthorId));
    }

    [Fact]
    public async Task A_participant_follows_a_tangents_new_topic_form_exactly_and_the_topic_starts_as_its_own()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);

        var tangent = await Send(ada.Client, HttpMethod.Get, "/t/" + Workshop, json: true);
        var newTopic = tangent.Json.GetProperty("newTopic").GetString()!;
        var page = await Send(ada.Client, HttpMethod.Get, newTopic[BaseUrl.Length..], json: true);
        Assert.Equal(HttpStatusCode.OK, page.Status);
        var start = page.Json.GetProperty("start");
        Assert.Equal((BaseUrl + "/t/" + Workshop + "/new", "POST"), (start.GetProperty("action").GetString(), start.GetProperty("method").GetString()));
        Assert.Equal([PostWrites.IdField, "title", PostWrites.BodyField, "kind"], FieldNames(start));
        Assert.Null(Valued(start, "kind"));
        var minted = Valued(start, PostWrites.IdField);
        Assert.True(Tid.IsValid(minted));

        var started = await Follow(ada, start, new Dictionary<string, string> { ["title"] = "From Ada", [PostWrites.BodyField] = "Started by Ada." });

        Assert.Equal((HttpStatusCode.Created, minted), (started.Status, started.Json.GetProperty("id").GetString()));
        var root = await Post.Get(minted!, Cancellation);
        Assert.Equal((Workshop, (string?)null, Ada, "From Ada", PostKind.Topic), (root?.TangentId, root?.ParentId, root?.AuthorId, root?.Title, root?.Kind));
    }

    [Fact]
    public async Task A_reader_who_may_not_reply_or_start_a_topic_finds_no_form_for_it_as_the_page_offers_none()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var root = await Started(Ada, "Lanterns", "How do you hang yours?");
        using var visitor = Agent(tangentSpace);
        using var bea = await SignedIn(tangentSpace, Bea);
        Assert.IsType<Outcome<Participant>.Done>(await new TimeOut(Mo, Bea, DateTimeOffset.UtcNow.AddHours(1)).Send(Cancellation));

        var asVisitor = await Send(visitor, HttpMethod.Get, TopicPath(root), json: true);
        var asTimedOut = await Send(bea.Client, HttpMethod.Get, TopicPath(root), json: true);
        Assert.Equal((Capabilities.SignIn.Word, false), (asVisitor.Json.GetProperty("gate").GetString(), asVisitor.Json.TryGetProperty("reply", out _)));
        Assert.Equal((Capabilities.TimedOut.Word, false), (asTimedOut.Json.GetProperty("gate").GetString(), asTimedOut.Json.TryGetProperty("reply", out _)));
        Assert.DoesNotContain("class=\"reply-form\"", (await Send(bea.Client, HttpMethod.Get, TopicPath(root))).Body);

        var newTopic = "/t/" + Workshop + "/new";
        Assert.False((await Send(visitor, HttpMethod.Get, newTopic, json: true)).Json.TryGetProperty("start", out _));
        Assert.False((await Send(bea.Client, HttpMethod.Get, newTopic, json: true)).Json.TryGetProperty("start", out _));
        Assert.DoesNotContain("name=\"id\"", (await Send(bea.Client, HttpMethod.Get, newTopic)).Body);
    }
}
