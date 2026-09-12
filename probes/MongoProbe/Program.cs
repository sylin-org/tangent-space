using System.Diagnostics;
using System.Linq.Expressions;
using System.Runtime.InteropServices;
using System.Text.Json;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Sorting;
using Koan.Data.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using TangentSpace.Conversation;

Fixture.SelfTest();
if (args.SequenceEqual(["--self-test"])) { Console.WriteLine("Mongo probe checks passed."); return; }
if (args.Length != 6 || args[0] != "--repo" || args[2] != "--root" || args[4] != "--posts")
    throw new ArgumentException("Use --repo <absolute> --root <new absolute mongo-baseline-GUID> --posts 10000|100000.");
var repo = Path.GetFullPath(args[1]); var root = Path.GetFullPath(args[3]);
var posts = int.Parse(args[5]);
var parent = Path.Combine(repo, ".local", "experiments", "epic005");
if (posts is not (10_000 or 100_000) || !Path.IsPathFullyQualified(args[1]) || !Path.IsPathFullyQualified(args[3])
    || !File.Exists(Path.Combine(repo, "docs", "epics", "EPIC-005.md"))
    || !string.Equals(Path.GetDirectoryName(root), parent, StringComparison.OrdinalIgnoreCase)
    || !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(root), "^mongo-baseline-[a-f0-9]{32}$")
    || Directory.Exists(root) || File.Exists(root)) throw new InvalidOperationException("Non-isolated or existing probe root rejected.");
for (var path = new DirectoryInfo(parent); path is not null; path = path.Parent)
    if (path.Exists && (path.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Reparse-point ancestor rejected.");
// Endpoint is deliberately not configurable: this program cannot target a live/default Mongo service.
const string connection = "mongodb://127.0.0.1:27119/?replicaSet=rs0&directConnection=true&serverSelectionTimeoutMS=5000&connectTimeoutMS=5000&maxPoolSize=8&w=majority&journal=true&appName=TangentEpic005MongoProbe";
var databaseName = "epic005_mongo_" + Path.GetFileName(root)["mongo-baseline-".Length..];
using var process = Process.GetCurrentProcess();
if (OperatingSystem.IsWindows()) process.ProcessorAffinity = (nint)3;
Directory.CreateDirectory(root);
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10)); var ct = timeout.Token;
var timer = Stopwatch.StartNew(); var started = DateTimeOffset.UtcNow;
using var client = new MongoClient(connection);
var database = client.GetDatabase(databaseName);
using (var existing = await database.ListCollectionNamesAsync(cancellationToken: ct))
    if ((await existing.ToListAsync(ct)).Count != 0) throw new InvalidOperationException("GUID database is not empty; refusing mutation.");
var hello = await database.RunCommandAsync<BsonDocument>(new BsonDocument("hello", 1), cancellationToken: ct);
if (hello.GetValue("setName", "").AsString != "rs0" || !hello.GetValue("isWritablePrimary", false).ToBoolean())
    throw new InvalidOperationException("Expected writable rs0 lab topology not present.");
