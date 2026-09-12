# EPIC-005 independent plan red team

Date: 2026-09-12. Scope: the initial EPIC-005 plan and the checked-out implementation;
not approval of a production implementation or of unperformed benchmarks. Read-only code
inspection was used. No database, running service, credential, or application source was changed.

## Verdict

The isolated P1 baseline and pure browser-window model are safe to pursue under the epic's
resource/isolation rules. Do not claim P2 correctness, existing atomic acceptance, or a
provider winner yet. The first finding materially changes the presumed starting point:
Koan's ambient transaction is deferred coordination, not a demonstrated database transaction.

The plan correctly separates rendered rows, retained browser data and API work; keeps
drafts/read acknowledgment distinct; requires real-browser evidence; and excludes live-site
reset and automatic agent execution. These are not substitutes for the gates below.

## Findings requiring explicit resolution

### RT-P01 — High: atomic acceptance is an unproved requirement, not an established invariant

**Gate:** blocks P2/P5 correctness approval and release claims, not isolated baseline work.

EPIC-005's API/persistence section says to preserve atomic message/source acceptance and
activity-journal commit. Tangent calls `EntityContext.Transaction` in
`src/server/web/Rooms/RoomGovernance.cs:209`, then `EntityContext.Commit` at line 225.
Acceptance stages separate source-decision, message, conversation-head, journal and
journal-head saves (`src/server/web/Conversation/ConversationService.Acceptance.cs:70-95`;
`src/server/web/Activity/ActivityJournal.cs:28-43`).

The pinned checkout's
`.local/upstream/koan-framework/src/Koan.Data.Core/Transactions/TransactionCoordinator.cs`
explicitly describes deferred coordination rather than native/distributed atomicity at
lines 16 and 261. `ExecuteOperations` clears the ambient transaction and executes tracked
operations one at a time (lines 276-322), reporting an unknown partial outcome after failure.
This is a concrete capability mismatch, even for an engine with native SQL transactions.
A method named `Transaction` and a successful happy-path write do not prove rollback.

Separately, the Mongo connector's README at lines 115-120 and `MongoRepository.cs:315`
reject `RequireAtomic` batches. That is a different seam; a single-entity batch capability
must not be mistaken for cross-entity atomicity of Tangent's domain operation. A replica-set
topology alone does not change the pinned adapter contract.

**Repair:** record cross-entity atomicity as a requirement to establish. Add an early
capability matrix for the exact path used by the application: ambient coordination,
same-entity atomic batches, native cross-entity transaction, isolation/concurrency behavior,
and failure recovery. A provider lacking the required path is explicitly inadmissible for
the complete workload until a reproducible framework/application correction exists.

**Acceptance test:** fault after each constituent save, including cancellation and process
termination; reopen the isolated database without process caches. Assert either no accepted
operation exists or the complete decision/message/sequence/journal/receipt set exists.
Retry the same operation ID and verify one accepted post and one coherent durable event.
Run through the real domain commit path for every candidate provider. Until measured,
report the static mismatch rather than claiming a reproduced corrupt transaction.

### RT-P02 — High: a fast message query can conceal unbounded response enrichment

**Gate:** blocks a bounded-API/P2 claim from a query-only benchmark.

`ConversationService.McpWindow.cs:89-94` resolves handles and participant decoration after
selecting its bounded message window. `ParticipantDirectory.LabelsFor` queries every
labeled or AT Protocol identity before filtering to the wanted IDs in memory
(`src/server/web/Participants/ParticipantDirectory.cs:114-124`). A 20-post response can
therefore read a growing identity collection. On the write path, facet derivation can
query all identities when the text contains a mention and no explicit package was supplied
(`src/server/web/Conversation/MessageFacets.cs:11-16`). Synthetic messages with empty
facets and synthetic participants with no labels can accidentally hide both costs.

**Repair:** name complete response enrichment, mention lookup/derivation and the activity
snapshot's channel enrichment as baseline dimensions, in addition to history-query plans.
Keep a query microbenchmark explicitly scoped and separate from API evidence. Seed varied
labeled identities and exercise both explicit and derived facets. Fix bounded lookup paths
before calling the application window bounded.

