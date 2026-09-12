using System.Collections;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Koan.Core;
using Koan.Core.Capabilities;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Sorting;
using Koan.Data.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using TangentSpace.Conversation;

// Adapter health only: never start a host, hosted worker, HTTP listener, auth flow,
// source client or domain acceptance pipeline. No production connection is accepted.
if (args.SequenceEqual(["--self-test"])) { Checks.SelfTest(); Console.WriteLine("Postgres probe self-checks passed."); return; }
Checks.SelfTest();
if (args.Length != 4 || args[0] != "--repo" || args[2] != "--root") throw new ArgumentException("Use --repo <absolute repo> --root <new absolute postgres-health-GUID directory>.");
var repo = Path.GetFullPath(args[1]); var root = Path.GetFullPath(args[3]);
var parent = Path.Combine(repo, ".local", "experiments", "epic005");
if (!Path.IsPathFullyQualified(args[1]) || !Path.IsPathFullyQualified(args[3])
    || !File.Exists(Path.Combine(repo, "docs", "epics", "EPIC-005.md"))
    || !string.Equals(Path.GetDirectoryName(root), parent, StringComparison.OrdinalIgnoreCase)
    || !Regex.IsMatch(Path.GetFileName(root), "^postgres-health-[a-f0-9]{32}$") || Directory.Exists(root) || File.Exists(root))
    throw new InvalidOperationException("Refusing an existing/non-isolated experiment root.");
for (var ancestor = new DirectoryInfo(parent); ancestor is not null; ancestor = ancestor.Parent)
    if (ancestor.Exists && (ancestor.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Refusing a reparse-point experiment ancestor.");
var connectionSettings = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("TANGENT_POSTGRES_LAB_CONNECTION") ?? throw new InvalidOperationException("Set the explicit synthetic lab connection."));
if (connectionSettings.Host != "127.0.0.1" || connectionSettings.Port != 25432 || connectionSettings.Database != "postgres"
    || connectionSettings.Username != "epic005" || connectionSettings.Password != "epic005-synthetic-local-only")
    throw new InvalidOperationException("Only the explicit EPIC005 loopback synthetic lab is accepted.");
connectionSettings.Pooling = false; connectionSettings.Timeout = 10; connectionSettings.CommandTimeout = 45;
connectionSettings.ApplicationName = "epic005-postgres-probe";
var connectionString = connectionSettings.ToString();
var schemaName = "epic005_pg_" + Guid.NewGuid().ToString("N");
using var process = Process.GetCurrentProcess();
if (OperatingSystem.IsWindows()) process.ProcessorAffinity = (nint)3;
Directory.CreateDirectory(root);
var started = DateTimeOffset.UtcNow; var elapsed = Stopwatch.StartNew();
var stage = "initializing";
var partial = new Dictionary<string, object?> { ["schema"] = schemaName, ["endpoint"] = "127.0.0.1:25432", ["hostStarted"] = false };
try
{
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10)); var ct = timeout.Token;
var sourcePaths = new[] { "src/server/web/Conversation/Message.cs", ".local/upstream/koan-framework/src/Koan.Data.Core/Data.cs",
    ".local/upstream/koan-framework/src/Koan.Data.Relational.Npgsql/NpgsqlRepository.cs", ".local/upstream/koan-framework/src/Koan.Data.Relational.Npgsql/Runtime/NpgsqlDialect.cs",
    ".local/upstream/koan-framework/src/Koan.Data.Core/Transactions/TransactionCoordinator.cs", ".local/upstream/koan-framework/src/Connectors/Data/Postgres/PostgresAdapterFactory.cs" };
