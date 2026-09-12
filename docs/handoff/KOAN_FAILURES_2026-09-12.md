# Koan failure handoff — Tangent EPIC-005

Date: 12 September 2026. Requested by Leo: pass Koan bugs to its agent and list identified
failures. Destination: existing **Report framework status**, koan-framework project,
local task `01a096b9-350b-7920-9559-3075077afcb0`.

Delivery status: **sent and accepted by the task messaging tool on 12 September 2026**.
The full report, exact revisions, reproducer/evidence paths, ownership distinctions and
request for triage/support guidance were sent to the destination above. Koan's issue
dispositions were returned against current HEAD `5e62f0d`: **K01 and K02 confirmed**.
The agent confirmed there is no supported Entity-level cross-entity native atomic API.
`QueryStream(predicate, sort, batchSize: 21)` consumed for only its first page is an
existing count-free workaround, subject to native pushdown and proper disposal.

Leo subsequently authorized issuing implementation work to that agent. A follow-up
assignment was delivered for K01 public-contract corrections, K02 count-free item-only
queries with regressions, and a bounded cross-entity capability design/work breakdown.
No commit, release, consumer pin update or live deployment was requested. Koan has now
returned implementation closeout for K01, K02 and the subsequently confirmed Q03 public
capability defect. Changes are in its main working tree, not a published revision.
Tangent consumer adoption and runtime verification against those changes remain pending.

### Q-04 — Array Contains silently becomes a full-scan residual (P2)

New performance defect reproduced on consumer pin
`e07a84cc3f71a0867f1122b03b723cc80727e772`, SDK 10.0.401 / runtime .NET 10.0.12.
`LinqFilterCompiler.cs` is unmodified (SHA256
`70DED322D2FE56E1A54C965164C411BABCDA2BC079BCECC816BB566F2B9D82B5`).

```csharp
string[] ids = ["p1"];
Expression<Func<ParticipantIdentity, bool>> e = x => ids.Contains(x.ParticipantId);
var filter = LinqFilterCompiler.Compile(e);
```

Expected: native structured IN for a supported collection Contains expression. Observed:
the current compiler binds this expression to `System.MemoryExtensions.Contains`, through
`op_Implicit(ids)` to span; Koan recognizes only instance or `Enumerable.Contains` forms
and emits `ClrFilter`. Source inspection independently corroborates the native trace.
The old scoped lookup read 16,293 identity IDs across three requested-owner chunks to
obtain 430 matches in a fixture with 5,000 unrelated identities. Results are correct;
the defect is silent query-work amplification, not a demonstrated data disclosure.

Tangent uses the supported explicit `Filter.In` workaround: exactly 430 matching IDs,
five count-free first-page/keyset reads, LIMIT 128 OFFSET 0, zero native full-scan steps,
and SEARCH on the `(ParticipantId, Id)` expression index. Reproducer/SQL/plan assertions:
`tests/TangentSpace.Tests/ParticipantLabelWindowTests.cs`,
`Native_scoped_keyset_pages_read_only_requested_identities_without_counts_or_global_scan`.
Use the detailed console logger to see lowering and SQL evidence.

Delivery: the coordinator re-resolved **Report framework status** and sent the exact
revision, reproducer, evidence, severity and implementation assignment on 12 September;
the messaging tool accepted delivery. Requested regression coverage for current span
lowering and supported closure/constant semantics, with unsupported expressions remaining
conservative. The task acknowledged the assignment and entered its framework change
workflow. Koan later reported the fix on development commit `afa869032` with its complete
Data Filtering suite at 112/112; Tangent remains pinned to `e07a84c`, so adoption is pending.
No ignored-checkout patch was made.

### Q-05 — empty transaction scopes amplify activity/log work (P2)

