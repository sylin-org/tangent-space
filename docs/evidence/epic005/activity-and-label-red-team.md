# EPIC-005 activity and label slice: independent red team

Date: 2026-09-12. Scope: the participant activity browser controller, its room-page
integration, and scoped participant label resolution. No deployment, provider switch,
public-network test, or framework modification is part of this review.

Status: **complete for this bounded slice**. Final independent verification passes
**64/64 browser tests** (24 red-team, 20 transport-worker, 20 room integration) and
**9/9 native SQLite label tests**. The findings below were repaired or explicitly
bounded; no remaining blocker was found within the reviewed slice. This is not approval
of a production deployment, complete SPA migration, database switch, or saturation claim.

## Contract checks

- `/api/activity`, `/wait`, and `/events` authenticate the participant and recheck
  delivery-time access. A 401/403 must terminate the current browser identity generation;
  retrying while leaving stale private content visible is not a recovery strategy.
- `Checkpoint` is the durable reconnect position. When `HasMore` is true, `NextCursor`
  equals that checkpoint and retains a frozen journal boundary. Channel pagination is a
  separate contract; an incomplete channel overview is not evidence of revoked access.
- Initial snapshot, stream, and fallback share one committed checkpoint. Parse/validate
  before advancement; a malformed frame, unknown event, incomplete frame, callback
  failure, or old-generation completion must not poison it.
- One active generation owns stream readers, wait requests, timers, and status callbacks.
  Hiding/stopping/replacing the identity aborts that generation. Its delayed `finally`
  cannot clear a successor, and polling must eventually reprobe streaming without a
  duplicate wait loop.
- Label enrichment must scope native reads to the requested IDs, avoid handle lookup
  when `resolveMissing=false`, preserve label priority/absence semantics, and remain
  display decoration rather than new authorization evidence.

## Findings and boundaries

### AR-01 — Existing session replacement notification can be missed (P1, mitigated; residual protocol debt)

The original `src/server/web/Activity/ActivityController.cs` obtained and wrote the initial snapshot
before registering its session in `LiveSessions`. `LiveSessions.Replaced` removes the
currently registered waiters and drops unknown session replacements. Therefore a sign-in
between the initial snapshot and registration can deliver no `identity_changed` event.
The existing streaming request retains its original cookie principal; activity access
checks validate participant/credential state, not whether that browser session was
replaced. `ParticipantSignIn` also explicitly treats this push as best effort.

A later fallback request uses the browser's current cookie, but the original `ActivitySnapshot`
contained no actor/session identity. A cross-participant cursor is reset, which by itself
does not tell the old UI who it now represents. Browser transport generation guards
cannot alone close this server protocol gap. This is Tangent application/session debt,
not a demonstrated Koan defect. Sent to root and transport worker before implementation
review. Root added authenticated `ParticipantRef` to HTTP/SSE activity snapshots,
registered replacement notification before initial asynchronous work, and added a
60-second absolute browser stream rotation. Independent tests reject foreign payloads
before callback/cursor mutation at bootstrap, SSE, and polling; keepalives cannot extend
the absolute rotation. This bounds discovery once a fresh request can complete; it is not
revocation of old cookies or proof of cross-process session replacement notification.

### AR-02 — Activity overview absence is not an access decision (P1, repaired and independently tested)

The old `rooms.js` revokes a selected readable room when a complete overview omits it.
However, `ActivityService.Channel` deliberately omits readable channels whose effective
watch mode is `None`; `AttentionRules.DeliversChannel` documents that behavior. Thus a
complete snapshot can falsely hide a readable muted topic. Activity is a filtered
attention feed, not a full access-control directory. Revalidate authoritative room
detail/policy before clearing access, or use an explicit authorization denial. Root
replaced this inference with a guarded authoritative topic-detail check. The independent
VM test executes both real scripts and verifies the readable muted topic remains open,
its policy is rechecked, and no legacy parallel room wait starts.

### AR-03 — Long-poll header timeout was shorter than the server's idle wait (P1, repaired and independently tested)

