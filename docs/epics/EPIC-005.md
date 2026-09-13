# EPIC-005 — A continuous workspace, at real scale

**Continuation:** [EPIC-006](EPIC-006.md) now owns the combined ideation-cycle backlog. Its S10/S11/S18 adopt the unfinished SPA/window/directory/provider slices here. Reuse this epic's implemented foundations and evidence; do not duplicate the work or turn all historical gates into prerequisites for unrelated improvements. Status statements below are dated evidence, not a claim that the full SPA is complete.

Owner-directed, 12 September 2026. Status: **plan reviewed; initial foundations measured/tested; full implementation gates remain open**.

Leo authorized creating this epic, execution through Codex subagents, and independent
red-team evaluation. Application data and our own internal schemas are disposable PoC
state: rebuild deliberately instead of adding migrations or compatibility shims. Source
work is not disposable. External AT Protocol and MCP requirements remain intact.

## Outcome

Tangent Space feels like one continuous place. Full-width header and footer surround a
readable central workspace. Contextual navigation and supporting views occupy side panes
when space permits; the same supporting view replaces central content on small screens.
Navigation, live activity and pane changes preserve the conversation, identity, draft and
reading position.

A Topic with a million posts is a dataset to navigate, never an array or document to load
in full. Browser DOM, retained data and API responses all have independent explicit bounds.
Large Tangent, Topic and participant collections follow the same principle. Real isolated
experiments establish the limits of the application, Koan adapter and database separately.

## What the audit established

- Main web routes share `index.html` as source, but ordinary navigation creates another
  document. `pages.js` parses its route once; `rooms.js` combines routing, state, rendering,
  drafts, administration and live communication.
- Topic/post CSS changes `--page-width` from 1120 to 960 pixels; header/footer positioning
  inherits it. Persistent routing alone would not fix this movement.
- Browser history currently starts from the beginning, accumulates every loaded row in
  memory and DOM, and can re-fetch all loaded history on an edit/delete.
- Server history has permission-filtered, protected sequence cursors; `McpWindow` already
  supports older/newer, around-post and unread/latest windows. Its historical boundary is
  distinct from a fresh live-tail continuation. The browser underuses this contract.
- Participant activity SSE has durable checkpoint/replay/reset and identity-change events.
  `/api/activity/wait` exists but is not the browser's participant-wide fallback. The older
  Topic long-poll is not a second Topic SSE stream. Profile decoration has a separate SSE.
- Some directory endpoints rescan earlier candidates and stop at a 500-candidate policy
  budget without a resumable continuation; opaque directory cursors can wrap offsets.
- The pinned SQLite adapter pushes simple predicates/order/limits to SQL. Ordinary
  materialized `Message.Query` also passes through a default count strategy that SQLite
  implements as an exact count. Message declares no compound index. Actual generated
  schemas and query plans require measurement; no large-scale performance claim exists.
- Koan source includes SQLite, PostgreSQL and MongoDB adapters. Adapter existence does not
  establish equivalent transaction, ordering, authorization or performance behavior.
- Independent review found that the pinned `EntityContext.Transaction` coordinator
  explicitly provides deferred coordination, not a native cross-entity atomic transaction.
  It executes tracked operations sequentially after clearing transaction context. Tangent's
  message/source/journal atomicity is therefore a required contract still needing proof,
  not an established guarantee to assume for any provider.

## Non-negotiable behavior

1. **Bound all three layers.** Render a viewport plus modest overscan; retain only a
   count/byte-bounded page cache; request count/byte-bounded server windows. Evict distant
   pages, including associated resolved-profile decoration and measured-height metadata.
   Neither hiding DOM nor keeping a full dataset behind a virtualizer satisfies this epic.
2. **Navigate by stable identity.** Restore post ID plus viewport-relative offset, not a
   global row index or accumulated pixel coordinate. Open a deep post directly, fetch
   adjacent windows in either direction, and jump to start/unread/latest without traversing
   intervening history. If an anchor disappears, retain the closest surviving neighbor and
   explain the unavailable target. Never make an enormous synthetic spacer stand for all
   history; distant navigation is cursor/anchor based.