New operability/performance defect reported against Tangent's exact consumer pin
`e07a84cc3f71a0867f1122b03b723cc80727e772`. Expected: committing or rolling back a scope
that queued zero operations is a cheap no-op without transaction lifecycle/activity noise.
Observed: ordinary read navigation and each 15-second activity reconciliation emit
`started`, `committing 0 operations across 0 adapter(s)` and `committed successfully`;
cancellation paths similarly log rollback of zero operations. The final local container
recorded many such triplets from one authenticated browser session.

Impact is allocation and severe log amplification as tab count grows; this was explicitly
distinguished from the 57.7-second HTTP/1.1 connection-slot failure. Tangent owns removing
ambient scopes from genuinely pure reads while retaining scopes around writes and bootstrap
intent. Koan owns making empty framework scopes silent/cheap. The coordinator sent this
exact revision, reproducer, timestamps, severity and ownership split to **Report framework
status** on 12 September; the task messaging tool accepted it. Framework disposition and
fix receipt were subsequently returned as upstream commit `917875d07`.

Adoption receipt: Tangent first isolated that three-file change onto its preserved local
framework checkout as `882bd550b653447467206c3f3e43cf04bef8c7d8`; its focused transaction
spec passed **6/6**. After Koan published the fix, the contribution was rebuilt from current
`origin/main` instead of replaying the backport. The resulting local contribution branch
head is `34f678d7c`: current Koan plus the static-header correction, generic protocol seam,
native atproto connector, solution membership, and dependency floors. The old branch remains
as a local recovery ref.

Tangent rebuilt successfully, the container is healthy, and signed-in navigation to
`/t/home/topics/are-ai-counscious` exercised the topic, Tangent, activity and message reads
without any new `tangent-room-policy-operation` start/commit/rollback-empty telemetry. The
anonymous home route measured 97 ms cold and 4 ms warm in the same deployment. Koan's task
was sent the consumer verification receipt.

The full Tangent suite now passes **396/396**. The 22 initially observed failures were
consumer test-fixture drift: MCP hosts lacked `IWebHostEnvironment` for `ProfileCapture`,
authorization fixtures lacked `IAtprotoHandleSource`, and one profile test seeded the old
projection instead of the local profile snapshot. Their focused repair run passed **40/40**;
no new Koan defect was inferred.

### Framework implementation closeout

The coordinator inspected Koan's AE-19 work card and relevant changed source. K01 corrects
the public/docs/runtime narration and adds a durable-prefix regression; it does **not**
make the ambient coordinator atomic. K02 preserves nullable count intent for item-only
All/Query/Page while explicit QueryWithCount retains its default count. Q03 exposes the
effective native batch guarantees through the facade and cache/variant wrappers, keeping
RequireAtomic fail-closed and soft-delete lowering conservative.

Koan's agent reports: Data Core owner suite **561/561**, SQLite provider-bounded tests
**2/2**, focused capability/source-policy suite **25/25**, cache build zero errors/warnings,
documentation lint zero errors and public-documentation truth checks passing. These are
framework-agent verification results, not Tangent's own rerun against the changed source;
the focused suite overlaps the owner suite and is not an additional unique test total.

Work card: `E:/repo/github/sylin-org/koan-framework/docs/initiatives/application-evolution/work-items/19-data-transaction-and-query-truth.md`.
No commit, package/release, pin update or deployment occurred. Cross-Entity native atomicity
remains a separate design gate: single-source preflight and provider-owned unit of work,
with relational connection/transaction and Mongo session implementations and corrective
cross-source rejection. No new atomic primitive is claimed by these fixes.

## Evidence scope

Tangent root: `E:\repo\github\sylin-org\tangent-space`.
Koan consumer pin: `e07a84cc3f71a0867f1122b03b723cc80727e772` under
`.local/upstream/koan-framework`. The app base is
`5c98bd4cf5a1bdd50d10ed8aef0a980cd6f48994` plus preserved dirty source; raw evidence hashes
the actual relevant files. Koan's main checkout currently reports
`5e62f0d04fcb82b173b59e642638d869296efd3b`; the K01 contradictory API/coordinator comments
were checked there too. Runtime reproductions below used the consumer pin, not that newer
main checkout. Retest proposed fixes/current behavior before marking the reports closed.

