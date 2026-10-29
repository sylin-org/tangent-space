using System.Net;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Posts;

/// <summary>Every write answers to the one form-token rule: a session's post without its form's token is refused and
/// writes nothing; a delegate's bearer carries no ambient credential, needs no token, and writes as the person who
/// approved it, exactly as they can.</summary>
public sealed class TokenTests
{
    [Fact]
    public async Task A_session_post_without_its_form_token_is_refused_and_writes_nothing_and_a_delegates_bearer_post_without_one_writes_as_the_person()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        var root = await Started(writer, Ada, "Lanterns", "How do you hang yours?");
        using var ada = await SignedIn(tangentSpace, Ada);
        var before = await Counted();
        const string form = "/t/" + Workshop + "/new";

        var start = await Send(ada.Client, HttpMethod.Post, form, FormBody(new Dictionary<string, string> { ["id"] = Tids.Mint(), ["title"] = "Tokenless", ["body"] = "No token." }));
        var startJson = await Send(ada.Client, HttpMethod.Post, form, JsonBody(new { id = Tids.Mint(), title = "Tokenless", body = "No token." }), json: true);
        var reply = await Send(ada.Client, HttpMethod.Post, TopicPath(root), FormBody(new Dictionary<string, string> { ["id"] = Tids.Mint(), ["body"] = "No token." }));
        var edit = await Send(ada.Client, HttpMethod.Post, PostPath(root, root) + "/edit", FormBody(new Dictionary<string, string> { ["body"] = "No token.", ["version"] = "1" }));
        var remove = await Send(ada.Client, HttpMethod.Post, PostPath(root, root) + "/remove", FormBody(new Dictionary<string, string>()));

        foreach (var refused in new[] { start, startJson, reply, edit, remove })
            Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
        Assert.Contains("Open the form again", start.Body);
        Assert.Equal("Open the form again", startJson.Title);
        Assert.Equal(before, await Counted());
        Assert.Equal(("How do you hang yours?", false), ((await Post.Get(root.Id, Cancellation))?.Body, (await Post.Get(root.Id, Cancellation))?.Removed));

        var token = await Delegate(tangentSpace, ada);
        using var agent = Agent(tangentSpace);
        var topicId = Tids.Mint();
        var started = await Send(agent, HttpMethod.Post, form, JsonBody(new { id = topicId, title = "From my agent", body = "Written by my delegate." }), json: true, bearer: token);
        var replyId = Tids.Mint();
        var replied = await Send(agent, HttpMethod.Post, TopicPath(root), FormBody(new Dictionary<string, string> { ["id"] = replyId, ["body"] = "A reply, by form." }), bearer: token);
        var edited = await Send(agent, HttpMethod.Post, PostPath(root, root) + "/edit", JsonBody(new { title = "Lanterns", body = "Edited by my delegate.", version = 1 }), json: true, bearer: token);
        var removed = await Send(agent, HttpMethod.Post, PostPath(root, root) + "/remove", null, json: true, bearer: token);

        Assert.True(started.Status == HttpStatusCode.Created, started.Body);
        Assert.Equal(Ada, started.Json.GetProperty("authorId").GetString());
        Assert.Equal((HttpStatusCode.SeeOther, AddressOf(root, new Post { Id = replyId })), (replied.Status, replied.Location));
        Assert.Equal(Ada, (await Post.Get(topicId, Cancellation))?.AuthorId);
        Assert.Equal(Ada, (await Post.Get(replyId, Cancellation))?.AuthorId);
        Assert.Equal((HttpStatusCode.OK, 2L), (edited.Status, edited.Json.GetProperty("version").GetInt64()));
        Assert.Equal((HttpStatusCode.OK, true, Ada), (removed.Status, removed.Json.GetProperty("removed").GetBoolean(), removed.Json.GetProperty("removedBy").GetString()));
    }
}
