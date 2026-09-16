using System.Diagnostics;
using System.Linq.Expressions;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sorting;
using Koan.Data.Core;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TangentSpace.Conversation;

// This is intentionally a query-path experiment. No host, HTTP server, hosted service,
// source client, profile worker, auth flow or domain acceptance pipeline is started.
if (args.SequenceEqual(["--self-test"])) { ProbeChecks.SelfTest(); Console.WriteLine("Probe checks passed."); return; }
ProbeChecks.SelfTest();
if (args.Length != 4 || args[0] != "--repo" || args[2] != "--root")
    throw new ArgumentException("Use --repo <absolute repo> --root <new absolute sqlite-baseline-GUID directory>.");
var repo = Path.GetFullPath(args[1]);
var root = Path.GetFullPath(args[3]);
var experimentParent = Path.Combine(repo, ".local", "experiments", "epic005");
if (!Path.IsPathFullyQualified(args[1]) || !Path.IsPathFullyQualified(args[3])
    || !File.Exists(Path.Combine(repo, "docs", "epics", "EPIC-005.md"))
    || !string.Equals(Path.GetDirectoryName(root), experimentParent, StringComparison.OrdinalIgnoreCase)
    || !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(root), "^sqlite-baseline-[a-f0-9]{32}$")
    || Directory.Exists(root) || File.Exists(root))
    throw new InvalidOperationException("Refusing an existing or non-isolated experiment root.");
// Reject reparse points anywhere in the existing target chain before creating anything.
for (var path = new DirectoryInfo(experimentParent); path is not null; path = path.Parent)
    if (path.Exists && (path.Attributes & FileAttributes.ReparsePoint) != 0)
        throw new InvalidOperationException("Experiment ancestors must not be reparse points.");
