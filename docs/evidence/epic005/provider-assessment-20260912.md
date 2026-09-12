# EPIC-005 provider assessment — 12 September 2026

## Outcome

MongoDB and PostgreSQL both pass actual Tangent Entity CRUD and bounded, ordered read
qualification at 10,000 and 100,000 posts in a hot Topic, with two additional Topics
sharing sequence ranges. They are now exercised through executable isolated probes,
not merely listed as available adapters. Neither is admitted for complete Tangent writes
by these tests. No live provider switch, user-data reset or framework-pin update occurred.

| Tested behavior | MongoDB 8.3.4, single-member rs0 | PostgreSQL 17.11 |
| --- | --- | --- |
| Actual Entity CRUD and 21-post keyset query results | Pass | Pass |
| Ordinary materialized list query on pinned Koan | Find plus unwanted exact count | SELECT plus unwanted exact count |
| First 21 rows of QueryStream, then dispose | One find, no count | One SELECT, no count |
| Matching isolated compound index | Forward read examines 21 documents/21 keys | Forward plan uses limited index scan, 21 rows |
| Same-Entity RequireAtomic | Correctly rejects before callback or database work | Atomic success and injected-failure rollback verified |
| Named ambient EntityContext.Transaction | Message → ActivityHead → Message fails second write; first stays durable | Three Message saves fail second write; first stays durable |
| Complete domain acceptance/restart recovery | Not proved | Not proved |

The ambient experiments are deliberately different scopes and are labeled accordingly;
the Mongo run additionally crosses Entity roots. Neither invokes a native cross-entity
transaction. PostgreSQL's stronger same-Entity batch capability does not repair Tangent's
current use of the ambient coordinator.

## What the measurements support

At 100,000 hot-Topic posts plus 20,000 distractors, Mongo's unindexed forward stream
examined 120,002 documents (including two test sentinels). With the isolated ascending
`roomKey, sequence, _id` index, it examined 21 documents and 21 keys. The ordinary list
query still counts all matching keys; avoiding hydration alone does not bound count work.
Its reverse `sequence DESC, _id ASC` query still examined 49,999 keys and sorted under
the all-ascending index. Index direction remains an application design issue.

PostgreSQL's indexed, count-free 21-row windows had instrumented warm p50 of 9.08–13.47 ms
in the worker run. The corresponding materialized path was 14.07–88.24 ms, with an exact
count still present. Actual emitted SQL, parameterized EXPLAIN, original indexes and
native durability settings are retained in its evidence. Six warm samples per case,
no cold-cache reset, different pooling/profiling paths and different prior SQLite resource
limits **do not establish a cross-engine winner, full-API latency, or saturation limit**.

The useful result is causal: scoped predicates, appropriate indexes and genuinely
count-free limited reads matter on both providers. A fast response or 21 returned rows
alone is insufficient evidence of bounded database work.

## Framework ownership and identified failures

- **K01, confirmed high:** public atomicity documentation contradicts the implemented
  deferred transaction contract. Koan agent owns correction and regression/documentation
  consistency; actual deferred partial persistence is documented behavior, not a new
  runtime contract violation.
- **K02, confirmed medium:** item-only Query manufactures count intent and discards the
  total. Reproduced on SQLite, Mongo and PostgreSQL. Koan agent owns the facade repair and
  count-free regressions; explicit count APIs must retain their meaning.
- **Q03, subsequently confirmed public-surface defect:** PostgreSQL's public `Message.Batch().ExecutionCapabilities`
  reports `None` even though repository AtomicBatch is advertised and RequireAtomic
  produces an Atomic receipt with proved rollback. The native batch itself advertises
  Atomic. Koan confirmed the facade/default discrepancy and implemented effective native
  capability forwarding with regressions. This is not evidence of failed native atomicity.
- **Documented admission limits:** no supported Entity-level cross-root native transaction
  in the inspected framework; Mongo atomic batches intentionally unsupported. A replica
  set alone does not add a Koan guarantee.
- **Tangent-owned:** missing index declarations/direction coverage, unbounded enrichment and
  browser collections, directory traversal gaps, incomplete SSE fallback and process-local
  coordination remain tracked in EPIC-005. These were not silently assigned to Koan.

Leo authorized scoped implementation work to the existing **Report framework status** task.
K01/K02 fixes and regressions were assigned, with cross-entity capability design guidance;
Mongo/PG evidence and Q03 were delivered. Koan returned implementation closeout for all
three issues: 561/561 owner tests, 2/2 SQLite tests and 25/25 focused tests reported passing
(overlapping suites, not additive unique totals). The coordinator inspected the work card
and changed source; these test results are attributed to Koan's agent. Changes remain
uncommitted/unpublished in Koan's main working tree. Tangent's pin and measured baseline
are unchanged; consumer adoption/revalidation remain pending. See the
[failure handoff](../../handoff/KOAN_FAILURES_2026-09-12.md).

## Next implementation gate

Keep Mongo as an active read-path/test candidate, not a falsely qualified complete backend.
Resolve Tangent's correlated message/source-decision/sequence/activity/receipt commit or
durable recovery protocol, with the Koan agent owning any new framework primitive. Then
apply count-free bounded queries and declared indexes, prove full-API enrichment and
authorization bounds, and run domain failure/restart tests before provider selection.
Only afterward does matched saturation/browser measurement become a meaningful gate.

## Reproduction and independent review

- [Provider lab configuration and safety checks](../../../probes/ProviderLab/README.md)
- [Mongo worker report](mongo-baseline-20260912.md), including separate initial harness failure
- [PostgreSQL worker report](postgres-health-20260912.md) and
  [raw evidence](postgres-health-20260912.json); original root
  `postgres-health-4546f279e5e641e6a8acf5cc910fb2a1` under `.local/experiments/epic005`.
- [Independent provider red team](provider-red-team.md): source/evidence review and rerun
  status are recorded there, not inferred from worker passes.

Independent runs completed successfully: Mongo 10k root
`mongo-baseline-7b9d5abed0574b2f9bc853027be032b8` and PostgreSQL 10k/100k root
`postgres-health-3331faf07309452c94897c03bd975a9c`. Nine independent mocked safety-guard
tests also pass. The reviewer repaired/rechecked profiler retention and failure-evidence
assertions before accepting the results; initial harness failures remain separately recorded.

Coordinator's final combined Node run: **72/72 passed** across the bounded-window,
independent window adversarial, room recovery, agent connection and new provider-guard
suites. This is a focused selection; the separately recorded existing WebMCP fixture
failures were not repaired or reclassified as passing by this provider work.

Final real safety check: combined database-volume state **652.59 MiB**, host free RAM
**34,038 MiB**. Both lab containers then stopped cleanly with exit 0; all synthetic
namespaces, reports and volumes are retained. Existing Tangent remains healthy on port
5220, untouched. The provider lab is not left consuming runtime resources.

Only synthetic GUID namespaces were created. Server limits are 1 CPU/2 GiB each; one
two-CPU, monitored-1.5-GiB client runs at a time. Container ports are loopback-only on a
dedicated bridge; outbound traffic is not firewalled. No application host/worker is started
by a probe. Native fixture seed time is not domain authoring throughput. Full-API, source
publication, authentication, browser heap, million-post and multi-process tests remain open.
