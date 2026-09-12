# EPIC-005: activity recovery and scoped label enrichment

Date: 2026-09-12. Implemented in the existing dirty Tangent workspace; unrelated edits
preserved. Koan consumer pin remains `e07a84cc3f71a0867f1122b03b723cc80727e772`.
Initial implementation verification made no deployed-site update, DB switch/reset, external
PDS calls or scale container restart. Subsequent user-requested local deployment is recorded below.

## Delivered behavior

- `activity-transport.js` is a reusable, DOM-independent classic script loaded before
  `rooms.js`. The latter owns a single participant controller, replacing its old SSE-only
  activity loop and removing the simultaneous selected-Topic updates loop.
- Snapshot, SSE and participant `/wait` share one accepted checkpoint. Header SSE IDs do
  not advance it. Malformed, foreign-actor, rejected, failed or timed-out deliveries cannot
  commit it. Every browser endpoint stamps the authenticated participant into its snapshot;
  this is response context, never caller-supplied authority.
- A delivery callback awaits dependent history/directory/settings reads. Its request signal
  aborts on hidden page, identity change or timeout; post-await guards prevent late DOM
  replacement even if a dependency ignores cancellation. Failed invalidations remain
  replayable; reset snapshots refresh directory state without relying on discarded events.
- Activity-channel absence is not an ACL result: a selected muted Topic is checked through
  its authoritative detail endpoint before access is removed. The global transport remains
  independent of selected-route availability. Explicit read acknowledgments and pending
  message retries are not automated by delivery or reconnect.
- 401/403 and actor mismatch terminate the transport and quarantine the old room state
  while welcome reloads. Repeated automatic identity recovery is bounded. Server replacement
  notification registers before initial async work; browser streams rotate through a fresh
  cookie-authenticated snapshot even when keepalives continue.
- The server cancels the losing participant wait when an identity notification wins and
  observes late faults; notification is flushed first. Cancellation remains cooperative,
  not guaranteed termination of a provider that ignores its token.
- Chrome navigation timing isolated the reported 57.7-second Topic load to browser-side
  queuing: `fetchStart` was 6.6 ms, `requestStart` 57,707 ms, `responseStart` 57,715 ms and
  transfer completion 57,716.6 ms over HTTP/1.1. A fresh process fetched the same document
  in 32 ms, while the affected browser had five established loopback connections and old
  tabs held activity/profile streams. This was connection-slot starvation, not payload,
  rendering, SQLite or server execution time.
- Supporting browsers now share one origin-wide activity transport through a classic
  `SharedWorker`. Profile decoration is multiplexed as named events on that stream. Each
  visible tab explicitly accepts a snapshot before its checkpoint commits; hidden tabs are
  excluded, all-hidden pauses the connection, and a returning tab receives a bounded forced
  reset rather than an unbounded backlog. Heartbeats prune abandoned ports. A newly active
  participant wins an identity generation; stale heartbeats cannot seize it back.
- Browsers without `SharedWorker`, or where worker startup fails, retain the direct per-tab
  transport. Both its activity and compatibility profile streams close while hidden. HTTP/2
  remains the production transport recommendation because fallback browsers cannot provide
  a one-connection-across-tabs guarantee over HTTP/1.1.

## Transport budgets

| Resource | Default bound |
| --- | --- |
| Active participant activity requests | One per browser origin; per-tab fallback when SharedWorker is unavailable |
| Shared ports / profile identities | 64 / 64 |
| Port heartbeat / stale-port expiry | 10 / 45 seconds |
| Snapshot JSON / SSE frame | 512 KiB each |
| Events / channels per snapshot | 25 / 100 |
| Opaque cursor length | 4,096 characters |
| Bootstrap/SSE response headers | 10 seconds |
| Wait total / stalled stream | 45 seconds (server may hold wait headers for 15 seconds) |
| Accepted-snapshot callback | 15 seconds |
| Absolute SSE lifetime / fallback reprobe | 60 seconds each |
| Retry | 1-second exponential base with jitter, capped at 30 seconds |
| Fast quiet wait pacing | At least 250 ms |