3. **Do not steal the reader's place.** Incoming posts follow only an already live-following
   reader; otherwise update a bounded new-activity marker. Prepends, eviction, edits,
   delayed images/fonts and pane-width changes preserve the reading anchor. Keep DOM order
   chronological; do not rely on inverted transforms. Respect reduced motion.
4. **Keep authored work outside disposable rows.** Composer text, selection, facets, reply
   target and uncertain write operation IDs survive navigation/window eviction. Scope by
   origin, participant and Topic. An identity change cannot adopt another actor's pending
   write. Reconnect never blindly resends. Draft retention policy must be explicit and
   preserve existing security behavior until a deliberate user-facing change is accepted.
5. **Fetching is not reading.** Activity delivery, prefetch/cache visibility, actual viewport
   visibility, local reading position and explicit server read acknowledgment are separate.
   Preserve explicit acknowledgment unless a separate product decision changes it.
6. **No cross-identity world.** Session transitions abort old transport and requests,
   invalidate permission-sensitive caches, advance a session generation and reload current
   identity. Late old-generation responses cannot render. A missing/forbidden route does
   not stop global activity for other routes.
7. **Keep navigation native where appropriate.** Real deep links, Back/Forward, hashes,
   opening in a new tab, focus/title changes, keyboard selection/copy and honest
   401/403/404 states work. OAuth and cross-origin application boundaries may reload.
   Virtualized content needs keyboard/focus retention, a usable accessible history path,
   and explicit search for unloaded history; browser Find cannot promise off-DOM results.
8. **Preserve product authority.** Profile metadata is decoration, not identity proof.
   Shared server Atmosphere configuration stays manager-authorized; this layout does not
   silently introduce personal overrides. No automatic model execution from activity.

## Target architecture

### Persistent shell and adaptive views

- An application shell owns Atmosphere, brand/account, connection status, footer, global
  notices/dialogs and layout. Full-width chrome geometry is independent of reading width.
- Central content is the primary activity. Left pane provides contextual orientation;
  right pane provides an explicitly selected inspector/configuration view. Panes are
  relevant and predictable, not automatically opened by unrelated events.
- Three columns become two, then one according to actual usable width. Preserve the pane
  explicitly opened by the user. Responsive placement does not push a history entry.
  Substantial pane selection does; Back closes it or returns to the underlying route.
- Conversation routes have one controlled history scroll surface and a composer outside
  the virtualized list. Supporting panes may scroll independently. Avoid nested message
  scrollers, focus traps and mobile keyboard/viewport clipping. Editorial routes can use
  document scrolling. Full-width footer does not require always pinning it on small screens.
- Share source-level UI tokens/components with the local connector manager, but retain
  separate origins, credentials and application runtimes. The browser WebMCP agent surface
  likewise retains bearer/human-cookie isolation. The public Connect guide belongs in the
  main route structure; OAuth bind/callback pages remain document boundaries.

The first implementation proposal is Vue 3 + TypeScript + Vue Router + Vite, with a small
explicit session/store layer and a measured-height virtualizer. Pin actual dependency
versions and prove variable-height bidirectional anchoring before committing the full UI.
Node is a build/test dependency, not a new production server. Preserve the approved warm
editorial/card/ASCII appearance. Framework selection is a gate, not a claim of implementation.

### Session, resources and events

- App-owned session and resource stores outlive routes. Route loaders own cancellation and
  stale-response generations. Conversation window state is separate from the mounted rows.
- One participant-wide activity transport per active authenticated tab: SSE primary,
  participant-wide long-poll fallback behind the same snapshot contract, bounded recovery
  snapshots if needed, reconnect backoff/jitter and a stalled-stream watchdog. Retry SSE
  deliberately without creating simultaneous duplicate primary/fallback loops.
- Keep the profile-decoration stream separate initially, with explicit bounded component
  subscriptions. Align visibility/page lifecycle behavior intentionally.
