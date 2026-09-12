# EPIC-005 planning/foundation verification — 12 September 2026

Scope: reviewed execution plan, isolated SQLite query/failure probe and pure bounded
browser-window model. No live app deployment, SPA conversion, provider switch or data wipe.

## Checks executed by the coordinator

```powershell
node --test --test-timeout=15000 tests/window-contract.test.mjs tests/window-red-team.test.mjs tests/room-recovery.test.mjs tests/agent-connection.test.mjs
```

Result: **63 passed** (20 worker model tests, 11 independent adversarial tests, 20 existing
room-recovery tests and 12 existing agent-connection tests). Tests cover generations,
overlap/order, immutable snapshots, byte/page/aggregate budgets, empty continuations,
anchor pressure, separate checkpoints and current identity/draft/pending safeguards.
They do not establish real-browser scroll geometry, focus, heap or transport integration.

The coordinator inspected the published query trace/schema/failure evidence. The worker
and red team each executed the isolated ScaleProbe sequentially. See
[baseline](sqlite-baseline-20260912.md) and [independent review](red-team-foundations.md).
The reproduced full scans and deferred partial persistence are limitations of the current
paths, not fixes delivered by this foundation work.

## Existing-suite mismatch retained as a baseline issue

```powershell
node --test --test-timeout=15000 tests/webmcp.test.mjs
```

Result: **4 passed, 10 failed, 4 cancelled by individual 15-second timeouts**. The first
assertion expects 10 tools while the current implementation registers 16; other failures
include old identity/input/dispatch expectations and waits that never enter the expected
transport. Neither this existing test file nor `wwwroot/webmcp.js` was changed in this turn.
An earlier unbounded combined invocation was stopped after identifying its exact Node
parent/child processes; the bounded standalone rerun above captured the actual result.

Do not advertise an all-suite pass. Reconcile these fixtures with the intended current
WebMCP contract before the relevant P3/P4 integration gate, retaining cancellation,
credential isolation, safe-write and untrusted-content coverage rather than merely
changing assertions to pass. This mismatch does not invalidate the separate 63 focused
checks or the query-only probe, but remains an open baseline issue.

## Handoff boundaries

- Epic plan-level red-team dispositions are incorporated. Foundation-level measurement
  and result-validation findings were fixed and independently retested.
- Full API/enrichment, directory, acceptance/crash-recovery, million-post, concurrent
  saturation, PostgreSQL/MongoDB and actual browser proofs remain open epic gates.
- Existing uncommitted source changes were preserved. New code is confined to the isolated
  probe, standalone window model and tests; documentation/evidence records the work.
- Generated synthetic databases remain under ignored `.local/experiments/epic005/` paths.
  No deletion occurred. The deployed application and unrelated containers were not restarted.