Streams are UTF-8/CRLF/multiline-safe and byte-counted before JSON parsing. A legacy fetch
implementation without readable bodies may buffer `text()` internally: headers and parsed
payload are bounded, but browser-internal download allocation is not proved there. Browser
heap is not inferred from these encoded-content limits. The old profile-decoration endpoint
remains available only for compatibility fallback; it is not opened by the shared path.

## Scoped author labels

`ParticipantDirectory.LabelsFor` admits at most 4,096 raw input items, participant IDs up to
128 characters, queries 64 requested IDs at a time, and consumes 128-row count-free pages
with an explicit Id keyset edge. More than 32,768 matching identities fails explicitly;
it does not silently return partial decoration. Only two deterministic candidates per
requested participant survive each page, and at most six handle lookups run concurrently.
`resolveMissing:false` makes no external handle requests. Existing Unicode/blank-label
semantics are preserved; labels do not become authority evidence.

Fresh SQLite fixtures prove native filtering and the declared `(ParticipantId, Id)` index:
130 requested owners, 300 aliases, 430 exact matching identities, 5,000 unrelated identities,
five SELECT pages (128/128/108/64/2), LIMIT 128 OFFSET 0, no COUNT, no full-scan steps, and
an EXPLAIN SEARCH plan. Oversized inputs/identity sets, late best candidates, cancellation,
six-wide resolution and cancellation between groups are tested.

The first test run correctly rejected an apparently scoped array-Contains expression:
current C# lowers it through `MemoryExtensions.Contains`, which the pinned Koan compiler
treats as a CLR residual. Explicit public `Filter.In` fixes this consumer path. Exact
upstream report and delivery are in [Koan handoff](../../handoff/KOAN_FAILURES_2026-09-12.md#q-04--array-contains-silently-becomes-a-full-scan-residual-p2).

## Verification and remaining gates

Coordinator final verification: **122/122 focused Node tests pass**, comprising 21
transport, five shared coordinator, 24 independent activity/red-team, 20 recovery, 20 window
contract, 11 window red-team, 12 agent-connection and nine provider-lab guard tests. All
four transport/coordinator/profile scripts pass `node --check`; `git diff --check` has no
whitespace errors (CRLF notices only).
Run the focused browser suites with Node's test runner (`activity-transport`,
`activity-red-team`, `room-recovery`, `window-contract`, `window-red-team`,
`agent-connection`, `provider-lab-red-team`). The recovery fixture executes both actual
served scripts, not a copied transport implementation. Independent adversarial results
are recorded in [red-team evidence](activity-and-label-red-team.md).

Worker and coordinator .NET verification: **20/20 focused tests pass** (nine label, ten existing activity and one
new actor-context JSON serialization). These build but do not start the host; fresh GUID
SQLite files contain synthetic identities. The serialization assertion is not a real HTTP
cookie/session-race test. The provider trace applies to fresh SQLite schemas, not a proved
index rollout on an existing database or all providers.
The coordinator rebuilt after the final server wait-cleanup change. Compiler nullable and
xUnit analyzer warnings remain elsewhere in the workspace; a warning-free build is not claimed.

```powershell
node --test --test-timeout=15000 tests/activity-transport.test.mjs tests/activity-coordinator.test.mjs tests/activity-red-team.test.mjs tests/room-recovery.test.mjs tests/window-contract.test.mjs tests/window-red-team.test.mjs tests/agent-connection.test.mjs tests/provider-lab-red-team.test.mjs
dotnet test tests/TangentSpace.Tests/TangentSpace.Tests.csproj --no-restore --filter "FullyQualifiedName~ParticipantLabelWindowTests|FullyQualifiedName~ActivityUpdatesTests|FullyQualifiedName~ActivityPagingTests|FullyQualifiedName~ActivityPrivacyTests|FullyQualifiedName~ActivitySnapshotContextTests" --logger "console;verbosity=minimal" --nologo -m:1
```

