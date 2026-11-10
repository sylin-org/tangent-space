using System.Net;
using System.Text.Json;
using Koan.Data.Core.Commands;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Posts;

/// <summary>A Post's writes and reads on Koan's entity endpoint (<see cref="TopicsController"/>, <see cref="PostsController"/>): only
/// what they declare is served and no partition is, a link another site marked to any declared read is sent on to its clean address, a start sent with no
/// id mints its own TID, and a moderator's edit takes the moderator as who reaches the images it names.</summary>
public sealed class EntityWritesTests
{
    [Fact]
    public async Task The_audit_partition_is_refused_naming_set_on_every_read_and_write_and_nothing_undeclared_is_served()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var root = await Started(Ada, "Lanterns", "How do you hang yours?");
        var reply = await Replied(root, Ada, "From the porch beam.");
        Assert.IsType<Outcome<Post>.Done>(await new Capture.Edited(reply.Id, 1, "From the porch beam, at dusk.", null, Ada).Send(Cancellation));
        using var ada = await SignedIn(tangentSpace, Ada);
        var before = (await Counted(), await Snapshots());
        const string audit = "?set=" + AuditPartition.Name;

        var refused = new[]
        {
            await Send(ada.Client, HttpMethod.Get, TopicPath(root) + audit, json: true),
            await Send(ada.Client, HttpMethod.Get, PostPath(root, reply) + audit, json: true),
            await Send(ada.Client, HttpMethod.Post, TopicPath(root) + audit, JsonBody(new { id = Tids.Mint(), body = "Into the audit." }), json: true),
            await Send(ada.Client, HttpMethod.Post, "/t/" + Workshop + audit, JsonBody(new { id = Tids.Mint(), title = "Audit", body = "Into the audit." }), json: true),
            await Send(ada.Client, HttpMethod.Patch, PostPath(root, reply) + audit, JsonBody(new { body = "Rewritten there.", version = 2 }), json: true)
        };
        var unserved = new[]
        {
            await Send(ada.Client, HttpMethod.Put, PostPath(root, reply), JsonBody(new { body = "Replaced." }), json: true),
            await Send(ada.Client, HttpMethod.Delete, PostPath(root, reply), json: true),
            await Send(ada.Client, HttpMethod.Get, TopicPath(root) + "/query", json: true),
            await Send(ada.Client, HttpMethod.Delete, TopicPath(root) + "/all", json: true)
        };

        Assert.All(refused, answered => Assert.Equal((HttpStatusCode.BadRequest, true), (answered.Status, answered.Json.GetProperty("errors").TryGetProperty("set", out _))));
        Assert.All(refused, answered => Assert.DoesNotContain("From the porch beam.\"", answered.Body));
        Assert.All(unserved, answered => Assert.True(answered.Status is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, answered.Status + " " + answered.Body));
        Assert.Equal(before, (await Counted(), await Snapshots()));
        Assert.Equal("From the porch beam, at dusk.", (await Post.Get(reply.Id, Cancellation))?.Body);

