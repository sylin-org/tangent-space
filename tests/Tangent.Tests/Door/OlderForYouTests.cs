using System.Net;
using System.Text.Json;
using Koan.Data.Core.Commands;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Events;
using Tangent.Posts;
using Tangent.Tools;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Space;

/// <summary>The door paged back: what reached a reader and is no longer new, newest first, at the door's own address with
/// <c>before</c>, a page at a time, never what is still new or what was someone else's; the same through <c>arrive</c>, and
/// a <c>before</c> that is no sequence refused.</summary>
public sealed class OlderForYouTests
{
    private static readonly string Cy = Persona.Id("cy4older");

    private static async Task<JsonElement> Door(Browser session, string path = "/") => (await Send(session.Client, HttpMethod.Get, path, json: true)).Json;

    private static string Older(JsonElement door) => new Uri(door.GetProperty("news").GetProperty("older").GetString()!).PathAndQuery;

    private static JsonElement[] Items(JsonElement past) => [.. past.GetProperty("older").GetProperty("items").EnumerateArray()];

    [Fact]
    public async Task What_reached_a_reader_and_was_read_pages_back_newest_first_without_what_is_still_new_or_someone_elses()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Cy, [Roles.Member], ct: Cancellation);
        var root = await Started(Ada, "Lanterns", "Who lights theirs first?");
        var first = await Replied(root, Bea, "I do, at six.");
        var second = await Replied(root, Bea, "Or at five in winter.");
        var cys = await Started(Cy, "Kites", "Where do you fly yours?");
        await Replied(cys, Bea, "On the hill.");
        using var ada = await SignedIn(tangentSpace, Ada);

        // Still new, they stand on the door and not among the older.
        var door = await Door(ada);
        Assert.Equal(2, door.GetProperty("news").GetProperty("replies").GetProperty("count").GetInt32());
        Assert.Empty(Items(await Door(ada, Older(door))));

        // Read, they leave the door and stand among the older, newest first; the reply to Cy is never Ada's.
        Assert.IsType<Outcome<Checkpoint>.Done>(await new Seen(Ada, 2, root.Id).Send(Cancellation));
        door = await Door(ada);
        Assert.Equal(0, door.GetProperty("news").GetProperty("replies").GetProperty("count").GetInt32());
        var past = await Door(ada, Older(door));
        Assert.Equal([(second.Id, "reply"), (first.Id, "reply")],
            Items(past).Select(item => (item.GetProperty("post").GetProperty("postId").GetString(), item.GetProperty("kind").GetString())).ToList());
        Assert.False(past.GetProperty("older").TryGetProperty("next", out var next) && next.ValueKind == JsonValueKind.String);
        var html = (await Send(ada.Client, HttpMethod.Get, Older(door))).Body;
        Assert.Contains("Older for you", html);
        Assert.Contains("replied to you", html);
        Assert.DoesNotContain("data-island=\"live\"", html);
        Assert.DoesNotContain("data-island=\"fragment\"", html);

        // A third reply, still new, stays on the door alone.
        var third = await Replied(root, Bea, "And at four on the shortest day.");
        Assert.DoesNotContain(Items(await Door(ada, Older(await Door(ada)))), item => item.GetProperty("post").GetProperty("postId").GetString() == third.Id);

        // The same through arrive, and a before that is no sequence is refused.
        using (var tools = await ToolClient.Connect(tangentSpace, ada))
        {
            var before = new Uri(door.GetProperty("news").GetProperty("older").GetString()!).Query.Split('=')[1];
            var arrived = await tools.Call("arrive", new { before });
            Assert.False(arrived.Failed, arrived.Text);
            Assert.Equal(2, arrived.Structured!.Value.GetProperty("older").GetProperty("items").GetArrayLength());
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(ada.Client, HttpMethod.Get, "/?before=abc", json: true)).Status);
    }

    [Fact]
    public async Task Many_things_read_page_back_twenty_at_a_time_each_on_exactly_one_page()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var root = await Started(Ada, "Lanterns", "Who lights theirs first?");
        var replies = new List<string>();
        for (var number = 1; number <= 25; number++) replies.Add((await Replied(root, Bea, "Reply " + number + ".")).Id);
        Assert.IsType<Outcome<Checkpoint>.Done>(await new Seen(Ada, 25, root.Id).Send(Cancellation));
        using var ada = await SignedIn(tangentSpace, Ada);

        var first = await Door(ada, Older(await Door(ada)));
        Assert.Equal(Unread.OlderLimit, Items(first).Length);
        var then = await Door(ada, new Uri(first.GetProperty("older").GetProperty("next").GetString()!).PathAndQuery);
        Assert.Equal(5, Items(then).Length);
        Assert.False(then.GetProperty("older").TryGetProperty("next", out var next) && next.ValueKind == JsonValueKind.String);
        var paged = Items(first).Concat(Items(then)).Select(item => item.GetProperty("post").GetProperty("postId").GetString()!).ToList();
        Assert.Equal(Enumerable.Reverse(replies).ToList(), paged);
    }

    [Fact]
    public async Task Replies_and_reports_read_together_page_back_twenty_at_a_time_in_all_each_on_exactly_one_page()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        Assert.IsType<Outcome<Tangents.Tangent>.Done>(await new CreateModeratorsTangent().Send(Cancellation));
        var root = await Started(Mo, "Lanterns", "Who lights theirs first?");
        var reached = new List<string>();
        for (var number = 1; number <= 25; number++)
        {
            var reply = await Replied(root, Bea, "Reply " + number + ".");
            var report = Assert.IsType<Outcome<Post>.Done>(await new ReportPost(Tids.Mint(), reply.Id, PostBody.Of("Reason " + number + "."), Ada).Send(Cancellation)).Result;
            reached.AddRange([reply.Id, report.Id]);
        }
        using var mo = await SignedIn(tangentSpace, Mo);
        Assert.IsType<Outcome<Checkpoint>.Done>(await new Seen(Mo, (await Door(mo)).GetProperty("news").GetProperty("through").GetInt64()).Send(Cancellation));

        var pages = new List<JsonElement[]>();
        for (var path = Older(await Door(mo)); path is not null && pages.Count < 4;)
        {
            var page = await Door(mo, path);
            pages.Add(Items(page));
            path = page.GetProperty("older").TryGetProperty("next", out var next) && next.ValueKind == JsonValueKind.String ? new Uri(next.GetString()!).PathAndQuery : null;
        }
        Assert.Equal([Unread.OlderLimit, Unread.OlderLimit, 10], pages.Select(page => page.Length).ToList());
        Assert.Equal(Enumerable.Reverse(reached).ToList(), pages.SelectMany(page => page).Select(item => item.GetProperty("post").GetProperty("postId").GetString()!).ToList());
    }
}