var serverBuild = await database.RunCommandAsync<BsonDocument>(new BsonDocument("buildInfo", 1), cancellationToken: ct);
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = root, EnvironmentName = "Testing", Args = [] });
builder.Configuration.Sources.Clear();
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Tangent:Site:Name"] = "EPIC005 Mongo isolated probe", ["Tangent:Conversation:Storage"] = "Local",
    ["Koan:Data:Sources:Default:Adapter"] = "mongo", ["Koan:Data:Sources:Default:ConnectionString"] = connection,
    ["Koan:Data:Sources:Default:Database"] = databaseName,
    ["Koan:Data:Mongo:ConnectionString"] = connection, ["Koan:Data:Mongo:Database"] = databaseName,
    ["Koan:Identity:Posture"] = "Closed", ["Koan:Identity:SeedDevUsers"] = "false"
});
builder.Logging.ClearProviders(); builder.Services.AddKoan();
using var host = builder.Build(); // No Start/Run: no application listeners or hosted/network workers.
AppHost.Current = host.Services; TestHooks.ResetDataConfigs();
var sourceFiles = new[] { "src/server/web/Conversation/Message.cs", "src/server/web/Conversation/ConversationService.History.cs", ".local/upstream/koan-framework/src/Koan.Data.Core/Data.cs", ".local/upstream/koan-framework/src/Koan.Data.Core/RepositoryFacade.cs", ".local/upstream/koan-framework/src/Connectors/Data/Mongo/MongoRepository.cs", ".local/upstream/koan-framework/src/Koan.Data.Core/Transactions/TransactionCoordinator.cs" };
var sourceHashes = sourceFiles.ToDictionary(path => path, path => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(repo, path)))));
var phases = new List<object>(); var outcomes = new Dictionary<string, object?>();
var failures = new List<string>();
const string collectionName = "TangentSpace.Conversation.Message";
var collection = database.GetCollection<BsonDocument>(collectionName);
var profiler = new Profiler(database, collectionName);
async Task Guard()
{
    ct.ThrowIfCancellationRequested(); process.Refresh();
    var stats = await database.RunCommandAsync<BsonDocument>(new BsonDocument("dbStats", 1), cancellationToken: ct);
    if (process.WorkingSet64 > 1536L * 1024 * 1024 || stats.GetValue("storageSize", 0).ToDouble() + stats.GetValue("indexSize", 0).ToDouble() > 5L * 1024 * 1024 * 1024)
        throw new InvalidOperationException("Client memory or per-run database size ceiling reached.");
}
try
{
    var cold = Stopwatch.StartNew();
    using (EntityContext.NoCache()) await Fixture.Make("hot", 1).Save(ct);
    cold.Stop(); outcomes["firstFacadeSaveMs"] = cold.Elapsed.TotalMilliseconds;
    var template = await collection.Find(new BsonDocument("_id", Fixture.Id("hot", 1))).FirstOrDefaultAsync(ct)
        ?? throw new InvalidDataException("Actual Koan Message storage was not found in the isolated database.");
    if (!template.Contains("roomKey") || !template.Contains("sequence")) throw new InvalidDataException("Unexpected Mongo Message schema.");
    outcomes["actualTemplate"] = Profiler.Json(template);
    outcomes["crud"] = await Admission.Crud(ct);
    outcomes["atomicRequirement"] = await Admission.Atomic(collection, profiler, ct);
    var seed = Stopwatch.StartNew();
    foreach (var (room, count) in new[] { ("hot", posts), ("noise-a", posts / 10), ("noise-b", posts / 10) })
    {
        var batch = new List<BsonDocument>(1000);
        for (var n = room == "hot" ? 2 : 1; n <= count; n++)
        {
            var doc = template.DeepClone().AsBsonDocument;
            doc["_id"] = Fixture.Id(room, n); doc["roomKey"] = room; doc["sequence"] = new BsonInt64(n);
            doc["authorParticipantId"] = "synthetic-" + (n % 32).ToString("D2");
            doc["content"]["text"] = Fixture.Text(n); doc["removed"] = n % 101 == 0;
            doc["editedAt"] = n % 53 == 0 ? new BsonDateTime(Fixture.Epoch.AddDays(2).UtcDateTime) : BsonNull.Value;
            batch.Add(doc);
            if (batch.Count == 1000) { await collection.InsertManyAsync(batch, new InsertManyOptions { IsOrdered = true }, ct); batch.Clear(); await Guard(); }
        }
        if (batch.Count > 0) await collection.InsertManyAsync(batch, new InsertManyOptions { IsOrdered = true }, ct);
    }
    seed.Stop(); outcomes["rawSeedMs"] = seed.Elapsed.TotalMilliseconds;
    foreach (var indexed in new[] { false, true })
    {
        if (indexed)
            await collection.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(new BsonDocument { { "roomKey", 1 }, { "sequence", 1 }, { "_id", 1 } }, new CreateIndexOptions { Name = "epic005_room_sequence_id" }), cancellationToken: ct);
        var cases = new List<object>();
        foreach (var (label, room, edge, descending) in new[] { ("beginning", "hot", 0L, false), ("middle-after", "hot", posts / 2L, false), ("tail-after", "hot", posts - 21L, false), ("middle-before", "hot", posts / 2L, true), ("noise-room", "noise-a", posts / 20L, false) })
        {
            await Guard();
            var member = typeof(Message).GetProperty(nameof(Message.Sequence))!;
            var boundary = room == "hot" ? posts : posts / 10;
            Expression<Func<Message, bool>> predicate = descending
                ? m => m.RoomKey == room && m.Sequence < edge && m.Sequence <= boundary
                : m => m.RoomKey == room && m.Sequence > edge && m.Sequence <= boundary;
            var query = new QueryDefinition { Page = 1, PageSize = 21, Sort = [new SortSpec(new MemberPath(typeof(Message), [member], typeof(long), false, -1), descending)] };
            var warm = new List<double>(); double firstMs = 0; object[] recorded = [];
            for (var iteration = 0; iteration < 7; iteration++)
            {
                var before = await profiler.Begin(ct); var watch = Stopwatch.StartNew();
                IReadOnlyList<Message> rows;
                using (EntityContext.NoCache()) rows = await Message.Query(predicate, query, ct);
                watch.Stop(); var commands = await profiler.End(before, explain: iteration == 1, ct);
                Profiler.AssertReadTrace(commands, expectCount: true);
                Fixture.Validate(rows, room, edge, descending);
                if (iteration == 0) firstMs = watch.Elapsed.TotalMilliseconds; else warm.Add(watch.Elapsed.TotalMilliseconds);
                if (iteration == 1) recorded = commands;
            }
            warm.Sort(); cases.Add(new { label, room, edge, descending, firstShapeCallMs = firstMs, p50Ms = Fixture.Rank(warm, .5), p95Ms = Fixture.Rank(warm, .95), warmMs = warm, representativeCommands = recorded });
            Console.WriteLine($"Mongo {posts} indexed={indexed} {label}: first={firstMs:F2} p50={Fixture.Rank(warm, .5):F2}ms");
        }
        // Existing stream seam: consume one provider-sized page then dispose. Not a full-history stream test.
        var streamEdge = posts / 2L;
        Expression<Func<Message, bool>> streamPredicate = m => m.RoomKey == "hot" && m.Sequence > streamEdge && m.Sequence <= posts;
        var streamQuery = new QueryDefinition { Filter = LinqFilterCompiler.Compile(streamPredicate), Sort = [new SortSpec(new MemberPath(typeof(Message), [typeof(Message).GetProperty(nameof(Message.Sequence))!], typeof(long), false, -1), false)] };
        var streamBefore = await profiler.Begin(ct); var streamed = new List<Message>();
        using (EntityContext.NoCache())
            await foreach (var row in Message.QueryStream(streamQuery, 21, ct)) { streamed.Add(row); if (streamed.Count == 21) break; }
        var streamCommands = await profiler.End(streamBefore, explain: true, ct); Fixture.Validate(streamed, "hot", streamEdge, false);
        Profiler.AssertReadTrace(streamCommands, expectCount: false);
        using var indexes = await collection.Indexes.ListAsync(ct);
        phases.Add(new { indexed, indexes = (await indexes.ToListAsync(ct)).Select(Profiler.Json).ToArray(), cases, firstStreamPage = new { count = streamed.Count, commands = streamCommands } });
    }
    outcomes["deferredCrossEntityFailure"] = await Admission.Deferred(database, collection, ct);
}
catch (Exception error)
{
    failures.Add(error.GetType().FullName + ": " + error.Message); Console.Error.WriteLine(error);
}
finally
{
    using var diagnosticTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    var diagnosticFailures = new List<string>(); JsonElement? stats = null; List<string>? collectionNames = null;
    try { await profiler.Disable(diagnosticTimeout.Token); } catch (Exception error) { diagnosticFailures.Add("disable-profile: " + error.Message); }
    try { stats = Profiler.Json(await database.RunCommandAsync<BsonDocument>(new BsonDocument("dbStats", 1), cancellationToken: diagnosticTimeout.Token)); }
    catch (Exception error) { diagnosticFailures.Add("dbStats: " + error.Message); }
    try { using var names = await database.ListCollectionNamesAsync(cancellationToken: diagnosticTimeout.Token); collectionNames = await names.ToListAsync(diagnosticTimeout.Token); }
    catch (Exception error) { diagnosticFailures.Add("listCollections: " + error.Message); }
    process.Refresh();
    var report = new
    {
        scope = "Actual Tangent Message / pinned Koan Mongo CRUD + read microqueries and capability/fault experiments; no app API, source acceptance, live activity, browser or provider winner claim",
        started, finished = DateTimeOffset.UtcNow, root, databaseName, fixedEndpoint = "127.0.0.1:27119", replicaSet = "rs0", directConnection = true,
        databaseVersion = serverBuild.GetValue("version", "unknown").AsString, mongoDriver = typeof(MongoClient).Assembly.FullName,
        runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription, postsInHotRoom = posts,
        sourceHashes, hostStarted = false, applicationPortsOpened = Array.Empty<int>(), credentialsLoaded = false,
        cpuAffinity = OperatingSystem.IsWindows() ? process.ProcessorAffinity.ToString() : "not set", peakWorkingSetBytes = process.PeakWorkingSet64,
        elapsedSeconds = timer.Elapsed.TotalSeconds, cpuSeconds = process.TotalProcessorTime.TotalSeconds,
        databaseStats = stats, touchedCollections = collectionNames, outcomes, phases, failures, diagnosticFailures,
        limitations = new[] { "Database profiling enabled during query timing; six serial warm samples, nearest-rank p95=max, directional only.", "First shape call is not a cold-disk/cache-cleared claim; first facade Save includes initialization.", "Raw BSON clone seed bypasses domain/lifecycle writes and uses synthetic non-protocol source labels. Accepted/content timestamps and sourceURI are inherited from the first row; read-query fixture only, not authoring fidelity.", "No app hosted services started. Replica set native capabilities do not establish Koan cross-entity atomicity.", "Compound index is added only in the isolated database; app index declarations remain unchanged.", "Two logical CPU client affinity; lab DB separately limited by coordinator. Monitored client1.5GiB/DB5GiB/time10min bounds are not all hard quotas." }
    };
    var output = Path.Combine(root, "result.json"); await File.WriteAllTextAsync(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine("REPORT " + output); AppHost.Current = null;
}
if (failures.Count > 0) Environment.ExitCode = 1;
