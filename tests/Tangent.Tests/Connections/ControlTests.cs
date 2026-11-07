using System.Net;
using Koan.Data.Core.Commands;
using Koan.Web.Auth.Connector.Atproto.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Capture;
using Tangent.Posts;
using Tangent.Tools;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Connections;

/// <summary>An author decides what of theirs is on Bluesky: they take one article's copy down while it stays here, and it
/// stays down through their edits until they publish it again; they stop their new articles going out on their own, and
/// what is out still follows them while each new one waits to be chosen, until they have new ones go out on their own
/// again; and they page through every article they have out — against a repository double that holds their grant,
/// never anyone's real account.</summary>
public sealed class ControlTests
{
    private static readonly TimeSpan Landing = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task An_author_takes_an_article_off_Bluesky_and_it_stays_down_through_an_edit_until_they_publish_it_again()
    {
        var repository = new PublishTests.Repository();
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(),
            services => services.AddSingleton<IAtprotoRepository>(repository), Cancellation);
        await Place(tangentSpace);
        var article = await Started(Ada, "Mending with gold", "A cracked bowl tells you where it was weak.", Articles);
        var never = await Started(Ada, "Gold leaf, not gold paint", "Leaf sits on the lacquer.", Articles);
        var page = TopicPath(article);
        using var ada = await SignedIn(tangentSpace, Ada);
        await Form(ada, page, page + "/" + PublishController.Segment, []);
        var document = Address(AtprotoAdapter.Documents, article);
        var note = Address(AtprotoAdapter.Notes, article);
        await Until(() => repository.Records.ContainsKey(document));
        await Until(async () => (await Receipt.Get(Receipt.Key(Ada, article.Id), Cancellation))?.IsLive() == true);

        var line = (await Send(ada.Client, HttpMethod.Get, page)).Body;
        Assert.Contains("Take it down from Bluesky", line);
        Assert.Contains(WebUtility.HtmlEncode(Publishing.RetractWarning), line);
        var connections = (await Send(ada.Client, HttpMethod.Get, ConnectionsController.Path, json: true)).Json;
        var row = Assert.Single(connections.GetProperty("articles").EnumerateArray().ToList());
        Assert.Equal(BaseUrl + page + "/" + PublishController.RetractSegment, row.GetProperty("retract").GetProperty("action").GetString());

        var taken = await Form(ada, ConnectionsController.Path, page + "/" + PublishController.RetractSegment,
            new() { [PublishController.BackField] = ConnectionsController.Path });
        Assert.Equal((HttpStatusCode.SeeOther, BaseUrl + ConnectionsController.Path), (taken.Status, taken.Location));
        await Until(() => !repository.Records.ContainsKey(document) && !repository.Records.ContainsKey(note));
        await Until(async () => (await Receipt.Get(Receipt.Key(Ada, article.Id), Cancellation))?.Retracted == true);

        var here = await Send(ada.Client, HttpMethod.Get, page);
        Assert.Equal(HttpStatusCode.OK, here.Status);
        Assert.Contains("Publish this on Bluesky again?", here.Body);
        Assert.DoesNotContain("Take it down from Bluesky", here.Body);
        Assert.IsType<Outcome<Post>.Done>(await new Edited(article.Id, 1, PostBody.Of("A cracked bowl tells you where it held."), "Mending with lacquer", Ada).Send(Cancellation));
        await ReadThrough(tangentSpace);
        Assert.DoesNotContain(document, repository.Records.Keys);
        var listed = Assert.Single((await Send(ada.Client, HttpMethod.Get, ConnectionsController.Path, json: true)).Json.GetProperty("articles").EnumerateArray().ToList());
        Assert.Equal(("retracted", false), (listed.GetProperty("state").GetString(), listed.TryGetProperty("retract", out _)));

        await Form(ada, page, page + "/" + PublishController.Segment, []);
        await Until(() => repository.Records.TryGetValue(document, out var back) && back["title"]!.GetValue<string>() == "Mending with lacquer");
        await Until(async () => (await Receipt.Get(Receipt.Key(Ada, article.Id), Cancellation))?.IsLive() == true);
        Assert.Contains("Take it down from Bluesky", (await Send(ada.Client, HttpMethod.Get, page)).Body);