using var process = Process.GetCurrentProcess();
if (OperatingSystem.IsWindows()) process.ProcessorAffinity = (nint)15;
Directory.CreateDirectory(root);
var database = Path.Combine(root, "baseline.sqlite");
var connectionString = new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString();
var started = DateTimeOffset.UtcNow;
var budget = Stopwatch.StartNew();
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
var ct = timeout.Token;
void CheckBudget()
{
    ct.ThrowIfCancellationRequested(); process.Refresh();
    if (process.WorkingSet64 > 1536L * 1024 * 1024 || new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) > 10L * 1024 * 1024 * 1024)
        throw new InvalidOperationException("Probe memory or generated-data ceiling reached.");
}
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = root, EnvironmentName = "Testing", Args = [] });
builder.Configuration.Sources.Clear();
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Tangent:Site:Name"] = "EPIC005 isolated query probe",
    ["Tangent:Conversation:Storage"] = "Local",
    ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
    ["Koan:Data:Sources:Default:ConnectionString"] = connectionString,
    ["Koan:Data:Sqlite:ConnectionString"] = connectionString,
    ["Koan:Identity:Posture"] = "Closed",
    ["Koan:Identity:SeedDevUsers"] = "false"
});
builder.Logging.ClearProviders();
builder.Services.AddKoan();
using var host = builder.Build(); // Deliberately do not Start/Run this host.
AppHost.Current = host.Services;
TestHooks.ResetDataConfigs();
SQLitePCL.Batteries_V2.Init();
using var trace = new NativeTrace(database);
var seedTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
Post Make(long sequence) => new()
{
    Id = "epic005-" + sequence.ToString("D10"), RoomKey = "epic005-hot-topic",
    AuthorParticipantId = "synthetic-" + (sequence % 32).ToString("D2"), Sequence = sequence,
    AcceptedAt = seedTime.AddSeconds(sequence),
    Content = new PostContent(sequence % 97 == 0 ? new string('x', 3900) : "Synthetic @member #scale post " + sequence + " — café 日本語", seedTime.AddSeconds(sequence),
        sequence > 1 && sequence % 7 == 0 ? new SourceReference("at://synthetic.invalid/local.tangent.message/" + (sequence - 1), "synthetic-cid") : null),
    SourceUri = "at://synthetic.invalid/local.tangent.message/" + sequence,
    SourceCid = "synthetic-cid-" + sequence,
    Removed = sequence % 101 == 0, EditedAt = sequence % 53 == 0 ? seedTime.AddDays(2) : null,
    Facets = sequence % 97 == 0 ? [] : [new PostFacet { Kind = PostFacet.Mention, Start = 10, End = 17, Did = "did:plc:syntheticnotresolvable" }]
};
using (EntityContext.NoCache()) await Make(1).Save(ct);
await using var seedConnection = new SqliteConnection(connectionString);
await seedConnection.OpenAsync(ct);
var schema = new List<object>();
string table = "";
await using (var command = seedConnection.CreateCommand())
{
    command.CommandText = "SELECT name, sql FROM sqlite_master WHERE type = 'table'";
    await using var reader = await command.ExecuteReaderAsync(ct);
    while (await reader.ReadAsync(ct))
    {
        var name = reader.GetString(0); schema.Add(new { name, sql = reader.GetString(1) });
        if (name.EndsWith(".Post", StringComparison.Ordinal) || name == "Post") table = name;
    }
}
if (table.Length == 0) throw new InvalidOperationException("Expected actual Post table was not found: " + JsonSerializer.Serialize(schema));
static string Quote(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";
var columns = new List<string>(); var template = new List<object>();
await using (var command = seedConnection.CreateCommand())
{
    command.CommandText = "SELECT * FROM " + Quote(table) + " LIMIT 1";
    await using var reader = await command.ExecuteReaderAsync(ct); await reader.ReadAsync(ct);
    for (var i = 0; i < reader.FieldCount; i++) { columns.Add(reader.GetName(i)); template.Add(reader.GetValue(i)); }
}
var idColumn = template.FindIndex(v => v is string value && value == Make(1).Id);
var jsonColumn = template.FindIndex(v => v is string value && value.StartsWith('{') && value.Contains("epic005-hot-topic"));
if (idColumn < 0 || jsonColumn < 0) throw new InvalidOperationException("Unrecognized Post storage shape.");
var jsonOptions = new JsonSerializerOptions();
// Preserve framework's actual JSON property casing from the provisioned row.
var original = JsonNode.Parse((string)template[jsonColumn])!.AsObject();
if (original.ContainsKey("roomKey")) jsonOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
var indexes = new List<object>();
await using (var command = seedConnection.CreateCommand())
{
    command.CommandText = "SELECT name, sql FROM sqlite_master WHERE type = 'index' AND tbl_name = @table";
    command.Parameters.AddWithValue("@table", table);
    await using var reader = await command.ExecuteReaderAsync(ct);
    while (await reader.ReadAsync(ct)) indexes.Add(new { name = reader.GetString(0), sql = reader.IsDBNull(1) ? null : reader.GetString(1) });
}
var tiers = new List<object>(); long seeded = 1;
foreach (var total in new[] { 10_000, 100_000 })
{
    CheckBudget(); var seedWatch = Stopwatch.StartNew();
    using (var transaction = seedConnection.BeginTransaction())
    {
        await using var insert = seedConnection.CreateCommand(); insert.Transaction = transaction;
        insert.CommandText = $"INSERT INTO {Quote(table)} ({string.Join(",", columns.Select(Quote))}) VALUES ({string.Join(",", columns.Select((_, i) => "@v" + i))})";
        for (var i = 0; i < columns.Count; i++) insert.Parameters.AddWithValue("@v" + i, template[i]);
        insert.Prepare();
        for (var n = seeded + 1; n <= total; n++)
        {
            if (n % 1000 == 0) CheckBudget();
            var message = Make(n);
            insert.Parameters[idColumn].Value = message.Id;
            insert.Parameters[jsonColumn].Value = JsonSerializer.Serialize(message, jsonOptions);
            await insert.ExecuteNonQueryAsync(ct);
        }
        transaction.Commit(); seeded = total;
    }
    seedWatch.Stop(); var cases = new List<object>();
    var count = (long)total;
    foreach (var (label, edge, descending) in new[] { ("beginning", 0L, false), ("middle-after", count / 2, false), ("tail-after", count - 21, false), ("middle-before", count / 2, true) })
    {
        var member = typeof(Post).GetProperty(nameof(Post.Sequence))!;
        var query = new QueryDefinition { Page = 1, PageSize = 21, Sort = [new SortSpec(new MemberPath(typeof(Post), [member], typeof(long), false, -1), descending)] };
        Expression<Func<Post, bool>> predicate = descending
            ? m => m.RoomKey == "epic005-hot-topic" && m.Sequence < edge && m.Sequence <= count
            : m => m.RoomKey == "epic005-hot-topic" && m.Sequence > edge && m.Sequence <= count;
        var times = new List<double>(); var samples = new List<object>();
        for (var iteration = 0; iteration < 7; iteration++)
        {
            CheckBudget(); trace.Begin(); var watch = Stopwatch.StartNew();
            IReadOnlyList<Post> rows;
            using (EntityContext.NoCache()) rows = await Post.Query(predicate, query, ct);
            watch.Stop(); var statements = trace.End();
            ProbeChecks.ValidateWindow(rows, edge, descending);
            if (iteration > 0) times.Add(watch.Elapsed.TotalMilliseconds);
            if (iteration == 1)
            {
                foreach (var statement in statements)
                {
                    var plan = new List<string>();
                    if (statement.Sql.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                    {
                        await using var explain = seedConnection.CreateCommand(); explain.CommandText = "EXPLAIN QUERY PLAN " + statement.Sql;
                        await using var reader = await explain.ExecuteReaderAsync(ct);
                        while (await reader.ReadAsync(ct)) plan.Add(reader.GetString(3));
                    }
                    samples.Add(new { statement.Sql, statement.Milliseconds, statement.FullScanSteps, statement.SortOperations, statement.VirtualMachineSteps, plan });
                }
            }
        }
        times.Sort();
        cases.Add(new { label, edge, returned = 21, warmSamples = times.Count, p50Ms = ProbeChecks.NearestRank(times, .5), p95Ms = ProbeChecks.NearestRank(times, .95), minMs = times[0], maxMs = times[^1], timingsMs = times, statements = samples });
        Console.WriteLine($"{total} {label}: p50={ProbeChecks.NearestRank(times, .5):F2}ms max={times[^1]:F2}ms");
    }
    process.Refresh();
    tiers.Add(new { posts = total, bulkSeedElapsedMs = seedWatch.Elapsed.TotalMilliseconds, databaseBytes = new FileInfo(database).Length, processWorkingSetBytes = process.WorkingSet64, cases });
}
var settings = new Dictionary<string, object?>();
foreach (var pragma in new[] { "journal_mode", "synchronous", "page_size", "cache_size", "temp_store" })
{
    await using var command = seedConnection.CreateCommand(); command.CommandText = "PRAGMA " + pragma; settings[pragma] = await command.ExecuteScalarAsync(ct);
}
// A separate failure experiment runs only after read timing/schema capture. It does not
// characterize acceptance: it tests the exact EntityContext deferred-commit primitive.
var transactionFailure = await TransactionProbe.Run(seedConnection, table, columns[idColumn], Make, ct);
var sourcePaths = new[]
{
    "src/server/web/Conversation/Post.cs", "src/server/web/Conversation/ConversationService.History.cs",
    "src/server/web/Conversation/ConversationService.McpWindow.cs",
    ".local/upstream/koan-framework/src/Koan.Data.Core/Data.cs",
    ".local/upstream/koan-framework/src/Connectors/Data/Sqlite/Runtime/SqliteRepository.cs",
    ".local/upstream/koan-framework/src/Koan.Data.Core/Transactions/TransactionCoordinator.cs"
};
var sourceHashes = sourcePaths.ToDictionary(path => path, path => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(repo, path)))));
var result = new
{
    scope = "Actual Post.Query/Koan/SQLite read micro-query; not full API, domain writes, SSE, transactions or browser proof",
    started, finished = DateTimeOffset.UtcNow, root, database, deterministicSeed = "epic005-sqlite-v1",
    processCpuAffinity = OperatingSystem.IsWindows() ? process.ProcessorAffinity.ToString() : "not-set",
    runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
    machine = Environment.MachineName, sqliteVersion = SQLitePCL.raw.sqlite3_libversion().utf8_to_string(),
    koanAssembly = typeof(Data<,>).Assembly.FullName, appAssembly = typeof(Post).Assembly.FullName,
    hostStarted = false, portsOpened = Array.Empty<int>(), credentialsLoaded = false,
    processPeakWorkingSetBytes = process.PeakWorkingSet64, processCpuSeconds = process.TotalProcessorTime.TotalSeconds,
    elapsedSeconds = budget.Elapsed.TotalSeconds, settings, schema, indexes, tiers, transactionFailure, sourceHashes,
    percentileConvention = "nearest rank: sorted[ceil(p * n) - 1]; 6 warm serial samples, p95 equals max",
    traceErrors = trace.Errors,
    limitations = new[] { "Six serial warm samples: max is reported as nearest-rank p95; this is not a load-test percentile.", "No OS cache flush or cold-disk claim.", "Direct bulk seed bypasses acceptance, Post lifecycle and real transactions; one Post.Save provisions the schema.", "No index added, no count/query tuning applied; default count behavior is captured as-is.", "100k ceiling for first baseline; million-post/provider/saturation proofs remain pending." }
};
var report = Path.Combine(root, "result.json");
await File.WriteAllTextAsync(report, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), ct);
Console.WriteLine("REPORT " + report);
AppHost.Current = null;

