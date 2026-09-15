# EPIC-007 work ledger

The single source of execution state for [EPIC-007](../EPIC-007.md). Whoever resumes — a new session, another machine, another agent — starts at **Resume here**, follows the resume protocol and updates this file at every checkpoint. If this file disagrees with the repository, the repository wins: verify with git before trusting any status. A memory pointer for Claude sessions exists outside the repository; this file is authoritative.

## Resume here

| Field | Value |
|---|---|
| Epic status | Accepted 2026-09-15 (every recommendation); in progress |
| Current slice | R1 — Subtract |
| Current task | R1.8 — verify R1 on a fresh install (`doing`) |
| Next action | Continue R1.8 from the first unticked step under [In flight](#in-flight) |
| Last checkpoint | 2026-09-15 · S-002 · R1.8 in progress: fresh install running; checkpoint commit "fix: drop the removed tools.json from the image build"; the walkthrough is next |
| Durability | Commits at task checkpoints are authorized (D10). Push is not yet authorized, so work exists only on this machine until Leo allows a push |
| Waiting on | Leo: the sign-in steps of the R1 walkthrough; push authorization (optional) |
| Blockers | None (Docker Desktop's startup crash was cleared on 2026-09-15; see N-023) |

## Resume protocol

1. Read **Resume here**, then the current task in the [task board](#task-board) and anything under [In flight](#in-flight).
2. Compare the repository with the last checkpoint:
   ```powershell
   git branch --show-current
   git log -3 --oneline
   git status --short
   ```
3. If the working tree differs from what the checkpoint and In flight describe, run `git diff --stat` and reconcile before editing. Never discard changes you cannot explain; ask Leo.
4. If the task touches the running app, check it: `docker compose ps` and `Invoke-WebRequest http://127.0.0.1:5220/health/ready`.
5. Re-run the current task's check command. A status written before an interruption is a claim, not evidence.
6. Continue from **Next action**, and update **Resume here** as you start.

## Checkpoint protocol

- **Starting a task:** set it to `doing`; write its steps, files and check command under In flight; update Resume here.
- **After each step that changes files:** tick the step under In flight; record surprises under [Notes and discoveries](#notes-and-discoveries).
- **Before a long or destructive operation** (build, wipe, mass rename): write a one-line checkpoint in Resume here first.
- **Finishing a task:** run the suites the task affects and `pwsh scripts/check-greenfield.ps1`; confirm the .NET failures equal the [known baseline failures](#known-baseline-failures); set the task to `done` with evidence; clear In flight; make the next task the Next action; commit the work and the ledger update together.
- **Ending a session, or when context runs long:** append a [session log](#session-log) entry and make sure Resume here is accurate.
- **Ending a slice:** run the full suites and the walkthrough, then update [Metrics](#metrics) and [Walkthrough results](#walkthrough-results).

Status values: `todo` · `doing` · `waiting` (on Leo) · `blocked` · `done` · `dropped`.

## Environment and commands

| Item | Value |
|---|---|
| Repository | `E:\repo\github\sylin-org\tangent-space` |
| Working branch | `claude/epic-007-realignment`, created from `codex/epic-006-stewardship` @ `d682c26` |
| Archive | Tag `archive/pre-epic-007` → `d682c26`: the full pre-realignment tree, including removed capabilities |
| Koan checkout | `.local/upstream/koan-framework` @ `21b18c69`, pinned in `scripts/prepare-framework.ps1`; `KoanSourceRoot` is set in `src/Directory.Build.props` |
| Toolchains on this machine | .NET SDK 10.0.401 (`global.json`), Node 24.20.0, Cargo 1.98.1, Docker 29.7.2, PowerShell 7 |
| New machine | Run `./Build.bat` (or `pwsh scripts/prepare-framework.ps1`) before `dotnet test` |
| Server | `http://127.0.0.1:5220`; health `/health/ready`; logs `docker compose logs -f tangent` |
| Public origin | `Tangent:Site:PublicOrigin` (`Tangent__Site__PublicOrigin` in `compose.yaml`) |
| Connector companion manager | `http://127.0.0.1:5219` (`tangent-connector serve`) |
| App state | `.local/docker/site`, mounted at `/state` |
| Build · launch | `./Build.bat` · `./Launch.bat` |
| Wipe | `./Wipe.bat -Force` (non-interactive; announce first) · `./Wipe.bat -WhatIf` (dry run) |
| Backup · restore | `./Backup.bat` · `./Restore.bat` |
| .NET tests | `dotnet test tests/TangentSpace.Tests/TangentSpace.Tests.csproj`; focused runs add `--filter "FullyQualifiedName~Name"` |
| Browser tests | `node --test "tests/*.test.mjs"`; syntax check `node --check <file>` |
| Connector tests | `cargo test --manifest-path src/server/mcp/Cargo.toml` |
| Greenfield check | `pwsh scripts/check-greenfield.ps1` (report); add `-Strict` to fail on findings |

## Guardrails

- **Cleanup is mandatory** (Leo, 15 September). Removing a capability removes its code, tests, scripts, configuration and documentation in the same task. Nothing deprecated stays in the working tree for reference; Git history and the `archive/pre-epic-007` tag are the archive.
- **Code reads greenfield** (Leo, 15 September). No legacy, compatibility, transitional or historical naming, comments or shims; names follow the [glossary](../../ARCHITECTURE.md#glossary). The greenfield check must reach zero by the end of R6.
- **No dialogs** (Leo, 15 September). The browser and the connector's companion page never block with `alert`, `confirm`, `prompt` or a modal `<dialog>`: destructive actions confirm inline in place, forms open as panels in the page. `tests/no-dialogs.test.mjs` and the connector's operator tests enforce it.
- Commit at each task's end together with the ledger update (D10). Push only when Leo authorizes it.
- Wipes are pre-authorized by the standing rule (DECISIONS, 11 September). Announce each one and back up first. Never delete backups or Git history.
- No `git reset --hard`, `git clean` or checkout-wide replacement. Preserve unrelated work.
- Lean verification: one relevant check plus the affected happy path per task; the suites and walkthrough at slice ends.
- Change the server and connector together, with no compatibility shims.
- Koan defects: record them under [Findings to route](#findings-to-route) and ask Leo to route them to the Koan agent, per AGENTS.md. Never patch `.local/upstream/koan-framework` as the fix.
- Claude never enters credentials: walkthrough steps that need an atproto sign-in are run by Leo.

## Decisions

Accepted by Leo on 2026-09-15 ("Accept all recommendations"). Details in [EPIC-007](../EPIC-007.md#decisions) and [ADR 0011](../../adr/0011-realigned-server-architecture.md).

| ID | Decision | Accepted direction | Status |
|---|---|---|---|
| D1 | Inbound MCP transport and browser WebMCP | Delete; move live pieces out under accurate names | accepted |
| D2 | Spaces storage | Remove from the server; archive tag preserves it | accepted |
| D3 | ONNX change classification | Remove; keep edit history | accepted |
| D4 | Membership store | Koan roles only | accepted |
| D5 | Authenticated API | One route family, one envelope | accepted |
| D6 | Concurrency | One pipeline write lock; lock-free reads | accepted |
| D7 | Internal names | Product words now, per the glossary | accepted |
| D8 | Node client `clients/participant` | Delete | accepted |
| D9 | Standalone enrollment proof audience | Default from the public origin, with an override | accepted |
| D10 | Working branch and commits | `claude/epic-007-realignment`; commit at task checkpoints; push only with Leo's authorization | accepted |

## Task board

### R0 — Decide and draw the map

| ID | Task | Status | Done when | Evidence |
|---|---|---|---|---|
| R0.1 | Owner decisions D1–D10 | done | Every decision answered | All accepted, 2026-09-15 |
| R0.2 | ADR 0011, a DECISIONS entry, and supersession notes on ADRs 0001, 0002, 0005, 0006 | done | The ADR records the accepted decisions | `ea55313` |
| R0.3 | `docs/ARCHITECTURE.md`: modules, glossary, shared components | done | Glossary follows D7 | `ea55313` |
| R0.4 | Reconcile EPIC-006: absorbed, re-homed and paused stories | done | EPIC-006 and its stories link here | `ea55313` |
| R0.5 | Pointers: README, AGENTS, CURRENT_STATE | done | A fresh reader lands on EPIC-007 | `ea55313` |
| R0.6 | Baseline: full .NET, browser and connector suites; greenfield counts | done | [Metrics](#metrics) filled | .NET 506/518 (12 [known failures](#known-baseline-failures)); browser 170/170; connector 98/98; greenfield 4,132 |
| R0.7 | Working branch and archive tag | done | Both exist | `claude/epic-007-realignment`; `archive/pre-epic-007` → `d682c26` |
| R0.8 | `scripts/check-greenfield.ps1` | done | Reports counts per rule | `ea55313` |

The runtime baseline (build, launch, walkthrough) happens once, at R1.8, on a freshly wiped install; until then the running instance keeps Leo's data.

### R1 — Subtract

| ID | Task | Status | Done when | Evidence |
|---|---|---|---|---|
| R1.1 | Remove the inbound MCP transport; move its live pieces to accurate homes (`Application/References`, `Application/OperationReceipts`, `Hosting/BearerRegistration`, `Tangent:Site:PublicOrigin`, `Communities/Web/InvitationController`, `TopicWindow`/`WindowPlanner`/`ReadWindow`); delete the rest of `Mcp/`, the embedded `tools.json`, `docs/design/tangent-mcp/`, transport tests and transport evidence | done | No `/mcp` route; build green; .NET failures equal the known set | `f7192d7`: .NET 459/471, failures = the 12 known; server C# 16,830 → 13,546; `Mcp/` 3,697 → 364 lines (enrollment only); entity types 30 → 28; greenfield 4,132 → 2,850 |
| R1.2 | Delete browser WebMCP: `agent.html`, `agent-page.js`, `agent-connection.js`, `webmcp.js`, `ParticipantArrivalController`, their two browser tests, `scripts/prove-webmcp-http.mjs`, `docs/WEBMCP.md`, WebMCP evidence; rewrite WebMCP passages in PRODUCT, the experience API spec, atmospheres and OPERATING | done | No WebMCP references remain; browser suite green | `b69e984`: browser 140/140; server build green (no .NET test referenced the arrival endpoint); greenfield WebMCP 0, total 2,711 |
| R1.3 | Create the home Tangent at claim; remove `EnsureHome` from its 17 call sites; keep one onboarding path by deleting `Create`'s home special case, `TangentsResponse.SetupRequired`, the unread `ServerSettings.SetupRequired` and the `rooms.js` home-key form (see N-016) | done | Reads create nothing; claim creates the home Tangent; onboarding happens only through `/onboarding/` | `c3a4b9b`: .NET 461/473, failures = the 12 known (2 new tests: claim creates the home Tangent; onboarding names it and its key stays taken); browser 139/139 (home-key form test removed); `EnsureHome` 17 → 0; greenfield bootstrap 0, total 2,691; server C# 13,427 |
| R1.4 | Delete pre-multi-Tangent branches (`tangent is null` paths, `LegacyRoomsAssigned`, `POST /api/rooms`) and the digest's non-facet mention parser (`ExperienceMentions`, `ResolvesUnambiguously`, `ExperienceMentionTests`) (see N-017) | done | No such paths remain; the digest uses facets only | `b2183ff`: .NET 455/467, failures = the 12 known (`RoomRulesTests` on the home Tangent; a missing-Tangent test; `MessageFacets` detection tests replace the parser tests); browser 139/139; greenfield 2,691 → 2,473; server C# 13,281; 1,515 lines deleted |
| R1.5 | Per D2 and D9: move the enrollment proof audience and DID-key resolution (`SpacesVerifier`, `ResolvedAuthorKey`) into Identity; then delete Spaces storage — services, notifications, readiness, controllers, `SpaceCarVerifier`, `ConversationSync`, `WriteIntent`, Spaces branches, `RoomSpaceState`, the CBOR package (BouncyCastle stays: it verifies ES256K proofs), the fixture-network launch path and configuration (see N-014), Spaces probes and scripts, the Spaces status UI in `rooms.js`, the discovery document's `sourceWriteConsent`, Spaces tests, and Spaces passages in README, DOCKER and OPERATING (see N-015). `SourceDecision` goes in R3.6 | done | No Spaces types remain; the connector enrolls on a fresh install | `ac9cb7b`, `5ed4f29`, `99e89be`: no Spaces types remain (`SourceDecision` stays for R3.6); enrollment derives its audience on a fresh install (`EnrollmentTests`); .NET 345/357 (the 12 known), browser 125/125, connector 98/98, lifecycle 76/76; server C# 13,281 → 11,464; greenfield 2,473 → 1,926 |
| R1.6 | Per D3: delete ONNX classification — `ChangeClassification`, `ChangeClass`, the Onnx project reference, `models/`, raw scores in `history.js`, its tests and configuration | done | Edit history works without scores | `29de598`: no classifier, `ChangeClass`, Onnx reference, model (23 MB) or `Koan:Ai:Onnx` configuration remain; edit history keeps its versions (`EditHistoryTests`); .NET 338/346, failures = the 8 remaining known (N-020); browser 125/125; lifecycle 76/76; greenfield classification 38 → 0, total 1,926 → 1,883; server C# 11,464 → 11,276 |
| R1.7 | Per D8: delete `clients/participant`; rewrite the README's credential paragraph and any remaining participant-runner passages (OPERATING went in R1.5, N-015) | done | No references remain | `5170f1b`: `clients/` is gone and no living document refers to it (R0 had already rewritten the README's credential paragraph; the Experience API spec's migration map and Spaces-era clauses were replaced, N-022); historical references wait for R6.6 (N-011); browser 125/125; greenfield 1,883; no server or connector change |
| R1.8 | Back up, wipe, build, launch; run the suites and the greenfield check; walkthrough with Leo; update metrics | doing | Walkthrough passes; about 5,000 fewer lines | |
| R1.9 | Per Leo (15 September): no dialogs. Replace the post-removal `confirm()`, the report `<dialog>`, the atmosphere `<dialog>` and the connector page's three `confirm()` calls with inline controls; guard against their return (N-027) | done | No dialog remains; the guards pass; the walkthrough uses the inline controls | Commit "refactor: replace every dialog with inline controls": `inline-confirm.js` confirms post removal in place, the report form and the atmosphere picker are in-page panels, the companion page confirms inline; `tests/no-dialogs.test.mjs` and `the_page_opens_no_dialog` guard it; browser 129/129, connector 99/99; the inline removal passed live in W3 |

### R2 — Rename to the product's words

| ID | Task | Status | Done when | Evidence |
|---|---|---|---|---|
| R2.1 | Apply the glossary to domain types, services and strings | todo | Retired vocabulary count is zero for the server | |
| R2.2 | Split `CompanionGovernance` by responsibility: membership, admission, watches, classification | todo | No file owns unrelated responsibilities | |
| R2.3 | Move folders into the modules of [ARCHITECTURE](../../ARCHITECTURE.md#modules), including `Mcp/Authentication` into Identity | todo | Namespaces match the module map | |
| R2.4 | Rename wire fields such as `channels` in the server, browser and connector together | todo | Connector and browser suites green | |
| R2.5 | Wipe and verify | todo | Suites and walkthrough green | |

### R3 — Build the shared components

| ID | Task | Status | Done when | Evidence |
|---|---|---|---|---|
| R3.1 | Short design note: pipeline, Access decision, receipts, signal bus, and ownership held once (N-025) | todo | Written into ARCHITECTURE | |
| R3.2 | One operation-receipt mechanism, replacing `CommandCommit`, the `PostChange` ledger and ad-hoc operation IDs | todo | One receipt type; replay and conflict tests pass | |
| R3.3 | Command pipeline: write lock (commands only), transaction, scope snapshot, Access hook, audit, journal, receipt, commit, signal | todo | Pipeline tests cover denial, replay, conflict and rollback | |
| R3.4 | Access evaluator grown from `TopicPermissionEvaluator`, with a single adapter over Koan role bags and table-driven tests; rewrite the [known baseline failures](#known-baseline-failures) against it, and cover an authority change racing a command at the Access hook (N-020); derive the Host owner's authority from the site so the Owner role stores no members (N-025) | todo | The evaluator explains every Topic decision; the known failures pass | |
| R3.5 | Host-owned live bus replacing the static bus and `ConversationUpdates` | todo | No process-wide signal state | |
| R3.6 | Port Conversation (post, edit, remove, read position, Topic window); replies by Post ID; drop `SourceDecision` and source URI/CID | todo | No `WithCurrentPolicy` or semaphores in Conversation; reads take no lock | |
| R3.7 | Verify with the suites and walkthrough; update metrics | todo | Green | |

### R4 — Move every family onto the pipeline

| ID | Task | Status | Done when | Evidence |
|---|---|---|---|---|
| R4.1 | Stewardship: reports, cases, restrictions, audit | todo | Moderation tests green on the pipeline | |
| R4.2 | Community: Tangent and Topic operations, access, admission, membership via Koan roles; add the missing `/api/v1` operations; repoint the browser; delete `/api/rooms`, `/api/tangents` and `ConversationController` | todo | One authenticated API family remains | |
| R4.3 | Hosting: claim, settings, access defaults, artwork, posture | todo | Hosting tests green on the pipeline | |
| R4.4 | Identity: arrival, enrollment, sessions, profiles, classification | todo | Identity tests green on the pipeline | |
| R4.5 | Delete `Room.CurrentPolicy`, the role-bag overwrite, `bool authorized`, membership rows, duplicated admission fields, `PolicyGate`, `CommandCommit`, `TangentServer`, `WithCurrentPolicy` | todo | None of these identifiers remain | |
| R4.6 | Wipe and verify; update metrics | todo | Suites and walkthrough green | |

### R5 — Read models

| ID | Task | Status | Done when | Evidence |
|---|---|---|---|---|
| R5.1 | One Topic window for browser, connector and public pages | todo | One implementation remains | |
| R5.2 | Attention projection, with a cheap attention summary on responses | todo | Digest cost follows item count | |
| R5.3 | Keyset directories without exact counts or per-row queries | todo | Query plans are bounded | |
| R5.4 | Journal sequence without the `ActivityHead` row | todo | No shared counter row | |
| R5.5 | Public reads without the lock, re-checking the Topic revision | todo | A concurrent audience-narrowing test passes | |
| R5.6 | Measure on the EPIC-005 synthetic dataset through the real API | todo | Results recorded here | |

### R6 — Lock the contract and finish the cleanup

| ID | Task | Status | Done when | Evidence |
|---|---|---|---|---|
| R6.1 | Connector↔server route and payload contract test | todo | The test fails on a missing route | |
| R6.2 | Enrollment routes: implement or remove the `identities/enroll` call; move `/mcp/token` and `/.well-known/tangent-mcp` to identity paths in server and connector together | todo | Connector and server agree; no `mcp` paths on the server | |
| R6.3 | Parallel integration tests: host-owned state; fixtures use `AppHost.PushScope` | todo | `DisableParallelization` removed or justified | |
| R6.4 | JSDoc types for the browser's API payloads | todo | `node --check` and the suites green | |
| R6.5 | Close-out: CURRENT_STATE rewritten as current state only; README, AGENTS, EPIC-006; final walkthrough on a fresh install | todo | Epic marked done | |
| R6.6 | Documentation cleanup: remove superseded handoffs, briefs, design prompts, research snapshots and evidence for removed capabilities; `check-greenfield.ps1 -Strict` passes | todo | Only living documents and decision records remain | |

## In flight

**R1.8 — verify R1 on a fresh install** (doing)

- [x] Announce the wipe; back up `.local/docker/site` as the [Docker guide](../../DOCKER.md) describes and record the backup path here. Announced 2026-09-15; backup `.local/backups/docker-20260915-134736-993` (164 files with a hash manifest). After the claim fix (N-025) a second backup, `.local/backups/docker-20260915-141626-412`, preceded a second wipe
- [x] Wipe, build (`Build.bat`), launch (`Launch.bat`); confirm `/health/ready`. The first build failed on a stale Dockerfile line (N-024). After the fix the image and connector built in 36 s, a fresh configuration was created and the app was healthy at 09:51. The startup log holds only the Data Protection key-encryptor warning (expected locally; see DOCKER) and the `HTTP_PORTS` override notice
- [x] .NET, browser, lifecycle and connector suites; greenfield check. At `5170f1b`: .NET 338/346 (only the 8 known failures), browser 125/125, lifecycle 76/76, connector 98/98, greenfield 1,883
- [ ] Walkthrough W1–W10 with Leo, who does every sign-in; record results under [Walkthrough results](#walkthrough-results)
- [ ] Update metrics, CURRENT_STATE's top section and the session log; commit

Check command: `dotnet test tests/TangentSpace.Tests/TangentSpace.Tests.csproj`, `node --test "tests/*.test.mjs"`, then `pwsh scripts/check-greenfield.ps1`.

**Completed: R1.7 — delete `clients/participant`**

- [x] Inventory: 10 tracked files and no untracked leftovers; no script, test glob, manifest or ignore file refers to it; R0 had already rewritten the README's credential paragraph; the one living document with references was the Experience API spec
- [x] Deleted `clients/participant`; the spec's migration map became a pointer to where the contract lives, and its Spaces-era clauses went too (N-022)
- [x] Browser 125/125, greenfield 1,883; no server or connector change; commit

## Known baseline failures

At `d682c26` the .NET suite passes 506 of 518. These 12 tests fail before any EPIC-007 change. Each assigns Topic authority through `RoomRole` membership rows, which no longer change effective permissions because `TangentRoleAccess.ProjectTopic` decides them from Koan role bags. Live consequence: the room-membership endpoints silently do not change what a participant may do. R3.4 rewrites these tests against the Access evaluator; until then every task must leave exactly this set failing. R1.6 deleted the four `Commit_rechecks_current_authority_and_current_post_after_pending_receipt` cases together with the seam they depended on (N-020), so the set is the eight below.

- `ModerationCaseTests.Http_profile_is_permission_shaped_and_saved_apply_is_denied_after_demotion`
- `ModerationCaseTests.Concurrent_decisions_have_one_winner_and_one_explicit_conflict`
- `ConversationPermissionEnforcementTests.Reader_cannot_create_edit_or_delete_and_denials_leave_no_receipts_or_history`
- `ConversationPermissionEnforcementTests.Moderator_can_remove_another_authors_post_but_cannot_rewrite_it`
- `ConversationPermissionEnforcementTests.Removed_post_rejects_new_author_edit_delete_and_moderator_remove_without_duplicate_effects`
- `ConversationPermissionEnforcementTests.Completed_delete_or_moderation_replays_after_topic_lock_without_another_snapshot(moderation: True)`
- `ConversationPermissionEnforcementTests.Management_or_ownership_without_read_access_cannot_remove_a_post(owner: False)`
- `ConversationPermissionEnforcementTests.Completed_moderation_receipt_survives_demotion_but_not_read_revocation`

## Metrics

| Measure | Baseline (`d682c26`) | Now (after R1.7) | Target |
|---|---|---|---|
| Server C# lines | 16,830 | 11,276 | about 11,000 |
| Authenticated API families | 5 | 3 (`/api/v1/experience`, `/api/v1/tangents`, legacy REST) | 1 |
| Persisted entity types | 30 | 25 | about 20 |
| Global lock entries, direct / via `WithCurrentPolicy` | 49 / 31 | 44 / 18 | 0 |
| `EntityContext.Transaction` outside the pipeline | 41 | 38 | 0 |
| `bool authorized` parameters | 9 | 8 | 0 |
| `EnsureHome` call sites | 17 | 0 | 0 |
| `Mcp` folder lines | 3,697 | 358 (enrollment) | 0 |
| Greenfield findings (lines): total | 4,132 | 1,883 | 0 |
| — inbound MCP / WebMCP / Spaces / classification | 712 / 20 / 220 / 38 | 30 / 0 / 21 (`SourceDecision`, R3.6) / 0 | 0 |
| — retired vocabulary / historical markers / bootstrap | 3,095 / 29 / 18 | 1,824 / 8 / 0 | 0 |
| .NET tests | 506 of 518 (12 known failures) | 338 of 346 (the 8 remaining known failures, N-020) | all pass |
| Browser tests | 170 of 170 | 125 of 125 | all pass |
| Connector tests | 98 of 98 | 98 of 98 (R1.5) | all pass |

Recompute with the commands in note N-008 and the greenfield check.

## Walkthrough results

Steps are defined in [EPIC-007](../EPIC-007.md#common-acceptance-walkthrough). Record `pass`, `fail` (with a note) or `n/a`, and who ran the step.

| Step | R1 | R2 | R3 | R4 | R5 | R6 |
|---|---|---|---|---|---|---|
| W1 Fresh install, claim, first Tangent | pass on the second attempt: the first found N-025. Leo signed in; Claude claimed and named "Workshop" | | | | | |
| W2 Topic created and made public | pass: Claude created "Open questions" and set "Anyone on the web"; the signed-out page answers 200 (N-026) | | | | | |
| W3 Post, reply, edit, remove; edit history | pass after R1.9: Claude posted, replied, edited (history shows the original, without scores) and removed through the inline confirmation; the first removal attempt opened a native dialog (N-027) | | | | | |
| W4 Participant and group mentions reach catch-up | | | | | | |
| W5 Agent enrolls, reads, posts, gets attention | | | | | | |
| W6 Report, case list, defer, escalate | | | | | | |
| W7 Signed-out permalink with older/newer paging | | | | | | |
| W8 Live post in another tab keeps a draft | | | | | | |
| W9 Account switch re-renders | | | | | | |
| W10 Container restart keeps state and sessions | | | | | | |

## Notes and discoveries

- **N-001** (planning) Removing legacy REST moved from R1 to R4.2: `/api/v1` has no edit or remove post, membership or role operations yet, and `clients/participant` calls `/api/rooms`.
- **N-002** (planning) Removing Spaces first requires moving the enrollment proof audience (`Tangent:Spaces:ManagingApp`, read by `ServiceProofAuthentication`) and DID-key resolution (`SpacesVerifier`) into Identity. Replies still reference Spaces-era source URI/CID through `SourceDecision`; that is simplified in R3.6.
- **N-003** (planning) `/invite/{invitationId}` is a live human page; R1.1 moved it to `Communities/Web/InvitationController`.
- **N-004** (R1.1) The public origin is `Tangent:Site:PublicOrigin` on `SiteOptions`. Startup rejects a malformed value; `References` requires a value when first constructed.
- **N-005** (planning) The connector calls `POST /api/v1/experience/identities/enroll` (`hub.rs` line 425), as specified in the W2 handoff documents, but the server has no such route (R6.2).
- **N-006** (planning) `agent-entry.js` (footer connector discovery) and `connect.html`/`connect.js` are live and were kept by R1.2.
- **N-007** (R1.1) `McpAuthenticationTests` became `EnrollmentTests`; its one transport test was removed. Its owner-claim and receipt-atomicity tests use the enrollment host and move with the module split in R2.
- **N-008** Metric commands, run from the repository root:

  ```powershell
  $cs = Get-ChildItem src/server/web -Recurse -Filter *.cs | Where-Object FullName -notmatch '\\(bin|obj)\\'
  ($cs | ForEach-Object { [IO.File]::ReadAllLines($_.FullName).Length } | Measure-Object -Sum).Sum   # server C# lines
  ($cs | Select-String -Pattern '(gate|policyGate)\.Enter\(').Count                                   # direct lock entries
  ($cs | Select-String -Pattern 'WithCurrentPolicy\(').Count
  ($cs | Select-String -Pattern 'EntityContext\.Transaction\(').Count
  ($cs | Select-String -Pattern 'bool authorized\b').Count
  ($cs | Select-String -Pattern 'EnsureHome\(').Count                                                 # includes the definition
  ($cs | Select-String -Pattern 'class \w+ : Entity<').Count                                          # persisted entity types
  ```
- **N-009** (2026-09-15) R1 was renumbered after acceptance: relocating live `Mcp` pieces and deleting the transport are one task (R1.1), because deleting first would otherwise require updating dead code. Archiving in the working tree was replaced by deletion under the cleanup rule.
- **N-010** (2026-09-15) The 12 [known baseline failures](#known-baseline-failures) show the stacked permission models in action: membership roles and Koan role bags disagree, and the role bags win.
- **N-011** (2026-09-15) Historical documents (CURRENT_STATE history sections, handoffs, older epics, evidence) are removed or rewritten wholesale in R6.6. Until then, links in them to files removed by earlier tasks may break; links in living documents are fixed in the task that removes the target.
- **N-012** (R1.1) The enrollment discovery document no longer lists `endpoints` or `standardMcpOAuthAuthorizationSupport`; the connector reads only `serviceProof`. The enrollment code stays in `Mcp/Authentication` until R2 moves it into Identity, and its `/mcp/token` and `/.well-known/tangent-mcp` routes stay until R6.2.
- **N-013** (R1.1) The .NET suite went from 518 to 471 tests: transport-only tests were removed; tests for `References` and `OperationReceipt` were added. Operation ids for new receipts now start with `op-`.
- **N-014** (R1.2) `scripts/server-lifecycle.ps1` names its connector build step with `Mcp` identifiers (2 greenfield lines). Rename them to connector wording in R1.5, which already edits the launch scripts' fixture paths. Done in R1.5.
- **N-015** (R1.2) `docs/OPERATING.md` predates Docker operation and mixes participant-runner and Spaces recovery content. R1.5 and R1.7 remove those parts; anything still useful merges into `docs/DOCKER.md` and OPERATING is deleted. Done in R1.5: OPERATING held only Spaces and native-PoC content; DOCKER was rewritten around the Docker workflow.
- **N-016** (R1.3) Onboarding had two paths: `/onboarding/` (`CompleteOnboarding`) and a `rooms.js` form that pre-filled the key `home` and relied on `Create` quietly editing the placeholder, driven by `TangentsResponse.SetupRequired`. Only the first remains, and `SiteWelcome.Onboarding` is the single onboarding state. `ServerSettings.SetupRequired` had no reader and was removed. `Claim` creates the home Tangent after its validations, so reads no longer write. `docs/handoff/RUNBOOK.md` still queries removed fields; it goes in R6.6 (N-011).
- **N-017** (R1.4) Removing the `home` default meant removing its last writer, `POST /api/rooms` (`RoomGovernance.Create`), a slice of R4.2 pulled forward; the rest of `/api/rooms` still goes in R4.2. The browser's create form now requires an active Tangent. Five fixture-network proof scripts called that route and were deleted: `prepare-demo` with its wrapper, `prove-ui`, `prove-outage`, `prove-rooms` and `prove-conversation`. `prove-arrival-suspension` and `prove-pending-access` go with the other Spaces scripts in R1.5. `MessageFacets.Detect` is now internal so its rules are unit-tested.
- **N-018** (R1.5) D9 confirmed: the `aud` parameter of `com.atproto.server.getServiceAuth` has `format: did`, so the default audience is a bare `did:web` of the public origin's host and port (`did:web:127.0.0.1%3A5220` locally), without the `#service` fragment the fixture-era value carried. Tangent verifies proofs itself, so the did:web need not resolve; the connector accepts any `did:` audience and its OAuth scope already allows `aud=*`. The override `Tangent:Enrollment:ProofAudience` is validated as a DID at startup. BouncyCastle stays for ES256K verification.
- **N-019** (R1.5) Removing the fixture network removed everything that signed in with its disposable accounts: the connector walkthrough script, the fixture OAuth driver, the native Windows-hosted lifecycle scripts, the arrival and activity proofs, the Spaces probes and the demo capture tooling (with its orphaned model adapter). Walkthroughs are now operator-run with real accounts (R1.8); the archive tag keeps the removed tooling. The `SourceDecision` references that remain under the greenfield "Spaces storage" rule go with R3.6.
- **N-020** (R1.6) The theory `Commit_rechecks_current_authority_and_current_post_after_pending_receipt` (four of the known failures) staged its competing change at the embedder resolution: `BeforeEmbedderProvider` intercepted `IAiAdapterRegistry`, the only yield point between `ChangePost`'s pending receipt and its commit. D3 removed that seam, so the theory and its provider were deleted. The window itself dates from Spaces: the pending receipt covered the remote write that ran between the transactions. R3.2 replaces the `PostChange` ledger and R3.3 makes check and commit one transaction; R3.4's tests cover an authority change racing a command at the pipeline's Access hook.
- **N-021** (R1.6) Removing a `ProjectReference` did not refresh `packages.lock.json` during the build's restore. `dotnet restore tests/TangentSpace.Tests/TangentSpace.Tests.csproj --force-evaluate` regenerates both lock files; `koan.lock.json` regenerates on every build.
- **N-022** (R1.7) The Experience API spec still described the pre-connector migration (a map of `src/TangentSpace` files, the MCP dispatcher, `tools.json` and the Node client) and Spaces source acceptance, which R1.1 and R1.5 missed. Section 8 now points to where the contract lives, and the Spaces-era clauses are gone. Its line "Preserve existing Room/Message storage names where convenient" goes with the R2 renames. Historical documents that mention `clients/participant` (S06, handoffs, evidence) wait for R6.6 (N-011).
- **N-023** (R1.8, environment) Docker Desktop 4.89.0 crashed at start: its backend renames each of its Unix sockets to `*.stale` and fails ("The file cannot be accessed by the system") when socket entries from an earlier session remain, and those entries cannot be deleted either. Moving `%LOCALAPPDATA%\Docker\run` aside (to `run.stale-20260915-094424`) cleared the first failure; the next one, `%LOCALAPPDATA%\docker-secrets-engine\engine.sock`, cleared when Leo restarted Docker Desktop. If it recurs, move the stale folder aside or restart Windows; never use "Reset to factory defaults".
- **N-024** (R1.8) The first real image build since R1.1 failed: the Dockerfile still copied `docs/design/tangent-mcp/tools.json`, which R1.1 deleted, and `.dockerignore` still re-included it. Both lines are gone. The lifecycle suite mocks Docker, so only a real `docker compose build` catches this; a task that removes files the image build names should run one.
- **N-025** (R1.8) W1 failed at the claim: "Confirm as Owner" reported "That step could not be completed" although the claim had committed. `ServerGovernance.Claim` projects the Owner role after its commit, and `TangentOwnerRoleGuard` refused every Owner-role change made with an HTTP actor, so the claim's own request was refused (a bodiless 401) and the owner held no Owner role until startup repaired it. Every test calls `Claim` outside a request, so none saw it. The guard now states the projection's rule and applies it whoever asks: members only converge on the site's owner, and the definition is only ever the built-in one. Two tests cover the signed-in claim and the refused edits. Ownership is still stored twice, and nothing requires it: Koan's `Operator` claim (`HostOwnerClaimsTransformation`) and the roles UI (`RoleUiController`) already read the site's owner, so only `TangentRoleAccess.Bag` decisions read the stored membership. R3.4 derives the owner's authority from the site in the Access evaluator and removes the stored membership, `EnsureOwner`, the startup repair and the guard's membership rule.
- **N-026** (R1.8 walkthrough, UI findings for R5) Public reading is only settable from the conversation's "Topic details → Topic settings" panel; the settings page's Access tab edits role tokens and cannot make a Topic public. The settings page renders its checkboxes as large detached white squares. The permission sentence under "Topic details" shows the raw token `reportPost`. After "Create Topic" the create form stays open above the new conversation. Saving the reading audience collapses the panel without confirmation.
- **N-027** (R1.8 walkthrough) Deleting a post opened the browser's native `confirm()`, which blocked the page and the automation driving it. Leo: dialogs are an antipattern, and the app never opens one. R1.9 replaces the removal confirm, the report `<dialog>`, the atmosphere `<dialog>` and the connector page's three confirms with inline controls, and guards against their return. The permission sentence's raw `reportPost` (N-026) comes from a missing entry in `rooms.js`'s action labels.

## Findings to route

None yet. Record Koan defects here with the framework revision, a reproducer, expected and observed behavior, severity and consumer impact, then ask Leo to route them.

## Session log

### S-001 · 2026-09-15 · Claude (Opus 5)

- Reviewed the server architecture at `d682c26`; wrote the assessment, EPIC-007 and this ledger; added a memory pointer to this file.
- Nothing was built, tested, committed or deployed.

### S-002 · 2026-09-15 · Claude (Opus 5) — in progress

- Leo accepted every recommendation and set two standing rules: cleanup of deprecated content is mandatory; code must read greenfield.
- R0 (`ea55313`): branch and archive tag; ADR 0011, ARCHITECTURE and the greenfield check; EPIC-006 reconciled; README, AGENTS, DECISIONS and CURRENT_STATE updated. Baseline: .NET 506/518 with 12 known failures; browser 170/170; connector 98/98; greenfield 4,132.
- R1.1 (`f7192d7`): inbound MCP transport removed and its live pieces renamed; .NET 459/471 with only the 12 known failures; server C# 13,546 lines.
- R1.2 (`b69e984`): browser WebMCP removed; browser 140/140; greenfield 2,711.
- R1.3 (`c3a4b9b`): the home Tangent is created at claim; reads no longer write; one onboarding path. .NET 461/473 with only the 12 known failures; browser 139/139; greenfield 2,691.
- R1.4 (`b2183ff`): every Topic names its Tangent; `POST /api/rooms` and five fixture-network scripts removed; the digest reads facets only. .NET 455/467 with only the 12 known failures; browser 139/139; greenfield 2,473.
- R1.5 step 1 (`ac9cb7b`): enrollment derives its proof audience from the public origin (D9). .NET 461/473 with only the 12 known failures.
- R1.5 step 2a (`5ed4f29`): Spaces storage removed from the server, browser and tests. .NET 345/357 with only the 12 known failures; browser 125/125; greenfield 1,964; server C# 11,468.
- R1.5 step 2b (`99e89be`): fixture network, native lifecycle scripts, Spaces probes and OPERATING removed; DOCKER rewritten. Lifecycle suite 76/76; connector 98/98; greenfield 1,926.
- R1.6 (`29de598`): ONNX change classification removed (classifier, `ChangeClass`, the 23 MB model, its configuration and the history chips); the theory that depended on the embedder seam went with it (N-020). .NET 338/346 with only the 8 remaining known failures; browser 125/125; lifecycle 76/76; greenfield 1,883; server C# 11,276.
- R1.7 (`5170f1b`): Node participant client deleted; the Experience API spec's migration map and Spaces-era clauses replaced (N-022). Browser 125/125; greenfield 1,883.
- R1.8 (in progress): suites green at `5170f1b`; Docker Desktop's startup crash cleared (N-023); backup `.local/backups/docker-20260915-134736-993`; state wiped; the Dockerfile's stale `tools.json` copy removed (N-024); the fresh install is healthy.
- R1.8 walkthrough: W1 passed after the Owner-role fix (N-025, `b7061dd`), W2 passed (N-026 UI findings), W3 passed after R1.9 removed every dialog (N-027). Leo: the app never opens dialogs; the ports report is next.
- Next: the random ports Leo saw (59998), then W4–W10.

## Evidence index

- [Architecture assessment, 15 September](../../ASSESSMENT_2026-09-15.md)
- [Previous assessment, 12 September](../../ASSESSMENT_2026-09-12.md)
- Recorded suite results before this epic: [CURRENT_STATE](../../CURRENT_STATE.md)
