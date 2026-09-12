# EPIC-005 SQLite baseline probe

This Windows x64 console probe references the actual TangentSpace application and the
pinned Koan source. It measures the current `Message.Query` path and inspects SQLite's
executed SQL, query plans, full-scan steps, sort operations and virtual-machine steps.
It does not start the application host, HTTP listeners, hosted services, profile capture,
source reconciliation or authentication. It loads no user credentials.

Run from PowerShell with the repository's .NET SDK and prepared Koan checkout:

```powershell
./probes/ScaleProbe/run.ps1
```

The runner requires at least 8 GiB free host RAM. The probe creates a new absolute
`.local/experiments/epic005/sqlite-baseline-<GUID>` directory, refuses existing targets
and reparse-point ancestors, and never deletes anything. The process is restricted to
four logical processors; its load phase has a ten-minute deadline, a monitored 1.5 GiB
working-set ceiling and a monitored 10 GiB generated-data ceiling. These monitored
limits are checked between seed batches/queries, not a hard OS memory quota. Build
parallelism is four; build memory is not included in the probe's working-set figures.

The only dataset is deterministic synthetic `Message` records in one hot Topic, with
32 synthetic author labels, short/long text, Unicode, reply references, facets, edits
and tombstones. One real `Message.Save` provisions Koan's schema. Prepared direct
SQLite inserts then expand the real serialized row shape to 10,000 and 100,000 rows.
This seed deliberately bypasses domain acceptance; its elapsed time is not application
write throughput. Synthetic URI/CID strings are fixture labels, not protocol evidence.

At each tier, beginning/after-middle/after-tail/before-middle sequence windows return
21 rows through the real `Message.Query` facade and SQLite adapter. This is the same
keyset predicate/order style as History/McpWindow, not a call to either full service.
Every sequence, identity, room and order is validated. No-cache entity scopes avoid
measuring only an ambient entity cache. SQLite connection pooling is disabled in this
probe, and OS/filesystem caches are not flushed. There is one discarded first query
and six warm serial samples for each case. The native SQLite profiler remains enabled
during timing, so timings are traced/instrumented and directional, not saturation or
production API latency claims. Nearest-rank percentiles are used; with six samples,
reported p95 equals the maximum and is not a meaningful tail-latency target.

Native tracing uses the bundled SQLite C API and only this process. Each measured
query's representative SQL and plan are included in `result.json`; callback errors
are recorded. No index or query/count correction is applied to this baseline.

After read measurements and schema capture, a separate failure probe queues three
actual `Message.Save` calls under `EntityContext.Transaction` using the application's
transaction name. A SQLite trigger rejects the second write. An independent connection
then reports durable rows after the scope exits. This tests deferred coordination,
not full domain acceptance, a multi-type acceptance transaction, restart or crash recovery.

Useful standalone check:

```powershell
dotnet ./probes/ScaleProbe/bin/Release/net10.0/ScaleProbe.dll --self-test
```

Self-checks cover percentile convention, both ordered directions and rejection of a
corrupt final row. The runner executes them before the benchmark. Generated databases
and reports remain ignored; publish only the small sanitized report as review evidence.

Current evidence: [12 September baseline](../../docs/evidence/epic005/sqlite-baseline-20260912.md).
