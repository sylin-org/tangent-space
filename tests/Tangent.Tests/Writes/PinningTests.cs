using System.Net;
using System.Text.Json;
using Koan.Data.Core.Commands;
using Tangent.Tangents;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Posts;

/// <summary>Pinning a Topic: whoever holds Moderate in its Tangent pins it from its root's footer, and it leads the
/// Tangent's first page and its shelf on the door, marked, and is listed nowhere else on any page; pinning another names
/// the one it unpins first and takes its place; unpinning, or removing its root, puts it back among the rest.</summary>
public sealed class PinningTests
{
    [Fact]
    public async Task A_pinned_topic_leads_its_tangent_and_its_shelf_once_and_unpinned_it_stands_in_its_place_again()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var rules = await Started(Mo, "House rules", "Be kind; stay on the work.");
        var later = new List<Post>();
        for (var index = 1; index <= TopicList.PageSize + 1; index++) later.Add(await Started(Ada, "Question " + index, "What about " + index + "?"));
        using var mo = await SignedIn(tangentSpace, Mo);
        var visitor = Agent(tangentSpace);

        var pinned = await Json(mo, TopicPath(rules), TopicPath(rules) + "/" + TopicPageController.PinSegment, new { });

        Assert.Equal((HttpStatusCode.OK, true), (pinned.Status, pinned.Json.GetProperty("pinned").GetBoolean()));
        var first = (await Send(visitor, HttpMethod.Get, "/t/" + Workshop, json: true)).Json;
        var shown = Topics(first);
        Assert.Equal((rules.Id, true), (shown[0].Id, shown[0].Pinned));
        Assert.Equal(1, shown.Count(topic => topic.Id == rules.Id));
        Assert.All(shown.Skip(1), topic => Assert.False(topic.Pinned));
        Assert.Equal(later[^1].Id, shown[1].Id);
        // The next page holds the oldest of the rest, and the pinned Topic is on no page but the first.
        var next = first.GetProperty("next").GetString()!;
        var second = Topics((await Send(visitor, HttpMethod.Get, next[BaseUrl.Length..], json: true)).Json);
        Assert.Equal([later[0].Id], second.Select(topic => topic.Id).ToList());
        Assert.Contains("<span class=\"badge is-pinned\">Pinned</span>", (await Send(visitor, HttpMethod.Get, "/t/" + Workshop)).Body);

        // A visitor's shelf of the Tangent leads with it.
        var door = (await Send(visitor, HttpMethod.Get, "/", json: true)).Json;
        var shelf = door.GetProperty("shelves").EnumerateArray().Single(each => each.GetProperty("slug").GetString() == Workshop);
        var onShelf = shelf.GetProperty("topics").EnumerateArray().First();
        Assert.Equal((rules.Id, true), (onShelf.GetProperty("id").GetString(), onShelf.GetProperty("pinned").GetBoolean()));

        var unpinned = await Form(mo, TopicPath(rules), TopicPath(rules) + "/" + TopicPageController.UnpinSegment, new Dictionary<string, string>());

        Assert.Equal((HttpStatusCode.SeeOther, BaseUrl + TopicPath(rules)), (unpinned.Status, unpinned.Location));
        var again = Topics((await Send(visitor, HttpMethod.Get, "/t/" + Workshop, json: true)).Json);
        Assert.Equal(later[^1].Id, again[0].Id);
        Assert.DoesNotContain(again, topic => topic.Id == rules.Id || topic.Pinned);
    }

    [Fact]
    public async Task Pinning_another_topic_names_the_pinned_one_first_and_takes_its_place()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var rules = await Started(Mo, "House rules", "Be kind; stay on the work.");
        var start = await Started(Mo, "Start here", "Say hello.");
        Assert.IsType<Outcome<Tangents.Tangent>.Done>(await new PinTopic(rules.Id, Mo).Send(Cancellation));
        using var mo = await SignedIn(tangentSpace, Mo);

        var page = WebUtility.HtmlDecode((await Send(mo.Client, HttpMethod.Get, TopicPath(start))).Body);
        var onRules = WebUtility.HtmlDecode((await Send(mo.Client, HttpMethod.Get, TopicPath(rules))).Body);
        var json = (await Send(mo.Client, HttpMethod.Get, TopicPath(start), json: true)).Json;

        Assert.Contains($"Pinning this unpins <a href=\"{BaseUrl + TopicPath(rules)}\">House rules</a>.", page);
        Assert.Contains($"action=\"{BaseUrl + TopicPath(start)}/pin\"", page);
        Assert.Contains($"action=\"{BaseUrl + TopicPath(rules)}/unpin\"", onRules);
        Assert.DoesNotContain("Pinning this unpins", onRules);
        var pinned = await Follow(mo, json.GetProperty("pinning"), new Dictionary<string, string>());

        Assert.True(pinned.Json.GetProperty("pinned").GetBoolean());
        var shown = Topics((await Send(mo.Client, HttpMethod.Get, "/t/" + Workshop, json: true)).Json);
        Assert.Equal([(start.Id, true), (rules.Id, false)], shown.Select(topic => (topic.Id, topic.Pinned)).ToList());
    }

    [Fact]
    public async Task A_pinned_topic_whose_root_is_removed_leads_nothing_and_stands_in_its_place()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        var rules = await Started(Mo, "House rules", "Be kind; stay on the work.");
        var newer = await Started(Ada, "A newer question", "What next?");
        Assert.IsType<Outcome<Tangents.Tangent>.Done>(await new PinTopic(rules.Id, Mo).Send(Cancellation));
        Assert.IsType<Outcome<Post>.Done>(await new RemovePost(rules.Id, Mo).Send(Cancellation));

        var listed = (await Send(Agent(tangentSpace), HttpMethod.Get, "/t/" + Workshop, json: true)).Json.GetProperty("topics").EnumerateArray().ToList();

        Assert.Equal([newer.Id, rules.Id], listed.Select(topic => topic.GetProperty("id").GetString()).ToList());
        Assert.All(listed, topic => Assert.False(topic.GetProperty("pinned").GetBoolean()));
        Assert.Equal(JsonValueKind.Object, listed[1].GetProperty("removed").ValueKind);
    }

    private static List<(string Id, bool Pinned)> Topics(JsonElement page)
        => page.GetProperty("topics").EnumerateArray().Select(topic => (topic.GetProperty("id").GetString()!, topic.GetProperty("pinned").GetBoolean())).ToList();
}