        using var client = await ToolClient.Connect(tangentSpace, ada);
        var again = await client.Call("retract", new { tangent = Articles, topic = article.Slug });
        Assert.False(again.Failed, again.Text);
        Assert.Equal("retracting", again.Structured?.GetProperty("state").GetString());
        await Until(() => !repository.Records.ContainsKey(document));
        // An earlier article she never chose has nothing out to take down.
        var nothing = await client.Call("retract", new { tangent = Articles, topic = never.Slug });
        Assert.Equal((true, PublishController.NotOut.Word), (nothing.Failed, nothing.Structured!.Value.GetProperty("word").GetString()));
    }

    [Fact]
    public async Task An_author_stops_new_articles_going_out_on_their_own_and_what_is_out_still_follows_their_edits()
    {
        var repository = new PublishTests.Repository();
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(),
            services => services.AddSingleton<IAtprotoRepository>(repository), Cancellation);
        await Place(tangentSpace);
        var gold = await Started(Ada, "Mending with gold", "A cracked bowl tells you where it was weak.", Articles);
        using var ada = await SignedIn(tangentSpace, Ada);
        await Form(ada, TopicPath(gold), TopicPath(gold) + "/" + PublishController.Segment, []);
        await Until(() => repository.Records.ContainsKey(Address(AtprotoAdapter.Documents, gold)));
        Assert.Contains("Stop publishing new articles", (await Send(ada.Client, HttpMethod.Get, ConnectionsController.Path)).Body);

        using var client = await ToolClient.Connect(tangentSpace, ada);
        var stopped = await client.Call("stop_publishing");
        Assert.False(stopped.Failed, stopped.Text);
        Assert.Equal("chosen", stopped.Structured?.GetProperty("bluesky").GetProperty("state").GetString());
        var leaf = await Started(Ada, "Gold leaf, not gold paint", "Leaf sits on the lacquer.", Articles);
        Assert.IsType<Outcome<Post>.Done>(await new Edited(gold.Id, 1, PostBody.Of("A cracked bowl tells you where it held."), "Mending with lacquer", Ada).Send(Cancellation));
        await Until(() => repository.Records.TryGetValue(Address(AtprotoAdapter.Documents, gold), out var edited) && edited["title"]!.GetValue<string>() == "Mending with lacquer");
        await ReadThrough(tangentSpace);
        Assert.DoesNotContain(Address(AtprotoAdapter.Documents, leaf), repository.Records.Keys);
        Assert.Contains("Publish this on Bluesky too?", (await Send(ada.Client, HttpMethod.Get, TopicPath(leaf))).Body);
        var page = (await Send(ada.Client, HttpMethod.Get, ConnectionsController.Path)).Body;
        Assert.Contains("Only the articles you choose go out on Bluesky", page);
        Assert.Contains("Publish new articles on their own", page);

        var bench = await Started(Ada, "The bench at night", "Most of the mending happens after the shop closes.", Articles);
        await Form(ada, TopicPath(bench), TopicPath(bench) + "/" + PublishController.Segment, []);
        await Until(() => repository.Records.ContainsKey(Address(AtprotoAdapter.Documents, bench)));
        Assert.Null((await Connection.Get(Ada, Cancellation))?.From);

        var resumed = await Form(ada, ConnectionsController.Path, ConnectionsController.ResumePath, []);
        Assert.Equal((HttpStatusCode.SeeOther, BaseUrl + ConnectionsController.Path), (resumed.Status, resumed.Location));
        var kiln = await Started(Ada, "Firing the kiln", "Slow up, slower down.", Articles);
        await Until(() => repository.Records.ContainsKey(Address(AtprotoAdapter.Documents, kiln)));
        Assert.IsType<Outcome<Post>.Done>(await new Edited(leaf.Id, 1, PostBody.Of("Leaf sits on the lacquer; paint on the surface."), null, Ada).Send(Cancellation));
        await ReadThrough(tangentSpace);
        Assert.DoesNotContain(Address(AtprotoAdapter.Documents, leaf), repository.Records.Keys);
        Assert.Contains("Stop publishing new articles", (await Send(ada.Client, HttpMethod.Get, ConnectionsController.Path)).Body);
    }

    [Fact]
    public async Task An_authors_connections_page_through_every_article_they_have_out_each_on_one_page()
    {
        var repository = new PublishTests.Repository();
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(),
            services => services.AddSingleton<IAtprotoRepository>(repository), Cancellation);
        await Place(tangentSpace);
        var head = await tangentSpace.Services.GetRequiredService<Events.EventStream>().Head(Cancellation);
        Assert.IsType<Outcome<Connection>.Done>(await new Store<Connection>(new Connection
        {
            Id = Ada, State = ConnectionState.Publishing, From = null, Cursor = head, Since = DateTimeOffset.UtcNow
        }).Send(Cancellation));
        var at = DateTimeOffset.UtcNow;
        for (var number = 1; number <= Receipt.PageSize + 5; number++)
        {
            var article = await Started(Ada, "Notebook, page " + number, "What the bench taught me.", Articles);
            // All but three share one instant, so a page ends between two receipts of the same time.
            Assert.IsType<Outcome<Receipt>.Done>(await new Store<Receipt>(new Receipt
            {
                Id = Receipt.Key(Ada, article.Id), ConnectionId = Ada, PostId = article.Id, TangentId = Articles, Version = article.Version,
                At = number <= 3 ? at.AddMinutes(-number) : at,
                Document = Address(AtprotoAdapter.Documents, article), Note = Address(AtprotoAdapter.Notes, article)
            }).Send(Cancellation));
        }

        using var ada = await SignedIn(tangentSpace, Ada);
        var first = (await Send(ada.Client, HttpMethod.Get, ConnectionsController.Path, json: true)).Json;
        var firstTitles = first.GetProperty("articles").EnumerateArray().Select(row => row.GetProperty("title").GetString()).ToList();
        Assert.Equal(Receipt.PageSize, firstTitles.Count);
        var next = first.GetProperty("next").GetString()!;
        Assert.StartsWith(BaseUrl + ConnectionsController.Path + "?" + ConnectionsController.After + "=", next);
        Assert.Contains("Next page", (await Send(ada.Client, HttpMethod.Get, ConnectionsController.Path)).Body);

        var rest = (await Send(ada.Client, HttpMethod.Get, next[BaseUrl.Length..], json: true)).Json;
        var restTitles = rest.GetProperty("articles").EnumerateArray().Select(row => row.GetProperty("title").GetString()).ToList();
        Assert.False(rest.TryGetProperty("next", out _));
        Assert.Equal(BaseUrl + ConnectionsController.Path, rest.GetProperty("first").GetString());
        Assert.Equal(Enumerable.Range(1, Receipt.PageSize + 5).Select(number => "Notebook, page " + number).Order(),
            firstTitles.Concat(restTitles).Order());
        Assert.Equal(["Notebook, page 5", "Notebook, page 4", "Notebook, page 1", "Notebook, page 2", "Notebook, page 3"], restTitles);
        Assert.Contains("First page", (await Send(ada.Client, HttpMethod.Get, next[BaseUrl.Length..])).Body);

        using var client = await ToolClient.Connect(tangentSpace, ada);
        var read = await client.Call("read_connections", new { after = next[(next.IndexOf('=') + 1)..] });
        Assert.Equal(5, read.Structured?.GetProperty("articles").GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(ada.Client, HttpMethod.Get, ConnectionsController.Path + "?after=not-a-cursor", json: true)).Status);

        // A take-down pressed on the next page comes back to the first, which the row it took down then heads.
        var later = rest.GetProperty("articles")[0];
        var retract = later.GetProperty("retract");
        var taken = await Form(ada, next[BaseUrl.Length..], retract.GetProperty("action").GetString()![BaseUrl.Length..],
            new() { [PublishController.BackField] = Valued(retract, PublishController.BackField)! });
        Assert.Equal((HttpStatusCode.SeeOther, BaseUrl + ConnectionsController.Path), (taken.Status, taken.Location));
        var heading = (await Send(ada.Client, HttpMethod.Get, ConnectionsController.Path, json: true)).Json.GetProperty("articles")[0];
        Assert.Equal(later.GetProperty("title").GetString(), heading.GetProperty("title").GetString());
        Assert.Contains(heading.GetProperty("state").GetString(), new[] { "retracting", "retracted" });
    }

    private static string Address(string collection, Post article) => $"at://{Persona.Did(Ada)}/{collection}/{article.Id}";

    /// <summary>Waits until Ada's Connection has read the log through its head.</summary>
    private static async Task ReadThrough(TangentSpaceFixture tangentSpace)
    {
        var head = await tangentSpace.Services.GetRequiredService<Events.EventStream>().Head(Cancellation);
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        bound.CancelAfter(Landing);
        while ((await Connection.Get(Ada, bound.Token))?.Cursor < head) await Task.Delay(TimeSpan.FromMilliseconds(100), bound.Token);
    }

    private static Task Until(Func<bool> landed) => Until(() => Task.FromResult(landed()));

    private static async Task Until(Func<Task<bool>> landed)
    {
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        bound.CancelAfter(Landing);
        while (!await landed()) await Task.Delay(TimeSpan.FromMilliseconds(100), bound.Token);
    }
}
