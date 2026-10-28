# Safety properties held by the deleted red-team suites

R1.15 deleted the browser's red-team suites because they were bound to window and activity implementations that R3.5, R5.1 and R5.2 replace — a test coupled to a doomed API gets patched green during a rewrite rather than thought about. The **properties** below were the asset; the coupling was the liability.

This file is the checklist. **R5.1 and R5.2 are not done until every property here has a test against the single remaining implementation**, or until this file records, with a reason, why the property no longer applies. Deleting a line without one of those two outcomes is a silent loss of a safety guarantee, which is exactly what this file exists to prevent.

Recovering the original assertions: `git show 88f470f -- tests/window-red-team.test.mjs tests/activity-red-team.test.mjs tests/provider-lab-red-team.test.mjs`, or the `archive/pre-epic-007` tag.

## Topic window — object capability and memory bounds

| # | Property | Restored |
|---|---|---|
| W1 | Tickets cannot cross session, leave/reopen, or evicted Topic generations | |
| W2 | Forged, structurally equal tickets do not bypass object-capability identity | |
| W3 | Invalid and over-budget replacements preserve content, cursor and anchor | |
| W4 | Retained records and snapshots both resist caller mutation | |
| W5 | Conflicting identity/revision pages reject atomically and cannot downgrade | |
| W6 | The latest lane wins, and replacing a window invalidates adjacent-lane work | |
| W7 | A boundary-page replacement rejects a ticket tied to the previous page | |
| W8 | Refusing an anchor-pinned growth preserves the original continuation | |
| W9 | Empty progressing pages do not accumulate, and stalled cursors reject | |
| W10 | Live and read checkpoints stay independent of fetched windows | |
| W11 | Aggregate memory keeps records and pages bounded across repeated Topic churn | |

## Activity transport — checkpoint integrity and actor isolation

| # | Property | Restored |
|---|---|---|
| A1 | A quiet long poll may take 15 s without hitting the SSE connect timeout | |
| A2 | A malformed frame or forged SSE id cannot poison the fallback cursor | |
| A3 | CRLF splits at chunk boundaries, multiline data and unknown events preserve checkpoint rules | |
| A4 | A 403 fallback is terminal; late requests cannot revive the old actor | |
| A5 | Late bootstrap completion cannot mutate a replacement generation | |
| A6 | Rejected and timed-out consumers cannot commit, or later claim a live context | |
| A7 | A non-progressing `HasMore` is rejected without losing the last valid checkpoint | |
| A8 | Oversized JSON headers are refused before decoding or committing a checkpoint | |
| A9 | A hidden page aborts its reader and preserves a verified checkpoint for resume | |
| A10 | Keepalives cannot prevent absolute SSE rotation and new-cookie identity validation | |
| A11 | A fallback re-probe cancels a pending wait and opens exactly one stream at its current cursor | |
| A12 | A malformed channel row or broken continuation cannot advance an otherwise valid snapshot | |

## Activity integration — stale responses never overwrite live state

| # | Property | Restored |
|---|---|---|
| I1 | A readable muted Topic survives an activity overview that omits it | |
| I2 | An actor mismatch quarantines Topic state before welcome finishes | |
| I3 | A failed edit invalidation must not acknowledge a cursor over stale history | |
| I4 | An expired same-actor delivery cannot render its delayed history response | |
| I5 | An expired directory callback cannot replace the visible Tangent list | |
| I6 | A reset without replayable events revalidates the directory before committing | |
| I7 | A missing Tangent route does not block other participants' activity on reset | |

## Provider lab

| # | Property | Restored |
|---|---|---|
| P1 | The provider guard accepts a fully matching synthetic lab | |