The initial controller used the 10-second stream connect timeout before headers for every
request. `ActivityService.Wait` intentionally returns an idle snapshot after 15 seconds,
so a quiet fallback would always be aborted early. The worker changed polling to a
45-second total header/body budget, clipped to the stream re-probe deadline. Independent
fake-clock tests verify a normal 15-second response, periodic re-probe, cancellation of
the pending wait, and exactly one live successor request.

### AR-04 — Swallowed invalidation errors acknowledge stale history (P1, repaired and independently tested)

`renderActivity` initially awaited the generic UI `action` helper for history refreshes.
That helper catches errors and resolves successfully, so a 503 while refreshing an edit
still commits its activity checkpoint. The only invalidation is then no longer replayed.
An independent VM test confirms `edit-c1` was committed after the history endpoint failed
instead of retaining accepted checkpoint `c0`. Sent to root with the executable test.
Root replaced the swallowed refresh with a direct awaited operation, propagated failure,
and requires explicit successful refresh before acceptance. The regression now retains
`c0` on the injected 503, keeping the invalidation replayable.

Related review request: activity-triggered asynchronous view refreshes must check the
delivery context, not only route/identity epochs, before mutation. The transport now
supplies request-linked cancellation, and history helpers check it immediately before
mutation. An independent test hides the page during an outstanding history request, then
delivers the response despite cancellation: no expired message reaches the DOM, the
dependent fetch signal is aborted, and checkpoint `c0` remains intact.

### AR-05 — Directory/settings invalidations need delivery guards (P1, repaired and independently tested)

Directory/settings invalidation still needs the same treatment: the reviewed intermediate
`refreshTangents().catch` and `refreshServer` swallowed failures, and their own mutations
checked identity rather than the expiring delivery context. This analogous gap was sent
to root separately. Three additional independent tests first reproduced settings failure
advancing the cursor and a delayed directory response replacing Home after hiding the
page. Root added optional delivery context and strict activity error propagation through
`refreshServer`, `refreshTangents`, and `refreshRooms`; all three regressions now pass.

### AR-06 — Reset must refresh governance without replay events (P1, repaired and independently tested)

At root's request, the reviewer checked reset semantics: an invalid/expired cursor can
bootstrap at current head with no events, losing the old governance markers. The initial
integration refreshed history/server on reset but refreshed directory only for a matching
event. A new independent test reproduced the missing directory fetch. Root made reset
force a directory refresh too; injected directory 503 now keeps the prior checkpoint.

### AR-07 — An unavailable route must not block global reset (P2, repaired and independently tested)

Also identified by root and independently reproduced: refreshing global activity on a
known missing Tangent route blindly retried that route's detail request, repeatedly
failing 404 and preventing a valid global reset from committing. The focused test expects
the global directory to refresh without re-entering the unavailable route; room content
must remain hidden. Root now skips route-specific refresh while `routeFailure` is set.
The independent rerun accepts the global reset, leaves the failed route hidden, and
confirms that its missing detail endpoint was requested only once.

### Remaining scale boundaries

Scoping `ParticipantDirectory.LabelsFor` does not remove the independent label scans in
`ExperienceDigest` and `ParticipantLookup`. An index declaration alone also does not
prove a previously created database now contains the index or that its plan uses it.

## Independent tests

Final command, independently executed after the last room-route repair:

```powershell
node --test tests/activity-red-team.test.mjs tests/activity-transport.test.mjs tests/room-recovery.test.mjs
```

Result: **64/64 passed**, including all **24 independently authored red-team tests**.
Tests use mocked fetch/readable streams, a deterministic fake clock, and VM execution of
both production browser scripts: no live accounts, credentials, PDS calls, public network,
or production data. Initial failures and their reproductions are retained in the finding
descriptions above; intermediate run totals are not added to the final unique test count.

