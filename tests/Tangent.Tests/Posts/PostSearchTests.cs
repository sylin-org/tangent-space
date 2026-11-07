using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Koan.Core.Diagnostics;
using Koan.Core.Observability.Health;
using Koan.Core.Observability.Probes;
using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tangent.Lifecycle;
using Tangent.Space;
using Tangent.Tangents;
using Xunit;

namespace Tangent.Posts;

/// <summary>Posts found by their words through Koan's lexical index over the SQLite connector: title and body, order
/// and snippets, the bounds, the index in the composition facts and in readiness, and the rebuild at start over a
/// restored folder.</summary>
public sealed class PostSearchTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private const string Workshop = "the-workshop";

    /// <summary>A Tangent Space with the public workshop, whose two authors, leo and ada, are members who
    /// may start and answer topics there.</summary>
    private static async Task Writing(TangentSpaceFixture tangentSpace)
    {
        await Given.AnEstablishedTangentSpace(tangentSpace, ct: Cancellation);
        await Given.ATangent(tangentSpace, Workshop, ct: Cancellation);
        await Given.AParticipant(tangentSpace, "leo", [Access.Roles.Member], ct: Cancellation);
        await Given.AParticipant(tangentSpace, "ada", [Access.Roles.Member], ct: Cancellation);
    }

    private static async Task<Post> Topic(string title, string body) =>
        Assert.IsType<Outcome<Post>.Done>(await new StartTopic(Tids.Mint(), Workshop, title, body, "leo").Send(Cancellation)).Result;

    private static async Task<Post> Answer(string topicId, string body) =>
        Assert.IsType<Outcome<Post>.Done>(await new Reply(Tids.Mint(), topicId, body, "ada").Send(Cancellation)).Result;

    /// <summary>A search as a visitor reads it, who may view the public workshop.</summary>
    private static Task<PostSearch.Answer> Run(string? text, int page = 1, int size = 20)
        => PostSearch.Run(text, new ViewableTangents.Met(null), null, page, size, Cancellation);

    private static async Task<IReadOnlyList<PostSearch.Hit>> Found(string text, int page = 1, int size = 20) => (await Run(text, page, size)).Hits;

    [Fact]
    public async Task A_post_is_found_by_a_word_of_its_title_and_by_a_word_of_its_body_and_not_by_a_word_in_neither()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        await Writing(tangentSpace);
        var topic = await Topic("Copper lanterns", "How the glass is cut.");
        var reply = await Answer(topic.Id, "Annealing matters most.");

        Assert.Equal([topic.Id], (await Found("lanterns")).Select(hit => hit.Post.Id));
        Assert.Equal([topic.Id], (await Found("glass")).Select(hit => hit.Post.Id));
        Assert.Equal([reply.Id], (await Found("annealing")).Select(hit => hit.Post.Id));
        Assert.Empty(await Found("porcelain"));
    }

    [Fact]
    public async Task A_two_term_query_answers_the_post_holding_both_terms_before_the_post_holding_one_and_the_snippet_marks_the_terms()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        await Writing(tangentSpace);
        var one = await Topic("Kilns", "A kiln fires hot.");
        var both = await Topic("Glaze", "A kiln fires glaze at the end.");

        var hits = await Found("kiln glaze");

        Assert.Equal([both.Id, one.Id], hits.Select(hit => hit.Post.Id));
        Assert.True(hits[0].Score > hits[1].Score);
        Assert.Contains(PostSearch.Open + "kiln" + PostSearch.Close, hits[0].Snippet);
        Assert.Contains(PostSearch.Open + "glaze" + PostSearch.Close, hits[0].Snippet);
    }

    [Fact]
    public async Task A_search_past_its_bounds_is_held_within_them_and_two_pages_neither_overlap_nor_skip()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        await Writing(tangentSpace);
        var ids = new List<string>();
        for (var number = 0; number < 5; number++) ids.Add((await Topic("Lantern " + number, "A lantern.")).Id);

        Assert.Equal(new string('a', PostSearch.TextLimit), PostSearch.Words(" " + new string('a', PostSearch.TextLimit + 300) + " "));
        Assert.Null(PostSearch.Words("   "));
        Assert.Equal(5, (await Found("lantern " + new string('x', 500))).Count);
        Assert.Equal(5, (await Found("lantern", 1, PostSearch.PageLimit + 1)).Count);
        Assert.Single(await Found("lantern", 1, 0));
        Assert.Equal(2, (await Found("lantern", 0, 2)).Count);
        Assert.Empty(await Found(" -- "));
        Assert.Empty(await Found("lantern", PostSearch.PageNumberLimit + 1, PostSearch.PageLimit));
        Assert.Empty(await Found("lantern", 50_000_000, PostSearch.PageLimit));
        Assert.Equal((1, 100, 1, 1), (PostSearch.Number("0"), PostSearch.Number("101"), PostSearch.Number("x"), PostSearch.Number("-1")));
        Assert.Equal((true, true, false), ((await Run("lantern", 1, 4)).More, (await Run("lantern", 2, 2)).More, (await Run("lantern", 1, 5)).More));

        var first = await Found("lantern", 1, 2);
        var second = await Found("lantern", 2, 2);
        var third = await Found("lantern", 3, 2);
        var paged = first.Concat(second).Concat(third).Select(hit => hit.Post.Id).ToList();
        Assert.Equal([2, 2, 1], [first.Count, second.Count, third.Count]);
        Assert.Equal(ids.Order(), paged.Order());
    }

    [Fact]
    public async Task The_composition_facts_name_the_post_index_with_its_provider_and_fields_and_readiness_reports_it_current_after_writes()
    {
        // A zero interval makes the probe after the writes measure, where the default would answer the start-time reading.
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(
            services: services => services.Configure<DataRuntimeOptions>(options => options.LexicalReadinessInterval = TimeSpan.Zero),
            ct: Cancellation);
        await Writing(tangentSpace);
        var topic = await Topic("Copper lanterns", "How the glass is cut.");
        await Answer(topic.Id, "Annealing matters most.");

        var fact = Assert.Single(tangentSpace.Services.GetRequiredService<IKoanRuntimeFacts>().Current.Facts, f => f.Subject == "data:lexical:post");
        Assert.Contains("provider 'sqlite'", fact.Summary);
        Assert.Contains("Title, Body", fact.Summary);

        var ready = await tangentSpace.Client.GetAsync("/health/ready", Cancellation);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Contains("healthy", await ready.Content.ReadAsStringAsync(Cancellation));

        // Production answers readiness with the overall state alone; the component's own report is the aggregator's.
        var aggregator = tangentSpace.Services.GetRequiredService<IHealthAggregator>();
        string? index = null;
        for (var attempt = 0; attempt < 100 && index is null; attempt++)
        {
            aggregator.RequestProbe(ProbeReason.Manual, "data:lexical", Cancellation);
            index = aggregator.GetSnapshot().Components.FirstOrDefault(c => c.Component == "data:lexical")?.Facts?.GetValueOrDefault("Post");
            if (index is null) await Task.Delay(100, Cancellation);
        }

        Assert.Equal("sqlite:current:2/2", index);
    }

    [Fact]
    public async Task A_tangent_space_started_over_a_restored_folder_whose_index_is_not_current_rebuilds_it_and_finds_its_posts()
    {
        await using var original = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        await Writing(original);
        var lantern = await Topic("Copper lanterns", "How the glass is cut.");
        var reply = await Answer(lantern.Id, "Annealing matters most.");

        var backup = Backup.Take(original.DataFolder, Path.Combine(original.Folder, "backups"));
        await original.StopAsync(Cancellation);
        var restoredFolder = Path.Combine(original.Folder, "restored");
        Backup.Restore(backup, restoredFolder, replace: false);
        EmptyTheIndex(Path.Combine(restoredFolder, TangentSpaceOptions.DatabaseFile));

        var log = new Captured();
        await using var restored = await TangentSpaceFixture.StartAsync(
            new Dictionary<string, string?> { ["Tangent:DataFolder"] = restoredFolder },
            services => services.AddSingleton<ILoggerProvider>(log),
            Cancellation);

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!log.Messages.Any(message => message.Contains("The Post index was rebuilt")) && DateTime.UtcNow < deadline)
            await Task.Delay(100, Cancellation);

        Assert.Contains(log.Messages, message => message.Contains("covers 0 of 2 Posts; rebuilding it"));
        Assert.Contains(log.Messages, message => message.Contains("The Post index was rebuilt and covers 2 Posts"));
        Assert.Equal([lantern.Id], (await Found("lanterns")).Select(hit => hit.Post.Id));
        Assert.Equal([reply.Id], (await Found("annealing")).Select(hit => hit.Post.Id));
    }

    [Fact]
    public async Task An_index_that_cannot_be_rebuilt_says_so_and_is_rebuilt_once_it_can_be()
    {
        var log = new Captured();
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(
            new Dictionary<string, string?> { ["Koan:Data:Sqlite:BusyTimeout"] = "00:00:01" },
            services => services.AddSingleton<ILoggerProvider>(log),
            Cancellation);
        await Writing(tangentSpace);
        var lantern = await Topic("Copper lanterns", "How the glass is cut.");
        EmptyTheIndex(tangentSpace.DatabasePath);

        using var holder = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = tangentSpace.DatabasePath, Pooling = false }.ToString());
        holder.Open();
        using (var begin = holder.CreateCommand())
        {
            begin.CommandText = "BEGIN IMMEDIATE";
            begin.ExecuteNonQuery();
        }

        var index = new PostIndex(tangentSpace.Services.GetRequiredService<ILogger<PostIndex>>(), tangentSpace.Services.GetRequiredService<IConfiguration>());
        var running = index.ExecuteCore(Cancellation);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!log.Messages.Any(message => message.Contains("could not be brought up to date")) && DateTime.UtcNow < deadline)
            await Task.Delay(50, Cancellation);
        Assert.Contains(log.Messages, message => message.Contains("could not be brought up to date"));
        using (var commit = holder.CreateCommand())
        {
            commit.CommandText = "COMMIT";
            commit.ExecuteNonQuery();
        }

        await running.WaitAsync(TimeSpan.FromSeconds(30), Cancellation);
        Assert.Contains(log.Messages, message => message.Contains("The Post index was rebuilt and covers 1 Posts"));
        Assert.Equal([lantern.Id], (await Found("lanterns")).Select(hit => hit.Post.Id));
    }

    /// <summary>Leaves the copy's index holding none of its Posts, as an index that is not current does. The copy keeps
    /// every row number, so the backup alone never produces one.</summary>
    private static void EmptyTheIndex(string database)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString());
        connection.Open();
        using var find = connection.CreateCommand();
        find.CommandText = "SELECT name FROM sqlite_master WHERE sql LIKE 'CREATE VIRTUAL TABLE%' AND sql LIKE '%fts5%'";
        var index = (string)find.ExecuteScalar()!;
        using var empty = connection.CreateCommand();
        empty.CommandText = $"INSERT INTO \"{index}\"(\"{index}\") VALUES('delete-all')";
        empty.ExecuteNonQuery();
    }

    private sealed class Captured : ILoggerProvider, ILogger
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Enqueue(formatter(state, exception));

        public void Dispose() { }
    }
}
