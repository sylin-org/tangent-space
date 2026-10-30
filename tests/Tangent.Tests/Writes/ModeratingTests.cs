using System.Text.Json;
using Tangent.Events;
using Tangent.Writer;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Posts;

/// <summary>A moderator's hand on someone's Post: an edit by anyone but the author is marked with who wherever the Post
/// is read, as a page and as JSON, until the author's own edit makes the words theirs again; and the author is told of
/// someone else's edit or removal in their notifications and on their door, with who did it. Editing or removing your own
/// Post marks nothing and tells no one.</summary>
public sealed class ModeratingTests
{
    [Fact]
    public async Task A_moderators_edit_shows_who_wherever_the_post_is_read_and_tells_the_author()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        var root = await Started(writer, Ada, "Lanterns", "How do you hang yours?");
        var reply = await Replied(writer, root, Ada, "From the porch beam.");
        using var ada = await SignedIn(tangentSpace, Ada);
        using var mo = await SignedIn(tangentSpace, Mo);
        var mark = "Edited by " + Name(await Send(mo.Client, HttpMethod.Get, "/", json: true));
        var inline = "(edited by " + Name(await Send(mo.Client, HttpMethod.Get, "/", json: true)) + ")";
        var visitor = Agent(tangentSpace);

        var written = await Json(mo, PostPath(root, reply) + "/edit", PostPath(root, reply) + "/edit", new { body = "From a hook, says the moderator.", version = 1 });
        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new EditPost(root.Id, 1, "How do you hang yours, and where?", null, Mo), Cancellation));

        // The write's own answer says who.
        Assert.Equal(Mo, written.Json.GetProperty("editedBy").GetString());
        Assert.Equal(JsonValueKind.String, written.Json.GetProperty("editedAt").ValueKind);

        // The Topic, both ways, for anyone who reads it.
        var topic = (await Send(visitor, HttpMethod.Get, TopicPath(root), json: true)).Json;
        Assert.Equal(Mo, topic.GetProperty("root").GetProperty("edited").GetProperty("by").GetProperty("id").GetString());
        Assert.Equal(Mo, topic.GetProperty("replies")[0].GetProperty("edited").GetProperty("by").GetProperty("id").GetString());
        Assert.Equal(2, Occurrences((await Send(visitor, HttpMethod.Get, TopicPath(root))).Body, "<p class=\"post-edited\">" + mark));

        // Its first words, wherever they are shown beside their author.
        var tangent = (await Send(visitor, HttpMethod.Get, "/t/" + Workshop, json: true)).Json.GetProperty("topics")[0];
        Assert.Equal(Mo, tangent.GetProperty("edited").GetProperty("by").GetProperty("id").GetString());
        Assert.Equal(Mo, tangent.GetProperty("latest").GetProperty("edited").GetProperty("by").GetProperty("id").GetString());
        Assert.Equal(2, Occurrences((await Send(visitor, HttpMethod.Get, "/t/" + Workshop)).Body, inline));
        var profile = (await Send(visitor, HttpMethod.Get, "/p/ada4writes.example", json: true)).Json.GetProperty("posts");
        Assert.All(profile.EnumerateArray(), post => Assert.Equal(Mo, post.GetProperty("edited").GetProperty("by").GetProperty("id").GetString()));
        Assert.Contains("Replied " + inline + ": From a hook, says the moderator.", (await Send(visitor, HttpMethod.Get, "/p/ada4writes.example")).Body);
        Assert.Contains("<p class=\"post-edited\">" + mark + "</p>", (await Send(mo.Client, HttpMethod.Get, PostPath(root, reply) + "/report")).Body);
        Assert.Contains("<p class=\"post-edited\">" + mark + "</p>", (await Send(ada.Client, HttpMethod.Get, PostPath(root, reply) + "/tangent")).Body);
        Assert.Contains("Last edited by", (await Send(ada.Client, HttpMethod.Get, PostPath(root, reply) + "/edit")).Body);

        // The author is told, as any event that targets them is, with who did it; the moderator is not.
        var edits = await Event.Query(e => e.Kind == EventKind.PostEdited, Cancellation);
        Assert.Equal(2, edits.Count);
        Assert.All(edits, e => Assert.Equal((Ada, Mo), (e.TargetId, e.ActorId)));
        var door = (await Send(ada.Client, HttpMethod.Get, "/", json: true)).Json;
        Assert.Equal(2, door.GetProperty("who").GetProperty("notifications").GetProperty("count").GetInt32());
        var told = door.GetProperty("news").GetProperty("edited");
        Assert.Equal(2, told.GetProperty("count").GetInt32());
        Assert.All(told.GetProperty("posts").EnumerateArray(), post => Assert.Equal(Mo, post.GetProperty("who").GetProperty("id").GetString()));
        var doorPage = (await Send(ada.Client, HttpMethod.Get, "/")).Body;
        Assert.Contains("Your posts someone else edited · 2", doorPage);
        Assert.Contains("edited your post: From a hook, says the moderator.", doorPage);
        var notifications = (await Send(ada.Client, HttpMethod.Get, Notifications.Path, json: true)).Json.GetProperty("entries");
        Assert.All(notifications.EnumerateArray(), entry => Assert.Equal(("edited", Mo), (entry.GetProperty("kind").GetString(), entry.GetProperty("who").GetProperty("id").GetString())));
        Assert.Contains("edited your post in", (await Send(ada.Client, HttpMethod.Get, Notifications.Path)).Body);
        Assert.Equal(0, (await Send(mo.Client, HttpMethod.Get, "/", json: true)).Json.GetProperty("who").GetProperty("notifications").GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task A_moderators_removal_tells_the_author_who_did_it_and_a_tombstone_has_no_mark_of_an_edit_before_it()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        var root = await Started(writer, Bea, "Lanterns", "How do you hang yours?");
        var reply = await Replied(writer, root, Ada, "From the porch beam.");
        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new EditPost(reply.Id, 1, "From a hook, says the moderator.", null, Mo), Cancellation));
        using var ada = await SignedIn(tangentSpace, Ada);

        var removed = Assert.IsType<Outcome<Post>.Done>(await writer.Send(new RemovePost(reply.Id, Mo), Cancellation)).Result;

        Assert.Equal((Mo, null, null), (removed.RemovedBy, removed.EditedBy, removed.EditedAt));
        var removal = Assert.Single(await Event.Query(e => e.Kind == EventKind.PostRemoved, Cancellation));
        Assert.Equal((Ada, Mo), (removal.TargetId, removal.ActorId));
        var shown = (await Send(ada.Client, HttpMethod.Get, TopicPath(root), json: true)).Json.GetProperty("replies")[0];
        Assert.Equal(Mo, shown.GetProperty("removed").GetProperty("by").GetProperty("id").GetString());
        Assert.False(shown.TryGetProperty("edited", out var edited) && edited.ValueKind != JsonValueKind.Null);

        var door = (await Send(ada.Client, HttpMethod.Get, "/", json: true)).Json;
        Assert.Equal(2, door.GetProperty("who").GetProperty("notifications").GetProperty("count").GetInt32());
        var news = door.GetProperty("news");
        Assert.Equal(0, news.GetProperty("edited").GetProperty("count").GetInt32());
        var told = Assert.Single(news.GetProperty("removed").GetProperty("posts").EnumerateArray());
        Assert.Equal((reply.Id, Mo, ""), (told.GetProperty("postId").GetString(), told.GetProperty("who").GetProperty("id").GetString(), told.GetProperty("line").GetString()));
        var doorPage = (await Send(ada.Client, HttpMethod.Get, "/")).Body;
        Assert.Contains("Your posts someone else removed · 1", doorPage);
        Assert.Contains("removed your post</span>", doorPage);

        var entries = (await Send(ada.Client, HttpMethod.Get, Notifications.Path, json: true)).Json.GetProperty("entries");
        Assert.Equal(("removed", Mo, true), (entries[0].GetProperty("kind").GetString(), entries[0].GetProperty("who").GetProperty("id").GetString(), entries[0].GetProperty("removed").GetBoolean()));
        Assert.Equal(("edited", true), (entries[1].GetProperty("kind").GetString(), entries[1].GetProperty("removed").GetBoolean()));
        var page = (await Send(ada.Client, HttpMethod.Get, Notifications.Path)).Body;
        Assert.Contains("removed your post in", page);
        Assert.Contains("edited your post in", page);
        Assert.Contains("— since removed", System.Net.WebUtility.HtmlDecode(page));
    }

    [Fact]
    public async Task An_authors_own_edit_and_removal_mark_nothing_and_tell_no_one_and_their_edit_makes_a_moderators_words_theirs_again()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var writer = await Place(tangentSpace);
        var root = await Started(writer, Ada, "Lanterns", "How do you hang yours?");
        var reply = await Replied(writer, root, Ada, "From the porch beam.");
        using var ada = await SignedIn(tangentSpace, Ada);

        var own = await Json(ada, PostPath(root, root) + "/edit", PostPath(root, root) + "/edit", new { body = "How do you hang yours, and where?", version = 1 });
        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new RemovePost(reply.Id, Ada), Cancellation));

        Assert.False(own.Json.TryGetProperty("editedBy", out var editedBy) && editedBy.ValueKind != JsonValueKind.Null);
        Assert.All(await Event.Query(e => e.Kind == EventKind.PostEdited || e.Kind == EventKind.PostRemoved, Cancellation), e => Assert.Null(e.TargetId));
        var read = await Send(Agent(tangentSpace), HttpMethod.Get, TopicPath(root));
        Assert.DoesNotContain("post-edited", read.Body);
        var door = (await Send(ada.Client, HttpMethod.Get, "/", json: true)).Json;
        Assert.Equal(0, door.GetProperty("who").GetProperty("notifications").GetProperty("count").GetInt32());
        Assert.Equal((0, 0), (door.GetProperty("news").GetProperty("edited").GetProperty("count").GetInt32(), door.GetProperty("news").GetProperty("removed").GetProperty("count").GetInt32()));

        // A moderator's own Post is theirs like anyone's.
        var theirs = await Started(writer, Mo, "On wicks", "Trim them.");
        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new EditPost(theirs.Id, 1, "Trim them short.", null, Mo), Cancellation));
        Assert.Null((await Post.Get(theirs.Id, Cancellation))?.EditedBy);

        // The moderator's words carry the mark until the author's own edit makes the words theirs again; the telling stays.
        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new EditPost(root.Id, 2, "Hung from the beam, says the moderator.", null, Mo), Cancellation));
        Assert.Equal(Mo, (await Post.Get(root.Id, Cancellation))?.EditedBy);
        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new EditPost(root.Id, 3, "Hung from the beam, I say.", null, Ada), Cancellation));
        var mine = await Post.Get(root.Id, Cancellation);
        Assert.Equal((null, null), (mine?.EditedBy, mine?.EditedAt));
        Assert.DoesNotContain("post-edited", (await Send(Agent(tangentSpace), HttpMethod.Get, TopicPath(root))).Body);
        Assert.Equal(1, (await Send(ada.Client, HttpMethod.Get, "/", json: true)).Json.GetProperty("who").GetProperty("notifications").GetProperty("count").GetInt32());
    }

    private static string Name(Answered door) => door.Json.GetProperty("who").GetProperty("name").GetString()!;

    private static int Occurrences(string text, string part) => text.Split(part).Length - 1;
}