- Events update markers and coalesce scoped invalidation. Fetch actual post content only
  for the retained window or a user-requested destination. Do not replay entire Topic history
  after a single edit. A historical window and live tail have separate continuations.
- Back-pressure bounds event queues, pending invalidations, retries and profile caches.
  Expose live/degraded/stale status honestly, without asserting source freshness from SSE.

### API and persistence

- Reuse/generalize current window semantics for browser and agent callers: explicit
  start/unread/latest/around selection, older/newer cursors and independent live-tail state.
  Cursors remain opaque, scoped, permission checked and recoverable after expiry/reset.
- Directory cursors advance from the last **scanned** candidate, including when a permission
  scan yields zero visible results. Never confuse scan budget exhaustion with end-of-list.
  Fetch Tangent metadata and Topic collections separately; stable identity deduplication
  handles overlapping pages. Counts must not expose inaccessible items.
- Directory ordering is an immutable server-assigned listing order with a captured upper
  boundary and stable identity tie-break, not live unread/name/recent-activity rank. Rename
  or incoming activity must not move an item across a traversal cursor. New entries after
  the boundary appear on refresh. Current permissions are rechecked on every page; a grant
  affecting previously scanned entries invalidates the affected enumeration and requests
  a refresh rather than falsely claiming a complete newly visible collection. Test these
  semantics explicitly; deduplication alone cannot repair skipped items.
- Window queries need count-free execution, deterministic ordering and matching indexes.
  Validate `(RoomKey, Sequence)` and actual directory/journal access paths through the
  generated schema and provider query plans. A limited response alone is not bounded work.
- Bound the full response path as well: participant identity/profile enrichment, mention
  resolution, authorization and activity aggregation must not scan entire identity or
  directory datasets for each small post page. Query microbenchmarks do not prove this.
- Establish and prove atomic message/source acceptance and activity-journal commit, durable write
  idempotency, concurrent sequence allocation and current-policy delivery. Provider
  performance is irrelevant if these invariants fail.
- Fault-inject between each correlated entity save (message/source decision/room sequence/
  journal entry/journal head/receipt), then restart and retry the same operation ID. Require
  either one native atomic commit or a specified durable recovery protocol proving the same
  observable invariants. Deferred coordination and same-entity atomic batches are not that
  proof. Capture current failure behavior before selecting a framework contribution or an
  application persistence repair; do not silently label coordinator scopes as DB transactions.
- Provider admissibility comes before load comparison. The pinned Mongo adapter explicitly
  declines atomic batch execution; a replica set does not by itself add missing Koan
  transaction support. Record unsupported capabilities as a failed app-provider gate;
  a read-only Mongo comparison remains possible but is not an application recommendation.
  Initial application runs are single-process: current policy locks and journal sequence
  allocation must be redesigned/proved before claiming multi-instance safety.
- Framework improvements must be reproducible contributions against the pinned checkout;
  editing ignored `.local/upstream` alone is not a deliverable. Prefer existing suitable
  Koan APIs and report framework limitations explicitly.

## Execution slices and gates

| Slice | Deliverable | Gate before proceeding |
| --- | --- | --- |
| P0 — Plan/red team | This epic, review findings, agreed budgets and ownership | Resolve blocking plan findings; mark remaining decisions explicitly |
| P1 — Reproducible baseline | Isolated seeded Koan/application scale harness, current SQLite evidence, transaction-failure baseline, browser baseline characterization | Prove isolation; record current failures without tuning them away |
| P2 — Window and persistence contracts | Bounded browser store tests; generalized history/tail and resumable directory contracts; query/index and atomic-commit/recovery corrections | Cursor/identity/read/idempotency/commit correctness, representative query-plan evidence |
| P3 — Persistent web slice | Home → Tangent topics → conversation → profile → Back, persistent shell/runtime and virtualized history | Real-browser continuity, measured bounds and preserved existing interaction behavior |
| P4 — Complete adaptive surface | Remaining settings/onboarding/guide routes, long directories, shared manager shell components | Desktop/mobile/keyboard route matrix; no stranded legacy route scripts or identity crossover |
| P5 — Provider and saturation proof | Matched SQLite/PostgreSQL/MongoDB correctness and load runs; documented provider recommendation | Equal workload/resources/durability and explicit unsupported capabilities; no invented winner |
| P6 — Red-team release gate | Independent attacks on integrated build and evidence; fixes/retests; final handoff | No unresolved blocking findings; distinguish completed, limited and deferred work |