Dictionary<string, string> Hashes() => sourcePaths.ToDictionary(path => path, path => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(repo, path)))));
var sourceHashes = Hashes();
partial["sourceHashes"] = sourceHashes; stage = "opening explicit laboratory connection";
using var trace = new SqlTrace();
using var traceFactory = LoggerFactory.Create(log => log.SetMinimumLevel(LogLevel.Information).AddProvider(trace));
NpgsqlLoggingConfiguration.InitializeLogging(traceFactory, parameterLoggingEnabled: true);
await using var native = new NpgsqlConnection(connectionString); await native.OpenAsync(ct);
static string Q(string text) => '"' + text.Replace("\"", "\"\"") + '"';
async Task<object?> Scalar(string sql) { await using var cmd = new NpgsqlCommand(sql, native); return await cmd.ExecuteScalarAsync(ct); }
async Task Execute(string sql) { await using var cmd = new NpgsqlCommand(sql, native); await cmd.ExecuteNonQueryAsync(ct); }
async Task CheckBudget()
{
    ct.ThrowIfCancellationRequested(); process.Refresh();
    if (process.WorkingSet64 > 1536L * 1024 * 1024 || Convert.ToInt64(await Scalar("SELECT pg_database_size(current_database())")) > 10L * 1024 * 1024 * 1024)
        throw new InvalidOperationException("Probe memory or laboratory database budget exceeded.");
}
if (Convert.ToInt64(await Scalar($"SELECT count(*) FROM pg_namespace WHERE nspname='{schemaName}'")) != 0) throw new InvalidOperationException("Fresh schema unexpectedly exists.");
await Execute("CREATE SCHEMA " + Q(schemaName));
stage = "building unstarted host and provisioning Message schema";
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = root, EnvironmentName = "Testing", Args = [] });
builder.Configuration.Sources.Clear();
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
    ["Tangent:Conversation:Storage"] = "Local", ["Koan:Data:Sources:Default:Adapter"] = "postgres",
    ["Koan:Data:Sources:Default:ConnectionString"] = connectionString, ["Koan:Data:Sources:Default:SearchPath"] = schemaName,
    ["Koan:Data:Postgres:ConnectionString"] = connectionString, ["Koan:Data:Postgres:SearchPath"] = schemaName,
    ["Koan:Identity:Posture"] = "Closed", ["Koan:Identity:SeedDevUsers"] = "false" });
