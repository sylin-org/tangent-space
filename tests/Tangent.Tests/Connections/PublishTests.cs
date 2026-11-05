using System.Collections.Concurrent;
using System.Net;
using System.Text.Json.Nodes;
using Koan.Data.Core.Commands;
using Koan.Web.Auth.Connector.Atproto.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Posts;
using Tangent.Space;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Connections;

/// <summary>An author publishes a public article under their own account, and it follows them: their document and its
/// Bluesky post land in their repository, a moderator's edit stays out of it until they choose it, their own edit puts
/// the document again, and a removal deletes both; as the proprietor, their account holds the place as a publication
/// until the place is private — against a repository double that holds the author's grant, never anyone's real
/// account.</summary>
public sealed class PublishTests
{
    /// <summary>The longest a Connection's job takes to deliver what a write asked for.</summary>
    private static readonly TimeSpan Landing = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task An_author_publishes_an_article_and_it_follows_their_edit_and_removal()
    {
        var repository = new Repository();
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(),
            services => services.AddSingleton<IAtprotoRepository>(repository), Cancellation);
        await Place(tangentSpace);
        var space = (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!;
        space.ProprietorId = Ada;
        Assert.IsType<Outcome<TangentSpace>.Done>(await new Store<TangentSpace>(space).Send(Cancellation));
        var article = await Started(Ada, "Mending with gold", "A cracked bowl tells you where it was weak.", Articles);
        var page = TopicPath(article);
        using var ada = await SignedIn(tangentSpace, Ada);

        Assert.Contains("Publish this on Bluesky as you?", (await Send(ada.Client, HttpMethod.Get, page)).Body);
        var published = await Form(ada, page, page + "/" + PublishController.Segment, []);
        Assert.Equal(HttpStatusCode.SeeOther, published.Status);
        Assert.Equal(BaseUrl + page, published.Location);

        var document = $"at://{Ada}/{AtprotoAdapter.Documents}/{article.Id}";
        var note = $"at://{Ada}/{AtprotoAdapter.Notes}/{article.Id}";
        await Until(() => repository.Records.ContainsKey(document));
        Assert.Equal("Mending with gold", repository.Records[note]["embed"]!["external"]!["title"]!.GetValue<string>());
        Assert.Equal(BaseUrl + page, repository.Records[note]["embed"]!["external"]!["uri"]!.GetValue<string>());
        Assert.Equal(note, repository.Records[document]["bskyPostRef"]!["uri"]!.GetValue<string>());
        Assert.Contains("Published on Bluesky as you.", (await Send(ada.Client, HttpMethod.Get, page)).Body);
        Assert.Contains($"<link rel=\"site.standard.document\" href=\"{document}\"", (await Send(Agent(tangentSpace), HttpMethod.Get, page)).Body);
        var publication = repository.Records.Keys.Single(key => key.StartsWith($"at://{Ada}/{AtprotoAdapter.Publications}/", StringComparison.Ordinal));
        Assert.Equal(publication, (await Send(Agent(tangentSpace), HttpMethod.Get, PublicationController.Path)).Body);

        Assert.IsType<Outcome<Post>.Done>(await new EditPost(article.Id, 1, PostBody.Of("A cracked bowl, mended by a moderator."), "Mending with brass", Mo).Send(Cancellation));
        await ReadThrough(tangentSpace);
        Assert.Equal("Mending with gold", repository.Records[document]["title"]!.GetValue<string>());
        Assert.Contains("Publish this version as yours?", (await Send(ada.Client, HttpMethod.Get, page)).Body);

        Assert.IsType<Outcome<Post>.Done>(await new EditPost(article.Id, 2, PostBody.Of("A cracked bowl tells you where it held."), "Mending with lacquer", Ada).Send(Cancellation));
        await Until(() => repository.Records.TryGetValue(document, out var edited) && edited["title"]!.GetValue<string>() == "Mending with lacquer");
        Assert.Contains("Updated", (await Send(ada.Client, HttpMethod.Get, ConnectionsController.Path)).Body);

        space.Audience = Audience.Private;
        Assert.IsType<Outcome<TangentSpace>.Done>(await new Store<TangentSpace>(space).Send(Cancellation));
        Assert.IsType<Outcome<Post>.Done>(await new RemovePost(article.Id, Ada).Send(Cancellation));
        await Until(() => !repository.Records.ContainsKey(document) && !repository.Records.ContainsKey(note) && !repository.Records.ContainsKey(publication));
        Assert.Equal(HttpStatusCode.NotFound, (await Send(Agent(tangentSpace), HttpMethod.Get, PublicationController.Path)).Status);
    }

    /// <summary>Waits until Ada's Connection has read the log through its head.</summary>
    private static async Task ReadThrough(TangentSpaceFixture tangentSpace)
    {
        var head = await tangentSpace.Services.GetRequiredService<Events.EventStream>().Head(Cancellation);
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        bound.CancelAfter(Landing);
        while ((await Connection.Get(Ada, bound.Token))?.Cursor < head) await Task.Delay(TimeSpan.FromMilliseconds(100), bound.Token);
    }

    private static async Task Until(Func<bool> landed)
    {
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        bound.CancelAfter(Landing);
        while (!landed()) await Task.Delay(TimeSpan.FromMilliseconds(100), bound.Token);
    }

    /// <summary>An author's repository that holds their grant and keeps every record put in it.</summary>
    internal sealed class Repository : IAtprotoRepository
    {
        private int _cids;

        public ConcurrentDictionary<string, JsonObject> Records { get; } = new(StringComparer.Ordinal);

        public Task<bool> Holds(string did, IReadOnlyCollection<string> scopes, CancellationToken ct = default) => Task.FromResult(true);

        public Task<AtprotoRecord> Put(string did, string collection, string rkey, JsonObject record, CancellationToken ct = default)
        {
            var address = $"at://{did}/{collection}/{rkey}";
            Records[address] = (JsonObject)record.DeepClone();
            return Task.FromResult(new AtprotoRecord(address, "bafyrei" + Interlocked.Increment(ref _cids)));
        }

        public Task Delete(string did, string collection, string rkey, CancellationToken ct = default)
        {
            Records.TryRemove($"at://{did}/{collection}/{rkey}", out _);
            return Task.CompletedTask;
        }

        public Task<JsonObject> Upload(string did, ReadOnlyMemory<byte> content, string mimeType, CancellationToken ct = default)
            => Task.FromResult(new JsonObject { ["$type"] = "blob", ["mimeType"] = mimeType, ["size"] = content.Length });
    }
}