P1 and a pure browser-window model can proceed independently after P0. Schema-changing
optimizations follow a captured baseline. Do not rewrite all surfaces before proving the
conversation vertical slice. Do not deploy a partial route replacement over the working app.

## Isolated experiment design

### Safety and reproducibility

- Use a separately named disposable project/database namespace and loopback ports, with
  explicit absolute data roots outside the live `.local/docker/site` mount. No copied real
  cookies, bearer tokens, OAuth grants or user profile. Reject live endpoints in seed/load tools.
- Seed deterministic synthetic participants, Tangents, Topics, posts, facets, memberships,
  read positions and activity. Record seed and schema/build/provider versions. Bulk seeding
  measures read scale only; separate writes through the real domain/API establish behavior.
- Local-storage test mode has no external PDS publication or real account registration.
  Prevent external source/profile workers from turning synthetic IDs into network traffic.
- Start small, then grow. Proposed local ceiling per run: 4 CPU cores, 6 GiB combined
  app/database memory, 10 GiB generated data, 10-minute load phase. Check machine headroom
  before running; stop on resource/error thresholds. Saturate the capped test allocation,
  not Leo's entire desktop. Change these budgets explicitly if evidence requires it.
- Never delete a computed/unvalidated broad directory. Teardown targets only the identified
  experiment resources; retain small sanitized evidence. PoC disposability removes migration
  cost, not target validation. Existing unrelated source edits and other containers stay intact.

### Dataset and workload matrix

- History tiers: 10,000 / 100,000 / 1,000,000 posts in a hot Topic; many smaller Topics in
  parallel. Mix short/long supported text, Unicode, mentions, replies, tombstones and edits.
- Directory tiers: up to 10,000 Tangents and 100,000 Topics overall, with sparse and dense
  permissions, empty visible pages, equal sort keys and changing membership.
- Read at beginning/middle/tail; jump around a deep post; traverse both directions; leave and
  return; alternate cold/warm caches. Page work must not grow by traversing preceding history.
- Increase independent request arrival rate and concurrent readers/writers/subscribers in
  bounded steps until the test allocation saturates. Include one hot Topic and distributed
  Topics, idle versus active SSE, reconnect bursts, cancellation and slow consumers.
- Exercise accepted writes, uncertain retries, concurrent sequence allocation, edit/delete,
  permission revocation and restart between commit and delivery. Validate results, not only
  HTTP success counts. Publish offered and achieved load to avoid hiding queueing delay.

### Measurements and acceptance

- Record p50/p95/p99 latency, throughput, error/timeout rates, event-delivery lag, database
  query count/plans and rows/documents examined where available; app/DB CPU, memory and
  storage; browser mounted rows, retained pages/bytes, heap trend and long tasks.
- Baseline and optimized runs are separate. Use equivalent compound indexes and equivalent
  durability/transaction settings across providers, recording differences and any provider-
  specific configuration (including Mongo transaction deployment requirements).
- Proposed initial browser budget: at most 200 mounted message rows, 1,000 cached post
  records and 8 MiB cached serialized post content per active Topic; at most three retained
  Topic windows and 16 MiB aggregate post content. Cap directory/profile/measurement caches
  separately. Very tall posts and retained focus/selection need explicit bounded handling.
  These are starting budgets to validate, not achieved performance claims.
- Correctness gates: no duplicate/missing ordered posts under overlapping windows, no
  cross-actor stale results, no read advancement from prefetch, no automatic pending resend,
  no unauthorized delivery, no unresumable directory truncation, no lost draft/facets/reply.