- [Baseline and exact environment](../evidence/epic005/sqlite-baseline-20260912.md)
- [Raw traced results](../evidence/epic005/sqlite-baseline-20260912.json)
- [Independent review/reproduction](../evidence/epic005/red-team-foundations.md)
- [Independent raw results](../evidence/epic005/sqlite-red-team-20260912.json)
- [Runnable probe](../../probes/ScaleProbe/README.md)

The probe uses actual `Message.Query`/`Message.Save` and Koan's SQLite adapter, deterministic
synthetic state and no started host, HTTP ports, credentials or external workers. Run
`probes/ScaleProbe/run.ps1` from Tangent. It rejects live/existing/unvalidated roots and
retains new state under `.local/experiments/epic005/`. No application/framework fixes or
indexes were inserted into the published baseline. Timings are six serial traced warm
samples, not saturation measurements. The separate failure probe is not a complete
domain-acceptance or process-crash test.

## K01 — High: public transaction API promises unsupported atomicity

**Confirmed Koan documentation/API-safety defect.** In both the consumer pin and inspected
main checkout, `src/Koan.Data.Core/EntityContext.cs:227` says operations are committed/
rolled back atomically. This conflicts with the deferred-only guarantee in
`Transactions/ITransactionCoordinator.cs:12`, `TECHNICAL.md:211`, and
`Transactions/TransactionCoordinator.cs:16,261` in the consumer pin.

The coordinator clears transaction context and executes queued operations sequentially.
Reproducer `probes/ScaleProbe/TransactionProbe.cs` queues three actual `Message.Save` calls
under Tangent's `tangent-room-policy-operation` transaction name. A SQLite trigger rejects
the second. Commit raises `TransactionException` after one completed operation; an
independent connection finds only `epic005-tx-first` persisted. Both worker and independent
reviewer reproduced this.

Tangent assumed its message/source-decision/sequence/journal/receipt operation had a stronger
boundary. Runtime deferred behavior is a documented capability limitation, not by itself
proof the coordinator violated its own technical contract. The public promise is still
incorrect and dangerous for consumers.

**Requested triage:** reconcile public API documentation and guidance; identify the
supported native cross-entity atomic path or required durable-recovery design. Consider
an explicit required-capability rejection before any writes when atomicity is requested.
Do not treat a same-entity atomic batch as cross-entity transaction support. Add a
failure-after-N-writes regression and honest capability assertions. Tangent's full domain
fault/restart/idempotent-retry proof remains our integration gate.

## K02 — Medium: item-only Query performs an unused exact count

**Confirmed framework performance behavior; design/defect triage requested.** This is not
being presented as a demonstrated public-contract violation or a claim about every read API.

`src/Koan.Data.Core/Data.cs:265,285` routes ordinary list-returning `Query` through
`QueryWithCount`; line 115 substitutes `CountStrategy.Optimized` when unspecified. The
SQLite adapter at `src/Connectors/Data/Sqlite/Runtime/SqliteRepository.cs:407-416` executes
an exact count for a non-null strategy. The caller discards the count.

With a predicate/sort/limit keyset query returning 21 Message rows, the native trace shows
SELECT plus COUNT. At 100,000 rows, each statement performs 99,999 full-scan steps; SELECT
also uses a temporary ORDER BY tree. Traced p50 is 305–423 ms across four positions in the
first final run, with comparable independent results. Missing Tangent indexes contribute
to both scans and are separately owned below.

**Requested triage:** identify an existing supported count-free predicate + ordering +
limit Entity path, or fix the item-only path so callers need not pay for an unused count.
Explicit `QueryWithCount` must retain its semantics. Regression evidence should show a
single count-free SELECT for this call while explicit count APIs still count. Do not
assume changing a nullable count option works: the inspected core substitutes a default.

## Related capability questions, not confirmed Koan bugs