This slice does **not** complete the SPA. Existing navigation still replaces documents;
browser Topic/directory collections still accumulate, and history invalidation still
refetches the loaded history. The pure retained-window model awaits revision normalization,
wire integration and real measured-height viewport tests. While an edit is open, history
invalidation retains its cursor and retries rather than discarding authored work; this can
show degraded status until editing ends. Long refreshes can exceed the delivery budget:
the bounded-window migration must remove that cost, not raise timeouts indefinitely.

Remaining security/protocol debt includes best-effort/process-local session replacement
and revocation of old credentials; 60-second rotation bounds fresh-cookie discovery only
when a new request can complete. Other global identity/digest/mention-candidate scans,
label-byte limits and concurrent label snapshot consistency remain unqualified. No real
browser layout, OAuth journey, saturation, production atomicity or all-suite pass is claimed.

## Local deployment receipt — 2026-09-12

Leo explicitly requested local deployment for manual testing. Verified the pinned Koan
contributions, built the Linux image through `docker compose build tangent`, then used
the existing `docker-state.ps1 -Action Backup` and `start-docker.ps1` launch path. Only
`tangent-space-tangent-1` was replaced; other containers and connector processes were not
touched. The build includes the current preserved web source, not an isolated patch image.

- URL: `http://127.0.0.1:5220`; loopback binding remains `127.0.0.1:5220 -> 8080`.
- Image: `sha256:8664ecf66cc2804ad73a3c348f6b85e62a492ef44596603a95ba90debeec2577`.
- Backup: `.local/backups/docker-20260912-204443-843`, complete mounted state with manifest.
- Configuration SHA256 before/after is identical:
  `55BA911E7443F6148EC08D2919469B7E381A02DC5DA33EAD6198E3142528C325`.
- Container healthy, zero restarts; `/health/ready`, `/`, `/settings`, `/api/site` return 200.
  The existing `/tangents/` alias correctly redirects to `/#tangent-return`.
- Served `activity-transport.js` and `rooms.js` byte hashes equal current source; shell
  loads the new transport before rooms with the `20260912-fallback1` version.
- Anonymous `/api/activity`, `/api/activity/wait`, `/api/activity/events` all return 401.
- Read-only schema inspection of the existing database confirms
  `IX_TangentSpace.Participants.ParticipantIdentity_ParticipantId_Id`, indexing the
  ParticipantId JSON expression and Id. This proves index presence on this database,
  not production-scale query cost or a general migration guarantee.

Authenticated SSE/poll interaction, account switching and byline rendering remain manual
browser checks for Leo. Existing open tabs must reload to use the new scripts. No cookie
export, sign-in, model invocation, provider switch, source-network recreation or state wipe
was used for deployment smoke checks. This local deployment is not EPIC-005 release approval.

### Multi-tab transport redeployment

The connection-starvation correction supersedes the asset/image portion of the receipt
above. State was backed up to `.local/backups/docker-20260912-211824-170`; the configuration
hash remained `55BA911E7443F6148EC08D2919469B7E381A02DC5DA33EAD6198E3142528C325`.
The final loopback image is
`sha256:b74edff55dc72f6566aaed056039b6141582dbab82279aa085f1c83e0290d395`;
the container is healthy with zero restarts. The served cache-busted scripts and worker use
`20260912-shared2` so an already-running predecessor worker cannot survive a page reload.

Three fresh authenticated Chrome tabs all reported `shared:true`, `ready:true`, `mode:sse`
and `status:live` for the same participant and checkpoint. A single Topic navigation
completed in 149 ms; three simultaneous full-document navigations completed in 269 ms.
After the final cache-busted deploy, three simultaneous reloads completed in 391 ms, the
Topic rendered its three posts and returned to Live, and the inspected tab recorded no
legacy `/api/profile-cache/events` resource. Server logs recorded one new activity stream
for that three-tab reload. Earlier log entries for the compatibility profile endpoint came
from still-open pre-refresh browser surfaces and are not attributed to the final tabs.

These are local Chrome observations, not a production HTTP/2 or unsupported-browser proof.
The current navigation is still full-document replacement; the SharedWorker removes the
HTTP/1.1 slot failure without pretending EPIC-005's retained-shell SPA/windowing work is done.
