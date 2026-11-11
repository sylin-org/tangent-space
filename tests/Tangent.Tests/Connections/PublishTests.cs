using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json.Nodes;
using Koan.Data.Core.Commands;
using Koan.Jobs;
using Koan.Web.Auth.Connector.Atproto.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Capture;
using Tangent.Posts;
using Tangent.Space;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Connections;

/// <summary>An author publishes a public article under their own account, and it follows them: their document and its
/// Bluesky post land in their repository, a moderator's edit stays out of it until they choose it, their own edit puts
/// the document again, and a removal deletes both, as deleting the article's Tangent does once their grant holds, their
/// Connections saying the copy remains until it does; as the proprietor, their account holds the place as a publication,
/// its mark as its icon and its colours as its theme, until the place is private — against a repository double that holds the author's grant, never anyone's real
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

        Assert.IsType<Outcome<Post>.Done>(await new Edited(article.Id, 1, PostBody.Of("A cracked bowl, mended by a moderator."), "Mending with brass", Mo).Send(Cancellation));
        await ReadThrough(tangentSpace);
        Assert.Equal("Mending with gold", repository.Records[document]["title"]!.GetValue<string>());
        Assert.Contains("Publish this version as yours?", (await Send(ada.Client, HttpMethod.Get, page)).Body);

        Assert.IsType<Outcome<Post>.Done>(await new Edited(article.Id, 2, PostBody.Of("A cracked bowl tells you where it held."), "Mending with lacquer", Ada).Send(Cancellation));
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
        Assert.IsType<Outcome<Tangents.Tangent>.Done>(await new DeleteTangentRow(Articles, leo, 1).Send(Cancellation));
        await Until(async () => (await Connection.Get(Ada, Cancellation))?.NeedsGrant == true);
        Assert.Contains(document, repository.Records.Keys);
        var listed = (await Send(ada.Client, HttpMethod.Get, ConnectionsController.Path, json: true)).Json;
        var line = Assert.Single(listed.GetProperty("articles").EnumerateArray().ToList());
        Assert.Equal((ConnectionsController.Deleted, "remains", "Still public on Bluesky, though it no longer is here. Grant it again to take it down.",
            Publishing.Bluesky(note)), (line.GetProperty("title").GetString(), line.GetProperty("state").GetString(), line.GetProperty("sentence").GetString(),
            line.GetProperty("elsewhere").GetString()));
        Assert.False(line.TryGetProperty("address", out _));
        Assert.EndsWith("Grant it again to take down what is still public there.", listed.GetProperty("bluesky").GetProperty("sentence").GetString());
        Assert.Contains(ConnectionsController.Deleted, (await Send(ada.Client, HttpMethod.Get, ConnectionsController.Path)).Body);
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
        await Until(async () => (await Receipt.Get(Receipt.Key(Ada, article.Id), Cancellation))?.Retracted == true);
        Assert.Empty((await Send(ada.Client, HttpMethod.Get, ConnectionsController.Path, json: true)).Json.GetProperty("articles").EnumerateArray());
    }

    [Fact]
    public async Task A_published_article_follows_its_moves_comes_down_in_a_private_tangent_goes_out_again_once_its_removal_is_undone_and_comes_down_once_deleted()
    {
        var repository = new Repository();
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(),
            services => services.AddSingleton<IAtprotoRepository>(repository), Cancellation);
        await Place(tangentSpace);
        const string notebook = "notebook", drafts = "drafts";
        await Given.ATangent(tangentSpace, notebook, ct: Cancellation, kind: PostKind.Article);
        await Given.ATangent(tangentSpace, drafts, Audience.Private, Cancellation, PostKind.Article);
        await Tangents.Overwrites.SetAccess(tangentSpace.Services.GetRequiredService<Koan.Identity.Roles.RoleCollection>(), drafts, Access.Roles.Moderator,
            Tangents.RoleAccess.Post, Cancellation);
        var article = await Started(Ada, "Mending with gold", "A cracked bowl tells you where it was weak.", Articles);
        using var ada = await SignedIn(tangentSpace, Ada);
        await Form(ada, TopicPath(article), TopicPath(article) + "/" + PublishController.Segment, []);
        var document = $"at://{Persona.Did(Ada)}/{AtprotoAdapter.Documents}/{article.Id}";
        await Until(() => repository.Records.ContainsKey(document));

        // Moved to another public Tangent, its copy names where it stands, and its receipt the Tangent that holds it.
        Assert.IsType<Outcome<Post>.Done>(await new MoveTopic(article.Id, notebook, Mo).Send(Cancellation));
        await Until(() => repository.Records.TryGetValue(document, out var put) && put["path"]!.GetValue<string>() == "/t/" + notebook + "/" + article.Slug);
        await Until(async () => (await Receipt.Get(Receipt.Key(Ada, article.Id), Cancellation))?.TangentId == notebook);

        // Moved where it is private, its copy comes down; moved back out, it goes out again.
        Assert.IsType<Outcome<Post>.Done>(await new MoveTopic(article.Id, drafts, Mo).Send(Cancellation));
        await Until(() => !repository.Records.ContainsKey(document));
        await Until(async () => (await Receipt.Get(Receipt.Key(Ada, article.Id), Cancellation))?.TangentId == drafts);
        Assert.IsType<Outcome<Post>.Done>(await new MoveTopic(article.Id, notebook, Mo).Send(Cancellation));
        await Until(() => repository.Records.ContainsKey(document));

        // Removed and brought back by its author, it comes down and goes out again.
        Assert.IsType<Outcome<Post>.Done>(await new RemovePost(article.Id, Ada).Send(Cancellation));
        await Until(() => !repository.Records.ContainsKey(document));
        Assert.IsType<Outcome<Post>.Done>(await new UndoRemoval(article.Id, Ada).Send(Cancellation));
        await Until(() => repository.Records.ContainsKey(document));

        // Deleted whole, its copy comes down from its receipt alone.
        Assert.IsType<Outcome<Post>.Done>(await new DeleteTopic(article.Id, Mo).Send(Cancellation));
        // The copy comes down before its receipt says so, so the receipt is waited for too.
        await Until(() => !repository.Records.ContainsKey(document));
        await Until(async () => (await Receipt.Get(Receipt.Key(Ada, article.Id), Cancellation))?.Retracted == true);
    }

    [Fact]
    public async Task The_publication_carries_the_places_mark_as_its_icon_and_its_colours_as_its_basic_theme()
    {
        var repository = new Repository();
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(),
            services => services.AddSingleton<IAtprotoRepository>(repository), Cancellation);
        await Place(tangentSpace);
        var leo = Persona.Id("leo4publishes");
        await Given.AParticipant(tangentSpace, leo, [Roles.Administrator], ct: Cancellation);
        var space = (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!;
        space.ProprietorId = Ada;
        Assert.IsType<Outcome<TangentSpace>.Done>(await new Store<TangentSpace>(space).Send(Cancellation));
        var article = await Started(Ada, "Mending with gold", "A cracked bowl tells you where it was weak.", Articles);
        var page = TopicPath(article);
        using var ada = await SignedIn(tangentSpace, Ada);
        await Form(ada, page, page + "/" + PublishController.Segment, []);
        var prefix = $"at://{Persona.Did(Ada)}/{AtprotoAdapter.Publications}/";
        await Until(() => repository.Records.Keys.Any(key => key.StartsWith(prefix, StringComparison.Ordinal)));
        var publication = repository.Records.Keys.Single(key => key.StartsWith(prefix, StringComparison.Ordinal));

        // Its letter is no picture, so the place starts with no icon, and with Tangent's palette in gold as Paper draws it.
        var record = repository.Records[publication];
        Assert.Null(record["icon"]);
        Assert.Equal("243,238,227 29,27,23 138,95,18 255,255,255", Theme(record));

        // A picture as its mark and jade as its accent put it again, the picture cut square and sent as a PNG blob.
        var mark = await Media.Keep(ArticleMediaTests.Jpeg(60), Cancellation);
        Assert.IsType<Outcome<TangentSpace>.Done>(await new ChangeTangentSpace(leo, Mark: Media.Reference(mark.Id), Palette: "jade").Send(Cancellation));
        await Until(() => repository.Records[publication]["icon"] is not null);
        record = repository.Records[publication];
        var upload = Assert.Single(repository.Uploads);
        Assert.Equal((Persona.Did(Ada), "image/png", AtprotoAdapter.IconEdge, AtprotoAdapter.IconEdge), (upload.Did, upload.MimeType, Width(upload.Content), Height(upload.Content)));
        Assert.Equal(upload.Blob.ToJsonString(), record["icon"]!.ToJsonString());
        Assert.Equal("243,238,227 29,27,23 47,107,94 255,255,255", Theme(record));

        // A new name puts it again naming the same blob, and a base's own palette leaves its theme out; a new mark sends its picture.
        Assert.IsType<Outcome<TangentSpace>.Done>(await new ChangeTangentSpace(leo, Name: "Mended things", Base: "simple", Palette: "base").Send(Cancellation));
        await Until(() => repository.Records[publication]["name"]!.GetValue<string>() == "Mended things");
        record = repository.Records[publication];
        Assert.Single(repository.Uploads);
        Assert.Equal(upload.Blob.ToJsonString(), record["icon"]!.ToJsonString());
        Assert.Null(Theme(record));

        var another = await Media.Keep(ArticleMediaTests.Jpeg(61), Cancellation);
        Assert.IsType<Outcome<TangentSpace>.Done>(await new ChangeTangentSpace(leo, Mark: Media.Reference(another.Id)).Send(Cancellation));
        await Until(() => repository.Uploads.Count == 2 && repository.Records[publication]["icon"]!.ToJsonString() == repository.Uploads.Last().Blob.ToJsonString());
    }

    [Fact]
    public async Task A_publish_that_fails_says_why_waits_longer_each_time_and_goes_out_once_bluesky_answers()
    {
        var repository = new Repository { Unreachable = true };
        var clock = new Participants.AccountTests.Ahead();
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(),
            services => services.AddSingleton<IAtprotoRepository>(repository).AddSingleton<TimeProvider>(clock), Cancellation);
        await Place(tangentSpace);
        var article = await Started(Ada, "Mending with gold", "A cracked bowl tells you where it was weak.", Articles);
        var page = TopicPath(article);
        using var ada = await SignedIn(tangentSpace, Ada);
        await Form(ada, page, page + "/" + PublishController.Segment, []);

        var first = await Failed(1);
        Assert.InRange(Waited(first, repository.Unanswered.First()), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30) + Close);
        var body = (await Send(ada.Client, HttpMethod.Get, page)).Body;
        Assert.Contains("data-state=\"" + ConnectionsController.Word(ArticleState.Trying) + "\"", body);
        Assert.Contains(System.Text.Encodings.Web.HtmlEncoder.Default.Encode(Syndication.Unreachable), body);
        Assert.Equal("trouble", (await Send(ada.Client, HttpMethod.Get, ConnectionsController.Path, json: true)).Json
            .GetProperty("bluesky").GetProperty("state").GetString());

        Past(first);
        var second = await Failed(2);
        Assert.InRange(Waited(second, repository.Unanswered.Last()), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60) + Close);

        repository.Unreachable = false;
        Past(second);
        await Until(() => repository.Records.ContainsKey($"at://{Persona.Did(Ada)}/{AtprotoAdapter.Documents}/{article.Id}"));
        await Until(async () => (await Connection.Get(Ada, Cancellation))?.Trouble is null);
        Assert.Contains("Published on Bluesky as you.", (await Send(ada.Client, HttpMethod.Get, page)).Body);

        // The stored Connection once its run has failed the given number of times in a row, the run settled.
        async Task<Connection> Failed(int times)
        {
            await Settled(async () => (await Connection.Get(Ada, Cancellation))?.Failures >= times);
            var connection = (await Connection.Get(Ada, Cancellation))!;
            Assert.True(repository.Unanswered.Count >= times, "The run never reached the author's repository.");
            Assert.Equal((times, Syndication.Unreachable), (connection.Failures, connection.Trouble));
            Assert.NotNull(connection.RetryAt);
            await Until(async () => await connection.Job.Status(Cancellation) == JobStatus.Queued);
            return connection;
        }

        // Moves the clock past the next attempt, its jitter included, while no run is in flight.
        void Past(Connection troubled) => clock.By += troubled.RetryAt!.Value - DateTimeOffset.UtcNow + TimeSpan.FromSeconds(3);
    }

    [Fact]
    public async Task A_connection_a_page_or_more_behind_reads_through_and_its_newer_article_goes_out_on_its_own()
    {
        var repository = new Repository();
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(),
            services => services.AddSingleton<IAtprotoRepository>(repository), Cancellation);
        await Place(tangentSpace);
        var stream = tangentSpace.Services.GetRequiredService<Events.EventStream>();
        var from = await stream.Head(Cancellation);
        Assert.IsType<Outcome<Connection>.Done>(await new Store<Connection>(new Connection
        {
            Id = Ada, State = ConnectionState.Publishing, From = from, Cursor = from, Since = DateTimeOffset.UtcNow
        }).Send(Cancellation));
        tangentSpace.Services.GetRequiredService<Nudge>().Changed();

        var talk = await Started(Bea, "Kintsugi", "Who has tried it?");
        Assert.IsType<Outcome<int>.Done>(await new Events.UnreadTests.Many(Events.EventKind.Mention, Bea, Mo, talk, Syndication.Page + 50).Send(Cancellation));
        var article = await Started(Ada, "Mending with gold", "A cracked bowl tells you where it was weak.", Articles);

        var head = await stream.Head(Cancellation);
        await Settled(async () => (await Connection.Get(Ada, Cancellation))?.Cursor >= head);
        Assert.Equal(head, (await Connection.Get(Ada, Cancellation))?.Cursor);
        await Until(() => repository.Records.ContainsKey($"at://{Persona.Did(Ada)}/{AtprotoAdapter.Documents}/{article.Id}"));
    }

    [Fact]
    public async Task An_article_going_out_on_its_own_says_its_trouble_and_never_offers_a_publish()
    {
        var repository = new Repository { Unreachable = true };
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(),
            services => services.AddSingleton<IAtprotoRepository>(repository), Cancellation);
        await Place(tangentSpace);
        var head = await tangentSpace.Services.GetRequiredService<Events.EventStream>().Head(Cancellation);
        Assert.IsType<Outcome<Connection>.Done>(await new ChooseToPublish(Ada, null, null, 0, head, DateTimeOffset.UtcNow).Send(Cancellation));
        tangentSpace.Services.GetRequiredService<Nudge>().Changed();
        var article = await Started(Ada, "Mending with gold", "A cracked bowl tells you where it was weak.", Articles);
        using var ada = await SignedIn(tangentSpace, Ada);

        await Until(async () => (await Connection.Get(Ada, Cancellation))?.Trouble == Syndication.Unreachable);
        var body = (await Send(ada.Client, HttpMethod.Get, TopicPath(article))).Body;
        Assert.Contains("data-state=\"" + ConnectionsController.Word(ArticleState.Trying) + "\"", body);
        Assert.Contains(System.Text.Encodings.Web.HtmlEncoder.Default.Encode(Syndication.Unreachable), body);
        Assert.DoesNotContain("Publish this on Bluesky", body);
        Assert.Null(await Receipt.Get(Receipt.Key(Ada, article.Id), Cancellation));
    }

    [Fact]
    public async Task An_article_the_connection_read_past_while_its_tangent_was_private_offers_a_publish_once_it_is_public()
    {
        var repository = new Repository();
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(),
            services => services.AddSingleton<IAtprotoRepository>(repository), Cancellation);
        await Place(tangentSpace);
        const string drafts = "drafts";
        await Given.ATangent(tangentSpace, drafts, Audience.Private, Cancellation, PostKind.Article);
        var head = await tangentSpace.Services.GetRequiredService<Events.EventStream>().Head(Cancellation);
        Assert.IsType<Outcome<Connection>.Done>(await new ChooseToPublish(Ada, null, null, 0, head, DateTimeOffset.UtcNow).Send(Cancellation));
        tangentSpace.Services.GetRequiredService<Nudge>().Changed();
        var article = await Started(Ada, "Mending with gold", "A cracked bowl tells you where it was weak.", drafts);
        await ReadThrough(tangentSpace);

        Assert.IsType<Outcome<Tangents.Tangent>.Done>(await new ChangeTangentRow(drafts, Mo, Audience: Audience.Public).Send(Cancellation));
        using var ada = await SignedIn(tangentSpace, Ada);
        var body = (await Send(ada.Client, HttpMethod.Get, TopicPath(article))).Body;
        Assert.Contains("Publish this on Bluesky too?", body);
        Assert.DoesNotContain("data-state=\"" + ConnectionsController.Word(ArticleState.Publishing) + "\"", body);
    }

    /// <summary>How long a failed run waits before its next attempt: its <see cref="Connection.RetryAt"/> less when the
    /// repository last went unanswered, which is when the run ended.</summary>
    private static TimeSpan Waited(Connection troubled, DateTimeOffset ended) => troubled.RetryAt!.Value - ended;

    /// <summary>How near a wait measured from outside the run comes to the one it set.</summary>
    private static readonly TimeSpan Close = TimeSpan.FromSeconds(1);

    /// <summary>The record's basic theme as its background, foreground, accent and accent foreground, each as r,g,b and each
    /// a <c>site.standard.theme.color#rgb</c>; null for none.</summary>
    private static string? Theme(JsonObject record)
    {
        if (record["basicTheme"] is not JsonObject theme) return null;
        Assert.Equal(AtprotoAdapter.BasicTheme, theme["$type"]!.GetValue<string>());
        var colours = new[] { "background", "foreground", "accent", "accentForeground" }.Select(name => theme[name]!).ToList();
        Assert.All(colours, colour => Assert.Equal(AtprotoAdapter.Rgb, colour["$type"]!.GetValue<string>()));
        return string.Join(" ", colours.Select(colour => $"{colour["r"]},{colour["g"]},{colour["b"]}"));
    }

    private static int Width(byte[] png) => BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));

    private static int Height(byte[] png) => BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));

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

    /// <summary>Waits until <paramref name="landed"/> holds or <see cref="Landing"/> passes, so the proof after it says
    /// what stands either way.</summary>
    private static async Task Settled(Func<Task<bool>> landed)
    {
        var until = DateTimeOffset.UtcNow + Landing;
        while (!await landed() && DateTimeOffset.UtcNow < until) await Task.Delay(TimeSpan.FromMilliseconds(100), Cancellation);
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

        /// <summary>Whether the author's server answers: while it does not, every put fails as a request that got no answer.</summary>
        public bool Unreachable { get; set; }

        /// <summary>When each put went unanswered, in order.</summary>
        public ConcurrentQueue<DateTimeOffset> Unanswered { get; } = [];

        public Task<AtprotoRecord> Put(string did, string collection, string rkey, JsonObject record, CancellationToken ct = default)
        {
            if (Unreachable)
            {
                Unanswered.Enqueue(DateTimeOffset.UtcNow);
                throw new HttpRequestException("The author's server did not answer.");
            }

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
        {
            var blob = new JsonObject
            {
                ["$type"] = "blob",
                ["ref"] = new JsonObject { ["$link"] = "bafkrei" + Interlocked.Increment(ref _cids) },
                ["mimeType"] = mimeType,
                ["size"] = content.Length
            };
            Uploads.Enqueue(new Uploaded(did, mimeType, content.ToArray(), (JsonObject)blob.DeepClone()));
            return Task.FromResult(blob);
        }

        /// <summary>Every blob sent, in the order it was sent.</summary>
        public ConcurrentQueue<Uploaded> Uploads { get; } = [];
    }

    internal sealed record Uploaded(string Did, string MimeType, byte[] Content, JsonObject Blob);
}
