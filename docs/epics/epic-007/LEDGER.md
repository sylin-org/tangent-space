# EPIC-007 work ledger

The single source of execution state for [EPIC-007](../EPIC-007.md). Whoever resumes — a new session, another machine, another agent — starts at **Resume here**, follows the resume protocol and updates this file at every checkpoint. If this file disagrees with the repository, the repository wins: verify with git before trusting any status. A memory pointer for Claude sessions exists outside the repository; this file is authoritative.

## Resume here

| Field | Value |
|---|---|
| Epic status | Accepted 2026-09-15 (every recommendation); in progress |
| Current slice | R2 — Rename to the product's words |
| Current task | R2.6 — wipe and verify (`waiting` on Leo for the walkthrough). The wipe itself is done |
| Next action | **Walkthrough W1–W11 with Leo on the fresh install**, which closes R2.6 and R2. The install is unclaimed, so W1 starts at the claim; the connector needs `forget` then Connect (N-056). After that, R3 begins with R3.1's design note. Two things still owed: the `probes/` question (N-047) and push authorization |
| Last checkpoint | 2026-09-16 · S-004 · R2.6's wipe is done and the install is fresh. The walkthrough found its first defect at the owner claim and it is fixed (N-059): first sign-in never subscribed to the profile announcement |
| Durability | Commits at task checkpoints are authorized (D10). **Pushed to `origin/claude/epic-007-realignment` on 2026-09-16**, so the work no longer exists only on this machine |
| Waiting on | Leo: the W1-W11 walkthrough on the fresh install, which closes R2.6. Nothing else |
| Blockers | None. The stranded tables are gone with the wipe, so N-044 is closed |

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
- **Finishing a task:** run the suites the task affects and `pwsh scripts/check-greenfield.ps1`; every suite is green, with no permitted failures; set the task to `done` with evidence; clear In flight; make the next task the Next action; commit the work and the ledger update together.
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
- **Fixed ports** (Leo, 15 September). The server listens on 5220 and the connector's companion page on 5219, or on another fixed port an operator names; no product code picks a random port, and no test opens a real browser.
- **No dialogs** (Leo, 15 September). The browser and the connector's companion page never block with `alert`, `confirm`, `prompt` or a modal `<dialog>`: destructive actions confirm inline in place, forms open as panels in the page. `tests/no-dialogs.test.mjs` and the connector's operator tests enforce it.
- Commit at each task's end together with the ledger update (D10). Push only when Leo authorizes it.
- Wipes are pre-authorized by the standing rule (DECISIONS, 11 September). Announce each one and back up first. Never delete backups or Git history.
- No `git reset --hard`, `git clean` or checkout-wide replacement. Preserve unrelated work.
- Lean verification: one relevant check plus the affected happy path per task; the suites and walkthrough at slice ends.
- **Tests earn their place by tier** (Leo, 15 September). A **rule** test pins a promise the product makes — no dialogs, fixed ports, no cross-site writes, a signed-in participant never sees less than a signed-out one. A **contract** test runs the browser or connector against the real server. A **mechanism** test asserts on internals or on values a fixture invented; it is not written, and an existing one is deleted by whatever task replaces its subject — never renamed forward. When a capability goes, its tests go in the same task.
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
| D11 | The access contract | One access map of capability → tokens (`everyone`, `participant`, `role:<key>`, `global:<permission>`); a category table carrying `WhenEmpty`, whether `everyone` may be listed, and the `AlwaysOn` bypass; no "nobody"; `everyone` exclusive by normalization; children narrow only. [ADR 0013](../../adr/0013-access-contract-and-role-model.md) | accepted 2026-09-15 |
| D12 | The role model | Four built-in roles — `everyone`, `participant`, `administrator`, `owner` — and no more; Member is not one of them. Roles are renameable data with stable keys; built-ins keep well-known keys and mandatory permissions; ownership derives from the Space and is only transferable; no one grants a permission they do not hold; always-on bypasses are disclosed to readers. [ADR 0013](../../adr/0013-access-contract-and-role-model.md) | accepted 2026-09-15 |
| D13 | The product's nouns | **Space, Tangent, Topic, Post.** A Space is a Tangent Space — what an operator runs. `Space` replaces the planned `TangentHost`, and R2.1 applies it (N-038) | accepted 2026-09-15 |
| D14 | The root namespace | **`Tangent`**, with one module per segment: `Tangent.Spaces`, `Tangent.Community`, `Tangent.Conversation`, `Tangent.Identity`, `Tangent.Access`, `Tangent.Activity`, `Tangent.Stewardship`, `Tangent.Api`, `Tangent.Application`, `Tangent.Infrastructure`. This replaces ARCHITECTURE's `Hosting` module with `Spaces`, resolving that row's conflict with D13 by removing the word rather than ruling on it. PascalCase product root, matching Sylin's own Koan (`Koan.Data.Core`), not reverse-DNS. The assembly stays `TangentSpace` (N-051) | accepted 2026-09-16 |
| D15 | Protocol identifiers | Lowercase reverse-DNS under **`org.sylin.tangent.`** — atproto NSIDs, the enrollment exchange method and the OAuth consent scope that travels with it. `local.` was a placeholder for a lexicon backed by no domain. **R6.2 applies it**, which is the task already obliged to replace `local.tangent.mcp.exchange` because its own rule is no `mcp` names on either side | accepted 2026-09-16 |

## Task board

### R0 — Decide and draw the map