Controller cases cover actor mismatch at all three response boundaries, terminal 403,
late bootstrap/finally after identity replacement, callback timeout with invalidated
context, malformed JSON/rows/cursors, forged SSE ID, CRLF split across chunks, multiline
data, unknown events, oversized JSON headers, hidden resume, absolute stream rotation,
fallback re-probe, and non-progressing continuation. Integration cases cover muted-topic
access, immediate old-world quarantine, failed edit/directory/settings invalidation,
same-actor late delivery, reset without events, and unavailable-route isolation. Two old
room-recovery fixtures initially loaded `rooms.js` alone and used the previous contract;
root migrated them to load the real transport and native-shaped responses. Their final
20/20 pass is included in the combined count above.

Label implementation and all nine final worker tests have been source-reviewed. The SQLite
fixture builds but never starts a host; it uses a fresh GUID database, explicit closed
configuration, fake handle resolution, and actual native SQL/row/index-plan observation.
Its source checks scoped IDs through multiple 64-ID chunks and 128-row pages, 5,000 noise
identities, a winner beyond the first page, no COUNT/full scans, precedence/casing/blank
behavior, bounded input/row overflow, and cancellation. A first independent run passed
8/8. The worker then replaced serial handle resolution with fixed groups of at most six,
avoiding multiplied per-handle latency without input-wide task fan-out. The final
independent command below passed **9/9** in 6.22 seconds on .NET 10.0.12, including an
80-author concurrency barrier proving exactly six and cancellation before the next group:

```powershell
dotnet test tests/TangentSpace.Tests/TangentSpace.Tests.csproj --no-build --no-restore --filter FullyQualifiedName~ParticipantLabelWindowTests --logger "console;verbosity=normal"
```

The native scoped-read assertion actually executed: exactly 430 distinct requested
identity rows across five bounded pages, zero COUNT statements and full-scan steps,
actual compound index plus SEARCH plans. This is an isolated SQLite correctness/pushdown
check, not a measurement of production throughput or other providers. Existing-schema
index migration, arbitrary preexisting label byte sizes, and snapshot consistency during
concurrent identity changes were not established by these tests.

### Compiler fallback found during worker verification

The worker's initial array `.Contains` predicate lowered under the current .NET compiler
to `System.MemoryExtensions.Contains` (span), which the pinned Koan LINQ compiler did not
translate. Its CLR residual caused native scope/pushdown tests to fail. The final Tangent
implementation uses explicit `Filter.In` and the independent native-read rerun above
passes. Root was notified for the Koan-agent handoff; this report does not claim a
framework fix or changed consumer pin. Unlike the former unscoped application query,
this is a framework translation/performance pitfall requiring precise upstream triage.

Pinned source corroboration: `.local/upstream/koan-framework/src/Koan.Data.Abstractions/Filtering/LinqFilterCompiler.cs`
lines 101–107 recognize instance or `Enumerable` `Contains` but not static
`MemoryExtensions`; line 153 supplies the CLR fallback. No false output or data leak was
shown by this issue: the failure is lost native scope and unexpectedly unbounded work.

## Explicit verification limits

- No real-browser visual/accessibility, scrolling/focus, proxy, mobile suspension, or
  actual multi-tab sign-in test was run by this reviewer. DOM VM tests verify application
  transitions, not rendering quality or native browser timing.
- The server's new per-wait linked cancellation and late-fault observation were
  source-reviewed. Root reports a subsequent .NET rebuild and focused test run; this
  reviewer did not induce a live socket/sign-in race or prove provider cancellation.
  Identity is flushed before cleanup, which does not await an uncooperative loser.
- Session replacement remains process-local/best effort. Rotation and actor-stamped
  responses prevent foreign payload application and bound fresh-request opportunities;
  they are not cryptographic revocation of an old cookie or distributed-session proof.
- Topic/history DOM and directory browsing are not yet a production integration of the
  separately tested bounded window store. Multi-tab memory, long-session heap bounds,
  whole-response byte budgets, scale saturation, and cross-entity integrity remain epic
  gates. The isolated label tests do not establish those properties.
