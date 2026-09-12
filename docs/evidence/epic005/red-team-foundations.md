# EPIC-005 independent foundation review

Date: 2026-09-12. Scope: the new, non-deployed ScaleProbe and bounded-window model.
This report does not approve SPA behavior, domain atomicity, full-API scalability, database
selection, or a saturated workload. This foundation review is complete for the versions
tested below; the epic's later implementation gates remain open.

## Plan follow-up

The revised EPIC-005 explicitly records RT-P01 through RT-P04: cross-entity commit proof is
required rather than presumed; enrichment is part of the bounded-response gate; directory
traversal uses an immutable boundary and current-policy/reset semantics; and initial
application benchmarks are single-process. These are accepted plan dispositions. The
corresponding implementation proof remains open, as the epic states.

## ScaleProbe safety checks

The probe clears inherited application configuration, provides its own SQLite connection,
does not start the built host, loads no user credentials, and targets a newly generated
experiment directory. Existing targets, unexpected target names, relative inputs and
reparse-point ancestors are rejected before experiment creation. No teardown/delete path
exists. These guards were inspected in `probes/ScaleProbe/Program.cs` and `run.ps1`.

Independent executable negative tests used the already-built probe's initialization
guard only. The three roots below all produced `Refusing an existing or non-isolated
experiment root` and a nonzero exit before host setup or database creation:

| Requested root | Result |
| --- | --- |
| `E:\repo\github\sylin-org\tangent-space` | Refused |
| `E:\repo\github\sylin-org\tangent-space\.local\docker\site` | Refused |
| `relative-root` | Refused |

No concurrent benchmark was launched by the reviewer. The run script checks free memory,
and the probe restricts CPU affinity, checks memory/data budgets between bounded steps and
uses cancellation. These are guardrails for this small serial probe, not proof of a hard
resource sandbox for future saturation runs. Build and load-process limits must remain
separate in later harnesses.

## Foundation findings and retests

### RT-F01 — Medium: p50 sample selection does not match a stated percentile convention

Initial `Program.cs` takes six measured warm samples, sorts them, and uses
`times[times.Count / 2]` as p50. That selects the fourth of six observations; nearest-rank
p50 is the third, and the conventional median averages the third and fourth. For
`[1, 2, 3, 4, 5, 6]`, the initial implementation reports 4.

Repair requested from the worker: use and disclose a consistent percentile convention,
with a deterministic sample check. This is a report-accuracy issue, not a claim that six
serial samples estimate production latency. The existing explicit rejection of load-test
percentile claims is appropriate. **Status: closed for this probe.** The worker added
`ProbeChecks.NearestRank` and deterministic six-value checks; the reviewer independently
ran the resulting `--self-test` and a complete fresh probe. p50 now uses nearest rank.

### RT-F02 — Medium: query result correctness checks inspect only the first sequence

Initial validation checks count 21, Topic identity and the first sequence. A response with
the correct first row plus duplicate, unordered or incorrect remaining rows would pass.
This is too weak for evidence accompanying a cursor/window architecture decision.

Repair requested: compare the entire ascending/descending expected sequence and IDs for
uniqueness. Include a deterministic negative case with a correct first row and malformed
remaining rows. **Status: closed for this probe.** `ProbeChecks.ValidateWindow` now checks
each expected sequence, identity and scope plus distinct IDs. The independent executable
self-test passed ascending/descending windows and rejection of a corrupt final sequence.

## Independent baseline rerun

The reviewer ran `./probes/ScaleProbe/run.ps1` after the worker's run finished, with no
concurrent benchmark. Release build and self-tests passed. The new generated root was
`.local/experiments/epic005/sqlite-baseline-edf63fb240394a96bd523178df20ee34`.
The probe took 13.7 seconds, peaked at about 146 MiB process working set, and the 100k
fixture database occupied about 85 MiB. Raw experiment state is retained, not deleted.

The compact independent artifact is [sqlite-red-team-20260912.json](sqlite-red-team-20260912.json).
It preserves source hashes, generated schema, SQL, native statement counters, plans,
settings and sample statistics. No native trace errors were reported.

| Query, 21 returned records | 10k posts p50 ms | 100k posts p50 ms |
| --- | ---: | ---: |
| Beginning | 40.20 | 358.34 |
| Middle, newer | 37.19 | 328.93 |
| Tail, newer | 34.04 | 300.43 |
| Middle, older | 42.79 | 375.18 |

Each 100k case executed a selected-row query **and** an exact-count query. Both reported
99,999 full-scan steps; selected-row queries also used a temporary ordering B-tree. The
generated Message table had only its Id primary-key index. The bounded output does not
make the current query bounded in database work.

The separate post-timing failure experiment also reproduced partial persistence: three
`Message.Save` calls were queued in `EntityContext.Transaction`, a SQLite trigger rejected
the second write, and a newly opened connection found only `epic005-tx-first` persisted.
This upgrades RT-P01's concern about the deferred primitive from static inspection to a
measured failure. It is not yet a full domain-acceptance, receipt-retry or process-crash
test, and does not establish what every application operation does after such failure.

## Independent bounded-window model tests

Added `tests/window-red-team.test.mjs`, owned separately from the implementation and its
worker tests. `node --test tests/window-red-team.test.mjs` passes **11/11** independent
cases against `src/client/core/window-store.mjs`:

- stale tickets after session change, leave/reopen and Topic eviction; forged ticket copy;
- invalid/oversized response rejection preserves the valid page, cursors and anchor;
- nested caller mutation and snapshot mutation cannot alter retained records;
- conflicting identities, sequences and equal revisions reject atomically, and lower
  revisions cannot overwrite retained higher revisions;
- same-lane supersession, replacement generations and moved-boundary ticket rejection;
- anchor-pinned over-budget growth leaves the valid continuation intact;
- 500 progressing empty pages retain one page; a stalled continuation is rejected;
- fetching pages neither acknowledges reading nor conflates historical/live checkpoints;
- 500 Topic switches preserve aggregate record/page/content bounds and no pending tickets.

No additional implementation defect was found in those cases. One initial failing test
was corrected because the test helper's default argument converted the intended invalid
`undefined` cursor into valid `null`; it was not a module defect. The corrected test passes.

This approves only the exercised pure-store invariants as a foundation. Transport response
bytes before JSON decoding, source freshness/revision contracts, auth revocation, DOM
windowing, focus/selection, heap retention by consuming UI, and actual scroll anchoring
still need their own implementation and real-browser proof.

## Final scope judgment

The probe correctly calls itself a `Message.Query`/Koan/SQLite read micro-query, explicitly
excluding full API, domain writes, SSE, transactions and browser proof. Direct synthetic
bulk seeding is identified as such. No count/index tuning has been silently introduced.
The SQL trace, provider settings and baseline output have now been independently reviewed
and reproduced. The baseline exposes genuine work/atomicity limitations rather than
validating readiness for large datasets. Continue the epic under its P2/P3 gates; do not
mark P1's full-API/transaction/browser baseline or the complete epic finished from these
query/store foundations alone.