**Acceptance test:** hold the returned post count and authors constant while increasing
unrelated labeled participants/identities by orders of magnitude. Capture total provider
queries, examined rows, allocations and latency for the complete window endpoint and a
real mention-bearing write. Confirm no whole identity collection is materialized and no
external handle lookup occurs in the isolated fixture.

### RT-P03 — Medium: directory continuation has no declared mutation-consistency contract

**Gate:** resolve before implementing generalized P2 directory cursors.

The epic correctly requires last-scanned advancement, stable ordering and identity
deduplication. It does not yet state whether a traversal is snapshot-consistent, live with
defined omissions/restarts, or sorted on an immutable traversal key. Deduplication only
removes repeat rows; it cannot recover an unseen row that moves behind the current anchor.
The current directory API uses numbered pages and re-scans earlier storage candidates
(`src/server/web/Communities/TangentGovernance.cs:340-388`). Current activity channel
cursors also encode a page number (`src/server/web/Activity/ActivityService.cs:118-134`).

**Repair:** define the enumeration contract explicitly. A practical first contract is
immutable keyset traversal with a stable tie-breaker, current-policy filtering on every
response, and an explicit refresh/reset mechanism for newly eligible or newly created rows
behind the anchor. If activity-ranked sorting is exposed, define how rank movement changes
the continuation; do not silently reuse the immutable directory contract. Permission loss
must invalidate already-retained protected data even if that row is not on the next page.

**Acceptance test:** traverse empty authorized pages; insert/delete before and after the
anchor; grant/revoke membership mid-traversal; create equal sort keys; and move a visible
row's displayed rank. Assert the documented set/order or explicit reset, not simply a
duplicate-free list. Verify an active Topic beyond the first activity-directory page still
receives relevant invalidation and does not disappear when a partial overview arrives.

## Required scope qualifications and non-blocking suggestions

### RT-P04 — Medium: explicitly scope the provider load baseline to one application process

`PolicyGate` is one in-process semaphore (`src/server/web/Infrastructure/PolicyGate.cs:6`),
and the journal allocates its sequence by reading and saving a shared head
(`src/server/web/Activity/ActivityJournal.cs:28-43`). Participant minting also uses a static
process-local gate (`src/server/web/Participants/ParticipantDirectory.cs:15`). Concurrent
clients in one process do not establish multi-process write safety.

The epic defers distributed services, so multi-instance support need not be added. State
single-application-process explicitly in benchmark metadata and provider recommendations.
Do not infer horizontal safety from a PostgreSQL/Mongo result. If multi-instance operation
is later admitted, add two-process acceptance/sequence/identity and cross-process event
notification tests before enabling it.

### RT-P05 — Medium: turn known focus/selection/budget tensions into a pre-UI decision

The epic already acknowledges that retained focus/selection and very tall messages need
bounded handling; this is appropriately unresolved. Before P3, choose a concrete policy
for a selection crossing an eviction edge, a focused row far outside the scroll window,
and a single supported record larger than the configured cache budget. Pinning arbitrary
selected rows indefinitely breaks the bounds; evicting them silently breaks copy/focus.
Measure resident heap as well as serialized content. Include message/version objects,
height maps, reply excerpts, facets, inspector state, virtualizer keys and request queues
in the audit—not only the main records map.

Suggested acceptance: a long selection/keyboard traversal across several eviction cycles
has a documented accessible behavior with no silent focus loss; a maximum-size supported
post has a defined readable path; over-budget invalid responses cannot empty the current
window or replace the valid continuation.

### RT-P06 — Low: specify the transport cutover race tests before P3 integration

The planned SSE/long-poll coordinator is sound in shape. Test a commit between snapshot
and subscription, a commit during SSE-to-poll transition, cursor expiry during a hidden
tab, stalled-but-open SSE, simultaneous pane navigation and identity change, and a failure
while a fallback request remains in flight. Assert bounded concurrent requests and at most
one active primary/fallback loop; eventual markers/content must agree with durable history.
These tests must inspect counters and accepted generation IDs, not only connection badges.

## Review status

- Initial plan inspection complete; the coordinator owns resolutions in EPIC-005.
- RT-P01 through RT-P03 need recorded disposition before their named gates close.
- RT-P04 through RT-P06 are qualifications/explicit later decisions, not reasons to stop
  a safe pure prototype or read-only baseline.
- No runtime claim, provider score, browser continuity approval, or release approval is
  issued by this report. P1/model implementations require a separate independent pass.