| ID | Task | Status | Done when | Evidence |
|---|---|---|---|---|
| R0.1 | Owner decisions D1–D10 | done | Every decision answered | All accepted, 2026-09-15 |
| R0.2 | ADR 0011, a DECISIONS entry, and supersession notes on ADRs 0001, 0002, 0005, 0006 | done | The ADR records the accepted decisions | `ea55313` |
| R0.3 | `docs/ARCHITECTURE.md`: modules, glossary, shared components | done | Glossary follows D7 | `ea55313` |
| R0.4 | Reconcile EPIC-006: absorbed, re-homed and paused stories | done | EPIC-006 and its stories link here | `ea55313` |
| R0.5 | Pointers: README, AGENTS, CURRENT_STATE | done | A fresh reader lands on EPIC-007 | `ea55313` |
| R0.6 | Baseline: full .NET, browser and connector suites; greenfield counts | done | [Metrics](#metrics) filled | .NET 506/518 (12 failures, deleted with their files in R1.15); browser 170/170; connector 98/98; greenfield 4,132 |
| R0.7 | Working branch and archive tag | done | Both exist | `claude/epic-007-realignment`; `archive/pre-epic-007` → `d682c26` |
| R0.8 | `scripts/check-greenfield.ps1` | done | Reports counts per rule | `ea55313` |
| R0.9 | Connector realignment: assessment, decisions C1–C9, ADR 0012, ARCHITECTURE's connector section, EPIC-007 | done | Every recommendation accepted | Leo accepted all, 2026-09-15 (N-031); commit "docs: accept the connector realignment" |

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
| R1.8 | Back up, wipe, build, launch; run the suites and the greenfield check; walkthrough with Leo; update metrics | done | Walkthrough passes; about 5,000 fewer lines | W1–W10 all pass on the fresh install (see [Walkthrough results](#walkthrough-results)); three fixes came out of it (`b7061dd` N-025, `efe0ecf` N-027, `deb70ab` N-029) and two standing rules (`efe0ecf` R1.9, `0c51fc4` R1.10). Re-verified at `f223a1b`: .NET 351/359 with exactly the 8 known failures, browser 129/129, connector 99/99, lifecycle 76/76, greenfield 1,884; server C# 16,830 → 11,333 (5,497 fewer). Findings for later slices: N-026, N-033, N-034, N-035 |
| R1.9 | Per Leo (15 September): no dialogs. Replace the post-removal `confirm()`, the report `<dialog>`, the atmosphere `<dialog>` and the connector page's three `confirm()` calls with inline controls; guard against their return (N-027) | done | No dialog remains; the guards pass; the walkthrough uses the inline controls | Commit "refactor: replace every dialog with inline controls": `inline-confirm.js` confirms post removal in place, the report form and the atmosphere picker are in-page panels, the companion page confirms inline; `tests/no-dialogs.test.mjs` and `the_page_opens_no_dialog` guard it; browser 129/129, connector 99/99; the inline removal passed live in W3 |
| R1.10 | Per Leo (15 September): no random ports. The connector refuses port 0; its hub opens pages only through an injected opener, silent unless the binary installs the platform browser; tests lose the shared no-browser guard and their random-looking fake addresses (N-028) | done | No product code picks a random port; no test opens a browser | Commit "fix: open pages only through the hub and drop random ports": `PageOpener` injected (silent by default, the platform browser only in `build_hub`); port 0 refused; the env guards and random-looking fake addresses gone; connector 99/99; .NET connector integration 3/3 on the release binary |
| R1.11 | Per C8: harden the companion manager — JSON-only writes, a loopback `Host`, same-origin `Origin` and `Sec-Fetch-Site`; detect the page through `/api/discovery`; tests inject the page address; no example keeps state outside the user profile | done | A cross-site `text/plain` POST and a foreign `Host` are refused; connector suite green | Two rules stated once at the connection: every request names a loopback `Host`, and every `/api/*` POST carries `application/json` with a matching `Origin` and — when sent — `Sec-Fetch-Site: same-origin`, mirroring `ParticipationController.Cookie`. `probe` identifies the page through `/api/discovery`; the default page candidate is injectable. Connector 100/100, .NET connector integration 3/3 on the release binary, greenfield 1,884, no new clippy lint |
| R1.12 | Per C1: one enrollment path — delete the unbound tier (`enroll-unbound`, `enroll_unbound`, its payloads, wording, fake route and tests), the app-password binding (`bind_atproto`, `createSession`) and session import (`enroll --token-file`), with the server's `/api/participation/credentials` endpoints; tests seed connector state directly, and the .NET connector tests enroll through the bound exchange against the fake account server; README passages go with them | done | One enrollment path and one binding path remain; suites green | The account-bound exchange through the OAuth bind is the only way in. Gone: `enroll_unbound` and its CLI verb, `bind_atproto`, `enroll --token-file` with `read_token_file`, three dead error helpers, `EnrollResponseDto`/`CreateSessionDto`/`EnrollParticipantDto`, the fake's unbound route and registry, `ParticipationController` with its two request DTOs and `ParticipationCredentials.Enroll/List/Revoke`, and six tests of the removed paths. Connector 94/94, .NET 349/357 (the 8 known), connector `src/` 9,489 → 9,174, server C# 11,333 → 11,227, greenfield 1,890 (N-039) |
| R1.13 | Per C9: delete code without a caller (`adapters/delivery.rs`, the automatic-turn policy, `ExperiencePort::wait`, `ToolOutcome::exit_code`, `experience::shared`, `Perspective::from_identity`, `Budget::truncated`, `StopFlag`, `CallerId::default`), the legacy-enrollment drop, the pre-scope client id and `StateFile.version`; render each attention item once | done | No listed identifier remains; suites green | Every item verified callerless before deletion. `experience::shared` was already gone; `StopFlag` was narrower than listed (`spawn_checkers` stays, its discarded return does not) and the automatic-turn policy far wider (N-041). Connector `src/` 9,174 → 8,816, 52/52; .NET 87/87 on a release binary; greenfield 1,496 |
| R1.14 | Verify the connector: suites, greenfield check, and W5 again on the release binary | done | W5 passes; metrics updated | Rebuilt and relaunched on the current tree (the running image was 12 hours stale against a server changed an hour earlier); healthy at 5220 with no errors in the startup log. All four suites green: .NET 87/87, browser 7/7, connector 52/52, lifecycle 30/30; greenfield 1,496. Verified live without a sign-in: the discovery document serves the atproto-valid audience `did:web:localhost%3A5220` (N-029), `POST /api/participation/credentials` is 404 (R1.12), and the release binary's catalog answers. **W5 itself waits on Leo's Bluesky sign-in** |
| R1.15 | The test audit (Leo, 15 September): the PoC carries too many tests for a moment of heavy mutation, and one coupled to a doomed API gets patched green rather than thought about. Delete the mechanism tier outright rather than freezing it | done | The suite holds only what survives the rewrite; every suite green | 42 files deleted (27 .NET, 12 browser, 3 connector journeys) and five lifecycle sections with them. **656 tests → 191**, and every suite is green for the first time in this epic — the eight permanently-red tests went with their files and the bug they marked is now [prose](#the-bug-the-known-failures-used-to-mark) that R3.4 owns. The 32 red-team safety properties are preserved as [SAFETY-PROPERTIES](SAFETY-PROPERTIES.md), which R5.1 and R5.2 must discharge. .NET 87/87, browser 7/7, connector 61/61, lifecycle 36/36 (N-040) |
| R1.16 | `Wipe.bat` and `Invoke-TangentWipe` stop taking a target: one fixed path, `.local/docker/site`, and nothing else is deletable (N-040) | done | No wipe entry point accepts a path; the guards that can no longer fire are gone | `-Target` removed from the script and unreachable through `Wipe.bat`; `Resolve-TangentWipeTarget` takes only an allowed root and always resolves `<root>/site`. Five guards deleted as unreachable (traversal, outside-the-root, the root itself, the repository root, a nested target); four kept because a fixed path still meets them (N-042). `./Wipe.bat -WhatIf` still names the right target; `-Target C:/` is now a parameter error. Lifecycle 36 → 30, all green |

### R2 — Rename to the product's words

| ID | Task | Status | Done when | Evidence |
|---|---|---|---|---|
| R2.1 | Apply the glossary to domain types, services and strings. The product's four nouns are **Space, Tangent, Topic, Post**: `TangentSite` becomes `Space` (not `TangentHost`), `TangentCommunity` becomes `Tangent`, `Room` becomes `Topic`, `Message` becomes `Post`. Narrow the greenfield check's "Spaces storage" rule to `SourceDecision` first, so the deleted concept stops claiming the word (N-038). Frozen test files (R1.15) are only kept compiling — no assertion is reworked in them | waiting | The four nouns are renamed; the residue is itemized and assigned (N-049) | Four commits, one per stage: `24365c2`, `507cae2`, `ebf7262` and stage 4. Greenfield 1,496 → 858. .NET 87/87, browser 7/7, connector 52/52, lifecycle 30/30. **The relaunch waits on Leo** (N-044) |
| R2.2 | Split `CompanionGovernance` by responsibility: membership, admission, watches, classification | done | Six files, each owning one responsibility; no `companion` left in server C# | `ParticipantGovernance` and five partial files — Membership, Admission, Restrictions (the fifth responsibility the row did not name), Watches, Classification — over a core that holds only the shared substrate. The `Companion*` types took the glossary's word (N-050). .NET 87/87, browser 7/7, greenfield 858 → 819 |
| R2.3 | Move folders into the modules of [ARCHITECTURE](../../ARCHITECTURE.md#modules), including `Mcp/Authentication` into Identity | done | Ten folders, ten namespaces, all `Tangent.<Module>` | 181 files into Access, Activity, Api, Application, Community, Conversation, Identity, Infrastructure, Spaces, Stewardship, with the root namespace `Tangent` (D14). `Mcp/` is gone, which took the inbound-MCP rule 30 → 17. Greenfield 819 → **720**. .NET 87/87, browser 7/7; server, tests and all five probes build (N-052) |
| R2.4 | Rename wire fields such as `channels` in the server, browser and connector together | done | The product's `channel` vocabulary is zero; server and browser moved together | `channels` → `topics`, `roomKey` → `topicKey`, `messageSequence` → `postSequence`, `lastMessageAt` → `lastPostAt`, the `Message*` activity kinds → `Post*`, `RoomChanged` → `TopicChanged`, `TopicListing.Rooms` → `Topics`, `RoleChangeResult.Community` → `Tangent`. The connector needed no change: it parses none of them. Two live defects found on the way (N-054). Greenfield 720 → **474**. .NET 87/87, browser 7/7, connector 52/52 |
| R2.5 | Per C6: the connector glossary in code, CLI and tools (`Companion`, `Account`, `Enrollment`, `Session`, `Context`, `Receipt`, the `manager` command); `room` → `topic`; the crate moves to `src/connector`; the greenfield check scans the connector, with rules for plan-item codes and owner narration; comments lose their history; the README describes the current connector only | done | The connector's vocabulary is the glossary's; the check scans it and reports no plan codes, owner narration or history there | Four stages: `7780b45`, `b54dd25`, and stage 4. The README rewrite is handed to R6.6. **Greenfield rules are frozen from here** — widening them mid-task was manufacturing work (N-057) |
| R2.6 | Wipe and verify | waiting | Suites green on the fresh install; the walkthrough needs Leo's sign-ins | Backup `.local/backups/docker-20260916-175319-423`; wiped, rebuilt, relaunched, healthy at 5220 with only the two expected warnings. .NET 87/87, browser 7/7, connector 52/52, lifecycle 30/30; greenfield 1,496 → **507**. **W1–W11 wait on Leo** |

### R3 — Build the shared components

| ID | Task | Status | Done when | Evidence |
|---|---|---|---|---|
| R3.1 | Short design note: pipeline, Access decision, receipts, signal bus, and ownership held once (N-025) | todo | Written into ARCHITECTURE | |
| R3.2 | One operation-receipt mechanism, replacing `CommandCommit`, the `PostChange` ledger and ad-hoc operation IDs | todo | One receipt type; replay and conflict tests pass | |
| R3.3 | Command pipeline: write lock (commands only), transaction, scope snapshot, Access hook, audit, journal, receipt, commit, signal | todo | Pipeline tests cover denial, replay, conflict and rollback | |
| R3.4 | Access evaluator grown from `TopicPermissionEvaluator`, with a single adapter over Koan role bags and table-driven tests; write the tests for [the bug they marked](#the-bug-the-known-failures-used-to-mark), and cover an authority change racing a command at the Access hook (N-020); derive the Host owner's authority from the site so the Owner role stores no members (N-025). Per D11: the capability category table (`WhenEmpty`, listable tokens, `AlwaysOn`) and `everyone` exclusivity in `AccessCriteria.Normalize`; `participant` derived from sign-in, replacing the `authenticated` token's name; delete `Room.ReadAudience` and fold public reading into `See`; fix `TangentDefaults()` (N-030). Per D12: role keys independent of display names, mandatory permissions, and "no one grants what they do not hold" | todo | The evaluator explains every Topic decision; membership rows change what a participant may do; a signed-in participant never sees less than a signed-out one | |
| R3.5 | Host-owned live bus replacing the static bus and `ConversationUpdates` | todo | No process-wide signal state | |
| R3.6 | Port Conversation (post, edit, remove, read position, Topic window); replies by Post ID; drop `SourceDecision` and source URI/CID | todo | No `WithCurrentPolicy` or semaphores in Conversation; reads take no lock | |
| R3.7 | Per C3: the connector's transactional state store — reads without a lock, each write under an OS file lock with a re-read; receipts in the state; account-session refresh under the lock; the lockfile, `--force` and the one-process rule removed; a second `serve` shares a running companion manager | todo | Two processes changing state concurrently lose nothing | |
| R3.8 | Per C2: sessions renew themselves — expiry recorded from the exchange; renewal before expiry or after a 401, once | todo | An expired session renews without a person | |
| R3.9 | Per C4: use cases instead of the hub (companions, enrollment, participation, activity) over one `Problem`; a concrete Tangent client with one route table; intakes stop reaching into the store; one background-check thread; the waiting `Connect` stays inside its use case (N-031) | todo | No file holds every use case; no `Result<_, String>` in the application | |
| R3.10 | Per C5: `Connect` as the only way in; `SelectCompanion`, `Arrive` and `OpenRegistration` removed; the Experience API spec and the connector README updated | todo | The base catalog has 11 tools | |
| R3.11 | Verify with the suites and walkthrough; update metrics | todo | Green | |

### R4 — Move every family onto the pipeline

| ID | Task | Status | Done when | Evidence |
|---|---|---|---|---|
| R4.1 | Stewardship: reports, cases, restrictions, audit | todo | Moderation tests green on the pipeline | |
| R4.2 | Community: Tangent and Topic operations, access, admission, membership via Koan roles; add the missing `/api/v1` operations; repoint the browser; delete `/api/rooms`, `/api/tangents` and `ConversationController` | todo | One authenticated API family remains | |
| R4.3 | Hosting: claim, settings, access defaults, artwork, posture | todo | Hosting tests green on the pipeline | |
| R4.4 | Identity: arrival, enrollment, sessions, profiles, classification | todo | Identity tests green on the pipeline | |
| R4.5 | Delete `Room.CurrentPolicy`, the role-bag overwrite, `bool authorized`, membership rows, duplicated admission fields, `PolicyGate`, `CommandCommit`, `TangentServer`, `WithCurrentPolicy` | todo | None of these identifiers remain | |
| R4.7 | Per D11 and D12, the front door (N-035): arrival as one page with four slots — the server card, open content, self-enrollable roles, ask to join — pulling the arrival parts of EPIC-006 S09 and S12 forward; the Applications queue over the existing `ListJoinRequests`/`DecideJoinRequest`; the access field's computed always-on note, rendered to readers as well as editors; the role editor with renaming, mandatory permissions and auto-enrollment | todo | A new participant arrives, reads without joining, self-enrols and applies where required (W11) | |
| R4.6 | Wipe and verify; update metrics | todo | Suites and walkthrough green | |

### R5 — Read models

| ID | Task | Status | Done when | Evidence |
|---|---|---|---|---|
| R5.1 | One Topic window for browser, connector and public pages; re-express `window-red-team`'s and `activity-red-team`'s 32 safety properties against it, checked against the list R1.15 extracts (N-040) | todo | One implementation remains; every listed property still has a test | |
| R5.2 | Attention projection, with a cheap attention summary on responses | todo | Digest cost follows item count | |
| R5.3 | Keyset directories without exact counts or per-row queries | todo | Query plans are bounded | |
| R5.4 | Journal sequence without the `ActivityHead` row | todo | No shared counter row | |
| R5.5 | Public reads without the lock, re-checking the Topic revision | todo | A concurrent audience-narrowing test passes | |
| R5.6 | The connector keeps only what its model has been shown; the attention projection carries the rest | todo | The connector stores no waiting counts, revisions or withdrawal heuristics | |
| R5.7 | Measure on the EPIC-005 synthetic dataset through the real API | todo | Results recorded here | |

### R6 — Lock the contract and finish the cleanup

| ID | Task | Status | Done when | Evidence |
|---|---|---|---|---|
| R6.1 | Per C7: the connector's journeys through every tool against the real server in the .NET suite; the Rust fake keeps only failure injection and loses the routes the server lacks | todo | A missing route or a renamed field fails a test | |
| R6.2 | Enrollment names: move `/mcp/token` and `/.well-known/tangent-mcp` to identity paths and rename the exchange method `local.tangent.mcp.exchange`, and with it the OAuth consent scope, in server and connector together; companions bind again once. **Per D15 the replacement is `org.sylin.tangent.…`** — the one place in this codebase where reverse-DNS is correct | todo | Connector and server agree; no `mcp` names on either side; the exchange method and consent scope are `org.sylin.tangent.…` | |
| R6.3 | Parallel integration tests: host-owned state; fixtures use `AppHost.PushScope` | todo | `DisableParallelization` removed or justified | |
| R6.4 | JSDoc types for the browser's API payloads | todo | `node --check` and the suites green | |
| R6.5 | Close-out: CURRENT_STATE rewritten as current state only; README, AGENTS, EPIC-006; final walkthrough on a fresh install | todo | Epic marked done | |
| R6.6 | Documentation cleanup: remove superseded handoffs, briefs, design prompts, research snapshots and evidence for removed capabilities; `check-greenfield.ps1 -Strict` passes | todo | Only living documents and decision records remain | |

## In flight

**R2.6 — wipe and verify** (waiting on Leo)

The wipe is done and the install is fresh, so R2.1's stranded tables are resolved and the epic is no longer carrying them. What remains is the part Claude cannot do.

- [x] Announce, back up (`.local/backups/docker-20260916-175319-423`), wipe, build, launch. Healthy at 5220; the startup log holds only the Data Protection key-encryptor warning and the `HTTP_PORTS` override notice
- [x] Four suites and the greenfield check on the fresh install: .NET 87/87, browser 7/7, connector 52/52, lifecycle 30/30, greenfield **507**
- [ ] **Walkthrough W1–W11 with Leo.** The install is unclaimed, so W1 starts from the claim. Every atproto sign-in is Leo's; Claude never enters credentials. The connector needs `forget` then Connect, because its `state.json` predates R2.5's field renames (N-056)
- [ ] Metrics and CURRENT_STATE updated once the walkthrough passes

Check command: `Invoke-WebRequest http://127.0.0.1:5220/health/ready`, then the four suites and `pwsh scripts/check-greenfield.ps1`.

**Completed: R1.13 — delete code without a caller**

Per C9. Every item verified to have no caller before deletion; two turned out to be narrower than the list implies, and one is already gone:

- `adapters/delivery.rs` — no `delivery::` reference anywhere. The whole module goes.
- `ExperiencePort::wait` and its `UreqExperience` implementation — the only `.wait(` in the tree is a test's `child.wait()`.
- `ToolOutcome::exit_code`, `Perspective::from_identity`, `Budget::truncated`, `CallerId::default` — no callers.
- `StopFlag` — **narrower**: `spawn_checkers` is called (`main.rs`), but its `Vec<StopFlag>` return is discarded, so the flag and the return type go and the function stays.
- The automatic-turn policy — `automatic_turns` is never set outside `policy.rs`'s own tests, so the flag, its branch and those tests go.
- The legacy-enrollment drop — `take_dropped_enrollments`, the `EnrollmentDropped` event and the `local_id.is_empty()` sweep at load, with the two tests that cover it.
- The pre-scope client id — `atproto_oauth::CLIENT_ID`, reached only by one `unwrap_or_else` fallback for sessions the wipe rule says cannot exist.
- `StateFile.version` — written once, never read.
- `experience::shared` — **already gone**; nothing matches it.

- [x] Deleted. Two turned out larger than the list: the automatic-turn policy took `PolicyLedger`, `TurnDecision`, `may_auto_turn`, `record_turn`, `day_key` and four of `AttentionPolicy`'s five fields with it, because the whole module is reached only through `store.policy().poll_seconds`; and the legacy-enrollment drop took the `EnrollmentDropped` event, whose last publisher it was. `policy.rs` 170 lines to 15
- [x] The compiler found the rest: `WAIT_TIMEOUT`, the orphaned trait doc line, two imports, the ledger store and its `remove_companion_state` line, `StateFile.version`, and `run_checker`'s per-second stop poll, which became one sleep once nothing could stop it
- [x] Connector 52/52 with no warnings (the 3 delivery, 5 policy and 1 legacy-drop tests went with their subjects); .NET 87/87 including the connector integration on a freshly built release binary; greenfield 1,496
- [x] Connector `src/` 9,174 → 8,816 against a target of about 8,300; the rest is R2.5, R3.7, R3.9 and R3.10

Check command: `cargo test --manifest-path src/server/mcp/Cargo.toml`, then `pwsh scripts/check-greenfield.ps1`.

**Completed: R1.12 — one enrollment path**

Per C1 and [ADR 0012](../../adr/0012-realigned-connector-architecture.md) §1. Inventory taken first, because two of the names look deletable and are not:

- `ParticipantCredential` **stays** — it is the session the bound exchange issues (`ServiceProofExchange`), and `ActivityService` and `ExperienceDigest` read it. The glossary renames it `Session` in R2.
- `ParticipationCredentials` **stays in part** — `Authenticate` and `Principal` back the bearer handler. Only `Enroll`, `List` and `Revoke` go with the controller.
- `CredentialInfo` and `CredentialIssued` **stay** — the bound exchange returns them.
- `CredentialEnrollmentRequest`, `CredentialRevocationRequest` and `ParticipationController` have no other caller and go.

- [x] Server: deleted `ParticipationController`, the two request DTOs, `ParticipationCredentials.Enroll/List/Revoke` and `ParticipationAccess.EnrollmentParticipant` if it is left without a caller; trim `ParticipationTests` to what survives
- [x] Connector: deleted the unbound tier (`enroll_unbound`, `enroll-unbound`, its payloads and wording) across `hub.rs`, `identity.rs` and `main.rs`
- [x] Connector: deleted the app-password binding (`bind_atproto`, `createSession`) across `atproto_oauth.rs`, `contract.rs`, `hub.rs`, `identity.rs` and `operator.rs`; the OAuth bind is the only binding path
- [x] Connector: deleted session import (`enroll --token-file`) from `main.rs`
- [x] Tests: the connector journeys seed state directly instead of enrolling unbound; the fake's unbound route goes; the .NET connector tests enroll through the bound exchange against the fake account server
- [x] README and the wording that names the removed paths
- [x] Connector 94/94, .NET 349/357 (failures must equal the 8 known), greenfield check; commit

Check command: `cargo test --manifest-path src/server/mcp/Cargo.toml`, `dotnet test tests/TangentSpace.Tests/TangentSpace.Tests.csproj`, then `pwsh scripts/check-greenfield.ps1`.

**Completed: R1.11 — harden the companion manager**

Per C8 and [ADR 0012](../../adr/0012-realigned-connector-architecture.md) §8: the companion manager is a hardened loopback page. Two rules, stated once at the connection and applied before routing:

- Every request must name a loopback `Host` (`127.0.0.1`, `localhost` or `[::1]`, with any port or none). This is the DNS-rebinding defence, and it covers reads as well as writes.
- Every `POST` to `/api/*` must carry `Content-Type: application/json`, an `Origin` equal to `http://{Host}`, and — when the browser sends one — `Sec-Fetch-Site: same-origin`. This mirrors the server's credential endpoint (`ParticipationController.Cookie`), so both sides read the same way.

`html_routes` keeps no POST surface at all, so its 404 stays the refusal there.

- [x] `operator.rs`: reads `Host`, `Origin` and `Sec-Fetch-Site` in the header loop; refuses a non-loopback host and a cross-site write before routing; a body classifies as JSON only on `application/json`, so a `text/plain` POST no longer parses as JSON. `RequestBody::Form` became `Other`: the rule is now "JSON or nothing", not "anything but a form"
- [x] `experience.rs`: `probe` asks `/api/discovery` and requires the connector's own `product`, instead of counting any HTTP response as the page. The marker is one shared constant, `CONNECTOR_PRODUCT`
- [x] `hub.rs`: the default page candidate is injectable (`with_default_page`, following R1.10's `with_pages`)
- [x] Tests: the operator journeys send the page's `Origin` and `Sec-Fetch-Site`; `the_companion_manager_refuses_foreign_hosts_and_cross_site_writes` covers a rebound `Host` on a read, a cross-site `text/plain` write, a foreign `Origin`, a cross-site `Sec-Fetch-Site` and a write with no origin, and proves the page's own write and cross-origin discovery still work; the dead-page test injects its own dead default
- [x] README: the MCP configuration example drops the override, so state stays in the user profile by default, and says what the override must not be (N-036)
- [x] Connector 100/100 (the new case), .NET connector integration 3/3 on a freshly built release binary (N-037), greenfield 1,884 unchanged, no new clippy lint

Check command: `cargo test --manifest-path src/server/mcp/Cargo.toml`, then `pwsh scripts/check-greenfield.ps1`.

**Completed: R1.8 — verify R1 on a fresh install**

- [x] Announce the wipe; back up `.local/docker/site` as the [Docker guide](../../DOCKER.md) describes and record the backup path here. Announced 2026-09-15; backup `.local/backups/docker-20260915-134736-993` (164 files with a hash manifest). After the claim fix (N-025) a second backup, `.local/backups/docker-20260915-141626-412`, preceded a second wipe
- [x] Wipe, build (`Build.bat`), launch (`Launch.bat`); confirm `/health/ready`. The first build failed on a stale Dockerfile line (N-024). After the fix the image and connector built in 36 s, a fresh configuration was created and the app was healthy at 09:51. The startup log holds only the Data Protection key-encryptor warning (expected locally; see DOCKER) and the `HTTP_PORTS` override notice
- [x] .NET, browser, lifecycle and connector suites; greenfield check. At `5170f1b`: .NET 338/346 (only the 8 known failures), browser 125/125, lifecycle 76/76, connector 98/98, greenfield 1,883
- [x] Walkthrough W1–W10 with Leo, who did every sign-in; results under [Walkthrough results](#walkthrough-results). All ten pass. W1, W3 and W5 each failed first and produced a fix (N-025, N-027, N-029); W2, W6, W9 and W10 recorded findings for later slices (N-026, N-034, N-035)
- [x] Re-ran every check at `f223a1b`, after the walkthrough's fixes: .NET 351/359 with exactly the 8 known failures, browser 129/129, connector 99/99, lifecycle 76/76, greenfield 1,884, server C# 11,333. Metrics, CURRENT_STATE's top section and the session log updated; committed

Check command: `dotnet test tests/TangentSpace.Tests/TangentSpace.Tests.csproj`, `node --test "tests/*.test.mjs"`, then `pwsh scripts/check-greenfield.ps1`.

## The bug the known failures used to mark

R1.15 deleted the eight permanently-red tests with their files. The defect they marked is real and still open, so it is recorded here instead of as a red suite: **Topic authority assigned through `RoomRole` membership rows does not change effective permissions**, because `TangentRoleAccess.ProjectTopic` decides them from Koan role bags. Live consequence: the room-membership endpoints silently do not change what a participant may do. R3.4 owns the fix and writes the tests that prove it, against the Access evaluator of D11.

The originals, if the assertions are wanted: `git show 88f470f -- tests/TangentSpace.Tests/ConversationPermissionEnforcementTests.cs tests/TangentSpace.Tests/ModerationCaseTests.cs`.

## Metrics

| Measure | Baseline (`d682c26`) | Now (R2.6) | Target |
|---|---|---|---|
| Server C# lines | 16,830 | 11,348 | about 11,000 |
| Authenticated API families | 5 | 3 (`/api/v1/experience`, `/api/v1/tangents`, legacy REST) | 1 |
| Persisted entity types | 30 | 25 | about 20 |
| Global lock entries, direct / via `WithCurrentPolicy` | 49 / 31 | 44 / 18 | 0 |
| `EntityContext.Transaction` outside the pipeline | 41 | 38 | 0 |
| `bool authorized` parameters | 9 | 8 | 0 |
| `EnsureHome` call sites | 17 | 0 | 0 |
| `Mcp` folder lines | 3,697 | 358 (enrollment) | 0 |
| Greenfield findings (lines): total | 4,132 | **507** | 0 |
| — inbound MCP / WebMCP / Spaces / classification | 712 / 20 / 220 / 38 | 30 / 0 / 21 (`SourceDecision`, R3.6) / 0 | 0 |
| — retired vocabulary / historical markers / bootstrap | 3,095 / 29 / 18 | 1,831 / 8 / 0 | 0 |
| .NET tests | 506 of 518 (12 known failures) | **87 of 87** | all pass |
| Browser tests | 170 of 170 | **7 of 7** | all pass |
| Connector tests | 98 of 98 | **52 of 52** | all pass |
| Connector Rust lines (baseline at `deb70ab`) | 9,489 | 8,820 | about 8,300 |
| Connector enrollment / account-binding paths | 3 / 2 | 1 / 1 | 1 / 1 |
| Connector source without a working path | about 850 | 0 | 0 |
| Mutexes in `ConnectorHub` | 10 | 10 | no hub |

Recompute with the commands in note N-008 and the greenfield check.

## Walkthrough results

Steps are defined in [EPIC-007](../EPIC-007.md#common-acceptance-walkthrough). Record `pass`, `fail` (with a note) or `n/a`, and who ran the step.

| Step | R1 | R2 | R3 | R4 | R5 | R6 |
|---|---|---|---|---|---|---|
| W1 Fresh install, claim, first Tangent | pass on the second attempt: the first found N-025. Leo signed in; Claude claimed and named "Workshop" | | | | | |
| W2 Topic created and made public | pass: Claude created "Open questions" and set "Anyone on the web"; the signed-out page answers 200 (N-026) | | | | | |
| W3 Post, reply, edit, remove; edit history | pass after R1.9: Claude posted, replied, edited (history shows the original, without scores) and removed through the inline confirmation; the first removal attempt opened a native dialog (N-027) | | | | | |
| W4 Participant and group mentions reach catch-up | pass: Claude posted a participant mention (@ox-omega.bsky.social, rendered as a link) and a group-only mention (@members, rendered as a chip); both reached the agent's catch-up addressed to it. The digest labels both `direct_mention` (N-033) | | | | | |
| W5 Agent enrolls, reads, posts, gets attention | pass: after N-029 the agent enrolled against Bluesky, joined Workshop once granted Member (N-030), read Open questions, posted (p7, p8 and 30 paging posts) and received both W4 mentions through `GetUpdates`. The expanded view listed the mention twice, as the connector assessment predicted (R1.13) | re-run at R1.14 on the rebuilt install: the saved session survived (a rebuild is not a wipe, so N-029 did not apply), `Connect` answered without a sign-in, the agent read Workshop / Open questions, posted p41 and received both W4 mentions. The duplicate is gone (N-043) | | | | |
| W6 Report, case list, defer, escalate | pass: Claude reported the agent's post through the inline report panel; the case appeared in "What needs care" as open, was deferred to the next day (preview, then apply) and escalated to the human Host (revision 3, `escalated`). UI notes in N-034 | | | | | |
| W7 Signed-out permalink with older/newer paging | pass, over HTTP without cookies: the permalink of "Paging check 15" returned the public page (200, public-document headers) with posts 03–27, "← Older posts" (`?before=10`) and "Newer posts →" (`?after=34`); the newest page offers only older posts and the oldest page only newer ones | | | | | |
| W8 Live post in another tab keeps a draft | pass: the agent's second post (p8) appeared live in Leo's open Topic, marked by the "1 new post" bar, while an unsent draft stayed in the composer | | | | | |
| W9 Account switch re-renders | pass: Claude signed Leo out and Leo signed in as Lumen (@lumen-bubbles.bsky.social); the header and home page re-rendered for that account. The same visit exposed the arrival gaps in N-035 | | | | | |
| W10 Container restart keeps state and sessions | pass: after `docker compose restart tangent` (ready in 7 s) Leo's page reloaded still signed in with every post and the escalated case; the agent's connector session answered `GetUpdates`; the signed-out Topic page served the newest posts; the log since the restart holds no errors | | | | | |
| W11 A stranger arrives, reads, self-enrols and applies | n/a (the front door lands in R4.7) | | | | | |

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
- **N-010** (2026-09-15) The 12 baseline failures, deleted in R1.15 and [recorded as prose](#the-bug-the-known-failures-used-to-mark), showed the stacked permission models in action: membership roles and Koan role bags disagree, and the role bags win.
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
- **N-028** (R1.10) Leo saw random ports such as 59998. Connector tests set and restored the process-wide `TANGENT_CONNECTOR_NO_BROWSER` around each test, but `cargo test` runs tests in parallel, so one test's restore could clear it while another popped a page: Chrome opened at the tests' fake `127.0.0.1:59998` address or at their OS-assigned listener ports. The hub now opens pages only through an injected `PageOpener` (silent unless `build_hub` installs the platform browser), the env guards are gone, the fake addresses use fixed ports, and the connector refuses port 0 (`--port`, `TANGENT_CONNECTOR_PORT`). Spawned test connectors get `NO_BROWSER` and their own fixed page ports (5229 for .NET, 5230 and up for the stdio journeys). In-process test fakes still bind OS-assigned loopback ports; nothing shows them now.
- **N-029** (R1.8, W5) N-018's derivation was wrong for atproto: Bluesky refused `did:web:127.0.0.1%3A5220` ("aud must be a valid atproto DID or did#serviceId reference"). atproto's did:web profile admits a hostname and allows a port only for localhost. A loopback origin now derives `did:web:localhost%3A<port>`, a hostname on its default port derives `did:web:<host>`, and an IP literal or other port derives nothing; `ProofAudience.IsAtprotoAudience` validates the configured override at startup. `EnrollmentTests` passed with the old value because their fake account server does not enforce the profile. The first reconnect also showed that after a server wipe the connector's saved enrollment is dead: Connect blocks with "the operator must renew this enrollment's session", and `forget` then Connect enrolls afresh.
- **N-030** (R1.8, W5) A newly enrolled participant sees no Tangent. The home Tangent is created "open to signed-in" (`TangentCommunity.Home`), but its access map inherits See = @Member, and a new participant holds no Member role until the owner grants it. The admission flag and the access map disagree — another instance of N-010's stacked permission models. R3.4's Access evaluator decides what "open to signed-in" means; the walkthrough grants the agent Member through the Roles UI.
- **N-031** (R0.9) Leo accepted C1–C9 with the standing rules and the server's architecture concepts: a DDD monolith, clear separation of concerns and the fewest meaningful moving parts, simple but not simplistic. Two findings shape the connector tasks. The waiting `Connect` that finishes by itself once the operator signs in was Leo's own addendum, so R3.9 keeps it, inside the Connect use case and without its sweeper thread. `ExperiencePort` has one implementation, and the tests reach the same `ureq` client over TCP, so by rule 3 R3.9 makes the Tangent client concrete. Leo also asked to take greenfield realignments where they fit: the crate leaves `src/server/mcp` for `src/connector` in R2.5.
- **N-032** (R0.9, environment) The walkthrough's connector state holds a lock from process 36356 (12 September), which no longer runs. CLI calls ignore it, but `serve` and `operator` refuse to start without `--force`. R3.7 removes the lockfile; until then, start the manager with `--force` if the walkthrough needs it.
- **N-033** (R1.8, W4) The digest labels group mentions `direct_mention`, like participant mentions (`ExperienceDigest.cs` lines 146 and 157). The recipient sees the post addressed to them, so W4 passes, but no client can tell "you were named" from "your group was named", and the connector renders both as "asked you". R5.2's attention projection gives group mentions their own kind.
- **N-034** (R1.8, W6; UI findings for R5) The defer preview states its time as a raw UTC timestamp ("2026-09-16T19:22:00.0000000+00:00") where the rest of the page shows local times. The case form's label wraps its Action list, so the list's accessible name reads out every option ("Action Defer and revisit Escalate to the human Host"). Two visible buttons are both named "Refresh".
- **N-035** (R1.8, W9) Arrival fails for a signed-in participant who belongs nowhere. Lumen's home page said only "No Tangents are available to this account yet", and the public Topic "Open questions" answered "This conversation isn't available · Not Found" while the same URL served every post to a signed-out visitor (W7): signing in shows less than signing out. Causes: the Tangent directory lists only Tangents where the viewer holds a role, so discovery is fused with membership (N-030); signed-in page loads use the member API, which decides from role grants and ignores the Topic's public audience, while the public page serves only signed-out visitors; the browser offers no Join, although `PUT /api/v1/experience/tangents/{tangent}/membership` exists; the signed-out landing shows no public content. The mandates ask the opposite: arrival answers where am I, who am I here, who can see this, what can I do and how do I return; reading public discussion never requires joining; public landing pages show value before asking. **Settled by Leo on 2026-09-15 as D11 and D12 ([ADR 0013](../../adr/0013-access-contract-and-role-model.md)), with the arrival model below.**

Arrival is one page with four slots, not three page designs — so it degrades gracefully and covers the combinations the three cases leave out, such as an open server that also offers self-enrollable roles:

1. **The server card.** Always present, signed in or out: the BBS hero is the face of the server and answers "where am I" before anything is asked. It already exists ([ADR 0004](../../adr/0004-page-routes-and-editorial-heroes.md)).
2. **Open content.** Tangents and Topics readable by `everyone`, listed for anyone — discovery is never fused with membership again.
3. **Self-enrollable roles.** Roles the owner or an administrator marked auto-enrollable. Toggling one enrols or leaves, instantly and reversibly, and the visible Tangents change with it. Leaving never deletes what the participant wrote. Widening an auto-enrollable role's reach is an explicit, warned action, so a role cannot quietly become a key to something private after people have taken it.
4. **Ask to join.** Where nothing else admits, a request. This is the smallest slot to build: `TangentAdmission.Approval`, `TangentJoinRequest`, `ListJoinRequests` and `DecideJoinRequest` already exist in `CompanionGovernance`; nothing surfaces them, and `rooms.js` renders admission as only two states so `Approval` is invisible even where it is set. The reviewing surface is the Applications queue.

- **N-036** (R1.11) Requiring `Origin` on `/api/*` writes costs the page nothing — a browser attaches it to every POST — but it does bind the surface to browsers: any future non-browser caller must send the page's own origin, as the connector's tests now do. That is the intended shape. The companion manager is the page's API, not a local IPC channel; the CLI and MCP intakes reach the same hub directly. The state directory keeps its own separate weakness: `state.json` holds bearer sessions, OAuth refresh tokens and DPoP private keys in plain text, and `store.rs` narrows directory permissions only where the platform offers them, so on Windows the protection is the user profile's own ACL. The README now says so; narrowing a Windows directory itself was not part of C8.
- **N-037** (R1.11, environment) `ConnectorIntegrationTests` prefers `target/release` over `target/debug`, so a debug-only `cargo build` leaves the .NET connector tests exercising a stale binary — they passed here against 11:27 code before the release build was made. Any task that changes the connector must run `cargo build --release` before them, as `Build.bat` does.

- **N-038** (R2.1, naming) Naming the server concept **Space** collides with the deleted atproto Spaces storage in exactly two places, both in the greenfield check: `\bSpaces[A-Z]\w*` would flag any plural identifier such as `SpacesController`, and `\bSpaceState\b` is banned outright — a plausible name for a Space's own state. Every remaining finding under that rule is `SourceDecision` (21 lines, removed in R3.6), so the rule can be narrowed to `\bSourceDecision\b` with no loss the moment R2.1 starts, and deleted entirely after R3.6. Do the narrowing before the rename, or the check will report the new vocabulary as the old one. The rule set already encodes the rest of this rename — `TangentSite`, `TangentCommunity`, `room*` and `Message` are listed as retired vocabulary — so R2.1 gains a destination word, not scope.

- **N-039** (R1.12) Two consequences worth recording rather than hiding. First, greenfield rose 1,884 → 1,890: the .NET connector tests now seed the connector's state file, whose wire fields are `companion_id`/`companionId`, which the retired-vocabulary rule counts. The wording is the connector's own and clears when R2.5 renames its glossary; the alternative was leaving those tests driving a deleted CLI verb. Second, the connector journeys seed a `sat_`-shaped PDS session, because minting an OAuth-shaped one needs `atproto_oauth`'s private key encoder — so the seeded shape is one the product can no longer produce. `bind_oauth_journey` still drives the real flow end to end, and R6.1 reconciles the fake with the real server. When it does, `AtprotoSession.dpop_key` and `refresh_jwt` can stop being optional: only the OAuth bind creates sessions now.

- **N-040** (R1.15) The test inventory behind Leo's judgement, counted on 15 September. **656 tests**: .NET 357, browser 129, connector 94 (plus about 35 unit tests inside its source), lifecycle 76.

  The suites did not find a single defect this slice. Every one came from the walkthrough: N-025, N-027, N-029, N-030, N-035. Meanwhile R1.12 deleted about 300 lines of product code and cost a session of test rework, including an assertion that pinned `Bearer sat_{port}_lumen.bsky.example` — a string the fixture itself invented.

  The concentration is the point. Tests attached to components this epic has already accepted replacing: `TopicPermissionEvaluatorTests` 15 and `AccessMapTests` 5 (R3.4 and D11 replace both), `RoomRulesTests` 13 (R2.1 renames `Room`; the file alone holds 102 greenfield findings), `ParticipantLabelWindowTests` and `WindowPlannerTests` 13 (R5.1 leaves one window), and in the browser `window-contract` 20, `activity-transport` 21 and `room-recovery` 13 (R3.5 and R5 replace their subjects). The eight known failures live in exactly two files, both of which R3.4 and R4.1 rewrite. The connector's fake is **1,471 lines**, over half the size of the five journey files it serves, and C7 already records that it has drifted from the real server.

  **A correction, on reading them.** The first pass counted the browser's two red-team files as mechanism because they sit on doomed APIs. They are not. `window-red-team` (11) and `activity-red-team` (21) pin safety properties: a forged structurally equal ticket does not bypass object-capability identity; a forged SSE id cannot poison the fallback cursor; a 403 fallback is terminal and late requests cannot revive the old actor; oversized headers are refused before decoding; aggregate memory stays bounded across Topic churn. Those are rule tests wearing mechanism clothing — the **property** is the asset and the **coupling to today's API** is the liability. R5.1 does not delete them; it re-expresses all 32 against the one window that remains, and R1.15's deliverable for these two files is the property list that re-expression is checked against.

  **A second correction, and then a correction to that.** The audit first proposed cutting the lifecycle suite from 76 to about 8, then reversed on seeing that its 76 are `Assert-That` calls inside a 239-line file guarding `Wipe.bat` — a real destructive operation — and left it whole. Leo asked the right question: why does a wipe script need guarding at all? Reading the sections instead of counting them: only three of the eight hold the irreversible risk — target resolution refusing the allowed root, the repository root, a filesystem root, a `..` traversal and a junction escape; the confirmation rules; and the wipe itself against a scratch tree. The rest asserted that a development script calls docker with the strings it calls docker with, that a `.bat` propagates an exit code, and that PowerShell files parse. Those five sections went: **76 → 36**, the file 239 → 134 lines.

  The deeper answer is that the script earned its own test surface. `Wipe.bat` has one job — delete `.local/docker/site` — but takes a target, resolves it, and reasons about junctions, so it *can* escape and therefore must be proved not to. R1.16 removes the parameter; a wipe that only ever deletes one fixed path cannot traverse anywhere, and the remaining guards become unnecessary rather than merely passing.

  Twice here a raw count pointed the wrong way, and once a safety word did. The lesson worth keeping: **the count diagnoses, it does not decide — and "destructive" is a reason to read the test, not to keep it unread.**

  So the decision is not how many tests to keep but **who owns each one's deletion** — and, for the red-team pair, who owns carrying their properties forward. Anything frozen must not be carried through R2.1's renames beyond what keeps it compiling, or the epic pays to rework assertions that R3.4 then deletes. Three rule tests are missing and would have caught this slice's defects: an operation's own side effects are never refused by its own guard (N-025); a Tangent open to signed-in admits a signed-in participant (N-030); and a signed-in participant never sees less than a signed-out one (N-035). All three are red until R3.4 lands, so they are written there, as its acceptance, rather than added now against the known-failure set.

- **N-041** (R1.13) C9's list named symptoms; two of them had roots. **The automatic-turn policy** was not a flag but a subsystem: `AttentionPolicy` carried five fields, `PolicyLedger` tracked a daily allowance per companion and persisted in `state.json`, and `TurnDecision` spelled out four outcomes — all reached by nothing. The single live path through the module is `store.policy().poll_seconds` in `main.rs`, so `policy.rs` went from 170 lines to 15 and the store lost its `ledgers` map. **`StopFlag`** was the opposite mistake: `spawn_checkers` *is* called, but its `Vec<StopFlag>` return was discarded, so the flag never flipped and `run_checker` polled it once a second forever to ask a question with one answer. Deleting it turned a per-second wake loop into one sleep. The lesson for the remaining C9-style tasks: read what the named identifier is attached to, because "delete `StopFlag`" and "delete the automatic-turn policy" were entries of the same size in the list and differ by an order of magnitude in the tree.

- **N-042** (R1.16) Removing the target parameter did not remove the need for every guard, and the tension is worth stating: **a wipe with a hardcoded path cannot be exercised by a test at all**, which is why the parameter existed in the first place. The answer was to move the seam rather than delete it. `AllowedRoot` stays injectable — production passes `<repo>/.local/docker`, the suite passes a scratch root — and the target under either is always `site`. That removes the arbitrary-path surface while keeping the wipe testable, and it splits the guards cleanly. Unreachable now, and deleted: traversal out of the root, a target outside it, the allowed root itself, the repository root, a nested target. Still reachable, and kept: a junction planted at the state directory, an allowed root that is itself a junction, a filesystem root as the allowed root, and a plain file where the directory belongs — a recursive delete would still follow a reparse point out of the tree. Lifecycle 36 → 30.

- **N-043** (R1.14) R1.13 was marked done having delivered only its deletions: C9's clause **"each attention item is rendered once" was never addressed**, and W5's re-run is what caught it. `push_attention` renders the digest's items and `push_pending` renders the connector's own records of the same Posts, and neither consulted the other — so whenever both were populated the same Post appeared twice, exactly as the assessment described and as the first W5 observed. `push_attention` now returns the source refs it rendered and `push_pending` skips them. Honest limit: the current state did not reproduce the duplicate before the change, so the fix is correct by construction rather than proven by a before-and-after. A second, separate defect is still open and is **server-side**: the expanded view ends with "Read leo.sylin.org's request; Read leo.sylin.org's request" — two actions for two different Posts carrying identical labels, so nothing downstream can tell them apart. The labels come from the server's action list, not the connector's rendering; R5.2's attention projection is the natural owner.

- **N-044** (R2.1) **Koan names tables by the fully-qualified entity type**, so renaming a persisted entity strands its rows rather than moving them. After stage 1 the database holds both `TangentSpace.Site.TangentSite`, with the real data, and a freshly created, empty `TangentSpace.Site.Space` that the app now reads — so the install presents as an unclaimed Space while its rows sit intact one table away. This is not a defect to patch: the standing wipe rule forbids migration code, and R2.6's wipe is the planned resolution. Two consequences worth stating. First, the remaining stages rename `TangentCommunity`, `Message` and `Room` — every one a persisted entity — so each will strand more tables, and nothing between here and R2.6 should be judged by what the running app shows. Second, this has already happened in this repository: `TangentSpace.Activity.WatchSetting` and `TangentSpace.Activity.TangentWatchSetting` both exist, the residue of an earlier rename. Backup before the stage-1 relaunch: `.local/backups/docker-20260916-051128-748`.

- **N-045** (R2.1) The product's nouns are short, and short nouns collide with the methods that name them. `References.Tangent(string)` issues a Tangent reference, so inside `References` the type `Tangent` resolves to that method and `Tangent.CheckKey(key)` stopped compiling — C#'s "Color Color" problem, arriving the moment `TangentCommunity` lost its prefix. The fix is one qualified call site, `Communities.Tangent.CheckKey`, not a renamed method: `References` issues references and its member names are the glossary's. It will happen again, and the site is already identified: `References` also has `Post(string, string, string)` and `Topic(string, string)`, and `IsTopicKey` two screens below calls `Room.CheckKey(key)` — which stage 4 turns into `Topic.CheckKey(key)`, the identical collision on the line after this one. The compiler finds these; the sweep does not.

- **N-046** (R2.1) A rename sweep is safe on identifiers and dangerous on string literals, because the compiler checks one and not the other. Two literals in stage 3 looked like type references and were not. `ExperienceService`'s replay check reads `record.Operation is "CreatePost" or "PostMessage"` — a **stored** operation name on a receipt, which the sweep rewrote to `"PostCreateRequest"`, a value nothing has ever written or will write; replay of any `PostMessage` receipt would have silently stopped matching. And `MongoProbe` compares an exception against Koan's own message text, `"The adapter backing Message does not expose a proved native atomic batch boundary…"`, which is the framework's string, not ours to rename. Both were restored. The method that found them is worth keeping for stage 4: extract every string literal from the `-` and `+` sides of the diff, sort each, and `comm` them — what is left is exactly what the sweep changed inside quotes, short enough to read one by one. Twenty-five literals changed in stage 3; twenty-three were prose or paths and correct, and two were these. `"PostMessage"` stays until R3.2, which owns operation receipts.

- **N-047** (R2.1) `probes/` compiles against the server and was nearly missed. A first check for `TangentSpace.csproj` under `probes` came back empty and was wrong — MongoProbe, PostgresProbe and ScaleProbe all carry `<ProjectReference Include="../../src/server/web/TangentSpace.csproj" />`, use the `Message` entity directly, and hardcode its Koan table name as `"TangentSpace.Conversation.Message"`. No build script builds them, so nothing would have reported the breakage; the entity renames of stages 3 and 4 have to carry them, and stage 3 did. **Read the project file, not a grep, before concluding a tree does not depend on the server.** Separately, `probes/` is EPIC-001 S01 residue and the greenfield check does not scan it: its README still documents the deleted `spaces-network` and the deleted OAuth driver, `AtprotoClientProbe` still describes Spaces operations, and `probes/SpaceVerificationProbe/` holds nothing but `bin` and `obj` — a project deleted without its build output. That is R1.5-era cleanup the check cannot see. **Leo deleted `probes/` on 2026-09-16**, with R5.7 in view: 46 files, the last thing outside `src/` that compiled against the server, and the reason R2.1's and R2.3's renames had to carry a tree no build script builds and no suite runs. R5.7 measures through the real API instead. The doc links left behind point at `probes/spaces-network`, which R1.5 had already deleted, so they were broken before this and belong to R6.6 with the rest of the evidence index.

- **N-048** (R2.1) Stage 4 needed two passes, and the order was forced rather than chosen. `Room.Topic` held a Topic's description text, so renaming the type first would have produced `Topic.Topic` — the shadowing of N-045 again, this time between a type and its own property. The description became `Description` first, while the type was still `Room`, and only then did the type rename run. The **entity** carries `Description`; `TopicDescription` and `PublicTopicDescription` still carry the wire name `Topic`, because `access.js` reads `resource.topic.topic` and posts `{ topic }` back. Server and browser move together in R2.4.

  The literal audit paid for itself a second time. `ConversationController`'s `Route("api/rooms/{roomKey}")` became `api/topics/{roomKey}` — a live route `rooms.js` calls — because the protect list held the exact string `"api/rooms"` and this was a different, longer literal. Reverted. The lesson is narrower than "protect the routes": **an exact-string protect list silently misses every longer literal that contains it.** Prefer a pattern, or re-read every changed literal afterwards, which is what actually caught this.

  Two transaction names were renamed on purpose: `"tangent-room-administration"` and `"tangent-room-policy-operation"` are `EntityContext.Transaction` coordinator names, in-process and not persisted, and three probes hardcode the second to observe the server's coordinator — so server and probes moved together in one commit.

- **N-049** (R2.1) R2.1's "Done when" — *retired vocabulary count is zero for the server* — **cannot be met by R2.1**, and recording that is more useful than marking the task green. The four nouns are renamed and `\bRoom\b` is zero, but 810 findings remain, and every one belongs to a task that already exists:

  | Residue | Count | Owner | Why it could not move in R2.1 |
  |---|---|---|---|
  | `RoomKey` / `roomKey` | 303 | R2.4 | An SSE event field `activity-transport.js` validates, and a route-bound parameter name |
  | The `TangentSpace.Rooms` namespace | 82 | R2.3 | Folder and namespace moves, by stage 1's precedent |
  | `wwwroot/` (`rooms.js` 145, `rooms.css` 41, the rest) | about 330 | R2.4 | No stage touched the browser: R1.15 deleted the UI suites, so a mistake there is invisible |
  | `companion*` | 90 | R2.2 · R2.5 | The `CompanionGovernance` split and the connector glossary |
  | `channel*` | 168 | R2.4 | A wire field name |
  | `ActivityKind.RoomChanged`, `TopicListing.Rooms`, `"room-not-found"` | 5 | R2.4 | `rooms.js` compares that kind string and reads that `rooms` field |

  The count reaches zero at the end of **R2**, not at the end of R2.1. The row's wording predates the wire boundary, which stage 1 discovered the hard way when it moved `/api/site` and had to revert.

- **N-050** (R2.2) The split is by **file, not by type**, and that was a decision rather than the easy path. Mapping which helpers each group uses showed that `LoadTangentContext`, `RequireSteward`, `RequireUnrestricted`, `Journal` and `TopicWithPolicy` are called by *every* group — membership, admission, restrictions, watches and classification alike. Together they are load the scope, check authority, refuse a restricted actor, journal the result: **a hand-rolled command pipeline**, which is exactly what R3.3 builds for real. Five separate services would have needed that substrate extracted into a collaborator, and R3.3 would then delete it — the waste N-040 warns about. Partial files put it in one visible place for R3.3 to replace, and the compiler proves the move changed no behavior, which matters because R1.15 removed the tests that would catch a mistake. It is also the convention already in this codebase: `ConversationService` and `ExperienceService` are split exactly this way. **If the intent was five services, this is the point to say so** — the substrate is now isolated in one file, so that change is cheaper after R3.3 than before it.

  The row named four responsibilities; the file held **five**. Scoped restrictions (`SetRestriction`, `GetRestriction`) are neither membership nor admission, and by [ARCHITECTURE](../../ARCHITECTURE.md#modules) they belong to Stewardship, not Community. They have their own file now, which is the honest intermediate state; R2.3 moves folders and R4.1 moves the family onto the pipeline.

- **N-051** (R2.3) `org.sylin.tangent` was the right idea at the wrong layer, and checking the local precedent settled both halves. Reverse-DNS is the Java and atproto convention; .NET namespaces are PascalCase `Company.Product.Feature` — and **Koan, which is sylin-org's own framework and this app's dependency, namespaces itself `Koan.Data.Core`**, with no company segment at all. So `Tangent.*` is not a shortcut past the convention, it *is* the org's convention (D14). Reverse-DNS belongs to the protocol identifiers instead, where exactly one lives today (D15).

  The root rename collides with the `Tangent` type that R2.1 stage 2 created, and the shape of the collision was measured rather than assumed, with a throwaway project. Only one form breaks — a **bare** `Tangent` referenced from a different `Tangent.*` namespace, where the root namespace shadows the type (`CS0118: 'Tangent' is a namespace but is used like a type`). Three forms keep working: a bare `Tangent` inside the namespace that declares it, a qualified `Community.Tangent` from anywhere, and — the N-045 shape — a *method* named `Tangent` inside `Tangent.Application`. That bounds the work at 40 sites in 12 files, and R2.3's own merge of `Communities/` and `Rooms/` into `Community` takes 19 of them inside the type's namespace, leaving about 21 in 10 files. Every one is a compiler error, which is the opposite of N-046's failure mode.

- **N-052** (R2.3) The move landed 181 files in ten modules and N-051's prediction held: every collision was a compiler error, and three rounds of read-the-error-and-fix converged. What is worth keeping is the four things that were *not* the predicted collision.

  **A rename nearly changed a persisted security scope.** `TangentModule` called `.SetApplicationName(nameof(TangentSpace))` — using the old root namespace as a convenient spelling of the application name. That name scopes every Data Protection payload, sign-in cookies included, so letting the rename carry it to `Tangent` would have signed out every participant and expired every cursor, silently, as a side effect of moving folders. It is now a named constant holding `"TangentSpace"`, with a comment saying what changing it costs. `nameof` over a namespace is the trap: it reads like a constant and moves like a name.

  **Merging a child namespace into a sibling loses implicit visibility.** Files in `TangentSpace.Rooms.Web` saw `TangentSpace.Rooms` types with no `using` at all, because C# searches enclosing namespaces. Moving them to `Tangent.Api` broke every one of those references at once — most of the first round's 80 errors. Nothing was wrong with the code; the old namespace had been supplying an import invisibly. Any later module move should expect this the moment a `*.Web` child is re-parented.

  **A nested type can shadow a namespace segment in a rewrite index.** `LiveSessions.Identity` is a record; the index that mapped type names to modules therefore mapped `Identity` to `Tangent.Activity`, and rewrote `using TangentSpace.Identity;` into `using Identity;` in six files. The tell was a single-segment `using`, and grepping for `^using [A-Z]\w*;` across the tree found all six in one pass — a cheap check worth repeating after any namespace rewrite.

  **Three more of N-045's family surfaced, and none were bare `Tangent`.** `References.Topic(string, string)` shadowed the `Topic` type exactly as it had shadowed `Tangent` in stage 2 and been predicted to in stage 4 — third occurrence in the same file. `TopicDescription` has a `Permissions` property that shadows the `Permissions` class, which is *why* the original code carried a fully-qualified `TangentSpace.Authorization.Permissions.Topic(policy)`; stripping qualifiers to their bare type names removed a qualification that was load-bearing. The lesson for R2.4: **a long qualified name may be doing work, and the compiler only tells you after you remove it.**

  Also done here: `<RootNamespace>` is now set in both project files (`Tangent`, `Tangent.Tests`), which was never set before and silently defaulted to the project name.

- **N-053** (R2.4) The greenfield check's channel clause was counting **.NET's `System.Threading.Channels`** as the product's retired word. Once the product's own channel vocabulary reached zero, all seven remaining matches were the BCL type, which is not ours to rename — so the rule had stopped measuring what it means to measure, exactly as the Spaces rule had in N-038. The clause now excludes `Threading.Channels`, `Channel<…>` and `Channel.CreateBounded`, and was checked both ways: `var channels` and `ChannelKey` are still flagged, the three BCL spellings are not. Without this, R6.6's `-Strict` could never pass.

- **N-054** (R2.4) Two live defects, neither of which any suite would have caught, because R1.15 left the browser with no wire coverage at all.

  **The browser was listening for three activity kinds the server has never sent.** `rooms.js` tested `event.kind` against `['MessageChanged', 'MessageEdited', 'MessageDeleted', 'PostChanged', 'PostDeleted']`, and `ActivityKind` contains only `MessageAccepted`, `MessageEdited` and `MessageDeleted`. Three of the five were dead. Worse, renaming `MessageDeleted` → `PostDeleted` would have made the dead `PostDeleted` branch *accidentally* live and left a duplicate — a rename quietly repairing a bug by coincidence is not a repair. The list is now `['PostEdited', 'PostDeleted']`, matching the enum. `MessageAccepted`/`PostAccepted` is deliberately absent: a new post appends and is served by the incremental paging branch, while an edit or a removal mutates rows already on screen and needs the full refresh.

  **Renaming a response field broke an internal call shape.** `listRooms` reads `listing.topics` now, but it is also called from inside `rooms.js` with an object literal — `listRooms({ rooms: topics })` — built by the browser itself rather than by the server. Renaming the server field silently orphaned both call sites. **A wire rename is not confined to the wire**: anything that constructs the same shape locally has to move with it, and the compiler says nothing about JavaScript.

  The method that found both is worth keeping. Server and browser cannot disagree about a word that appears in neither, so for a *total* rename absence is the proof, and the greenfield check already measures it. What absence does not prove is that both sides chose the *same* new word — so each renamed field was read back on both sides, and the DOM ids the browser resolves were cross-checked against the ids the HTML declares (a script that lists every `$('id')` with no matching `id=` in the markup; it reports four pre-existing dynamically-created elements, and the renamed `topic-details` was not among them).

  Routes stayed behind on purpose. `/api/rooms`, `/api/tangents` and `ConversationController` are **deleted** by R4.2, not renamed, so touching them here would be work R4.2 throws away — the N-040 trap. The one exception was `POST {tangentKey}/channels`, which the literal audit caught: it has **no caller** in browser, connector or tests (topic creation goes through `/api/v1/tangents/{id}/topics`), so the reason the boundary exists did not apply and the word went.

- **N-055** (R2.5) Pointing the greenfield check at the connector was worth more than the renames it will drive, because it found things no suite could. Three kinds.

  **Comments describing capabilities that were already deleted.** Four of the connector's six historical markers were not narration but *stale fact*: `hub.rs` and `identity.rs` both said legacy rows "are dropped at load", and R1.13 removed that sweep; `atproto_oauth.rs` contrasted a scope-era session with "a legacy one", and R1.13 removed the pre-scope client id that made one possible; `operator.rs` said "the hub-level password method remains the documented non-UI fallback", and R1.12 deleted `bind_atproto` outright. A deletion task that removes the code but leaves the comment leaves a reader worse off than before, because the comment still reads as current.

  **A label that outlived its action.** `operator.html` mapped `"operator.bind_atproto"` to "Account sign-in" in its attribution feed. Nothing has published that event since R1.12. Grepping each event name against its publishers found it in one pass, and is worth repeating whenever an action is deleted.

  **A dangling section header.** `identity_journey.rs` still carried `// ---------- legacy state ----------` with nothing under it: R1.13 deleted the two tests it introduced and left the heading.

  A fourth thing came from the check itself rather than the code. `companion` is **retired on the server and current in the connector** — ARCHITECTURE carries two glossaries and they disagree on that word by design. One pattern applied to both trees has to be wrong about one of them, so rules can now name the paths they do not govern, and `Server vocabulary` does not govern `src/connector`. The same shape will be needed again for any word the pair spells differently.

- **N-056** (R2.5) Renaming `companion_id` to `enrollment_id` changes the **connector's own persisted state**, and that state is not covered by the wipe. `Wipe.bat` deletes `.local/docker/site`; the connector keeps `state.json` in the operator's user profile, so an operator carrying a bound companion across this commit finds it will not load. The failure is clean rather than destructive — `Store::open` answers `state file is malformed: …` and stops, having written nothing — and the resolution is the one N-029 already documented for a server wipe: `forget`, then Connect enrolls afresh. R2.6's wipe forces exactly that anyway, so the cost is paid once and at a moment the plan already schedules. Recording it because it is the one rename in this epic whose consequence lands outside the repository, on a real machine's profile directory, where no wipe or test reaches it.

  Worth noting for R3.7, which rewrites the store: the state file has no version field — R1.13 deleted `StateFile.version` as written-once-never-read. That was correct then, and it is why this rename can only announce itself as malformed rather than as out of date.

- **N-057** (R2.5) Two lessons from stage 4, both about method rather than vocabulary, and both worth more than the rename.

  **A substring sweep can rebind a call silently when two functions share a signature.** After `identity` became `companion`, `store.remove_companion(&str)` existed twice in meaning — the identity remover and the enrollment remover — and `forget_enrollment` kept calling the one that now meant the wrong thing. It compiled, and it did nothing. The compiler cannot help here, and neither can a diff review that reads the *changed* lines: the broken line was **unchanged**. Only the journey tests caught it. The same sweep also turned `com.atproto.identity.resolveHandle` into an XRPC path that does not exist, in the fake server rather than the client, which is why it surfaced as a bind test failing rather than a build error.

  **Do not widen a rule while executing against it.** R2.5 asked for rules for plan-item codes and owner narration. Writing them was the task; widening them twice mid-task — first for "owner correction" and "owner decision", then for `P4`-style codes — manufactured work that was never asked for, and each pass found more. The rules are frozen from here: a gap found later is a note, not an immediate sweep.

  The related judgement, recorded so it is not re-made by accident: most of stage 4 edits `hub.rs`, which **R3.9 dissolves into use cases**. Vocabulary was still worth moving, because the words carry forward into that port; a *structure* built there would not have been, which is exactly the call R2.2 made the other way (N-050).

- **N-059** (R2.6 walkthrough) **First sign-in showed no profile: "Fetching your profile…" forever.** Leo hit it at the owner claim on the fresh install; a forced refresh filled the card in. Everything the server owes was already right — arrival writes the identity with the handle, commits, and schedules the capture (`Spaces/Arrival.cs`); `/me` answers a default carrying the handle; the SSE at `/api/profile-cache/events` even **replays current state on subscribe**, so there is no missed-event window. The break was that nothing ever subscribed.

  `activity-coordinator.js`'s `setProfiles` returned `typeof SharedWorker === 'function'` when it had no session yet — telling `profile-live.js` *the coordinator will deliver this*. `profile-live` therefore opened no stream of its own. But the session is created by `rooms.js` on `tangent:welcome`, and **`app.js` returns before dispatching that on the onboarding route** (`if (window.TangentOnboarding?.show(site)) return;`). So on the one page that must wait for the announcement, the announcement channel was never opened. A reload worked only because the capture had finished by then and `/me` answered `loaded` outright, needing no announcement at all.

  The comment stated its own assumption — "a profile selection can arrive before rooms has created its participant controller" — which is true on the app route and false on onboarding, where no controller is ever created. `setProfiles` now returns `false` with no session: the caller opens a direct stream and hands over on `tangent:live-ready`, which `profile-live` already implements (it closes the direct stream when `viaCoordinator` flips). The cost is one transient SSE; the alternative was a promise of delivery that could not be kept.

  **Not reproduced after the fix, and it cannot be from here:** the bug needs a first sign-in with no captured profile, and this install now has one. It is verified by reading the handover, not by re-running it. The next wipe is where it gets proved, so W1 should watch the owner card. The wwwroot is baked into the image, so the fix needs `./Build.bat` and `./Launch.bat` to take effect.

  Leo's framing is what located it: log in triggers capture, the read resolves on a default, and the SSE announces arrival. Three of those four were already built; the missing one was the subscription, and looking for *which step of that sequence was absent* found it faster than reading the rendering path did.

- **N-058** (R2.6) The `Connector vocabulary` rule now reports **9 false positives** and no real findings: stage 4 renamed `identityId` to `companionId`, which the rule still lists as retired from the days when it meant an enrollment. The code is right and the rule is stale. It is recorded rather than fixed, because N-057 froze the rules mid-epic and the first thing that happened afterwards was a temptation to widen one again. **R6.6 owns it**, together with the `-Strict` pass it blocks.

  Three smaller things the work turned up. `CompanionGovernance`'s class doc still described "the inbound MCP boundary", which R1.1 deleted — rewritten. `CarpaNet.Identity` was imported by the original file and used by none of it; each of the six files now carries only the usings the compiler proves it needs. And `TangentRole`'s comment mixed a real storage invariant with its own history — the invariant (`Member=0` and `Removed=1` are fixed) is kept, the history is gone. One `companion` remains in server C#: the problem code `"companion_unavailable"`, which the connector emits from `SelectCompanion` and `Arrive`. **R3.10 deletes both tools, and this entry should go with them.**

## Findings to route

None yet. Record Koan defects here with the framework revision, a reproducer, expected and observed behavior, severity and consumer impact, then ask Leo to route them.

## Session log

### S-001 · 2026-09-15 · Claude (Opus 5)

- Reviewed the server architecture at `d682c26`; wrote the assessment, EPIC-007 and this ledger; added a memory pointer to this file.
- Nothing was built, tested, committed or deployed.

### S-002 · 2026-09-15 · Claude (Opus 5)

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
- R1.10: the random ports Leo saw came from connector tests opening real browser tabs; the hub now opens pages only through an injected opener and the connector refuses port 0 (N-028).
- The derived proof audience became atproto-valid (`did:web:localhost%3A5220`, N-029, `deb70ab`); the agent enrolled, joined Workshop once granted Member (N-030) and posted; W8 passed.
- Leo paused development for a connector assessment in the server assessment's form: [ASSESSMENT_2026-09-15-CONNECTOR](../../ASSESSMENT_2026-09-15-CONNECTOR.md). Connector suite 99/99; clippy 10 lints; no code changed.
- R0.9: Leo accepted every connector recommendation (C1–C9); ADR 0012, ARCHITECTURE's connector section, EPIC-007 and this ledger carry them (N-031).
- Walkthrough completed: W4 and W5 (mentions reach the agent's catch-up, N-033), W6 (report, defer, escalate, N-034), W7 (signed-out paging), W9 (account switch, and the arrival gaps in N-035) and W10 (restart keeps state and sessions). The session was interrupted after the results were written but before R1.8's close-out.

### S-003 · 2026-09-15 · Claude (Opus 5) — in progress

- Resumed from an interruption: the working tree held W4–W7, W9, W10 and N-033–N-035 uncommitted, while Resume here still named W9 as the next action. Nothing was discarded.
- Re-ran every check rather than trusting the written numbers, per the resume protocol: .NET 351/359 with exactly the 8 known failures, browser 129/129, connector 99/99, lifecycle 76/76, greenfield 1,884, server C# 11,333. Every metric in the table was confirmed, including the connector's 9,489 `src/` lines.
- R1.8 closed: the walkthrough passes end to end and the server is 5,497 lines smaller than the baseline, against a target of about 5,000.
- R1.11: the companion manager refuses foreign hosts and cross-site writes, and identifies itself through `/api/discovery` instead of being taken on trust. Connector 100/100; .NET connector integration 3/3 on the release binary (N-037); greenfield 1,884.
- Leo settled the access contract and the role model in conversation: D11, D12 and [ADR 0013](../../adr/0013-access-contract-and-role-model.md), with N-035's arrival model, R3.4's widened scope, the new R4.7 and walkthrough step W11. A first version of this design was lost with an interrupted chat before it reached the repository; that is why it is written down before any of it is built.
- R1.12: one enrollment path. The unbound tier, the app-password binding and session import are gone from the connector, together with the server's person-scoped credential endpoints; six tests of the removed paths went with them, and the rest seed state instead of driving a deleted verb. Connector 94/94; .NET 349/357 with only the 8 known; connector `src/` 9,489 → 9,174; server C# 11,333 → 11,227 (N-039).
- Leo, after R1.12: the PoC is saddled with unreasonable tests. The evidence agrees — every defect this slice found (N-025, N-027, N-029, N-030, N-035) came from the walkthrough, never from the suites, while R1.12's ~300 deleted product lines cost a session of test rework. R1.15 audits them (N-040).
- R1.15: 42 test files deleted and five lifecycle sections with them, 656 tests → 191, every suite green for the first time in this epic. Leo's judgement was that 656 is itself the diagnosis and the ceiling should be about 100. The audit misread its own evidence twice and both corrections are in N-040 — the browser's red-team files hold safety properties rather than mechanism, and the lifecycle count was assertions rather than tests — and then Leo's "why is it guarding a wipe script?" exposed the second reversal as its own error: only three of eight sections held irreversible risk, and the other five went. The floor is 191 rather than 100 because what remains is proof verification, replay and expiry rules, the owner claim, DPoP and OAuth, session secrecy, real-binary journeys and the wipe's escape guards — and R1.16 removes the reason those last ones exist.
- Next: R1.13–R1.14 close R1.

### S-004 · 2026-09-16 · Claude (Opus 5)

- Resumed on a clean tree at `24365c2`. Resume here was stale — it still named the greenfield narrowing as the next action, though that and stage 1 were both committed — so the checks were re-run rather than trusted: .NET 87/87, browser 7/7, greenfield 1,444, matching the stage-1 checkpoint exactly.
- **R2.1 finished its four rename stages**, one commit each. `507cae2` a TangentCommunity is a Tangent; `ebf7262` a Message is a Post; `bd3da18` a Room is a Topic. Greenfield **1,496 → 858**; .NET 87/87, browser 7/7, connector 52/52, lifecycle 30/30; server, tests and all five probes build.
- The boundary the stages held, and why it is not timidity: C# types, their family and C# prose move; routes, reason codes, serialized property names and every `wwwroot/` file do not. Stage 1 set it by moving `/api/site` and having to revert, and R1.15 deleted the UI suites, so a browser mistake now has nothing to catch it. Everything deferred is itemized and assigned in **N-049** rather than left to be rediscovered.
- Three findings, each from a different failure mode. **N-045**: the product's nouns are short, and short nouns collide with the methods that name them — `References.Tangent`, then three methods named `Post`, then `References.Topic` on the line below the one stage 2 fixed. The compiler finds these. **N-046**: the compiler does not check string literals, and two looked like type references and were not — a *stored* operation name on a receipt, and Koan's own exception text that a probe compares against. Diffing the literal multiset of the `-` and `+` sides caught both, and caught a live route in stage 4 that an exact-string protect list had missed. **N-047**: `probes/` compiles against the server; a grep said otherwise and was simply wrong. Read the project file.
- **R2.1 is `waiting`, not `done`.** Its "Done when" asked for zero retired vocabulary on the server, and that number is reachable at the end of R2, not at the end of R2.1 — the row now says so. The relaunch it also owes waits on Leo, because four entity renames have now stranded four tables (N-044) and R2.6's wipe is the sanctioned resolution.
- Owed to Leo, neither blocking R2.2: the wipe timing, and what to do about `probes/` — EPIC-001 residue that the greenfield check does not scan, still documenting the deleted spaces-network, with a `SpaceVerificationProbe` that is now only `bin` and `obj`. R5.7 may want ScaleProbe, so deleting it is not a sweep's call.
- R2.2 (`74b308f`): `CompanionGovernance` is `ParticipantGovernance` across six files, one responsibility each — and the split is by file rather than by type on purpose, because the substrate all five groups share is a hand-rolled command pipeline that R3.3 replaces (N-050). The row named four responsibilities; the file held five.
- Leo settled two naming questions rather than letting the module map decide them. **D14**: the root namespace is `Tangent`, which retires the map's `Hosting` row — it said Host in the same document where D13 says Host survives only as an HTTP header. **D15**: reverse-DNS belongs to protocol identifiers, `org.sylin.tangent.`, and R6.2 applies it. The evidence for both was local: Sylin'''s own Koan namespaces itself `Koan.Data.Core` (N-051).
- R2.3 (`fc133d1`): 181 files into ten `Tangent.<Module>` namespaces; `Mcp/` gone. The predicted collision behaved; four unpredicted ones did not, and the costliest would have been silent — `SetApplicationName(nameof(TangentSpace))` scopes every Data Protection payload, so the rename would have signed out every participant as a side effect of moving folders (N-052).
- Greenfield across the session: **1,496 → 720**. Every suite green throughout.
- Next: R2.4, the wire — and the first task that must change the browser, where R1.15 left no suite to catch a mistake.

## Evidence index

- [Architecture assessment, 15 September](../../ASSESSMENT_2026-09-15.md)
- [Connector architecture assessment, 15 September](../../ASSESSMENT_2026-09-15-CONNECTOR.md)
- [Previous assessment, 12 September](../../ASSESSMENT_2026-09-12.md)
- Recorded suite results before this epic: [CURRENT_STATE](../../CURRENT_STATE.md)