sealed class NativeTrace : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Extension(nint db, nint error, nint api);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int Trace(uint kind, nint context, nint statement, nint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int AutoExtension(nint callback);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int TraceV2(nint db, uint mask, Trace callback, nint context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate nint ExpandedSql(nint statement);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void Free(nint value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int StmtStatus(nint statement, int operation, int reset);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate nint DbFilename(nint db, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    readonly nint library; readonly Extension extension; readonly Trace callback; readonly AutoExtension cancel;
    readonly TraceV2 trace; readonly ExpandedSql expanded; readonly Free free; readonly StmtStatus status; readonly DbFilename filename;
    readonly List<Statement> statements = []; bool capturing; public List<string> Errors { get; } = [];
    public NativeTrace(string database)
    {
        var native = Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", "e_sqlite3.dll");
        library = NativeLibrary.Load(native);
        T Function<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
        trace = Function<TraceV2>("sqlite3_trace_v2"); expanded = Function<ExpandedSql>("sqlite3_expanded_sql"); free = Function<Free>("sqlite3_free");
        status = Function<StmtStatus>("sqlite3_stmt_status"); filename = Function<DbFilename>("sqlite3_db_filename"); cancel = Function<AutoExtension>("sqlite3_cancel_auto_extension");
        callback = (kind, _, statement, value) =>
        {
            try
            {
                if (capturing && kind == 2)
                {
                    var text = expanded(statement);
                    try { statements.Add(new Statement(Marshal.PtrToStringUTF8(text) ?? "", Marshal.ReadInt64(value) / 1_000_000d, status(statement, 1, 0), status(statement, 2, 0), status(statement, 4, 0))); }
                    finally { free(text); }
                }
            }
            catch (Exception error) { Errors.Add(error.Message); }
            return 0;
        };
        extension = (db, _, _) =>
        {
            var path = Marshal.PtrToStringUTF8(filename(db, "main"));
            if (path is not null && !string.Equals(Path.GetFullPath(path), database, StringComparison.OrdinalIgnoreCase)) return 1;
            return trace(db, 2, callback, 0);
        };
        if (Function<AutoExtension>("sqlite3_auto_extension")(Marshal.GetFunctionPointerForDelegate(extension)) != 0) throw new InvalidOperationException("Could not attach SQLite trace.");
    }
    public void Begin() { statements.Clear(); capturing = true; }
    public Statement[] End() { capturing = false; return statements.ToArray(); }
    public void Dispose() { cancel(Marshal.GetFunctionPointerForDelegate(extension)); GC.KeepAlive(callback); GC.KeepAlive(extension); NativeLibrary.Free(library); }
    public sealed record Statement(string Sql, double Milliseconds, int FullScanSteps, int SortOperations, int VirtualMachineSteps);
}
