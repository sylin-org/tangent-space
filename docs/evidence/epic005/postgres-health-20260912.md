# PostgreSQL adapter health — 12 September 2026

The actual Tangent `Message` entity passed create/read/update/delete, both keyset sort
directions, full 21-row identity/scope/order validation, count-free first-page streaming,
explicit exact counts, and native same-entity atomic-batch rollback through the pinned
Koan PostgreSQL provider. This qualifies the tested adapter seams. It does not establish
Tangent's cross-entity acceptance atomicity or admit PostgreSQL as a production provider.

[Reproducible probe](../../../probes/PostgresProbe/README.md) and
[compact machine-readable evidence](postgres-health-20260912.json). The complete result
remains in ignored experiment `postgres-health-4546f279e5e641e6a8acf5cc910fb2a1`.

## Scope and isolation

The successful run lasted 24.65 seconds at 14:10:32–14:10:56 America/New_York. It used
PostgreSQL 17.11, pinned container image
`postgres:17.11-bookworm@sha256:051f7b7b3abdd564d5d1bd1e8c4b9c1b6e77087d1dd22020ede611c096a272e0`,
on `127.0.0.1:25432`, with the dedicated public synthetic laboratory credentials. All
writes targeted fresh schema `epic005_pg_ec0e319a19454730992e0744a6ac8f24`; no live Tangent
database, application host, source/profile worker, user session or external identity ran.

The runner verified the exact lab network/container/volume identities, loopback bindings,
resource limits and machine headroom before mutation and afterward. The PostgreSQL
container was capped at one CPU and 2 GiB memory; the client used two logical processors
and peaked at 145.67 MiB working set, below its monitored 1.5 GiB limit. At completion,
combined PostgreSQL/MongoDB lab data was 457.55 MiB and free host RAM was about 33 GiB.
No schema, volume or directory was deleted.

Checkout HEAD was `e07a84cc3f71a0867f1122b03b723cc80727e772`. Exact relevant source-file
hashes are recorded and checked unchanged during the run; this is not a claim that all
checkout files were pristine. The probe changed no application or framework source.
The actual selected native adapter was `NpgsqlRepository`, behind `RepositoryFacade`.

PostgreSQL reported `fsync=on`, `synchronous_commit=on`, `full_page_writes=on`,
`shared_buffers=128MB`, `work_mem=4MB`, and `max_parallel_workers_per_gather=2`. The CPU
quota still constrained aggregate execution even where the planner launched workers.

## Dataset and query results

One `Message.Save` provisioned the actual `Id` plus `Json` JSONB storage shape. A small
entity-facade CRUD check then passed. Native binary COPY expanded the real stored template
to 10,000 and 100,000 hot-Topic rows, plus two distractor Topics each with 10% as many
posts and overlapping Sequence ranges. Every returned row was checked against its exact
expected ID, room, sequence and order; a wrong-room row fails the self-test/validator.
Explicit exact counts were 12,000 and 120,000.

Seeding bypassed domain acceptance/lifecycle and used fixed template timestamps with
varied short/long Unicode text, synthetic source references and tombstones. Its elapsed
time is fixture construction, not application write throughput. These fixtures are useful
for adapter isolation/order checks, not a full permissions, source protocol or concurrency
test.

Each row below reports the median of six warm serial instrumented calls, after one
discarded initial call. Connection pooling was disabled; no OS cache was flushed. The
reported p95 in JSON equals the maximum of six samples and is not a load-test percentile.

| Hot rows / phase | Beginning Query / stream | After-middle Query / stream | After-tail Query / stream | Before-middle Query / stream |
| --- | --- | --- | --- | --- |
| 10,000, original indexes | 39.44 / 35.45 ms | 31.59 / 30.06 ms | 26.90 / 16.77 ms | 32.93 / 26.54 ms |
| 100,000, original indexes | 298.49 / 223.93 ms | 195.74 / 113.61 ms | 167.61 / 66.29 ms | 191.21 / 180.72 ms |
| 100,000, isolated expression index | 88.24 / 10.58 ms | 79.66 / 13.47 ms | 14.07 / 11.11 ms | 82.32 / 9.08 ms |