- Real-browser gates: scroll/prepend/eviction preserves stable post + offset within 2 CSS px
  after layout settles; anchor deletion has defined fallback; 100 route round trips do not
  grow live transports/listeners/windows; repeated long traversal reaches a memory plateau.
  Test delayed media/fonts, changed display names, panel resizing, mobile keyboard, zoom,
  keyboard focus/text selection, and reduced motion. DOM-stub tests are insufficient.
- Calibrate latency/frame targets against recorded test hardware after baseline; publish
  concrete values before optimized comparisons. An API returning 20 rows with a full-scan
  count fails the bounded-window query goal even when a tiny fixture appears fast.

## Codex delegation and independent red team

The coordinator owns this epic, contract decisions, shared build files, integration,
framework contribution packaging, experiment resource limits and final verification.
Workers receive bounded file ownership and do not independently deploy/reset the live site.

Initial independent work assignments:

1. **Scale/API worker:** reproduce current SQLite behavior through real Koan/application
   queries in an isolated harness; deliver raw sanitized evidence and reproducible commands.
2. **Browser worker:** specify and test bounded window/store ownership and frontend spike
   plan against the current surface; implement only within an assigned new-file boundary
   until shared contracts are integrated.
3. **Red-team worker:** independently attack this plan and subsequent implementations.
   Do not implement the same slice being reviewed. Findings name severity, evidence,
   reproducer, impact and a concrete acceptance test. The coordinator resolves or records
   each finding; a passing worker self-review is not independent approval.

Red-team priorities: accidental live targeting/resource exhaustion; unbounded secondary
caches and framework residual/count work; gaps/duplicates under concurrent writes; cursor
scope/expiry/revocation; draft/facet identity leaks; focus/selection/anchor destruction;
event storms and reconnect loops; unfair provider comparisons and misleading load metrics.

## Completion and evidence

Track slice state here and observed results in `docs/CURRENT_STATE.md`. Place small
reproducible reports under `docs/evidence/epic005/`; large generated fixtures and raw runtime
data belong in ignored experiment directories. A benchmark result names its exact scope
(query, API, browser, local writes, or source integration) and never substitutes one for another.

Completion requires the full route/window experience, complete reachable directories,
real-browser evidence, a provider decision supported by correctness and scale measurements,
and independent red-team closure. No database switch is assumed. SQLite may remain the
simple default and another provider may be justified for larger hosted workloads; the data
must establish that recommendation.

Deferred unless separately justified: distributed services, sharding, search clusters,
production migration tooling, cloud provisioning and paid infrastructure. The earlier local
agent-discovery idea is a separate follow-on: loopback presence alone does not establish an
active MCP listener, and cross-origin browser access needs an explicit safe contract. Keep
the project/guide link usable while that contract is unresolved.

### Progress

- [x] Initial code and transport audit completed (read-only).
- [x] Owner directions and proposed execution gates recorded.
- [x] P0 independent red-team review incorporated; safe isolated foundations admitted.
- [ ] P1 baseline measured.
  - [x] Current SQLite 10k/100k Message.Query trace/plan baseline measured and independently reproduced.
  - [x] Deferred-save partial persistence reproduced with forced second-write failure.
  - [ ] Full API/enrichment, actual domain acceptance/crash, directory and browser baselines.
- [ ] P2 contracts and bounds verified.
  - [x] Pure bounded browser-window model implemented with worker and independent adversarial tests.
  - [x] Reusable participant SSE/poll transport integrated in current browser source, with actor binding, accepted-only checkpoints and dependent-refresh cancellation tests.
  - [x] Scoped author-label native query/index correction verified on fresh SQLite fixtures; input/row/task admission bounded. Existing database rollout and other enrichment scans remain open.
  - [ ] Wire/API integration, count/index/enrichment corrections and commit/recovery proof.
- [ ] P3 persistent conversation slice verified.
- [ ] P4 remaining adaptive surfaces verified.
- [ ] P5 provider comparison and saturation evidence complete.
- [ ] P6 final independent red-team closure complete.

