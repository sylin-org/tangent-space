# EPIC-007 work ledger

The single source of execution state for [EPIC-007](../EPIC-007.md). Whoever resumes — a new session, another machine, another agent — starts at **Resume here**, follows the resume protocol and updates this file at every checkpoint. If this file disagrees with the repository, the repository wins: verify with git before trusting any status. A memory pointer for Claude sessions exists outside the repository; this file is authoritative.

## Resume here

| Field | Value |
|---|---|
| Epic status | Accepted 2026-09-15 (every recommendation); in progress |
| Current slice | R1 — Subtract |
| Current task | R1.1 — remove the inbound MCP transport (`doing`: preparing edits) |
| Next action | Follow the R1.1 steps under [In flight](#in-flight) |
| Last checkpoint | 2026-09-15 · S-002 · R0 complete and committed on `claude/epic-007-realignment` (commit "docs: accept EPIC-007 and open its work ledger"); no R1 code changed yet |
| Durability | Commits at task checkpoints are authorized (D10). Push is not yet authorized, so work exists only on this machine until Leo allows a push |
| Waiting on | Leo: push authorization (optional); the sign-in steps of each slice walkthrough |
| Blockers | None |

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
| R0.2 | ADR 0011, a DECISIONS entry, and supersession notes on ADRs 0001, 0002, 0005, 0006 | done | The ADR records the accepted decisions | `docs/adr/0011-realigned-server-architecture.md` |
| R0.3 | `docs/ARCHITECTURE.md`: modules, glossary, shared components | done | Glossary follows D7 | `docs/ARCHITECTURE.md` |
| R0.4 | Reconcile EPIC-006: absorbed, re-homed and paused stories | done | EPIC-006 and its stories link here | EPIC-006 status note; STORIES header |
| R0.5 | Pointers: README, AGENTS, CURRENT_STATE | done | A fresh reader lands on EPIC-007 | README table, AGENTS epic paragraph, CURRENT_STATE entry |
| R0.6 | Baseline: full .NET, browser and connector suites; greenfield counts | done | [Metrics](#metrics) filled | .NET 506/518 (12 [known failures](#known-baseline-failures)); browser 170/170; connector 98/98; greenfield 4,132 |
| R0.7 | Working branch and archive tag | done | Both exist | `claude/epic-007-realignment`; `archive/pre-epic-007` → `d682c26` |
| R0.8 | `scripts/check-greenfield.ps1` | done | Reports counts per rule | Baseline recorded under Metrics |

The runtime baseline (build, launch, walkthrough) happens once, at R1.8, on a freshly wiped install; until then the running instance keeps Leo's data.

### R1 — Subtract

| ID | Task | Status | Done when | Evidence |
|---|---|---|---|---|
| R1.1 | Remove the inbound MCP transport. Move live pieces to accurate homes: `McpRefs` → `Application/References`; `McpRequests`, `McpRequestRecord` → `Application/OperationReceipts`, `OperationReceipt`; `McpConsumerRegistration` → `Hosting/BearerRegistration`; `McpOptions.PublicBaseUrl` → `Tangent:Site:PublicOrigin`; the two request exceptions → `Application`; `/invite/{invitationId}` → `Communities/Web/InvitationController`; the `McpWindow` family → Topic-window names. Delete the rest of `Mcp/`, the embedded `tools.json`, `docs/design/tangent-mcp/`, the transport tests and transport evidence; keep and rename tests of live behavior. Enrollment endpoints `/mcp/token` and `/.well-known/tangent-mcp` keep their paths until R6.2 | doing | No `/mcp` route; build green; .NET failures equal the known baseline set; browser and connector suites green | |
| R1.2 | Delete browser WebMCP: `agent.html`, `agent-page.js`, `agent-connection.js`, `webmcp.js`, `ParticipantArrivalController`, `webmcp.test.mjs`, `agent-connection.test.mjs`, `scripts/prove-webmcp-http.mjs`, `docs/WEBMCP.md`. Keep `agent-entry.js`, `connect.html`, `connect.js` | todo | No WebMCP references remain; browser suite green | |
| R1.3 | Create the home Tangent at claim or onboarding; remove `EnsureHome` from its 17 call sites | todo | `EnsureHome` runs only at claim | |
| R1.4 | Delete pre-multi-Tangent branches (`tangent is null` paths, `LegacyRoomsAssigned`) and the digest's non-facet mention parser (`ExperienceMentions`, `ResolvesUnambiguously`, `ExperienceMentionTests`) | todo | No such paths remain; the digest uses facets only | |
| R1.5 | Per D2 and D9: move the enrollment proof audience and DID-key resolution (`SpacesVerifier`, `ResolvedAuthorKey`) into Identity; then delete Spaces storage — services, notifications, readiness, controllers, `SpaceCarVerifier`, `ConversationSync`, `WriteIntent`, Spaces branches, `RoomSpaceState`, BouncyCastle and CBOR packages, the fixture-network launch path and configuration, Spaces probes and scripts, the Spaces status UI in `rooms.js`, Spaces tests, and Spaces passages in README and DOCKER. `SourceDecision` goes in R3.6 | todo | No Spaces types remain; the connector enrolls on a fresh install | |
| R1.6 | Per D3: delete ONNX classification — `ChangeClassification`, `ChangeClass`, the Onnx project reference, `models/`, raw scores in `history.js`, its tests and configuration | todo | Edit history works without scores | |
| R1.7 | Per D8: delete `clients/participant`; rewrite the README's credential paragraph | todo | No references remain | |
| R1.8 | Back up, wipe, build, launch; run the suites and the greenfield check; walkthrough with Leo; update metrics | todo | Walkthrough passes; about 5,000 fewer lines | |

### R2 — Rename to the product's words

| ID | Task | Status | Done when | Evidence |
|---|---|---|---|---|
| R2.1 | Apply the glossary to domain types, services and strings | todo | Retired vocabulary count is zero for the server | |
| R2.2 | Split `CompanionGovernance` by responsibility: membership, admission, watches, classification | todo | No file owns unrelated responsibilities | |
| R2.3 | Move folders into the modules of [ARCHITECTURE](../../ARCHITECTURE.md#modules) | todo | Namespaces match the module map | |
| R2.4 | Rename wire fields such as `channels` in the server, browser and connector together | todo | Connector and browser suites green | |
| R2.5 | Wipe and verify | todo | Suites and walkthrough green | |

### R3 — Build the shared components

| ID | Task | Status | Done when | Evidence |
|---|---|---|---|---|
| R3.1 | Short design note: pipeline, Access decision, receipts, signal bus | todo | Written into ARCHITECTURE | |
| R3.2 | One operation-receipt mechanism, replacing `CommandCommit`, the `PostChange` ledger and ad-hoc operation IDs | todo | One receipt type; replay and conflict tests pass | |
| R3.3 | Command pipeline: write lock (commands only), transaction, scope snapshot, Access hook, audit, journal, receipt, commit, signal | todo | Pipeline tests cover denial, replay, conflict and rollback | |
| R3.4 | Access evaluator grown from `TopicPermissionEvaluator`, with a single adapter over Koan role bags and table-driven tests; rewrite the [known baseline failures](#known-baseline-failures) against it | todo | The evaluator explains every Topic decision; the known failures pass | |
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

**R1.1 — remove the inbound MCP transport** (S-002)

- [ ] Confirm which discovery fields the connector reads before changing the discovery document
- [ ] `Application/References.cs` from `McpRefs`, keeping only members live code uses, with Topic and Post names and new data-protection purposes
- [ ] `Application/OperationReceipts.cs` and `Application/OperationReceipt.cs` from `McpRequests` and `McpRequestRecord`; `RequestArgumentException` and `RequestConflictException` in `Application`
- [ ] `Hosting/BearerRegistration.cs`; `ParticipationRegistration` updated
- [ ] `Tangent:Site:PublicOrigin` on `SiteOptions` with its validation; `compose.yaml` and test fixtures; discovery controller updated and its `/mcp` entry removed
- [ ] `Communities/Web/InvitationController.cs`
- [ ] Window names: `TopicWindow`, `WindowCursor`, `WindowPlanner`, `ConversationService.ReadWindow`
- [ ] Callers updated: Experience files, `ModerationCaseService`, `VersionedTangentsController`, `TangentModule`, `ExperienceWebApp`, `ConnectorIntegrationTests`
- [ ] Delete the transport files, `McpRegistration`, `McpOptions`, the embedded `tools.json`, `docs/design/tangent-mcp/`, transport tests and transport evidence; keep and rename tests of live behavior
- [ ] Fix living-document links to deleted files
- [ ] Build; .NET suite equals the known baseline failures; browser and connector suites; greenfield check; commit

Check command: `dotnet test tests/TangentSpace.Tests/TangentSpace.Tests.csproj`, then `pwsh scripts/check-greenfield.ps1`.

## Known baseline failures

At `d682c26` the .NET suite passes 506 of 518. These 12 tests fail before any EPIC-007 change. Each assigns Topic authority through `RoomRole` membership rows, which no longer change effective permissions because `TangentRoleAccess.ProjectTopic` decides them from Koan role bags. Live consequence: the room-membership endpoints silently do not change what a participant may do. R3.4 rewrites these tests against the Access evaluator; until then every task must leave exactly this set failing.

- `ModerationCaseTests.Http_profile_is_permission_shaped_and_saved_apply_is_denied_after_demotion`
- `ModerationCaseTests.Concurrent_decisions_have_one_winner_and_one_explicit_conflict`
- `ConversationPermissionEnforcementTests.Reader_cannot_create_edit_or_delete_and_denials_leave_no_receipts_or_history`
- `ConversationPermissionEnforcementTests.Moderator_can_remove_another_authors_post_but_cannot_rewrite_it`
- `ConversationPermissionEnforcementTests.Removed_post_rejects_new_author_edit_delete_and_moderator_remove_without_duplicate_effects`
- `ConversationPermissionEnforcementTests.Completed_delete_or_moderation_replays_after_topic_lock_without_another_snapshot(moderation: True)`
- `ConversationPermissionEnforcementTests.Management_or_ownership_without_read_access_cannot_remove_a_post(owner: False)`
- `ConversationPermissionEnforcementTests.Completed_moderation_receipt_survives_demotion_but_not_read_revocation`
- `ConversationPermissionEnforcementTests.Commit_rechecks_current_authority_and_current_post_after_pending_receipt` × 4 (`post-removed`, `author-becomes-reader`, `moderator-loses-reading`, `post-moved`)

## Metrics

| Measure | Baseline (`d682c26`) | Now | Target |
|---|---|---|---|
| Server C# lines | 16,830 | 16,830 | about 11,000 |
| Authenticated API families | 5 | 5 | 1 |
| Persisted entity types | 30 | 30 | about 20 |
| Global lock entries, direct / via `WithCurrentPolicy` | 49 / 31 | 49 / 31 | 0 |
| `EntityContext.Transaction` outside the pipeline | 41 | 41 | 0 |
| `bool authorized` parameters | 9 | 9 | 0 |
| `EnsureHome` call sites | 17 | 17 | 1 |
| `Mcp` folder lines | 3,697 | 3,697 | 0 |
| Greenfield findings (lines): total | 4,132 | 4,132 | 0 |
| — inbound MCP / WebMCP / Spaces / classification | 712 / 20 / 220 / 38 | same | 0 |
| — retired vocabulary / historical markers / bootstrap | 3,095 / 29 / 18 | same | 0 |
| .NET tests | 506 of 518 (12 known failures) | same | all pass |
| Browser tests | 170 of 170 | same | all pass |
| Connector tests | 98 of 98 | same | all pass |

Recompute with the commands in note N-008 and the greenfield check.

## Walkthrough results

Steps are defined in [EPIC-007](../EPIC-007.md#common-acceptance-walkthrough). Record `pass`, `fail` (with a note) or `n/a`, and who ran the step.

| Step | R1 | R2 | R3 | R4 | R5 | R6 |
|---|---|---|---|---|---|---|
| W1 Fresh install, claim, first Tangent | | | | | | |
| W2 Topic created and made public | | | | | | |
| W3 Post, reply, edit, remove; edit history | | | | | | |
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
- **N-003** (planning) `/invite/{invitationId}` (`McpInvitationController` with `invitation.js`) is a live human page: relocate it, don't delete it.
- **N-004** (planning) `Tangent:Mcp:PublicBaseUrl` is set in `compose.yaml` and in test fixtures and feeds `McpRefs`; it moves with the references (R1.1).
- **N-005** (planning) The connector calls `POST /api/v1/experience/identities/enroll` (`hub.rs` line 425), as specified in the W2 handoff documents, but the server has no such route (R6.2).
- **N-006** (planning) `agent-entry.js` (footer connector discovery) and `connect.html`/`connect.js` are live and not part of the WebMCP deletion.
- **N-007** (planning) `McpAuthenticationTests` covers both the live service-proof exchange and the inbound transport: split it, don't delete it.
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

## Findings to route

None yet. Record Koan defects here with the framework revision, a reproducer, expected and observed behavior, severity and consumer impact, then ask Leo to route them.

## Session log

### S-001 · 2026-09-15 · Claude (Opus 5)

- Reviewed the server architecture at `d682c26`; wrote the assessment, EPIC-007 and this ledger; added a memory pointer to this file.
- Nothing was built, tested, committed or deployed.

### S-002 · 2026-09-15 · Claude (Opus 5) — in progress

- Leo accepted every recommendation and set two standing rules: cleanup of deprecated content is mandatory; code must read greenfield.
- Created `claude/epic-007-realignment` and tag `archive/pre-epic-007`; wrote ADR 0011, ARCHITECTURE and the greenfield check; reconciled EPIC-006; updated README, AGENTS, DECISIONS and CURRENT_STATE.
- Baseline: .NET 506/518 with 12 known failures; browser 170/170; connector 98/98; greenfield 4,132 findings.
- R0 committed; R1.1 started.

## Evidence index

- [Architecture assessment, 15 September](../../ASSESSMENT_2026-09-15.md)
- [Previous assessment, 12 September](../../ASSESSMENT_2026-09-12.md)
- Recorded suite results before this epic: [CURRENT_STATE](../../CURRENT_STATE.md)