- **C01 — Cross-entity native commit support:** required for Tangent's chosen operation
  semantics; the deferred primitive currently does not provide it. Needs capability/design
  guidance independently for SQLite/PostgreSQL/MongoDB. A database switch alone is not proof.
- **C02 — Mongo atomic batches:** pinned connector documentation explicitly declines
  `RequireAtomic` batch execution. A replica set alone does not change that adapter
  contract. Treat as an admission limitation, not a broken implemented feature.
- **C03 — Residual/offset query behavior:** bounded response size does not prove bounded
  provider work or mutation-safe traversal. Framework fallback/numbered streams are
  documented behaviors; no separate violated contract has been reproduced here.
- **Q03 — Public batch capability observability:** the PostgreSQL probe's public
  `Message.Batch().ExecutionCapabilities` is `None`, while repository AtomicBatch is
  advertised and RequireAtomic succeeds with an Atomic receipt and actual injected-failure
  rollback. The native Npgsql batch advertises Atomic; RepositoryFacade's public wrapper
  appears to inherit the default None property. Koan subsequently confirmed this as a
  public-surface defect and implemented effective-capability forwarding with regressions.
  It was not an atomic execution failure. Tangent's pinned baseline remains unchanged.

## Additional Mongo/PostgreSQL runtime evidence

Both providers passed actual Entity CRUD and ordered 21-post queries at 10k/100k hot-Topic
rows with 20% distractor rows. Mongo's atomic request correctly rejects before its callback
or captured Message database commands; independent sentinels stay unchanged. Its cross-Entity
Message → ActivityHead → Message deferred scope failed with provider validation code 121,
leaving only the first write durable. PostgreSQL's native same-Entity atomic batch rolls
back every row on injected P0001, whereas its ambient deferred scope leaves only the first
Message durable. Independent red-team reruns reproduced both providers' outcomes.

Materialized Query still adds an exact count on both providers; the first 21 rows of
QueryStream are count-free. Matching isolated indexes materially reduce native read work.
The [provider assessment](../evidence/epic005/provider-assessment-20260912.md) separates
bounded query evidence, provider capabilities, index-direction gaps and still-unproved
domain acceptance. These results and Q03 were delivered to the Koan agent; no additional
confirmed runtime bug was inferred from documented capability limits.

## Tangent-owned failures retained here, not assigned to Koan

1. Message has no matching compound index declaration for Topic/Sequence windows. Measured
   schema has only Id PK. There is no evidence that Koan ignored a declared index.
2. `ParticipantDirectory.LabelsFor` and mention/facet derivation scan growing identity
   collections before filtering in memory; small output does not bound enrichment work.
3. Tangent directory rescans/offset traversal and a 500-candidate cap can leave later
   authorized entries unreachable; last-scanned resumable cursors are required.
4. Current browser accumulates post/directory data and DOM, can reload all previously
   loaded history on edits, and lacks a persistent SPA route lifecycle.
5. Browser participant SSE fallback is incomplete: participant `/api/activity/wait` is
   unused and loss of an established SSE does not guarantee equivalent fallback coverage.
6. Process-local policy gates and read/save sequence-head allocation do not establish
   multi-application-process safety; current benchmark claims stay single-process.
7. Existing WebMCP tests mismatch the current interface: 4 passed, 10 failed, 4 timed out
   in the bounded rerun. See [coordinator verification](../evidence/epic005/coordinator-verification.md).

The probe's original percentile-selection and result-validation defects were fixed and
independently retested (RT-F01/RT-F02); they are not open framework failures.

## Coordination

The Koan agent should own framework triage and changes in its main checkout. Tangent owns
consumer adoption, declared indexes, API enrichment/windowing and end-to-end verification.
Please return supported APIs/capability boundaries, issue disposition and any exact fix
revision/reproducer instructions to this task. Do not edit Tangent's consumer pin or live
application while investigating; these fixtures remain reproducible baseline evidence.