### Plan review dispositions

Independent review: [red-team-plan.md](../evidence/epic005/red-team-plan.md).
Incorporating a plan repair does not close the corresponding implementation gate.

| Finding | Plan disposition | Remaining evidence gate |
| --- | --- | --- |
| RT-P01: unproved cross-entity atomicity | Corrected presumed guarantee; fault-injection baseline and commit/recovery repair added | P2/P5 remain blocked on actual domain-path proof |
| RT-P02: unbounded enrichment | Full response/mention/identity cardinality explicitly added | P2 bounded-API proof; micro-query timing is insufficient |
| RT-P03: directory mutation semantics | Immutable listing boundary/current-policy/reset contract specified | P2 concurrent traversal tests |
| RT-P04: process-local coordination | Single-process scope explicit | No multi-instance recommendation from these benchmarks |
| RT-P05: focus/selection versus eviction | Required P3 design decision retained; no unbounded pinning exception | Real-browser focus, copy, oversized-post and heap tests |
| RT-P06: transport cutover races | Required P3 adversarial transport tests retained | Snapshot/connect, SSE/poll, hidden-tab and identity-race proof |

Initial worker outputs are new-file, non-deployed probes: `probes/ScaleProbe/` and
`src/client/core/window-store.mjs` with its contract/tests. See the
[SQLite baseline](../evidence/epic005/sqlite-baseline-20260912.md) and
[independent foundation review](../evidence/epic005/red-team-foundations.md).
Neither is a claim that the current SPA/API is complete. The baseline's full scans and
reproduced deferred partial persistence justify the P2 query/commit gates before provider
selection; swapping databases alone is not a demonstrated repair.

[Coordinator verification](../evidence/epic005/coordinator-verification.md) records focused
test results and an existing WebMCP fixture/implementation mismatch. Reconcile the latter
before relevant surface integration; no all-suite pass is claimed by the foundation checks.

Owner-directed framework escalation: [Koan failure handoff](../handoff/KOAN_FAILURES_2026-09-12.md)
was sent to Koan's existing **Report framework status** task. Koan owns framework triage;
Tangent retains schema/API/browser repairs and adoption proofs. Public atomicity guidance
is a confirmed Koan documentation defect; deferred-only execution/Mongo limitations and
Tangent's undeclared indexes are not silently reclassified as framework implementation bugs.

Leo subsequently authorized scoped implementation assignments to the Koan agent and actual
MongoDB/other-adapter health experiments. Koan confirmed K01/K02 against current HEAD;
fixes and regressions are assigned there, with consumer adoption still separately verified.
The new `probes/ProviderLab/` runs digest-pinned MongoDB/PostgreSQL on dedicated loopback
ports with synthetic GUID namespaces, capped servers and serial probe allocation. This
extends P1/P5 evidence collection; it does not close the domain integrity or saturation
gates or authorize an unproved live-provider switch.

[Provider assessment](../evidence/epic005/provider-assessment-20260912.md): MongoDB and
PostgreSQL actual Entity CRUD/21-row windows at10k/100k passed, including independent
reruns. Indexed count-free reads reduce native work; implicit exact counts remain on the
unchanged pin. PostgreSQL proves same-Entity native atomic rollback; Mongo correctly
rejects that capability. Both reproduce ambient deferred partial persistence. This is
partial P5 adapter-health evidence, **not** matched saturation, complete domain acceptance,
or a selected provider. Public batch capability observability Q03 was sent to Koan for triage.

Koan later confirmed Q03 and returned implementation closeout for K01/K02/Q03, with
framework regression suites passing (agent-reported; see the handoff). The coordinator
inspected the changed source/work card. No native cross-Entity transaction was introduced.
Tangent now consumes current Koan `main` plus its reconciled auth/static contribution;
Q-05's empty-scope correction is verified in the deployed Topic path. Re-run the bounded
provider/query probes against the adopted K01/K02/Q03/Q04 source before closing the P2
repair gate.
