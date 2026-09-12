# EPIC-005 provider-lab independent red team

Date: 2026-09-12. Initial scope: the pinned Koan MongoDB/PostgreSQL adapter contracts and
the new isolated provider probes. No production-provider switch is approved by this report.
Application correctness, schema/query design, framework capabilities and native database
performance are separate questions. This adapter/query-probe review is complete for the
recorded builds and runs below. Production integrity and saturation gates remain open.

## Admission checklist sent to workers

### Isolation and resources

- Root owns Docker provisioning and schedules heavy runs serially. New provider databases,
  containers, volume/data roots and loopback ports must be explicitly identifiable. Do not
  rely on a service name or a familiar port as proof that an existing database is disposable.
- Clear inherited application configuration; use a concrete connection and explicit unique
  namespace/source. Mongo's automatic-discovery path can otherwise fall back to localhost,
  and its database is a separately resolved source setting. PostgreSQL must name the
  dedicated lab database and fresh schema/search path explicitly; a shared lab database
  with a verified new GUID schema is sufficient for this isolated read experiment.
- Inspect actual container identity, mounted roots, port binding and resource limits. Seed
  only the new namespace. No external profile/source/auth workers or real credentials.
- Seed/load tools must reject existing/unexpected targets; teardown remains separately
  validated. No benchmark should invent cleanup against other containers or broad roots.
- Record exact server/container image, adapter source/build, durability settings, indexes,
  limits and warm/cold treatment. A small serial probe is not desktop saturation proof.

### Query/window correctness

- Validate every returned identity, Topic scope and sequence, not only count/first row.
  Test ascending and descending edges, beginning/middle/tail, overlapping windows, and
  another Topic containing the same sequence range. Use equal sort keys where tie-breaking
  matters; numeric sequence comparison must not degrade to string comparison.
- Capture actual SQL or BSON commands, both selected rows and implicit counts. Explain
  the actual emitted filter/sort/limit and report examined rows/documents/keys and sorts.
  An index declaration is not evidence that the database used it.
- Preserve untuned baseline evidence before adding a compound index. The index must match
  emitted expressions/paths and stable tie-breaks; compare optimized and baseline separately.
  Never present an indexed Mongo run against unindexed PostgreSQL/SQLite as an engine winner.
- A count-free first `QueryStream` page may be useful query evidence. Both adapters document
  their streams as numbered pages, not resumable, mutation-safe keyset cursors or snapshots.
  To prove a Topic window, issue each fresh query with the stable sequence/identity edge and
  captured upper boundary; do not treat whole-stream iteration as the desired browser API.
- Counts and selected pages can observe different committed states. Test/document this
  instead of treating a concurrent count mismatch as proof of lost records.

### Mutation integrity and capability boundaries

- Mongo `RequireAtomic=true` must reject before callbacks, loads or writes, not merely
  throw sometime before returning. Queue a fresh sentinel, mutation of an existing row
  with a counted callback, and deletion of another sentinel. Verify callback count zero
  and all documents unchanged through an independent driver connection after rejection.
- Mongo's current atomic-batch rejection is a documented missing capability, not itself a
  framework defect. A replica set does not add a capability the adapter has not elected.
- PostgreSQL native transactions and same-entity atomic batches are not proof of Tangent's
  ambient `EntityContext.Transaction` behavior. Repeat the failing-second-write experiment
  and inspect durable state independently. Label native transaction, batch and ambient
  deferred coordination results separately.
- The previous SQLite partial-commit reproduction already disqualifies assumptions that
  the ambient coordinator is atomic. No provider may be admitted for complete Tangent
  writes until actual decision/message/head/journal/receipt atomicity or a specified durable
  recovery protocol passes domain-level failure and retry tests.
- Concurrent clients through one process do not establish multi-process sequence allocation,
  policy isolation or journal notification safety. Current application locks are local.

## Initial source evidence and ownership

| Evidence | Classification |
| --- | --- |
| Mongo `MongoRepository.cs:315-326` checks `RequireAtomic`/idempotency support before mutation loads; `Runtime/MongoBatch.cs:21` advertises no batch execution capability | Guard to prove; documented limitation, not a found bug |
| Mongo `MongoAdapterFactory.cs:65-91` resolves connection and database separately; README configuration describes automatic localhost discovery fallback | Isolation hazard if lab configuration is incomplete |
| Mongo README streaming boundary and PostgreSQL README/TECHNICAL provider-bounded streaming sections explicitly disclaim mutation-safe/resumable offset traversal | Application API-design responsibility; bounded pages alone are not resumable windows |
| Data Core list-only `Query` routes through `QueryWithCount`; its default count strategy can produce an exact native count | Confirmed framework performance pitfall; request a supported count-free filtered-window surface |
| Current Message model does not declare a matching compound query index | Application schema deficiency unless a correctly declared index is later shown to be ignored |
| `EntityContext.Transaction` public XML summary promises atomicity, while coordinator contract/implementation disclaims it | Previously confirmed Koan documentation defect, handed to root for the Koan agent; runtime capability limit remains distinct |

