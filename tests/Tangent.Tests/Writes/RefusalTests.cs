using System.Net;
using Tangent.Access;
using Tangent.Writer;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Posts;

/// <summary>Refusals carry the gate word Capabilities names — a problem for an agent, the form again with the reason
/// inline for a person — and what the reader may not view answers as absent, exactly as a missing address; none of them
/// writes anything.</summary>
public sealed class RefusalTests
{
    [Fact]
    public async Task A_visitor_is_refused_with_sign_in_as_a_problem_and_on_the_form_with_the_reason_inline_and_nothing_is_written()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        var root = await Started(writer, Ada, "Lanterns", "How do you hang yours?");
        using var visitor = Agent(tangentSpace);
        var before = await Counted();
        const string form = "/t/" + Workshop + "/new";

        var page = await Send(visitor, HttpMethod.Get, form);
        var asJson = await Send(visitor, HttpMethod.Post, form, JsonBody(new { id = Tids.Mint(), title = "Hello", body = "Anyone?" }), json: true);
        var onForm = await Send(visitor, HttpMethod.Post, form, FormBody(new Dictionary<string, string> { ["id"] = Tids.Mint(), ["title"] = "Hello", ["body"] = "Anyone?" }));
        var reply = await Send(visitor, HttpMethod.Post, TopicPath(root), JsonBody(new { id = Tids.Mint(), body = "Anyone?" }), json: true);
        var edit = await Send(visitor, HttpMethod.Post, PostPath(root, root) + "/edit", JsonBody(new { body = "Mine now.", version = 1 }), json: true);
        var editForm = await Send(visitor, HttpMethod.Post, PostPath(root, root) + "/edit", FormBody(new Dictionary<string, string> { ["body"] = "Mine now.", ["version"] = "1" }));

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains("data-gate=\"sign-in\"", page.Body);
        Assert.Contains("href=\"" + BaseUrl + "/sign-in?return=%2Ft%2Fworkshop%2Fnew\"", page.Body);
        Assert.DoesNotContain("type=\"submit\"", page.Body);
        Assert.Equal((HttpStatusCode.Unauthorized, "application/problem+json", Capabilities.SignIn), (asJson.Status, asJson.MediaType, asJson.Gate));
        Assert.Equal(HttpStatusCode.Unauthorized, onForm.Status);
        Assert.Contains("data-gate=\"sign-in\"", onForm.Body);
        Assert.Contains("Sign in to write here.", onForm.Body);
        Assert.Contains("value=\"Hello\"", onForm.Body);
        Assert.Equal((HttpStatusCode.Unauthorized, Capabilities.SignIn), (reply.Status, reply.Gate));
        Assert.Equal((HttpStatusCode.Unauthorized, Capabilities.SignIn), (edit.Status, edit.Gate));
        Assert.Equal(HttpStatusCode.Unauthorized, editForm.Status);
        Assert.Contains("data-gate=\"sign-in\"", editForm.Body);
        Assert.Equal(before, await Counted());
    }

    [Fact]
    public async Task Another_participant_editing_or_removing_is_refused_with_not_yours_as_a_problem_and_on_the_form_and_nothing_is_written()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        var root = await Started(writer, Ada, "Lanterns", "How do you hang yours?");
        using var bea = await SignedIn(tangentSpace, Bea);
        var before = await Counted();
        var edit = PostPath(root, root) + "/edit";
        var remove = PostPath(root, root) + "/remove";

        var page = await Send(bea.Client, HttpMethod.Get, edit);
        var editJson = await Json(bea, edit, edit, new { title = "Mine", body = "Mine now.", version = 1 });
        var editForm = await Form(bea, edit, edit, new Dictionary<string, string> { ["title"] = "Mine", ["body"] = "Mine now.", ["version"] = "1" });
        var removeJson = await Json(bea, edit, remove, new { });
        var removeForm = await Form(bea, edit, remove, new Dictionary<string, string>());

        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains("data-gate=\"not yours\"", page.Body);
        Assert.DoesNotContain("type=\"submit\"", page.Body);
        Assert.DoesNotContain("post-remove", page.Body);
        Assert.Equal((HttpStatusCode.Forbidden, "application/problem+json", Capabilities.NotYours), (editJson.Status, editJson.MediaType, editJson.Gate));
        Assert.Equal(HttpStatusCode.Forbidden, editForm.Status);
        Assert.Contains("data-gate=\"not yours\"", editForm.Body);
        Assert.Contains("Only its author, or a moderator, can change this post.", editForm.Body);
        Assert.Contains(">Mine now.</textarea>", editForm.Body);
        Assert.Equal((HttpStatusCode.Forbidden, Capabilities.NotYours), (removeJson.Status, removeJson.Gate));
        Assert.Equal(HttpStatusCode.Forbidden, removeForm.Status);
        Assert.Contains("data-gate=\"not yours\"", removeForm.Body);
        Assert.Equal(before, await Counted());
        var stored = await Post.Get(root.Id, Cancellation);
        Assert.Equal(("How do you hang yours?", 1L, false), (stored?.Body, stored?.Version, stored?.Removed));
    }

    [Fact]
    public async Task A_reply_in_a_locked_topic_is_refused_with_locked_as_a_problem_and_as_the_page_and_nothing_is_written()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        var locked = Post.Root(Tids.Mint(), Workshop, "Closed", "closed", PostKind.Topic, "No more replies.", Mo, DateTimeOffset.UtcNow);
        locked.Locked = true;
        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new Store<Post>(locked), Cancellation));
        using var ada = await SignedIn(tangentSpace, Ada);
        var before = await Counted();

        var asJson = await Json(ada, "/t/" + Workshop + "/new", TopicPath(locked), new { id = Tids.Mint(), body = "Still?" });
        var onForm = await Form(ada, "/t/" + Workshop + "/new", TopicPath(locked), new Dictionary<string, string> { ["id"] = Tids.Mint(), ["body"] = "Still?" });

        Assert.Equal((HttpStatusCode.Forbidden, "application/problem+json", Capabilities.Locked), (asJson.Status, asJson.MediaType, asJson.Gate));
        Assert.Equal(("text/html", HttpStatusCode.Forbidden), (onForm.MediaType, onForm.Status));
        Assert.Contains("data-gate=\"locked\"", onForm.Body);
        Assert.Contains("This topic is locked, so no one replies in it.", onForm.Body);
        Assert.Equal(before, await Counted());
    }

    [Fact]
    public async Task A_tangent_the_reader_may_not_view_answers_every_write_as_a_missing_address_does_and_nothing_is_written()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        var hidden = Post.Root(Tids.Mint(), Studio, "Plans", "plans", PostKind.Topic, "Not for everyone.", Mo, DateTimeOffset.UtcNow);
        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new Store<Post>(hidden), Cancellation));
        using var ada = await SignedIn(tangentSpace, Ada);
        var before = await Counted();
        const string page = "/t/" + Workshop + "/new";

        var form = await Send(ada.Client, HttpMethod.Get, "/t/" + Studio + "/new", json: true);
        var nowhere = await Send(ada.Client, HttpMethod.Get, "/t/nowhere/new", json: true);
        var start = await Json(ada, page, "/t/" + Studio + "/new", new { id = Tids.Mint(), title = "In here", body = "Let me in." });
        var startNowhere = await Json(ada, page, "/t/nowhere/new", new { id = Tids.Mint(), title = "In here", body = "Let me in." });
        var startForm = await Form(ada, page, "/t/" + Studio + "/new", new Dictionary<string, string> { ["id"] = Tids.Mint(), ["title"] = "In here", ["body"] = "Let me in." });
        var reply = await Json(ada, page, TopicPath(hidden), new { id = Tids.Mint(), body = "Let me in." });
        var edit = await Json(ada, page, PostPath(hidden, hidden) + "/edit", new { body = "Let me in.", version = 1 });
        var editPage = await Send(ada.Client, HttpMethod.Get, PostPath(hidden, hidden) + "/edit", json: true);
        var remove = await Json(ada, page, PostPath(hidden, hidden) + "/remove", new { });
        var removeForm = await Form(ada, page, PostPath(hidden, hidden) + "/remove", new Dictionary<string, string>());

        var missing = (HttpStatusCode.NotFound, "Not found", Capabilities.Absent);
        Assert.Equal(missing, (nowhere.Status, nowhere.Title, nowhere.Gate));
        Assert.Equal(missing, (startNowhere.Status, startNowhere.Title, startNowhere.Gate));
        foreach (var answered in new[] { form, start, reply, edit, editPage, remove })
            Assert.Equal(missing, (answered.Status, answered.Title, answered.Gate));
        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (startForm.Status, removeForm.Status));
        Assert.Contains("data-gate=\"absent\"", startForm.Body);
        Assert.DoesNotContain("Let me in.", startForm.Body);
        Assert.Equal(before, await Counted());
        Assert.Equal(("Not for everyone.", false), ((await Post.Get(hidden.Id, Cancellation))?.Body, (await Post.Get(hidden.Id, Cancellation))?.Removed));
    }
}
