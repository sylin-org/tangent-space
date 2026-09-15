# EPIC-007 — Fewer, clearer parts: the server realignment

Accepted 15 September 2026 — Leo accepted every recommendation. **Status: in progress.** The [work ledger](epic-007/LEDGER.md) is the single source of what is in progress and what comes next.

Evidence: the [architecture assessment](../ASSESSMENT_2026-09-15.md). Decision record: [ADR 0011](../adr/0011-realigned-server-architecture.md). Modules, glossary and shared components: [ARCHITECTURE](../ARCHITECTURE.md). Product commitments remain those in [MANDATES](../MANDATES.md); this epic changes how the server is built, not what it promises.

## Outcome

The same product with one clear internal shape:

- one authenticated API for the browser and the connector, plus the public reader and identity endpoints;
- one command pipeline that owns locking, transactions, receipts, audit, journaling and live signals;
- one access evaluator that answers every "may this participant do this here, and why?";
- one history window, one receipt mechanism, one live-signal bus;
- code that uses the product's words and reads as if written today, with nothing deprecated left behind;
- reads that never wait on writes, and a fresh install that can enroll an agent without hand-edited configuration.

Estimated result: server C# shrinks from about 16,800 lines to about 11,000.

## Design rules

The seven rules in [ARCHITECTURE](../ARCHITECTURE.md#rules) govern every task. Two come directly from Leo and apply without exception: **cleanup of deprecated content is mandatory**, and **code must read greenfield**.

## Decisions

| ID | Decision | Accepted direction |
|---|---|---|
| D1 | Inbound MCP transport and browser WebMCP | Delete; move the live pieces out of `Mcp` under accurate names |
| D2 | Spaces storage | Remove from the server; the tag `archive/pre-epic-007` preserves it; a future source returns as a module behind a storage interface |
| D3 | ONNX change classification | Remove; keep edit history |
| D4 | Membership store | Koan roles only; removal becomes a restriction; invitations and join requests become admission records |
| D5 | Authenticated API | One route family with one response envelope for browser and connector |
| D6 | Concurrency | One write lock owned by the pipeline and never held by reads |
| D7 | Internal names | The product's words, per the [glossary](../ARCHITECTURE.md#glossary) |
| D8 | Node client in `clients/participant` | Delete; the connector is the unattended path |
| D9 | Enrollment proof audience on standalone installs | Default from the public origin, with an explicit override; confirmed against atproto service-auth rules during R1.5 |
| D10 | Working branch and commits | `claude/epic-007-realignment`; commit at task checkpoints; push only with Leo's authorization |

## Slices

### R0 — Decide and draw the map

- **Outcome:** accepted decisions, ADR 0011, ARCHITECTURE, the greenfield check, baseline measurements, and EPIC-006 reconciled.
- **Done when:** a fresh reader of README and AGENTS lands on this epic, and the ledger's baseline metrics are filled.

### R1 — Subtract

- **Outcome:** the same user journeys with about 5,000 fewer lines.
- **Scope:**
  - Remove the inbound MCP transport after moving its live pieces (references, receipts, bearer registration, the public-origin option, the invitation page, the Topic window) to accurate homes.
  - Delete browser WebMCP and `/api/participation/arrival`.
  - Create the home Tangent at claim time instead of in read paths.
  - Delete pre-multi-Tangent branches and the digest's non-facet mention parser.
  - Remove Spaces storage, after moving the proof audience and DID-key resolution into Identity; remove ONNX classification and the Node client.
  - Remove each capability's tests, scripts, configuration and documentation with it. Wipe the data.
- **Not in scope:** `/api/rooms` and `/api/tangents`. Their edit, remove, membership and role operations don't exist on `/api/v1` yet, so they are removed in R4.
- **Done when:** the walkthrough passes, the remaining suites are green, and the connector still connects, enrolls, reads and posts.

### R2 — Rename to the product's words

- **Outcome:** the code speaks Host, Tangent, Topic, Post and Participant.
- **Scope:** apply the [glossary](../ARCHITECTURE.md#glossary) to types, services and strings; split `CompanionGovernance` by responsibility; move folders into the [modules](../ARCHITECTURE.md#modules); rename wire fields such as `channels` in the server, browser and connector together. Wipe the data.
- **Done when:** the greenfield check reports no retired vocabulary in the server, and the suites are green.

### R3 — Build the shared components

- **Outcome:** Conversation runs on the command pipeline and the Access evaluator.
- **Scope:**
  - One operation-receipt mechanism, replacing `CommandCommit`, the `PostChange` ledger and ad-hoc operation IDs.
  - The command pipeline.
  - The Access evaluator, with table-driven tests.
  - The host-owned signal bus.
  - Port post, edit, remove, read position and the Topic window.
  - Simplify the post model: replies by Post ID; drop `SourceDecision` and source URI/CID.
- **Done when:** no `WithCurrentPolicy`, `PolicyGate` or service-level semaphore remains in Conversation, conversation reads take no lock, and one Access decision explains every conversation denial.

### R4 — Move every family onto the pipeline

- **Outcome:** no hand-written lock, transaction, audit or journal code outside the pipeline.
- **Scope:**
  - Stewardship.
  - Community: add the missing operations to `/api/v1`, repoint the browser, delete `/api/rooms`, `/api/tangents` and `ConversationController`.
  - Hosting, then Identity.
  - Delete `Room.CurrentPolicy`, the role-bag overwrite, `bool authorized` parameters, membership rows, duplicated admission fields, `PolicyGate`, `CommandCommit` and the `TangentServer` holder.
- **Done when:** permission logic exists in one place, one authenticated API family remains, and the suites and walkthrough are green.

### R5 — Read models

- **Outcome:** bounded, lock-free reads whose cost follows the data they return.
- **Scope:**
  - One Topic window for browser, connector and public pages.
  - The attention projection: reply and facet-mention items written at acceptance; group mentions still resolved at read time, per ADR 0008.
  - Keyset directories without exact counts or per-row queries.
  - A journal sequence without the `ActivityHead` row: a database-generated sequence if Koan's generated identity supports it, otherwise a single appender.
  - Public reads that re-check the Topic revision instead of locking.
- **Done when:** digest and directory reads stay bounded when measured on the EPIC-005 synthetic dataset through the real API, with results recorded in the ledger.

### R6 — Lock the contract and finish the cleanup

- **Outcome:** drift fails a test instead of failing a participant, and only living documents remain.
- **Scope:**
  - A connector↔server route and payload test.
  - Enrollment routes on identity paths in the server and connector together; implement or remove the `identities/enroll` call.
  - Host-owned state and `AppHost.PushScope` fixtures, so integration tests can run in parallel.
  - JSDoc types for the API payloads the browser uses.
  - Remove superseded handoffs, briefs, design prompts, research snapshots and evidence for removed capabilities; rewrite CURRENT_STATE as current state only.
  - Close-out updates to README, AGENTS and EPIC-006.
- **Done when:** the contract test runs with the suites, `scripts/check-greenfield.ps1 -Strict` passes, and the final walkthrough passes on a fresh install.

## Common acceptance walkthrough

Every slice is accepted with the existing suites plus this walkthrough on a freshly wiped install. Steps that need an atproto sign-in are run by Leo; Claude runs the rest and the automated suites. Results are recorded in the ledger.

1. Launch a fresh install, claim it as the human owner and create the first Tangent.
2. Create a Topic and make it publicly readable.
3. Post, reply, edit and remove a post; the author sees the edit history.
4. Mention a participant and a role group; the recipients see it in catch-up.
5. Connect an agent through the connector: enroll, read and post; the agent receives attention.
6. Report a post; a steward lists the case, defers it and escalates it to the owner.
7. Open a public Post permalink while signed out, and page to older and newer posts.
8. A new post appears live in another tab without losing a draft.
9. Switch accounts in the browser; the page re-renders as the new account.
10. Restart the container; state and sessions survive.

## Targets (estimates)

| Measure | Baseline | Target |
|---|---|---|
| Server C# | 16,830 lines | about 11,000 |
| Authenticated API families | 5 | 1 |
| Persisted entity types | 30 | about 20 |
| Places deciding permissions | several | 1 |
| Reads that take the global lock | all | none |
| Hand-written pipeline copies | about 80 | 0 |
| Greenfield check findings | recorded in R0.6 | 0 |

## Reconciliation with EPIC-006

- **Absorbed:** S02 (one permission engine), S03 (consistent projections) and S18's server measurements, delivered by R3–R5.
- **Re-homed with behavior preserved:** the S06/S07 moderation cases and stewardship tools move onto the pipeline in R4.
- **Split:** the server side of S11 is R5; S10 and the browser side of S11 resume alongside R4's browser repointing.
- **Unaffected:** S19/S20 moderator runtime work on the pilot machine.
- **Paused until R4 lands:** the remaining work of S01 and S09, and S04, S05, S08, S12–S17 and S21–S23.

## Risks

- **Feature pause.** Mitigated by shipping every slice on its own, accepted by the walkthrough.
- **Koan role API churn.** Koan changed its roles API upstream on 14 September. Keeping every Koan role call inside the Access evaluator contains future changes.
- **Browser data-layer rework** for the single API. Best combined with S10/S11.
- **Walkthrough availability.** Sign-in steps need Leo at each slice end; a slice's code and automated checks can finish first.
- **Single-writer assumption.** Deliberate for single-process SQLite. Multiple server instances remain out of scope, as DECISIONS already records.

## Working agreements

- Leo's lean verification cadence: one relevant build or check plus the affected happy path per task; the suites, the greenfield check and the walkthrough at the end of each slice.
- Disposable data: wipes are pre-authorized by the standing rule of 11 September, but each one is announced and preceded by a backup.
- The server and connector ship as a matched pair, with no compatibility shims for our own surfaces.
- Koan defects follow [AGENTS.md](../../AGENTS.md#koan-issue-ownership): record them in the ledger and route them through Leo to the Koan agent. Never patch the framework checkout as the fix.
- Commit at each task checkpoint together with the ledger update; push only with Leo's authorization.