Source paths above are relative to `.local/upstream/koan-framework/src/Connectors/Data/`
for provider files and `.local/upstream/koan-framework/src/Koan.Data.Core/` for core files.
No framework files were edited by this review. Newly reproduced framework failures must
be sent through root to the Koan agent, with exact source hashes and a bounded reproducer.

## Execution/artifact review

MongoProbe and PostgresProbe source was independently reviewed while workers held serial
build/run slots. After both worker runs completed, root granted a separate reviewer slot;
both probes were independently rerun serially without rebuilding or changing their source.

### PR-01 — Medium: capped Mongo profiler offsets can silently lose commands

Initial `MongoProbe/Profiler.cs` captures a `system.profile` document count at `Begin`, then
skips that count at `End`. The profile collection is capped. If it wraps, retained count
can stay constant while the records shift; checking only a reduced count or a 1,000-record
bound does not detect every loss. A silently empty capture could incorrectly support a
count-free-query claim. This is a probe evidence defect, not a Mongo/Koan defect.

Requested repair: prove retention/capture identity, assert intact expected selected-row
and count commands for list queries, and require a selected-row command before claiming
the stream lacked a count. **Closed for this harness:** the worker replaced offsets with
a unique profiled start-comment marker. Its continued presence proves later capped records
were retained. Exact command assertions require one find plus one count aggregate for list
queries and one find alone for the tested first stream page. Reviewed code and all retained
markers/command shapes in the successful worker and independent rerun evidence agree.

### PR-02 — Medium: diagnostic failure can erase a failed-run report

Initial Mongo `finally` fetches profiler/database metadata using `CancellationToken.None`
before writing `result.json`. A dead/stalled database can hang beyond the intended deadline
or throw before local evidence is written. Initial PostgreSQL writes its report only on
complete success, similarly losing structured partial results on an adapter/index failure.

Requested repair: record original stage/failure, bound best-effort cleanup/metadata fetch,
catch those secondary failures, and write local structured evidence independently of
database availability. These are harness robustness defects, not provider defects.
**Source repair reviewed:** Mongo cleanup uses a separate eight-second cancellation budget,
records unavailable diagnostics, and writes local results after operational failures.
PostgreSQL writes a bounded, staged `failure.json` offline, preserving partial evidence.
An earlier PostgreSQL harness assertion failure was retained through this path. No induced
database outage, stalled socket or process-restart test was run by this reviewer, so this
disposition is not an end-to-end outage-recovery claim.

### PR-03 — Medium: fault assertions must identify the intended failure

Initial atomic/deferred probe catches accept any exception. A network/argument error is
not proof that a database validator rejected the intended second write or that an atomic
capability gate rejected before I/O. PostgreSQL's successful atomic batch before its
failing batch is useful, but the failure still needs its known SQLSTATE/marker. Mongo
must identify capability rejection and its expected validator failure separately.

Requested repair: assert the specific injected/capability failure and exact independently
observed durable state, rather than merely recording a boolean or any exception. Root
also requested these checks directly. **Closed for the exercised faults:** Mongo now checks
the expected facade/provider capability rejection and nested Mongo validation error 121;
PostgreSQL checks nested SQLSTATE `P0001` plus the exact injected marker. Independent fresh
runs reproduced the required unchanged/rolled-back/first-only durable states described below.

### Non-blocking measurement qualifications

- Mongo seeds clone a real BSON template but initially retain first-row source/timestamp
  fields. PostgreSQL likewise declares fixed template timestamps. This is acceptable only
  as explicitly synthetic query work, not domain/source fidelity or authoring throughput.
- The added all-ascending compound index may not satisfy `Sequence DESC, Id ASC` by simple
  reversal. Both workers must report actual older-window sort plans rather than assume
  one index serves every ordering.
- The current PostgreSQL trace asserts an actual selected query and no count in its first
  stream page; full expected identity/sequence checks protect result ordering and scope.
- Lab guard source now checks the sole expected container network and exact two network
  members, in addition to images/ports/mounts/resources. Root reports an actual successful
  run. Probe entry scripts should call this guard so fixed endpoint reuse is checked again.

### Infrastructure correction before probe runs

The initial `internal: true` network configuration was replaced: on this Docker Desktop
host it suppressed published-port reachability despite healthy containers. The current
configuration uses dedicated bridge `tangent-epic005-provider-lab_host-access`, with both
host ports explicitly bound to `127.0.0.1`. **Outbound network traffic is not firewalled.**
The reviewer read the updated Compose/README; root reports inspecting actual bindings,
successful host TCP checks, Mongo `rs0` writable-primary status and no unrelated containers
on the dedicated network. These reported runtime checks are not independently re-run here.

Containers were recreated with the same identified lab-only named volumes, without data
deletion. Root reports initial storage of approximately 206 MiB Mongo/39 MiB PostgreSQL and
continues monitoring the combined ceiling. Both servers have 1 CPU/2 GiB hard Docker limits;
the serial client uses a two-CPU mask and monitored 1.5 GiB ceiling, with build resources
accounted separately. This replaces any earlier description of the lab as egress-isolated.

### Independent guard tests and actual pre/post checks

