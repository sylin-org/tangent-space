# EPIC-005 — current SQLite query baseline, 12 September 2026

Status: **measured read micro-query baseline and deferred-scope failure reproduction**.
This is one part of P1, not an API, browser, provider-saturation or release gate pass.

Reproducer: [ScaleProbe](../../../probes/ScaleProbe/README.md).
Raw sanitized evidence: [JSON result](sqlite-baseline-20260912.json).

## Observed outcome

Every 21-row window executes a limited SELECT **and an exact COUNT**. At 10,000 rows,
both statements report 9,999 full-scan steps; at 100,000 rows both report 99,999. Every
SELECT plan reports `SCAN koan_row` and `USE TEMP B-TREE FOR ORDER BY`; every COUNT
plan reports `SCAN koan_row`. The provisioned Message table has only its Id primary-key
index. The result limit is enforced, but database work grows with the dataset.

| Window | 10k traced p50 | 100k traced p50 | 100k maximum |
| --- | ---: | ---: | ---: |
| Beginning | 40.25 ms | 374.50 ms | 386.32 ms |
| After middle | 40.69 ms | 337.89 ms | 348.27 ms |
| After tail | 37.44 ms | 305.44 ms | 331.78 ms |
| Before middle | 48.22 ms | 423.24 ms | 469.61 ms |

These are six serial warm samples after one discarded query, with native tracing enabled.
The nearest-rank convention yields the third sorted value for p50; with six samples p95
equals max. These are directional instrumented measurements, not a saturation test or
an established latency target. No cold-disk claim is made. SQLite pooling was disabled
for the probe; concurrent benchmark/test work in this task was paused for the final run.

The separate fault probe queued three `Message.Save` operations under the application's
`tangent-room-policy-operation` deferred transaction name. A SQL trigger rejected the
second operation. Koan threw a `TransactionException` reporting one completed operation;
an independently opened connection found `epic005-tx-first` durable, with the second and
third absent. This reproduces a partial commit of the deferred scope. It does not yet
exercise complete source/message/journal acceptance or crash/restart behavior.

## Run identity and limits

- UTC run: 2026-09-12 17:36:55 to 17:37:09; measured load phase 14.42 seconds.
- CPU: Intel Core i7-12700KF, 12 physical / 20 logical processors; probe affinity mask
  15 allowed four logical processors. Host has about 63.8 GiB RAM, about 35 GiB free.
- Windows build 26200, .NET SDK 10.0.401, runtime 10.0.12, bundled SQLite 3.53.3.
- Application base commit `5c98bd4cf5a1bdd50d10ed8aef0a980cd6f48994`; dirty source was
  preserved. Koan checkout base `e07a84cc3f71a0867f1122b03b723cc80727e772`; raw report
  records SHA-256 values of the relevant actual application/framework source files.
- Deterministic seed `epic005-sqlite-v1`: a single hot Topic; 10k then 100k posts, with
  short/long supported text, Unicode, reply/facet fixtures, edit timestamps and tombstones.
- Current generated schema: `Id TEXT NOT NULL, Json TEXT, PRIMARY KEY (Id)`.
- SQLite: delete journal, synchronous 2 (FULL), 4 KiB pages, cache_size -2000, temp_store 0.
- 100k database size before the separate fault probe: 89,399,296 bytes (85.3 MiB).
  Peak process working set: 150,618,112 bytes (143.6 MiB). Process CPU: 15.33 seconds.
- The host was built but never started. No ports, copied credentials or external workers.
  Native trace reported no callback errors. No application/framework query or index tuning.
- Isolated retained root: `.local/experiments/epic005/sqlite-baseline-b8036f8930d94aa09d9f57b85a9f7e70`.
  A previous development run remains in another isolated directory; it is not the published
  baseline. No generated fixtures were deleted and live application state was untouched.

## Verification and next gate

Release harness build succeeded. Self-tests verify nearest-rank statistics, both sequence
directions and corrupt-window rejection. All measured results validate the full returned
sequence, unique identities, order and Topic scope. Native SQL plus EXPLAIN and full-scan
counters establish the measured work; wall time alone is not the conclusion.

Next work must establish count-free execution and matching indexes, then rerun an
equivalent workload. The deferred partial-commit finding needs the separate acceptance
atomicity gate before any claim of transaction-preserving scale. Million-post tests,
directories, actual API/governance work, concurrent writers, cancellation/restart, SSE,
browser bounds and PostgreSQL/MongoDB remain unmeasured by this probe.