builder.Logging.ClearProviders(); builder.Services.AddKoan();
using var host = builder.Build(); // Deliberately no Start/Run.
AppHost.Current = host.Services; TestHooks.ResetDataConfigs();
var dataService = host.Services.GetRequiredService<IDataService>();
var repository = dataService.GetRepository<Message, string>();
var selectedAdapter = dataService.GetScopeDiagnostics<Message, string>().AdapterName;
if (!selectedAdapter.Contains("NpgsqlRepository", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected provider selected: " + selectedAdapter);
var caps = DataCaps.Describe(repository, repository.GetType().Name);
var capabilities = new { linq = caps.Has(DataCaps.Query.Linq), boundedPaging = caps.Has(DataCaps.Query.ProviderBoundedPaging), atomicBatch = caps.Has(DataCaps.Write.AtomicBatch), bulkUpsert = caps.Has(DataCaps.Write.BulkUpsert) };
partial["selectedRepository"] = repository.GetType().FullName; partial["selectedAdapter"] = selectedAdapter; partial["capabilities"] = capabilities;
using (EntityContext.NoCache()) await Checks.Make("hot", 1).Save(ct);
string table;
await using (var cmd = new NpgsqlCommand("SELECT tablename FROM pg_tables WHERE schemaname=@schema", native))
{
    cmd.Parameters.AddWithValue("schema", schemaName); var tables = new List<string>();
    await using var reader = await cmd.ExecuteReaderAsync(ct); while (await reader.ReadAsync(ct)) tables.Add(reader.GetString(0));
    if (tables.Count != 1) throw new InvalidOperationException("Expected one Message table; got " + string.Join(',', tables));
    table = tables[0];
}
var qualified = Q(schemaName) + "." + Q(table);
var columnInfo = new List<object>(); string idColumn = "", jsonColumn = "";
await using (var cmd = new NpgsqlCommand("SELECT column_name,data_type FROM information_schema.columns WHERE table_schema=@s AND table_name=@t ORDER BY ordinal_position", native))
{
    cmd.Parameters.AddWithValue("s", schemaName); cmd.Parameters.AddWithValue("t", table);
    await using var reader = await cmd.ExecuteReaderAsync(ct);
    while (await reader.ReadAsync(ct)) { var name = reader.GetString(0); var type = reader.GetString(1); columnInfo.Add(new { name, type }); if (type == "jsonb") jsonColumn = name; else if (name.Equals("Id", StringComparison.OrdinalIgnoreCase)) idColumn = name; }
}
if (columnInfo.Count != 2 || idColumn.Length == 0 || jsonColumn.Length == 0) throw new InvalidOperationException("Unrecognized Message storage shape.");
var template = JsonNode.Parse((string)(await Scalar($"SELECT {Q(jsonColumn)}::text FROM {qualified} LIMIT 1"))!)!.AsObject();
var jsonCase = template.ContainsKey("RoomKey") ? "pascal" : template.ContainsKey("roomKey") ? "camel" : throw new InvalidOperationException("RoomKey not found in actual JSON.");
string Field(string value) => jsonCase == "camel" ? char.ToLowerInvariant(value[0]) + value[1..] : value;
async Task<List<object>> Indexes()
{
    var found = new List<object>(); await using var cmd = new NpgsqlCommand("SELECT indexname,indexdef FROM pg_indexes WHERE schemaname=@s AND tablename=@t ORDER BY indexname", native);
    cmd.Parameters.AddWithValue("s", schemaName); cmd.Parameters.AddWithValue("t", table);
    await using var reader = await cmd.ExecuteReaderAsync(ct); while (await reader.ReadAsync(ct)) found.Add(new { name = reader.GetString(0), sql = reader.GetString(1) }); return found;
}
var originalIndexes = await Indexes();
partial["table"] = table; partial["columns"] = columnInfo; partial["originalIndexes"] = originalIndexes; stage = "Message CRUD qualification";
// Small CRUD qualification through the actual entity facade, independent of native fixture seeding.
using (EntityContext.NoCache())
{
    var item = Checks.Make("crud", 1); await item.Save(ct);
    var read = await Message.Get(item.Id, ct) ?? throw new InvalidDataException("CRUD insert missing.");
    if (read.Content.Text != item.Content.Text || read.RoomKey != item.RoomKey) throw new InvalidDataException("CRUD roundtrip mismatch.");
    read.Content = read.Content with { Text = "Updated synthetic content" }; await read.Save(ct);
    if ((await Message.Get(item.Id, ct))?.Content.Text != "Updated synthetic content") throw new InvalidDataException("CRUD update mismatch.");
    if (!await Message.Remove(item.Id, ct) || await Message.Get(item.Id, ct) is not null) throw new InvalidDataException("CRUD delete mismatch.");
}
partial["crudPassed"] = true;
async Task<List<object>> Explain(SqlTrace.Statement[] statements, long edge, long upper)
{
    var plans = new List<object>();
    foreach (var statement in statements.Where(item => item.Sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)))
    {
        await using var cmd = new NpgsqlCommand("EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) " + statement.Sql, native);
        var values = new object[] { Checks.Room("hot"), edge, upper };
        if (statement.Sql.Contains("$1")) { foreach (var value in values) cmd.Parameters.Add(new NpgsqlParameter { Value = value }); }
        else for (var index = 0; index < values.Length; index++) cmd.Parameters.AddWithValue("p" + index, values[index]);
        var plan = await cmd.ExecuteScalarAsync(ct);
        var parsed = JsonNode.Parse((string)plan!);
        var expectedRows = statement.Sql.Contains("COUNT(", StringComparison.OrdinalIgnoreCase) ? 1 : 21;
        if (parsed?[0]?["Plan"]?["Actual Rows"]?.GetValue<long>() != expectedRows) throw new InvalidDataException("Captured SQL replay did not reproduce the expected result shape; predicate parameter mapping needs investigation.");
        plans.Add(new { statement.Sql, statement.Rendered, knownPredicateParameters = values, plan = parsed });
    }
    return plans;
}
async Task<object> Measure(int hotCount, string phase)
{
    var cases = new List<object>();
    partial["currentMeasurement"] = new { phase, hotCount, cases };
    foreach (var (label, edge, descending) in new[] { ("beginning", 0L, false), ("middle-after", hotCount / 2L, false), ("tail-after", hotCount - 21L, false), ("middle-before", hotCount / 2L, true) })
    foreach (var mode in new[] { "materialized-query", "first-stream-page" })
    {
        var member = typeof(Message).GetProperty(nameof(Message.Sequence))!;
        var query = new QueryDefinition { Page = 1, PageSize = 21, Sort = [new SortSpec(new MemberPath(typeof(Message), [member], typeof(long), false, -1), descending)] };
        var room = Checks.Room("hot"); long upper = hotCount;
        Expression<Func<Message, bool>> predicate = descending ? m => m.RoomKey == room && m.Sequence < edge && m.Sequence <= upper : m => m.RoomKey == room && m.Sequence > edge && m.Sequence <= upper;
        var times = new List<double>(); SqlTrace.Statement[] sample = [];
        for (var iteration = 0; iteration < 7; iteration++)
        {
            stage = $"{phase}: {hotCount} {label} {mode} iteration {iteration}";
            await CheckBudget(); trace.Begin(); var timer = Stopwatch.StartNew(); IReadOnlyList<Message> rows;
            using (EntityContext.NoCache())
            {
                if (mode == "materialized-query") rows = await Message.Query(predicate, query, ct);
                else { var first = new List<Message>(); await foreach (var item in Message.QueryStream(predicate, descending ? "-Sequence" : "Sequence", 21, ct)) { first.Add(item); if (first.Count == 21) break; } rows = first; }
            }
            timer.Stop(); var captured = trace.End(); Checks.Validate(rows, edge, descending);
            if (!captured.Any(item => item.Sql.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("Actual SQL capture did not record the query.");
            if (mode == "first-stream-page" && captured.Any(item => item.Sql.Contains("COUNT(", StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("Count-free stream performed a count.");
            if (iteration > 0) times.Add(timer.Elapsed.TotalMilliseconds); if (iteration == 1) sample = captured;
        }
        times.Sort(); var plans = await Explain(sample, edge, hotCount);
        cases.Add(new { label, mode, edge, returned = 21, p50Ms = Checks.Rank(times, .5), p95Ms = Checks.Rank(times, .95), timingsMs = times,
            countStatements = sample.Count(item => item.Sql.Contains("COUNT(", StringComparison.OrdinalIgnoreCase)), statements = sample, plans });
        Console.WriteLine($"{phase} hot={hotCount} {label} {mode}: p50={Checks.Rank(times, .5):F2}ms countStatements={sample.Count(item => item.Sql.Contains("COUNT(", StringComparison.OrdinalIgnoreCase))}");
    }
    trace.Begin(); var countTimer = Stopwatch.StartNew(); long exact;
    using (EntityContext.NoCache()) exact = await Message.Count.Exact(ct);
    countTimer.Stop(); var countSql = trace.End();
    if (exact != hotCount + 2 * (hotCount / 10)) throw new InvalidDataException("Exact count differs from the synthetic multi-room fixture.");
    return new { phase, hotPosts = hotCount, distractorPostsPerTopic = hotCount / 10, totalPosts = exact, cases,
        explicitExactCount = new { value = exact, elapsedMs = countTimer.Elapsed.TotalMilliseconds, statements = countSql },
        databaseBytes = Convert.ToInt64(await Scalar("SELECT pg_database_size(current_database())")), workingSetBytes = process.WorkingSet64 };
}
var measurements = new List<object>(); var seeds = new List<object>(); int seededHot = 1, seededDistractors = 0;
partial["measurements"] = measurements; partial["seeds"] = seeds;
foreach (var hotCount in new[] { 10_000, 100_000 })
{
    stage = "native synthetic seed: hot " + hotCount;
    var seedTimer = Stopwatch.StartNew();
    foreach (var lane in new[] { "hot", "side-a", "side-b" })
    {
        var from = lane == "hot" ? seededHot + 1 : seededDistractors + 1; var to = lane == "hot" ? hotCount : hotCount / 10;
        await using var importer = await native.BeginBinaryImportAsync($"COPY {qualified} ({Q(idColumn)}, {Q(jsonColumn)}) FROM STDIN (FORMAT BINARY)", ct);
        for (var sequence = from; sequence <= to; sequence++)
        {
            ct.ThrowIfCancellationRequested(); process.Refresh();
            if (sequence % 1000 == 0 && process.WorkingSet64 > 1536L * 1024 * 1024) throw new InvalidOperationException("Client working-set budget exceeded during seed.");
            var document = (JsonObject)template.DeepClone();
            document[Field("RoomKey")] = Checks.Room(lane); document[Field("Sequence")] = sequence;
            document[Field("AuthorParticipantId")] = "synthetic-" + (sequence % 32).ToString("D2");
            document[Field("SourceUri")] = "at://synthetic.invalid/local.tangent.message/" + lane + "-" + sequence;
            document[Field("SourceCid")] = "synthetic-cid-" + sequence;
            document[Field("Removed")] = sequence % 101 == 0;
            document[Field("Content")]![Field("Text")] = sequence % 97 == 0 ? new string('x', 3900) : "Synthetic @member #scale post " + sequence + " — café 日本語";
            await importer.StartRowAsync(ct); await importer.WriteAsync(Checks.Id(lane, sequence), NpgsqlDbType.Text, ct); await importer.WriteAsync(document.ToJsonString(), NpgsqlDbType.Jsonb, ct);
        }
        await importer.CompleteAsync(ct);
    }
    seededHot = hotCount; seededDistractors = hotCount / 10; seedTimer.Stop(); await CheckBudget(); await Execute("ANALYZE " + qualified);
    seeds.Add(new { hotCount, nativeCopyElapsedMs = seedTimer.Elapsed.TotalMilliseconds, bypassesDomainWrites = true });
    measurements.Add(await Measure(hotCount, "baseline-no-compound-index"));
}
var indexSql = $"CREATE INDEX {Q("epic005_room_sequence_id")} ON {qualified} (({Q(jsonColumn)} #>> '{{{Field("RoomKey")}}}'), ((({Q(jsonColumn)} #>> '{{{Field("Sequence")}}}')::bigint)), {Q(idColumn)})";
stage = "isolated compound-expression-index creation"; partial["indexSql"] = indexSql;
await Execute(indexSql); await Execute("ANALYZE " + qualified); await CheckBudget();
measurements.Add(await Measure(100_000, "isolated-expression-index"));
var indexed = await Indexes();
stage = "same-entity native and deferred-coordinator failure qualification"; partial["indexed"] = indexed;
var transactions = await TransactionHealth.Run(native, connectionString, qualified, idColumn, ct);
partial["transactions"] = transactions;
var settings = new Dictionary<string, object?>();
foreach (var key in new[] { "server_version", "fsync", "synchronous_commit", "full_page_writes", "shared_buffers", "work_mem", "max_parallel_workers_per_gather" }) settings[key] = await Scalar("SHOW " + key);
var endingHashes = Hashes();
if (sourceHashes.Any(entry => endingHashes[entry.Key] != entry.Value)) throw new InvalidOperationException("Source changed during the measured run.");
process.Refresh();
var report = new { scope = "Actual Tangent Message/Koan PostgreSQL adapter health and query microexperiment, not production admission or domain throughput", started, finished = DateTimeOffset.UtcNow,
    root, schemaName, table, columns = columnInfo, selectedRepository = repository.GetType().FullName, selectedAdapter, capabilities, crudPassed = true,
    endpoint = "127.0.0.1:25432", database = "postgres", credentials = "public synthetic lab fixture only; not copied from user state", hostStarted = false,
    runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription, koanAssembly = typeof(Data<,>).Assembly.FullName, npgsqlAssembly = typeof(NpgsqlConnection).Assembly.FullName,
    settings, originalIndexes, indexSql, indexed, seeds, measurements, transactions, sourceHashes, elapsedSeconds = elapsed.Elapsed.TotalSeconds,
    processCpuAffinity = OperatingSystem.IsWindows() ? process.ProcessorAffinity.ToString() : "not-set", processPeakWorkingSetBytes = process.PeakWorkingSet64,
    limitations = new[] { "Only first21 rows of QueryStream; no mutation-safe offset continuation claim.", "One discarded first query plus six warm serial samples; nearest-rank p95 is max, not load-test tail latency.", "Npgsql logging enabled and pooling disabled in this experiment; no cold-cache or matched-provider winner claim.", "Native binaryCOPY fixture seed preserves actual storage template, uses fixed template timestamps and bypasses domain acceptance/lifecycle.", "Same-entity batch atomicity is distinct from cross-entity domain acceptance and deferred coordinator semantics.", "The explicit index exists only in this fresh disposable schema; no application/upstream mapping was modified." } };
await File.WriteAllTextAsync(Path.Combine(root, "result.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), ct);
Console.WriteLine("REPORT " + Path.Combine(root, "result.json")); AppHost.Current = null;
}
catch (Exception failure)
{
    // Failure/timeout evidence is written offline with no canceled token, preserving
    // already completed measurements instead of silently dropping the baseline.
    var detail = failure.ToString();
    File.WriteAllText(Path.Combine(root, "failure.json"), JsonSerializer.Serialize(new {
        status = "failed", stage, started, finished = DateTimeOffset.UtcNow, root,
        error = detail[..Math.Min(detail.Length, 16000)], partial,
        scope = "Incomplete adapter health run; no passing claims for the failed stage." }, new JsonSerializerOptions { WriteIndented = true }));
    AppHost.Current = null;
    Console.Error.WriteLine("FAILURE REPORT " + Path.Combine(root, "failure.json"));
    throw;
}

internal static class Checks
{
    internal static string Room(string lane) => "epic005-" + lane + "-topic";
    internal static string Id(string lane, long sequence) => "epic005-" + lane + "-" + sequence.ToString("D10");
    internal static Message Make(string lane, long sequence) => new() { Id = Id(lane, sequence), RoomKey = Room(lane), AuthorParticipantId = "synthetic-01", Sequence = sequence,
        AcceptedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), Content = new MessageContent("Synthetic @member #scale post " + sequence + " — café 日本語", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), null),
        SourceUri = "at://synthetic.invalid/local.tangent.message/" + lane + "-" + sequence, SourceCid = "synthetic-cid-" + sequence, Facets = [] };
    internal static double Rank(IReadOnlyList<double> sorted, double p) => sorted[(int)Math.Ceiling(p * sorted.Count) - 1];
    internal static void Validate(IReadOnlyList<Message> rows, long edge, bool descending)
    {
        if (rows.Count != 21 || rows.Select(row => row.Id).Distinct().Count() != 21) throw new InvalidDataException("Bad window count/identity.");
        for (var index = 0; index < rows.Count; index++) { var sequence = descending ? edge - index - 1 : edge + index + 1; if (rows[index].Sequence != sequence || rows[index].RoomKey != Room("hot") || rows[index].Id != Id("hot", sequence)) throw new InvalidDataException("Wrong room, order, sequence, or ID in window."); }
    }
    internal static void SelfTest()
    {
        var rows = Enumerable.Range(1, 21).Select(sequence => Make("hot", sequence)).ToArray(); Validate(rows, 0, false); Validate(rows.Reverse().ToArray(), 22, true);
        if (Rank([1, 2, 3, 4, 5, 6], .5) != 3 || Rank([1, 2, 3, 4, 5, 6], .95) != 6) throw new Exception("Percentile self-check failed.");
        rows[^1].RoomKey = Room("side-a"); try { Validate(rows, 0, false); } catch (InvalidDataException) { return; } throw new Exception("Scope corruption self-check failed.");
    }
}

internal sealed class SqlTrace : ILoggerProvider
{
    internal sealed record Statement(string Sql, string Rendered);
    private readonly List<Statement> statements = []; private bool enabled;
    internal void Begin() { lock (statements) { statements.Clear(); enabled = true; } }
    internal Statement[] End() { lock (statements) { enabled = false; return statements.ToArray(); } }
    public ILogger CreateLogger(string categoryName) => new Capture(this, categoryName);
    public void Dispose() { }
    private sealed class Capture(SqlTrace owner, string category) : ILogger
    {
        public bool IsEnabled(LogLevel level) => category == "Npgsql.Command" && level == LogLevel.Information;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level)) return;
            lock (owner.statements)
            {
                if (!owner.enabled) return;
                var entries = state as IEnumerable<KeyValuePair<string, object?>>;
                var sql = entries?.FirstOrDefault(entry => entry.Key == "CommandText").Value?.ToString();
                if (string.IsNullOrWhiteSpace(sql)) return;
                if (owner.statements.Count >= 32) throw new InvalidOperationException("SQL trace statement bound exceeded.");
                owner.statements.Add(new Statement(sql, formatter(state, exception)));
            }
        }
    }
}