“Query” is materialized `Message.Query`; “stream” is only the first 21 rows of
`Message.QueryStream`, with immediate iterator disposal. The emitted SQL used the same
room/range predicates, `LIMIT 21 OFFSET 0`, and Sequence plus Id tie-breaker. This does
not test or recommend QueryStream's numbered-page continuation under concurrent mutation.

Actual Npgsql command logging captured **one extra exact COUNT statement per materialized
window**, and **zero counts in every first-stream-page call**. An explicit
`Message.Count.Exact` returned the correct total; it is a deliberately requested aggregate,
not a free property of a page. The provider supports a count-free query path already, but
the ordinary materialized facade currently adds counting by default.

## Actual query plans

The original schema had only its primary-key index on `Id`. Original-index plans scanned
the full 12,000/120,000-row table and sorted eligible rows to return 21. The 100,000-row
cases included parallel sequential scans and gather/sort nodes; a limited result did not
mean bounded database work.

The isolated second phase added one nonunique expression index matching the generated
managed JSON expressions: RoomKey text, Sequence cast to bigint, then Id. No index or
mapping change was applied to Tangent or Koan. Representative captured SQL was replayed
with its known synthetic parameters under `EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)`;
the probe checked the replay's actual result shape before publishing its plan.

With that index, ascending content windows used a forward index scan returning exactly
21 rows, with 5–6 shared-buffer hits in the sampled plans. The older window ordered
`Sequence DESC, Id ASC`: PostgreSQL used a backward index scan reading 22 rows plus an
incremental sort, because reversing the all-ASC index also reverses Id. The report retains
this sort instead of claiming both directions became sort-free.

The extra COUNT remained substantial: beginning and middle predicates still chose
sequential-scan aggregate plans across the table, while the tiny tail predicate used the
index. This explains why removing the count and adding the correct index were separate
improvements in this experiment. It is not evidence of a PostgreSQL-versus-MongoDB or
PostgreSQL-versus-SQLite winner: payload details, drivers, instrumentation, resource
allocations and measurement order differ from earlier runs.

## Atomicity qualification and remaining limitation

| Seam actually tested | Observed result |
| --- | --- |
| Two-row `Message.Batch().Save(RequireAtomic: true)` | `Atomic` receipt; both rows visible from an independent connection |
| Three-row same-entity atomic batch, database trigger rejects second write | Expected PostgreSQL SQLSTATE `P0001` and exact injected marker; zero rows durable |
| Three `Message.Save` calls in named `EntityContext.Transaction`, second write rejected | Same expected injected failure; only the first row durable; coordinator reports unknown/partial outcome and does not replay |

The trigger cases reject unrelated exceptions rather than interpreting a connectivity or
schema failure as successful rollback. The persisted first-only deferred result is also
asserted. The failing second write was real PostgreSQL execution, not a thrown client-side
stub. These observations preserve the distinction between a native transaction and Koan's
deferred coordination. No cross-entity Message/source decision/sequence/journal/receipt
atomicity, restart recovery or concurrent writer correctness was proved here.

There is a capability-observability mismatch: `ExecutionCapabilities` on the public
`Message.Batch()` facade reads `None`, although the underlying Npgsql batch declares
`Atomic`, the adapter capability describes AtomicBatch, and `RequireAtomic` returned an
Atomic receipt and passed rollback. The JSON labels the public facade explicitly. This
is an API capability-reporting question for Koan, not an observed native atomic execution
failure.

An earlier invocation stopped at a harness assertion that incorrectly expected the
outer repository to be Npgsql rather than `RepositoryFacade`. It was corrected to use
Koan's public native-adapter diagnostic. Its `failure.json` and fresh empty schema remain
under experiment `postgres-health-172ffa91ea104152a497e7880215c973`, with a
[separate harness-failure artifact](postgres-harness-correction-20260912.json); it is not counted as
an adapter failure or successful measurement. The final build and validation self-test
passed, and the entire successful measurement/fault run exited zero.

The independent red-team worker also ran the final built probe in a fresh schema and
reported a zero exit under experiment `postgres-health-3331faf07309452c94897c03bd975a9c`.
That rerun reproduced the probe assertions; it does not enlarge their scope. The
coordinator subsequently stopped the isolated provider laboratory.

Full API authorization/enrichment, event replay, acceptance/recovery, million-post scale,
multi-instance behavior, fair provider saturation, and browser viewport/heap behavior
remain separate EPIC-005 gates.
