using System.Net;
using System.Text.Json;
using Koan.Data.Core.Commands;
using Tangent.Access;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Posts;

/// <summary>Closing a Topic: whoever holds Moderate in its Tangent closes it from its root's footer, as a form or as JSON;
/// every word stays, its reply box says who closed it and when, a reply from anyone else is refused with <c>closed</c>,
/// its moderators still reply, its lists mark it, and reopening lets everyone reply again. Nothing reaches the log.</summary>
public sealed class ClosingTests
{
    [Fact]
    public async Task A_moderator_closes_a_topic_and_it_keeps_its_words_and_takes_replies_from_its_moderators_alone_until_they_reopen_it()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var root = await Started(Ada, "Lanterns", "How do you hang yours?");
        await Replied(root, Bea, "From a hook by the door.");
        using var ada = await SignedIn(tangentSpace, Ada);
        using var mo = await SignedIn(tangentSpace, Mo);
        var events = (await Counted()).Events;

        // Mo follows the closing form the Topic's JSON gives them, as an agent does.
        var forMo = (await Send(mo.Client, HttpMethod.Get, TopicPath(root), json: true)).Json;
        var closed = await Follow(mo, forMo.GetProperty("closing"), new Dictionary<string, string>());

        Assert.Equal((HttpStatusCode.OK, true, false), (closed.Status, closed.Json.GetProperty("closed").GetBoolean(), closed.Json.GetProperty("pinned").GetBoolean()));
        Assert.Equal(BaseUrl + TopicPath(root), closed.Json.GetProperty("address").GetString());
        var stored = await Post.Get(root.Id, Cancellation);
        Assert.Equal((true, Mo, "How do you hang yours?", 1L), (stored?.Closed, stored?.ClosedBy, stored?.Body, stored?.Version));

        // Ada's reply is refused, by form and by JSON, with the word and its sentence.
        var byForm = await Form(ada, TopicPath(root), TopicPath(root), new Dictionary<string, string> { ["id"] = Tids.Mint(), ["body"] = "Still open?" });
        var byJson = await Json(ada, TopicPath(root), TopicPath(root), new { id = Tids.Mint(), body = "Still open?" });

        Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, Capabilities.Closed.Word), (byForm.Status, byJson.Status, byJson.Word));
        Assert.Contains(Capabilities.Closed.Text, WebUtility.HtmlDecode(byForm.Body));

        // Her page keeps every word and says who closed it in place of the reply box; its JSON says it, with no form to reply.
        var page = WebUtility.HtmlDecode((await Send(ada.Client, HttpMethod.Get, TopicPath(root))).Body);
        Assert.Contains("<p class=\"reply-gate is-closed\" data-gate=\"closed\">Closed by ", page);
        Assert.Contains("every word stays, and it takes no new replies.", page);
        Assert.Contains("From a hook by the door.", page);
        Assert.Contains("<span class=\"badge is-closed\">Closed</span>", page);
        Assert.DoesNotContain("class=\"reply-form reply\"", page);
        var forAda = (await Send(ada.Client, HttpMethod.Get, TopicPath(root), json: true)).Json;
        Assert.Equal((Mo, Capabilities.Closed.Word), (forAda.GetProperty("closed").GetProperty("by").GetProperty("id").GetString(), forAda.GetProperty("gate").GetString()));
        Assert.True(Absent(forAda, "reply") && Absent(forAda, "closing") && Absent(forAda, "pinning"));

        // Mo still replies, under the line that says so; the Tangent lists the Topic closed.
        var asMo = WebUtility.HtmlDecode((await Send(mo.Client, HttpMethod.Get, TopicPath(root))).Body);
        Assert.Contains("only moderators reply now.", asMo);
        Assert.Contains("class=\"reply-form reply\"", asMo);
        Assert.Contains($"action=\"{BaseUrl + TopicPath(root)}/reopen\"", asMo);
        Assert.Equal(HttpStatusCode.Created, (await Json(mo, TopicPath(root), TopicPath(root), new { id = Tids.Mint(), body = "Closing word: hooks win." })).Status);
        var listed = (await Send(ada.Client, HttpMethod.Get, "/t/" + Workshop, json: true)).Json.GetProperty("topics").EnumerateArray()
            .Single(topic => topic.GetProperty("id").GetString() == root.Id);
        Assert.True(listed.GetProperty("closed").GetBoolean());

        // Closing again changes nothing; reopening by form lets Ada reply. The log took only the replies.
        var again = await Json(mo, TopicPath(root), TopicPath(root) + "/" + TopicPageController.CloseSegment, new { });
        Assert.Equal((HttpStatusCode.OK, stored?.ClosedAt), (again.Status, (await Post.Get(root.Id, Cancellation))?.ClosedAt));
        var reopened = await Form(mo, TopicPath(root), TopicPath(root) + "/" + TopicPageController.ReopenSegment, new Dictionary<string, string>());

        Assert.Equal((HttpStatusCode.SeeOther, BaseUrl + TopicPath(root)), (reopened.Status, reopened.Location));
        Assert.Equal(HttpStatusCode.Created, (await Json(ada, TopicPath(root), TopicPath(root), new { id = Tids.Mint(), body = "Open again." })).Status);
        Assert.Equal(events + 2, (await Counted()).Events);
        var open = await Post.Get(root.Id, Cancellation);
        Assert.Equal((false, (string?)null), (open?.Closed, open?.ClosedBy));
    }

    [Fact]
    public async Task Only_a_moderator_closes_or_pins_a_topic_and_no_one_closes_or_pins_a_removed_one()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var root = await Started(Ada, "Lanterns", "How do you hang yours?");
        using var ada = await SignedIn(tangentSpace, Ada);

        var close = await Json(ada, TopicPath(root), TopicPath(root) + "/" + TopicPageController.CloseSegment, new { });
        var pin = await Json(ada, TopicPath(root), TopicPath(root) + "/" + TopicPageController.PinSegment, new { });
        var page = (await Send(ada.Client, HttpMethod.Get, TopicPath(root), json: true)).Json;

        Assert.Equal((HttpStatusCode.Forbidden, "gated"), (close.Status, close.Kind));
        Assert.Equal((HttpStatusCode.Forbidden, "gated"), (pin.Status, pin.Kind));
        Assert.True(Absent(page, "closing") && Absent(page, "pinning"));
        Assert.Equal((false, (string?)null), ((await Post.Get(root.Id, Cancellation))?.Closed, (await Tangents.Tangent.Get(Workshop, Cancellation))?.PinnedId));

        Assert.IsType<Outcome<Post>.Done>(await new RemovePost(root.Id, Mo).Send(Cancellation));
        Assert.Equal(Capabilities.Removed, Assert.IsType<Outcome<Post>.Refused>(await new CloseTopic(root.Id, Mo).Send(Cancellation)).Reason);
        Assert.Equal(Capabilities.Removed, Assert.IsType<Outcome<Tangents.Tangent>.Refused>(await new Tangents.PinTopic(root.Id, Mo).Send(Cancellation)).Reason);
    }

    private static bool Absent(JsonElement page, string name) => !page.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null;
}