Added `tests/provider-lab-red-team.test.mjs`. It mocks Docker/CIM entirely and never invokes
the real Docker CLI. All **9/9** tests passed: matching lab accepted; unrelated network member,
extra container network, wrong container owner, public host binding, uncapped memory, foreign
volume owner, excessive generated data and low host-memory headroom each rejected.

Both current probe scripts call `ProviderLab/check.ps1`. Their actual pre/post checks also
passed during the independent runs, including exact container/network identity, bindings,
volumes, limits and resource headroom. Final reviewer check measured about 652 MiB combined
database volume state and 33 GiB free host RAM. Root subsequently stopped both lab containers
without deleting their named volumes; root reported approximately 653 MiB retained afterward.

### Independent executable results

Compact reviewer-owned evidence:
[provider-red-team-reruns-20260912.json](provider-red-team-reruns-20260912.json).
Full generated reports remain in the identified ignored experiment roots. No worker evidence
was overwritten, and no application or framework source was changed by this review.

| Independent command | New experiment root | Result |
| --- | --- | --- |
| `./probes/MongoProbe/run.ps1 -Posts 10000 -SkipBuild` | `.local/experiments/epic005/mongo-baseline-7b9d5abed0574b2f9bc853027be032b8` | Pass: CRUD, window identity/content/scope, retained command capture, capability gate, injected cross-entity deferred failure |
| `./probes/PostgresProbe/run.ps1 -SkipBuild` | `.local/experiments/epic005/postgres-health-3331faf07309452c94897c03bd975a9c` | Pass: 10k/100k CRUD/query/count/plan checks, native same-entity batch success/rollback, injected deferred failure |

Mongo's worker 10k and 100k artifacts were also independently inspected. At 100k hot posts
plus 20k distractor posts and two sentinels, the unindexed selected-row and count commands
each examined 120,002 documents. An isolated ascending compound index reduced forward
selection to 21 documents/21 keys; the accompanying count still examined up to 100,000 keys.
The older `sequence DESC, _id ASC` query still examined 49,999 keys and sorted despite returning
21 documents. The separately captured first stream page performed one find and no count.
These results expose index-direction and unnecessary-count costs; they are not a provider
winner or resumable whole-stream traversal proof.

PostgreSQL's independent 100k hot/20k distractor run likewise used full scans/sorts before
the isolated expression index. With that index, forward first-stream selection read 21
index rows. Older selection used incremental sorting over 22 input rows. Materialized
`Query` still issued its count; beginning/middle counts chose sequential scans even after
the selection index existed. Query-only warm p50 for indexed first stream pages was roughly
8–10 ms in this run. These serial, logged, pooling-disabled measurements are directional
evidence for this configuration, not full API latency or matched-engine rankings.

### Integrity and Koan ownership conclusions

- **Mongo native atomic batch capability:** correctly unavailable through the pinned adapter.
  `RequireAtomic` rejected before the mutation callback and before any profiled application
  command; all sentinel documents remained unchanged. The available replica-set topology
  did not turn missing adapter support into a guarantee. This is not a framework bug.
- **PostgreSQL native same-entity atomic batch:** a successful two-row batch returned an
  atomic receipt and durable rows; a deliberately rejected second write in a three-row
  batch left zero rows. This proves that tested batch seam, not Tangent's multi-entity commit.
- **Ambient deferred coordinator on both providers:** expected injected failure left the
  first operation durable and later operations absent. Mongo exercised Message → ActivityHead
  → Message; PostgreSQL exercised three Messages. Neither experiment is the complete
  decision/message/sequence/journal/receipt acceptance/restart/retry path. The observed
  non-atomicity agrees with the coordinator's documented capability limit; Tangent must
  repair its assumption or use a proved native transaction/recovery protocol.
- **Public capability metadata nuance:** PostgreSQL's public `Message.Batch()` facade reports
  `ExecutionCapabilities=None`, while the underlying Npgsql native batch advertises Atomic
  and execution produces the proved atomic receipt above. `IBatchSet` documents that field
  for a created native batch; `BatchFacade` inherits its conservative default and qualifies
  the native batch only during Save. Root sent this as a Koan API-semantics/clarification
  question, not a confirmed runtime bug. It must not be reported as PostgreSQL lacking the
  tested native batch support.
- The previously found public transaction XML atomicity promise is a confirmed Koan
  documentation defect; root already handed it to the Koan agent. The list-only-query
  count is a measured framework performance pitfall, not a newly proved contract violation.
  Missing application indexes and mixed-direction index mismatch remain application
  schema/query decisions. No additional provider runtime correctness defect was established.

## Final disposition

The new probes are independently qualified for their stated isolated CRUD/query/capability
and injected-failure scope. The reviewed harness defects were repaired as qualified above.
No production database selection or switch is approved: complete domain atomicity/recovery,
real API enrichment/authorization, concurrent writes/readers, permission changes, resumable
cursor behavior, million-post saturation and browser windowing still require their own gates.
SQLite, PostgreSQL and Mongo timings here differ in fixture details, process topology,
pooling, instrumentation and workload; comparing their headline milliseconds as a winner
would exceed the evidence. All reviewer workloads are complete; synthetic state is retained.