        // A link another site marked opens the Post and the Topic at their addresses, the window it names kept, for a page and for JSON alike:
        // the word it added is ignored.
        var marked = await Send(ada.Client, HttpMethod.Get, PostPath(root, reply) + "?fbclid=abc");
        var windowed = await Send(ada.Client, HttpMethod.Get, TopicPath(root) + "?before=2&utm_source=elsewhere");
        var markedJson = await Send(ada.Client, HttpMethod.Get, PostPath(root, reply) + "?fbclid=abc", json: true);
        Assert.Equal((HttpStatusCode.OK, null), (marked.Status, marked.Location));
        Assert.Equal((HttpStatusCode.OK, null), (windowed.Status, windowed.Location));
        // Each read mints its reply form a fresh id, so the reads are compared by the window they open.
        Assert.Equal(HttpStatusCode.OK, markedJson.Status);
        Assert.Equal(Window((await Send(ada.Client, HttpMethod.Get, PostPath(root, reply), json: true)).Json), Window(markedJson.Json));
    }

    /// <summary>The Topic a read opens, the Post it opens at, and the Posts its window shows.</summary>
    private static string Window(JsonElement page)
        => string.Join(" ", new[] { page.GetProperty("id").GetString(), page.GetProperty("anchor").ToString(), page.GetProperty("root").GetProperty("id").GetString() }
            .Concat(page.GetProperty("replies").EnumerateArray().Select(post => post.GetProperty("id").GetString())));

    [Fact]
    public async Task A_link_another_site_marked_to_any_declared_read_opens_its_page_and_json_reads_what_it_reads_without_the_mark()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var leo = Persona.Id("leo4marks");
        await Given.AParticipant(tangentSpace, leo, [Access.Roles.Administrator], ct: Cancellation);
        using var proprietor = await SignedIn(tangentSpace, leo);
        string[] addresses = ["/t", "/p/" + Ada, "/t/new", "/t/" + Workshop + "/new", Participants.ApplicationsController.Path];

        foreach (var address in addresses)
        {
            var marked = await Send(proprietor.Client, HttpMethod.Get, address + "?fbclid=abc");
            Assert.Equal((HttpStatusCode.OK, null, "text/html"), (marked.Status, marked.Location, marked.MediaType));
        }

        var tagged = await Send(proprietor.Client, HttpMethod.Get, "/p/" + Ada + "?utm_source=newsletter&utm_medium=email");
        var asJson = await Send(proprietor.Client, HttpMethod.Get, "/t?fbclid=abc", json: true);
        Assert.Equal((HttpStatusCode.OK, null), (tagged.Status, tagged.Location));
        Assert.Equal(HttpStatusCode.OK, asJson.Status);
        Assert.Equal((await Send(proprietor.Client, HttpMethod.Get, "/t", json: true)).Json.GetProperty("tangents").GetRawText(),
            asJson.Json.GetProperty("tangents").GetRawText());
    }

    [Fact]
    public async Task A_start_and_a_reply_sent_with_no_id_each_mint_a_tid_and_one_whose_id_is_no_tid_is_refused_naming_it()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        using var ada = await SignedIn(tangentSpace, Ada);

        var started = await Json(ada, "/t/" + Workshop + "/new", "/t/" + Workshop, new { title = "Lanterns", body = "How do you hang yours?" });
        var replied = await Json(ada, "/t/" + Workshop + "/lanterns", "/t/" + Workshop + "/lanterns", new { body = "From the porch beam." });
        var notATid = await Json(ada, "/t/" + Workshop + "/lanterns", "/t/" + Workshop + "/lanterns", new { id = "not-a-tid", body = "Hello." });

        Assert.Equal(HttpStatusCode.Created, started.Status);
        Assert.True(Tid.IsValid(started.Json.GetProperty("id").GetString()));
        Assert.Equal(HttpStatusCode.Created, replied.Status);
        Assert.True(Tid.IsValid(replied.Json.GetProperty("id").GetString()));
        Assert.Equal((HttpStatusCode.BadRequest, Post.NeedsTid.Text), (notATid.Status, notATid.Json.GetProperty("errors").GetProperty(PostWrites.IdField)[0].GetString()));
    }

    [Fact]
    public async Task A_moderators_edit_names_who_edited_and_takes_them_as_who_reaches_the_images_it_names()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var root = await Started(Ada, "Lanterns", "How do you hang yours?");
        var reply = await Replied(root, Ada, "From the porch beam.");
        using var mo = await SignedIn(tangentSpace, Mo);
        var image = ArticleMediaTests.IdOf(await ArticleMediaTests.Bring(mo, ArticleMediaTests.Jpeg(30), Workshop, TopicPath(root)));

        var edited = await Json(mo, PostPath(root, reply) + "/edit", HttpMethod.Patch, PostPath(root, reply), new { body = "From the porch beam. ![The beam](/media/" + image + ")", version = 1 });

        Assert.Equal((HttpStatusCode.OK, Mo, 2L), (edited.Status, edited.Json.GetProperty("editedBy").GetString(), edited.Json.GetProperty("version").GetInt64()));
        Assert.NotNull(await MediaUse.Get(MediaUse.Key(image, reply.Id), Cancellation));
        Assert.Equal(Ada, (await Post.Get(reply.Id, Cancellation))?.AuthorId);
    }
}
