using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TangentSpace.Participants;
using Xunit;

namespace TangentSpace.Tests;

// Uses a fresh local SQLite file and builds, but never starts, a host. No HTTP,
// hosted workers, credentials or public handle resolver participate in this suite.
[Collection("Experience integration")]
public sealed class ParticipantLabelWindowTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Empty_duplicate_and_invalid_inputs_are_bounded_before_any_data_or_handle_read()
    {
        using var fixture = new SqliteFixture();
        var source = new Handles();
        var directory = new ParticipantDirectory(TimeProvider.System, source);
        fixture.Trace.Begin();
        Assert.Empty(await directory.LabelsFor([], fixture.Ct));
        Assert.Empty(await directory.LabelsFor([null!, null!], fixture.Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => directory.LabelsFor(
            Enumerable.Repeat("same", ParticipantDirectory.LabelInputLimit + 1), fixture.Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => directory.LabelsFor(
            [new string('x', ParticipantDirectory.LabelParticipantIdLimit + 1)], fixture.Ct));
        Assert.Empty(fixture.Trace.End());
        Assert.Empty(source.Calls);
    }

    [Fact]
    public async Task Input_limits_are_inclusive_duplicates_deduplicate_and_missing_labels_remain_absent()
    {
        using var fixture = new SqliteFixture();
        var longest = new string('x', ParticipantDirectory.LabelParticipantIdLimit);
        await fixture.Seed([Identity("label", longest, "internal", "café 日本語")]);
        var source = new Handles();
        var directory = new ParticipantDirectory(TimeProvider.System, source);
        fixture.Trace.Begin();
        var labels = await directory.LabelsFor(Enumerable.Repeat(longest, ParticipantDirectory.LabelInputLimit), fixture.Ct, false);
        Assert.Equal("café 日本語", Assert.Single(labels).Value);
        Assert.Single(fixture.Trace.End().Where(IsIdentityRead));
        Assert.Empty(await directory.LabelsFor(["missing"], fixture.Ct, false));
        Assert.Empty(source.Calls);
    }

    [Fact]
    public async Task Native_scoped_keyset_pages_read_only_requested_identities_without_counts_or_global_scan()
    {
        using var fixture = new SqliteFixture();
        var requested = Enumerable.Range(0, 130).Select(n => $"participant-{n:D4}").ToArray();
        System.Linq.Expressions.Expression<Func<ParticipantIdentity, bool>> shorthand = identity => requested.Contains(identity.ParticipantId);
        output.WriteLine($"Array Contains method: {((System.Linq.Expressions.MethodCallExpression)shorthand.Body).Method.DeclaringType}; lowered filter: {Koan.Data.Abstractions.Filtering.LinqFilterCompiler.Compile(shorthand)}");
        var ordinary = requested.Select((id, n) => Identity($"row-{n:D4}", id, "internal", $"label-{n:D4}"));
        var aliases = Enumerable.Range(0, 300).Select(n => Identity($"alias-{n:D4}", requested[0], "zz-alias", $"alias label {n}"));
        var noise = Enumerable.Range(0, 5000).Select(n => Identity($"noise-row-{n:D5}", $"noise-participant-{n:D5}", "atproto", "not requested"));
        await fixture.Seed(ordinary.Concat(aliases).Concat(noise));
        // A case-different owner and a null-label non-atproto identity must not leak.
        await fixture.Seed([Identity("case-row", requested[1].ToUpperInvariant(), "internal", "wrong case"),
            Identity("not-a-label", requested[0], "connector-client", null)]);
        var source = new Handles();
        var directory = new ParticipantDirectory(TimeProvider.System, source);

        fixture.Trace.Begin();
        var labels = await directory.LabelsFor(requested.Concat([requested[0], null!]), fixture.Ct, false);
        var statements = fixture.Trace.End();
        Assert.Equal(requested.Length, labels.Count);
        foreach (var (id, n) in requested.Select((id, n) => (id, n))) Assert.Equal($"label-{n:D4}", labels[id]);
        Assert.Empty(source.Calls);
        Assert.DoesNotContain(statements, statement => Regex.IsMatch(statement.Sql, @"\bCOUNT\s*\(", RegexOptions.IgnoreCase));
        var reads = statements.Where(IsIdentityRead).ToArray();
        foreach (var read in reads.Take(6)) output.WriteLine($"Rows={read.RowIds.Length}, FullScanSteps={read.FullScanSteps}: {read.Sql}");
        Assert.True(reads.Length >= 5, "The fixture must exercise multiple participant chunks and multiple identity pages.");
        Assert.Equal(430, reads.Sum(read => read.RowIds.Length));
        Assert.Equal(430, reads.SelectMany(read => read.RowIds).Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(reads.SelectMany(read => read.RowIds), id => id.StartsWith("noise-", StringComparison.Ordinal) || id is "case-row" or "not-a-label");
        Assert.Contains(reads, read => read.Sql.Contains(" > ", StringComparison.Ordinal));
        var indexes = await fixture.Indexes();
        var index = Assert.Single(indexes.Where(row => row.Sql.Contains("participantId", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains("Id", index.Sql, StringComparison.OrdinalIgnoreCase);
        foreach (var read in reads)
        {
            Assert.Contains("participantId", read.Sql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(" IN (", read.Sql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("LIMIT 128 OFFSET 0", read.Sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("noise-participant", read.Sql, StringComparison.Ordinal);
            Assert.InRange(read.RowIds.Length, 0, ParticipantDirectory.LabelIdentityPageSize);
            Assert.Equal(0, read.FullScanSteps);
            var plan = await fixture.Explain(read.Sql);
            Assert.Contains(plan, detail => detail.Contains("SEARCH", StringComparison.OrdinalIgnoreCase)
                && detail.Contains(index.Name, StringComparison.Ordinal));
        }
        Assert.Empty(fixture.Trace.Errors);
    }

    [Fact]
    public async Task Label_precedence_and_ordinal_ties_preserve_spelling_case_and_blank_behavior()
    {
        using var fixture = new SqliteFixture();
        await fixture.Seed([
            Identity("Z-atproto", "preferred", "atproto", "stored.EXAMPLE"),
            Identity("a-atproto", "preferred", "atproto", "later.example"),
            Identity("fallback", "preferred", "aaa", "must not win"),
            Identity("z-fallback", "fallback-only", "β-kind", "later"),
            Identity("a-fallback", "fallback-only", "Z-kind", "ÉCOLE 日本語"),
            Identity("b-fallback", "fallback-only", "Z-kind", "same-kind later"),
            Identity("a-null", "null-first", "atproto", null),
            Identity("b-labeled", "null-first", "atproto", "second.example"),
            Identity("a-other", "null-first", "aaa", "ordinal fallback"),
            Identity("a-empty", "empty", "atproto", ""),
            Identity("b-other", "empty", "internal", "not used with empty"),
            Identity("a-blank", "blank", "atproto", " \t"),
            Identity("b-blank", "blank", "internal", "not used with whitespace"),
            Identity("nolabel", "none", "internal", null)
        ]);
        var source = new Handles();
        var directory = new ParticipantDirectory(TimeProvider.System, source);
        var labels = await directory.LabelsFor(["preferred", "fallback-only", "null-first", "empty", "blank", "none", "missing"], fixture.Ct, false);
        Assert.Equal("stored.EXAMPLE", labels["preferred"]);
        Assert.Equal("ÉCOLE 日本語", labels["fallback-only"]);
        Assert.Equal("ordinal fallback", labels["null-first"]);
        Assert.Equal(" \t", labels["blank"]);
        Assert.False(labels.ContainsKey("empty"));
        Assert.False(labels.ContainsKey("none"));
        Assert.False(labels.ContainsKey("missing"));
        Assert.Empty(source.Calls);
    }

    [Fact]
    public async Task Missing_handle_resolution_is_six_wide_and_only_for_requested_first_atproto_identities()
    {
        using var fixture = new SqliteFixture();
        var ids = Enumerable.Range(0, 80).Select(n => $"person-{n:D3}").ToArray();
        await fixture.Seed(ids.SelectMany((id, n) => new[] {
            Identity($"a-{n:D3}", id, "atproto", null), Identity($"b-{n:D3}", id, "atproto", "later.example") }));
        await fixture.Seed([Identity("unrequested", "private-other", "atproto", null)]);
        var firstGroup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var begun = 0;
        var source = new Handles(async (did, ct) =>
        {
            if (Interlocked.Increment(ref begun) == ParticipantDirectory.LabelResolveConcurrency) firstGroup.SetResult();
            // A serial implementation cannot make this first group ready. Every later
            // group proceeds normally; the fixture cancellation bounds a broken barrier.
            await firstGroup.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            return "resolved:" + did;
        });
        var labels = await new ParticipantDirectory(TimeProvider.System, source).LabelsFor(ids, fixture.Ct);
        Assert.Equal(ids.Length, labels.Count);
        Assert.Equal(ids.Length, source.Calls.Count);
        Assert.Equal(ParticipantDirectory.LabelResolveConcurrency, source.PeakConcurrent);
        Assert.All(source.Calls, did => Assert.StartsWith("a-", did));
        foreach (var (id, n) in ids.Select((id, n) => (id, n))) Assert.Equal($"resolved:a-{n:D3}", labels[id]);
    }

    [Fact]
    public async Task Winner_beyond_the_first_identity_page_is_not_truncated()
    {
        using var fixture = new SqliteFixture();
        await fixture.Seed(Enumerable.Range(0, 300).Select(n => Identity($"early-{n:D3}", "many", "zz-alias", "fallback"))
            .Append(Identity("last", "many", "atproto", "late-priority.example")));
        var labels = await new ParticipantDirectory(TimeProvider.System, new Handles()).LabelsFor(["many"], fixture.Ct, false);
        Assert.Equal("late-priority.example", Assert.Single(labels).Value);
    }

    [Fact]
    public async Task Cancellation_during_resolution_stops_before_the_next_six_wide_group()
    {
        using var fixture = new SqliteFixture();
        var ids = Enumerable.Range(0, 8).Select(n => $"cancel-person-{n}").ToArray();
        await fixture.Seed(ids.Select(id => Identity(id, id, "atproto", null)));
        using var canceled = CancellationTokenSource.CreateLinkedTokenSource(fixture.Ct);
        var begun = 0;
        var source = new Handles(async (_, ct) =>
        {
            if (Interlocked.Increment(ref begun) == ParticipantDirectory.LabelResolveConcurrency) canceled.Cancel();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return null;
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ParticipantDirectory(TimeProvider.System, source).LabelsFor(ids, canceled.Token));
        Assert.Equal(ParticipantDirectory.LabelResolveConcurrency, source.Calls.Count);
    }

    [Fact]
    public async Task Identity_limit_throws_instead_of_returning_a_partial_map()
    {
        using var fixture = new SqliteFixture();
        await fixture.Seed(Enumerable.Range(0, ParticipantDirectory.LabelIdentityLimit + 1)
            .Select(n => Identity($"overflow-{n:D6}", "too-many", "internal", "bounded")));
        var source = new Handles();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ParticipantDirectory(TimeProvider.System, source).LabelsFor(["too-many"], fixture.Ct, false));
        Assert.Contains(ParticipantDirectory.LabelIdentityLimit.ToString(), error.Message, StringComparison.Ordinal);
        Assert.Empty(source.Calls);
    }

    [Fact]
    public async Task Cancellation_stops_before_query_and_during_input_enumeration()
    {
        using var fixture = new SqliteFixture();
        using var canceled = new CancellationTokenSource();
        var source = new Handles();
        var directory = new ParticipantDirectory(TimeProvider.System, source);
        IEnumerable<string> CancelDuringEnumeration()
        {
            yield return "first";
            canceled.Cancel();
            yield return "second";
        }
        fixture.Trace.Begin();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => directory.LabelsFor(CancelDuringEnumeration(), canceled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => directory.LabelsFor([], canceled.Token));
        Assert.Empty(fixture.Trace.End());
        Assert.Empty(source.Calls);
    }

    private static ParticipantIdentity Identity(string id, string owner, string kind, string? label)
        => new() { Id = id, ParticipantId = owner, Kind = kind, Value = id, Label = label,
            AddedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) };

    private static bool IsIdentityRead(SqliteTrace.Statement statement)
        => statement.Sql.StartsWith("SELECT ", StringComparison.OrdinalIgnoreCase)
            && statement.Sql.Contains("ParticipantIdentity\"", StringComparison.Ordinal)
            && statement.Sql.Contains(" LIMIT ", StringComparison.OrdinalIgnoreCase);

    private sealed class Handles(Func<string, CancellationToken, Task<string?>>? resolve = null) : IAtprotoHandleSource
    {
        public List<string> Calls { get; } = [];
        public int PeakConcurrent { get; private set; }
        private int active;
        private readonly object gate = new();
        public async Task<string?> HandleOf(string did, CancellationToken ct)
        {
            lock (gate)
            {
                Calls.Add(did);
                PeakConcurrent = Math.Max(PeakConcurrent, ++active);
            }
            try
            {
                await Task.Yield(); // Exposes fan-out even if the fake answer is synchronous.
                return resolve is null ? null : await resolve(did, ct);
            }
            finally { lock (gate) active--; }
        }
    }

    private sealed class SqliteFixture : IDisposable
    {
        private readonly IHost host;
        private readonly string root = Path.Combine(Path.GetTempPath(), "TangentSpace-ParticipantLabels", Guid.NewGuid().ToString("N"));
        private readonly string connectionString;
        private readonly CancellationTokenSource timeout = new(TimeSpan.FromMinutes(2));
        public CancellationToken Ct => timeout.Token;
        public SqliteTrace Trace { get; }

        public SqliteFixture()
        {
            Directory.CreateDirectory(root);
            var database = Path.Combine(root, "labels.sqlite");
            connectionString = new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString();
            TestHooks.ResetDataConfigs();
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = root, EnvironmentName = "Testing", Args = [] });
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> {
                ["Koan:Data:Sources:Default:Adapter"] = "sqlite",
                ["Koan:Data:Sources:Default:ConnectionString"] = connectionString,
                ["Koan:Data:Sqlite:ConnectionString"] = connectionString,
                ["Koan:Identity:Posture"] = "Closed", ["Koan:Identity:SeedDevUsers"] = "false"
            });
            builder.Logging.ClearProviders();
            builder.Services.AddKoan();
            host = builder.Build(); // Never StartAsync: no workers or network listeners.
            AppHost.Current = host.Services;
            SQLitePCL.Batteries_V2.Init();
            Trace = new SqliteTrace(database);
        }

        public async Task Seed(IEnumerable<ParticipantIdentity> identities)
        {
            using var fresh = EntityContext.NoCache();
            foreach (var chunk in identities.Chunk(512))
            {
                var batch = ParticipantIdentity.Batch();
                foreach (var identity in chunk) batch.Add(identity);
                await batch.Save(new BatchOptions(RequireAtomic: true, MaxItems: 512), Ct);
            }
        }

        public async Task<List<(string Name, string Sql)>> Indexes()
        {
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(Ct);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT name, sql FROM sqlite_master WHERE type='index' AND sql IS NOT NULL";
            await using var reader = await command.ExecuteReaderAsync(Ct);
            var rows = new List<(string, string)>();
            while (await reader.ReadAsync(Ct)) rows.Add((reader.GetString(0), reader.GetString(1)));
            return rows;
        }

        public async Task<List<string>> Explain(string sql)
        {
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(Ct);
            await using var command = connection.CreateCommand();
            command.CommandText = "EXPLAIN QUERY PLAN " + sql;
            await using var reader = await command.ExecuteReaderAsync(Ct);
            var details = new List<string>();
            while (await reader.ReadAsync(Ct)) details.Add(reader.GetString(3));
            return details;
        }

        public void Dispose()
        {
            if (ReferenceEquals(AppHost.Current, host.Services)) AppHost.Current = null;
            host.Dispose();
            Trace.Dispose();
            TestHooks.ResetDataConfigs();
            timeout.Dispose();
            // Only this fixture's freshly minted GUID directory; never a supplied database.
            Directory.Delete(root, recursive: true);
        }
    }

    // Observe provider SQL, returned Ids and SQLite full-scan counters, not LINQ intent.
    // Auto-extension catches provider-created connections without patching Koan. It is
    // installed only during the nonparallel ambient-host collection, for one exact file.
    private sealed class SqliteTrace : IDisposable
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Extension(nint db, nint error, nint api);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int TraceCallback(uint kind, nint context, nint statement, nint value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int AutoExtension(nint callback);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int TraceV2(nint db, uint mask, TraceCallback callback, nint context);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint ExpandedSql(nint statement);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Free(nint value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int StmtStatus(nint statement, int operation, int reset);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint DbFilename(nint db, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ColumnCount(nint statement);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint ColumnText(nint statement, int column);
        private readonly nint library;
        private readonly Extension extension;
        private readonly TraceCallback callback;
        private readonly AutoExtension cancel;
        private readonly List<Statement> statements = [];
        private readonly Dictionary<nint, List<string>> rowIds = [];
        private bool capturing;
        public List<string> Errors { get; } = [];

        public SqliteTrace(string database)
        {
            var rid = RuntimeInformation.RuntimeIdentifier;
            var filename = OperatingSystem.IsWindows() ? "e_sqlite3.dll" : OperatingSystem.IsMacOS() ? "libe_sqlite3.dylib" : "libe_sqlite3.so";
            var path = Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native", filename);
            library = File.Exists(path) ? NativeLibrary.Load(path)
                : NativeLibrary.Load("e_sqlite3", typeof(SQLitePCL.raw).Assembly, DllImportSearchPath.SafeDirectories);
            T Function<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
            var trace = Function<TraceV2>("sqlite3_trace_v2");
            var expanded = Function<ExpandedSql>("sqlite3_expanded_sql");
            var free = Function<Free>("sqlite3_free");
            var status = Function<StmtStatus>("sqlite3_stmt_status");
            var dbFilename = Function<DbFilename>("sqlite3_db_filename");
            var columnCount = Function<ColumnCount>("sqlite3_column_count");
            var columnName = Function<ColumnText>("sqlite3_column_name");
            var columnText = Function<ColumnText>("sqlite3_column_text");
            cancel = Function<AutoExtension>("sqlite3_cancel_auto_extension");
            callback = (kind, _, statement, _) =>
            {
                try
                {
                    if (!capturing) return 0;
                    if (kind == 4) // SQLITE_TRACE_ROW
                    {
                        for (var i = 0; i < columnCount(statement); i++)
                            if (string.Equals(Marshal.PtrToStringUTF8(columnName(statement, i)), "Id", StringComparison.OrdinalIgnoreCase))
                            {
                                if (!rowIds.TryGetValue(statement, out var ids)) rowIds[statement] = ids = [];
                                ids.Add(Marshal.PtrToStringUTF8(columnText(statement, i)) ?? "");
                                break;
                            }
                    }
                    if (kind == 2) // SQLITE_TRACE_PROFILE: includes early-disposed pages.
                    {
                        var sql = expanded(statement);
                        try
                        {
                            statements.Add(new(Marshal.PtrToStringUTF8(sql) ?? "", status(statement, 1, 0),
                                rowIds.Remove(statement, out var ids) ? ids.ToArray() : []));
                        }
                        finally { free(sql); }
                    }
                }
                catch (Exception error) { Errors.Add(error.ToString()); }
                return 0;
            };
            extension = (db, _, _) =>
            {
                var name = Marshal.PtrToStringUTF8(dbFilename(db, "main"));
                if (name is null || !string.Equals(Path.GetFullPath(name), database, StringComparison.OrdinalIgnoreCase)) return 1;
                return trace(db, 2 | 4, callback, 0);
            };
            if (Function<AutoExtension>("sqlite3_auto_extension")(Marshal.GetFunctionPointerForDelegate(extension)) != 0)
                throw new InvalidOperationException("Could not attach SQLite query trace.");
        }

        public void Begin() { statements.Clear(); rowIds.Clear(); capturing = true; }
        public Statement[] End() { capturing = false; return statements.ToArray(); }
        public void Dispose()
        {
            cancel(Marshal.GetFunctionPointerForDelegate(extension));
            GC.KeepAlive(callback); GC.KeepAlive(extension);
            NativeLibrary.Free(library);
        }
        public sealed record Statement(string Sql, int FullScanSteps, string[] RowIds);
    }
}
