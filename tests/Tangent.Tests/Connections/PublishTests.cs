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
/// the document again, and a removal deletes both, as deleting the article's Tangent does once their grant holds, their
/// Connections saying the copy remains until it does; as the proprietor, their account holds the place as a publication
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

        var document = $"at://{Persona.Did(Ada)}/{AtprotoAdapter.Documents}/{article.Id}";
        var note = $"at://{Persona.Did(Ada)}/{AtprotoAdapter.Notes}/{article.Id}";
        await Until(() => repository.Records.ContainsKey(document));
        Assert.Equal("Mending with gold", repository.Records[note]["embed"]!["external"]!["title"]!.GetValue<string>());
        Assert.Equal(BaseUrl + page, repository.Records[note]["embed"]!["external"]!["uri"]!.GetValue<string>());
        Assert.Equal(note, repository.Records[document]["bskyPostRef"]!["uri"]!.GetValue<string>());
        Assert.Contains("Published on Bluesky as you.", (await Send(ada.Client, HttpMethod.Get, page)).Body);
        Assert.Contains($"<link rel=\"site.standard.document\" href=\"{document}\"", (await Send(Agent(tangentSpace), HttpMethod.Get, page)).Body);
        var publication = repository.Records.Keys.Single(key => key.StartsWith($"at://{Persona.Did(Ada)}/{AtprotoAdapter.Publications}/", StringComparison.Ordinal));
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_copy_out_under_an_earlier_account_comes_down_through_its_grant_or_is_said_to_stay(bool earlierGrantHolds)
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
        await Form(ada, page, page + "/" + PublishController.Segment, []);
        var earlier = Persona.Did(Ada);
        var document = $"at://{earlier}/{AtprotoAdapter.Documents}/{article.Id}";
        await Until(() => repository.Records.ContainsKey(document));
        var placed = repository.Records.Keys.Single(key => key.StartsWith($"at://{earlier}/{AtprotoAdapter.Publications}/", StringComparison.Ordinal));

        const string later = "did:plc:ada4later";
        repository.Holding = did => did == later || earlierGrantHolds;
        (await ada.UseAnotherAccount(later, "ada.later.example", Cancellation)).Dispose();
        Assert.Equal(later, (await Participants.Participant.Get(Ada, Cancellation))?.Did);
        Assert.IsType<Outcome<Post>.Done>(await new RemovePost(article.Id, Ada).Send(Cancellation));
        await ReadThrough(tangentSpace);
        // The place's publication moves to the account its proprietor holds now, withdrawn from the earlier one where it may be.
        Assert.Contains(repository.Records.Keys, key => key.StartsWith($"at://{later}/{AtprotoAdapter.Publications}/", StringComparison.Ordinal));
        Assert.Equal(!earlierGrantHolds, repository.Records.ContainsKey(placed));
        var receipt = await Receipt.Get(Receipt.Key(Ada, article.Id), Cancellation);
        var connections = (await Send(ada.Client, HttpMethod.Get, ConnectionsController.Path)).Body;
        if (earlierGrantHolds)
        {
            Assert.DoesNotContain(document, repository.Records.Keys);
            Assert.Contains(earlier, repository.Deleted);
            Assert.True(receipt?.Retracted);
        }
        else
        {
            Assert.Contains(document, repository.Records.Keys);
            Assert.True(receipt is { Stays: true, Retracted: false });
            Assert.Contains(System.Net.WebUtility.HtmlEncode(Publishing.StaysSentence), connections);
            Assert.Contains(System.Net.WebUtility.HtmlEncode(Publishing.StaysSentence), (await Send(ada.Client, HttpMethod.Get, page)).Body);
        }

        Assert.DoesNotContain("It is being taken down", connections);
        Assert.DoesNotContain(later, repository.Records.Keys.Where(key => key.Contains(AtprotoAdapter.Documents, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task An_articles_copy_is_said_to_remain_while_the_grant_is_needed_and_comes_down_once_its_tangent_is_deleted()
    {
        var repository = new Repository();
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(),
            services => services.AddSingleton<IAtprotoRepository>(repository), Cancellation);
        await Place(tangentSpace);
        var leo = Persona.Id("leo4publish");
        await Given.AParticipant(tangentSpace, leo, [Access.Roles.Administrator], ct: Cancellation);
        var article = await Started(Ada, "Mending with gold", "A cracked bowl tells you where it was weak.", Articles);
        var page = TopicPath(article);
        using var ada = await SignedIn(tangentSpace, Ada);
        await Form(ada, page, page + "/" + PublishController.Segment, []);
        var document = $"at://{Persona.Did(Ada)}/{AtprotoAdapter.Documents}/{article.Id}";
        var note = $"at://{Persona.Did(Ada)}/{AtprotoAdapter.Notes}/{article.Id}";
        await Until(() => repository.Records.ContainsKey(document));

        // Ada's grant lapses, and the article's Tangent is deleted: the copy stays out, and her Connections say so.
        repository.Holding = _ => false;
        Assert.IsType<Outcome<Tangents.Tangent>.Done>(await new Tangents.DeleteTangent(Articles, leo, Topics: 1).Send(Cancellation));
        await Until(async () => (await Connection.Get(Ada, Cancellation))?.NeedsGrant == true);
        Assert.Contains(document, repository.Records.Keys);
        var listed = (await Send(ada.Client, HttpMethod.Get, ConnectionsController.Path, json: true)).Json;
        var line = Assert.Single(listed.GetProperty("articles").EnumerateArray().ToList());
        Assert.Equal((ConnectionsController.DeletedWithItsTangent, "remains", "Still public on Bluesky, though it no longer is here. Grant it again to take it down.",
            Publishing.Bluesky(note)), (line.GetProperty("title").GetString(), line.GetProperty("state").GetString(), line.GetProperty("sentence").GetString(),
            line.GetProperty("elsewhere").GetString()));
        Assert.False(line.TryGetProperty("address", out _));
        Assert.EndsWith("Grant it again to take down what is still public there.", listed.GetProperty("bluesky").GetProperty("sentence").GetString());
        Assert.Contains(ConnectionsController.DeletedWithItsTangent, (await Send(ada.Client, HttpMethod.Get, ConnectionsController.Path)).Body);
        // Stopping her new articles going out on their own answers with her Connections as she reads them: the grant still
        // comes back to take the copy down.
        using (var client = await Tools.ToolClient.Connect(tangentSpace, ada))
        {
            var stopped = await client.Call("stop_publishing");
            Assert.False(stopped.Failed, stopped.Text);
            var account = stopped.Structured!.Value.GetProperty("bluesky");
            Assert.Equal("grant", account.GetProperty("state").GetString());
            Assert.EndsWith("Grant it again to take down what is still public there.", account.GetProperty("sentence").GetString());
        }

        // She grants it again, as its Connections page offers, and the copy comes down.
        repository.Holding = _ => true;
        var head = await tangentSpace.Services.GetRequiredService<Events.EventStream>().Head(Cancellation);
        Assert.IsType<Outcome<Connection>.Done>(await new ChooseToPublish(Ada, null, null, 0, head, DateTimeOffset.UtcNow).Send(Cancellation));
        var nudge = tangentSpace.Services.GetRequiredService<Nudge>();
        nudge.Changed();
        await nudge.Wake(Ada);
        await Until(() => !repository.Records.ContainsKey(document) && !repository.Records.ContainsKey(note));
        Assert.True((await Receipt.Get(Receipt.Key(Ada, article.Id), Cancellation))?.Retracted);
        Assert.Empty((await Send(ada.Client, HttpMethod.Get, ConnectionsController.Path, json: true)).Json.GetProperty("articles").EnumerateArray());
    }

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

    /// <summary>An author's repository that holds their grant and keeps every record put in it.</summary>
    internal sealed class Repository : IAtprotoRepository
    {
        private int _cids;

        public ConcurrentDictionary<string, JsonObject> Records { get; } = new(StringComparer.Ordinal);

        /// <summary>Whose grant it holds: everyone's unless a proof says otherwise.</summary>
        public Func<string, bool> Holding { get; set; } = _ => true;

        /// <summary>Every DID a record was deleted from.</summary>
        public ConcurrentBag<string> Deleted { get; } = [];

        public Task<bool> Holds(string did, IReadOnlyCollection<string> scopes, CancellationToken ct = default) => Task.FromResult(Holding(did));

        public Task<AtprotoRecord> Put(string did, string collection, string rkey, JsonObject record, CancellationToken ct = default)
        {
            var address = $"at://{did}/{collection}/{rkey}";
            Records[address] = (JsonObject)record.DeepClone();
            return Task.FromResult(new AtprotoRecord(address, "bafyrei" + Interlocked.Increment(ref _cids)));
        }

        public Task Delete(string did, string collection, string rkey, CancellationToken ct = default)
        {
            if (!Holding(did)) throw new AtprotoRepositoryException(HttpStatusCode.Unauthorized, null, "com.atproto.repo.deleteRecord");
            Deleted.Add(did);
            Records.TryRemove($"at://{did}/{collection}/{rkey}", out _);
            return Task.CompletedTask;
        }

        public Task<JsonObject> Upload(string did, ReadOnlyMemory<byte> content, string mimeType, CancellationToken ct = default)
            => Task.FromResult(new JsonObject { ["$type"] = "blob", ["mimeType"] = mimeType, ["size"] = content.Length });
    }
}
