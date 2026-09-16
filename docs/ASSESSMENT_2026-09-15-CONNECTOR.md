# Tangent connector architecture assessment — 15 September 2026

Requested by Leo in the form of the [server assessment](ASSESSMENT_2026-09-15.md), which reviewed the connector only for the endpoints it calls. This evaluates the connector's design; it does not record new functionality. Its recommendations are proposals until Leo decides them; accepted ones join [EPIC-007](epics/EPIC-007.md) and its [work ledger](epics/epic-007/LEDGER.md).

## Verdict

The connector's local engineering is careful: reads and frames are bounded, credentials never follow a redirect or reach a view or log, a write is journaled before it leaves and reconciled from its receipt, references work only at the origin that issued them, exit codes never report an uncertain outcome as success, and the OAuth bind implements PAR, PKCE and DPoP with details verified against Bluesky's live servers. The architecture is the weak part, for the same reason as the server's: it grew by addition. Each wave — local identities, account binding, the Connect handshake, the OAuth bind, CLI parity, the companion manager — added a path beside the previous one instead of replacing it. It now has:

- three ways to enroll and two ways to bind an account, of which one each serves real users; the others are dead or used only by tests;
- one hub of 2,594 lines that is the application layer, the state service, the handshake coordinator and the error vocabulary at once, guarded by ten mutexes and a lock rule kept by comments;
- a state file rewritten whole at least twice per tool call by processes that do not coordinate, beside a journal that is never compacted;
- a second copy of the server contract in its tests, already drifted from the server;
- sessions that expire after seven days with nothing to renew them.

The remedy is again mostly subtraction plus three components: one enrollment path that renews itself, a transactional state store, and use cases in place of the hub. The domain model — companion, enrollment, context, receipt, deterministic view — needs no rewriting.

## Scope and method

- **Baseline:** `claude/epic-007-realignment` at `deb70ab`; the connector's code is unchanged since `0c51fc4`.
- **Subject:** the Rust connector in `src/server/mcp`: 9,489 lines of Rust in 31 files (8,751 before the unit-test modules), the embedded companion page (326 lines of HTML, 88 of CSS), 4,383 lines of integration tests in 7 files and the 348-line README; plus the server routes it calls and how the browser finds it.
- **Method:** every source file read; call sites searched for each public function that looked unused; `cargo clippy --all-targets` and `cargo test` on the current code (99 of 99 pass); a read-only look at the connector state behind the R1.8 walkthrough, reporting counts only (no session or token value was printed). No code or data changed.

## Assessment by dimension

| Dimension | Verdict | Evidence |
|---|---|---|
| Architecture | Mixed | The spoke shape is right: MCP, the CLI and the companion page decode into one `Operation` vocabulary and cross one entry point, so every intake sees the same outcome. But one hub holds every use case, intakes also reach around it into the store (`hub.store()`, six times in `main.rs` and once in the tray), and enrollment has three tiers. |
| DDD alignment | Weak to mixed | The domain types are small and pure (`identity`, `attention`, `writes`, `refs`). The central nouns are tangled: a `CompanionEntry` is an enrollment, the page calls identities "companions", `SelectCompanion` picks an enrollment, and "session" names the context handle, the Tangent bearer and the atproto session. Failures travel as strings with a `code: ` prefix that two places parse back. A domain policy for automatic model turns has no caller. |
| Separation of concerns | Mixed | Presentation is separate, deterministic and budgeted, and HTTP sits behind a port. But the hub also builds routes, words errors in nine functions, stores OAuth flights and runs a sweeper thread; the Tangent port also carries PDS calls (`ExperiencePort::enroll` posts app passwords to the PDS); the CLI's `forget` edits the store directly, skipping the hub's attributed `forget_enrollment`. |
| Right-sizing | Over- and under-built at once | Over-built: three enrollment tiers, two binding paths, an automatic-turn policy and a delivery-mode enum without callers. Under-built: cross-process state safety (the lock covers only long-running verbs), session renewal, journal compaction, a contract check. The hand-rolled HTTP server and OAuth client follow from the no-async-runtime choice, but the page does not yet meet the security duties that choice brings (see Other findings). |

## What to keep

- One decode and one dispatch for every intake ([`decode`](../src/server/mcp/src/application/operations.rs#L110), [`invoke`](../src/server/mcp/src/application/hub.rs#L1660)), and exit codes that never call an uncertain outcome a success.
- The write journal: the tuple is recorded before the request leaves, settled from its receipt, and recovered through `GetOperation` or the background check.
- Qualified references that only work at the origin that issued them ([refs.rs](../src/server/mcp/src/domain/refs.rs)).
- Deterministic, budgeted views that quote participant text so it cannot pose as scaffolding, with critical lines exempt from the budget ([perspective.rs](../src/server/mcp/src/presentation/perspective.rs)).
- Transport discipline: no redirects, 512 KiB response caps, 8 MiB frames, capped header and body reads on the page, at most four live-feed clients.
- The OAuth bind: PAR, PKCE and DPoP with a separate nonce context per server, mandatory `iss` and `sub`, and a consent scope limited to the one exchange method ([atproto_oauth.rs](../src/server/mcp/src/adapters/atproto_oauth.rs#L47)); `AtprotoSession`'s hand-redacted `Debug`.
- `deny(unsafe_code)` with one audited message-pump module.
- Moderation tools offered only when the server's response grants them, announced with `tools/list_changed`.
- The fixed page port and the injected page opener (R1.10).

## Root causes

### 1. Every enrollment and binding path stayed

| Concern | Paths | What serves real users |
|---|---|---|
| Enrollment | Account-bound proof exchange (`enroll_bound` → `/mcp/token`); unbound tier (`enroll-unbound` → `POST /api/v1/experience/identities/enroll`); session import (`enroll --token-file`) | The bound exchange. The unbound route has never existed on the server (N-005). Import needs a token from [`POST /api/participation/credentials`](../src/server/web/Participation/ParticipationController.cs#L9), which issues a credential for the signed-in person and has no caller in the repository; the test suites mint theirs in process |
| Account binding | OAuth `/bind` route; app password (`bind_atproto` → `createSession`) | OAuth. The app-password method has no production caller and 13 test callers |
| Getting a context | `Connect`; `SelectCompanion` then `Arrive` | Both work; `Connect` does what the pair does |
| Forgetting an enrollment | The CLI's `forget` edits the store ([main.rs](../src/server/mcp/src/main.rs#L567)); the page calls `forget_enrollment`, which publishes attribution events | Two implementations |
| Refreshing an OAuth session | Under the stored client id, or under the "pre-scope-era" client id when none is stored ([hub.rs](../src/server/mcp/src/application/hub.rs#L791)) | The fallback serves sessions the wipe rule already discarded |

Consequence: 29 test call sites and most of the README keep dead tiers alive. The README documents the app-password form as the way to bind and calls OAuth "not yet implemented"; the model's instructions say "select your companion, arrive" ([mcp.rs](../src/server/mcp/src/adapters/mcp.rs#L24)) while `Connect` is the working path.

### 2. One hub is the application layer, the state service and the error vocabulary

- [`hub.rs`](../src/server/mcp/src/application/hub.rs) has 2,802 lines (2,594 before its test module) and 102 functions. `ConnectorHub` has 16 fields, 10 of them mutexes, plus a sweeper thread armed on first use.
- The store lock is a "leaf lock" by comment ([hub.rs](../src/server/mcp/src/application/hub.rs#L1644)). A re-entrant lock already froze the hub once (`90d1779`), and 27 `expect("state lock")` calls turn a poisoned lock into a panic.
- Context resolution is written twice and the copies disagree: `GetUpdates` ([hub.rs](../src/server/mcp/src/application/hub.rs#L2008)) skips the caller check that `with_context` applies ([hub.rs](../src/server/mcp/src/application/hub.rs#L2087)), so a context issued to one intake answers another, although the README says handles never leak across intakes.
- Smaller rules repeat too: whether attention is directed is decided in four places ([attention.rs](../src/server/mcp/src/domain/attention.rs#L41), [hub.rs](../src/server/mcp/src/application/hub.rs#L2398), [store.rs](../src/server/mcp/src/adapters/store.rs#L413) twice), and percent-encoding is written twice.
- 68 signatures return `Result<_, String>`. Nine functions word `ExperienceError` for people, and two places recover the code with `split_once(": ")` ([hub.rs](../src/server/mcp/src/application/hub.rs#L1449), [operator.rs](../src/server/mcp/src/adapters/operator.rs#L800)).
- Test seams are production API: `set_atproto_oauth`, `set_bind_flight_ttl_ms`, `sign_in_target_url`, `set_operator_page_url`, and `store()`, which tests use 30 times.
- 11 of the connector's 16 commits changed `hub.rs`.

Consequence: every rule — caller binding, lock order, attribution, wording — has to be re-checked at each call site, and every change lands in one file.

### 3. Whole-file state with no owner of cross-process safety

- `StateStore::save` rewrites all of `state.json`: temporary file, fsync, rename ([store.rs](../src/server/mcp/src/adapters/store.rs#L127)). A read tool call saves twice (`with_context`, then `finish`); `Arrive` can save three times.
- `pending-writes.jsonl` only grows, and `unsettled_writes()` re-reads and parses all of it for every tool response ([store.rs](../src/server/mcp/src/adapters/store.rs#L494)).
- `serve` and `operator` take a lockfile; the one-shot verbs (`call`, `enroll`, `forget`, `check`) open their own copy and save the whole file. Yet the design relies on processes sharing state: a CLI `Connect` opens the page another process recorded, and the README promises that "a later CLI call Connect completes it by itself". A CLI change made while `serve` runs is lost at `serve`'s next save, and the reverse.
- The lock has no liveness check ([lockfile.rs](../src/server/mcp/src/adapters/lockfile.rs)). The walkthrough's state directory holds a lock taken on 12 September by process 36356, which no longer runs; the next `serve` or `operator` refuses to start until someone passes `--force`. With the fixed page port as well, two MCP hosts cannot each run the connector.
- Cascades are incomplete: `remove_companion_state` drops an enrollment's contexts but not their alias maps ([store.rs](../src/server/mcp/src/adapters/store.rs#L525)); the live state holds two alias maps for one context.
- Every response lists all identities' unsettled request ids ([hub.rs](../src/server/mcp/src/application/hub.rs#L2244)), while the status view filters per enrollment ([hub.rs](../src/server/mcp/src/application/hub.rs#L1564)).

Consequence: cost grows with history, and the documented pairing of an MCP host with the CLI can silently lose an enrollment or a session.

### 4. The server contract is copied, not checked

- The connector builds its 19 server routes by hand and reads responses through 24 DTOs; the result data — posts, Tangents, Topics, cases — is read by string key in presentation, so a renamed field renders empty instead of failing.
- [`tests/common/mod.rs`](../src/server/mcp/tests/common/mod.rs) re-implements the server in 1,465 lines and has drifted: it serves `POST /api/v1/experience/identities/enroll` ([line 889](../src/server/mcp/tests/common/mod.rs#L889)), which the server never had, discovery `endpoints` ([line 694](../src/server/mcp/tests/common/mod.rs#L694)) removed in R1.1 (N-012), and Spaces-era `source.uri` and `cid` ([line 1166](../src/server/mcp/tests/common/mod.rs#L1166)) removed in R1.5. Its proof audience, `did:plc:fixture-tangent-server`, is not a valid `did:plc`; fakes on both sides accepted the audience Bluesky refused (N-029).
- The only connector tests that run against the real server, three .NET integration tests, enroll through session import.
- The exchange still carries inbound-MCP names: `/.well-known/tangent-mcp`, `/mcp/token` and the method `local.tangent.mcp.exchange`. The method is now part of the OAuth consent scope (`rpc:local.tangent.mcp.exchange?aud=*`), so renaming it later asks every operator to consent again. R6.2 renames the routes; the method has to change in the same step.

Consequence: the suites pass against a server that does not exist.

### 5. Sessions expire and nothing renews them

The connector asks for 7-day sessions ([hub.rs](../src/server/mcp/src/application/hub.rs#L997)), and the server grants them ([ServiceProofExchange.cs](../src/server/web/Mcp/Authentication/ServiceProofExchange.cs#L33), [ParticipantCredential.cs](../src/server/web/Participation/ParticipantCredential.cs#L29)). `Connect` reuses any stored session without checking it ([hub.rs](../src/server/mcp/src/application/hub.rs#L1254)), and a 401 becomes "the operator must renew this enrollment's session" ([hub.rs](../src/server/mcp/src/application/hub.rs#L2450)). After seven days, or any server wipe (N-029), an agent is locked out until a person forgets the enrollment and the agent connects again, although the connector holds a refreshable account session and could repeat the exchange itself.

Consequence: unattended agents stop every week with an instruction only a person can follow.

### 6. Compatibility and history the wipe rule no longer needs

- **Legacy handling.** Enrollments without an identity are dropped at load and announced as `EnrollmentDropped` ([store.rs](../src/server/mcp/src/adapters/store.rs#L107)); OAuth sessions without a stored client id refresh under the pre-scope one; `StateFile.version` is written and never read.
- **No caller.** [`adapters/delivery.rs`](../src/server/mcp/src/adapters/delivery.rs) (95 lines); the automatic-turn policy ([`PolicyLedger`](../src/server/mcp/src/domain/policy.rs#L33), `may_auto_turn`, `record_turn`, `TurnDecision`, `ledger_mut`, `set_policy`; only `poll_seconds` is read); `ExperiencePort::wait`; `ToolOutcome::exit_code`, which the CLI re-implements; `experience::shared`; `Perspective::from_identity`; `Budget::truncated`; `poller::StopFlag`; `CallerId::default`.
- **Size.** About 850 lines of source serve no working path: about 460 have no production caller (the app-password binding and the list above), about 230 call a route the server lacks (the unbound tier), and about 170 import a credential only the test suites mint.
- **Stale text.** The tray's header cites the removed keyring; the store and the operation docs describe a page token that no longer exists; `Identity` calls account binding "a later wave"; the README describes an app-password form and `#bind-` anchors.
- **History in comments.** About 60 comment lines cite plan items (R3, F2, A3, P5a, W2-A…) or narrate owner corrections.
- **Unchecked.** `scripts/check-greenfield.ps1` does not scan the connector. Its rules would find 11 `room` and 13 historical-marker lines there; its `channel` rule would mostly match the intake channel, and `companion` is the connector's word by the glossary.

## Other findings

- **The companion page accepts cross-site writes.** Its API parses any body that is not form-encoded as JSON ([operator.rs](../src/server/mcp/src/adapters/operator.rs#L379)), so a `text/plain` POST from any website — a CORS simple request, sent without a preflight — reaches the hub. `POST /api/identities` needs no identifier, and a second identity breaks single-identity resolution. No `Host`, `Origin` or `Sec-Fetch-Site` check exists, so DNS rebinding can read the API as well. Browser local-network protections vary by browser and version. The server's credential endpoint already applies the missing checks ([ParticipationController.cs](../src/server/web/Participation/ParticipationController.cs#L42)).
- **A world-readable example.** The README's MCP configuration sets `TANGENT_CONNECTOR_HOME` to `C:/tangent/connector` (line 280). State holds bearer sessions, OAuth refresh tokens and DPoP private keys in plain text; on a default Windows install a folder created at the drive root is readable by every local account, and the store narrows permissions only on Unix ([store.rs](../src/server/mcp/src/adapters/store.rs#L564)).
- **The page probe trusts any answer.** Connect's last-resort probe of `127.0.0.1:5219` counts any HTTP response as the companion page ([experience.rs](../src/server/mcp/src/adapters/experience.rs#L62), [hub.rs](../src/server/mcp/src/application/hub.rs#L1185)), so an unrelated local service there would be opened at `/bind/…`; the connector's own `/api/discovery` would identify it. The same probe means [`an_unreachable_recorded_page_gets_the_honest_start_operator_instruction`](../src/server/mcp/tests/connect_journey.rs#L373) would fail on a machine whose companion page is running.
- **Views repeat attention.** In orientation and expanded views the same mention appears twice: once from the digest and once from the records `sync_attention` just stored from it ([presentation/mod.rs](../src/server/mcp/src/presentation/mod.rs#L79)). A first compact `GetUpdates` states the waiting count three ways and ends by suggesting `GetUpdates`. W5 confirmed the repetition on the release binary: the expanded view listed the one mention twice.
- **Attention is modeled twice.** The server returns the digest with most responses; the connector also keeps records, waiting counts, revisions and checkpoints, and infers withdrawals from a "complete page" heuristic ([store.rs](../src/server/mcp/src/adapters/store.rs#L400)). Only what the model has already been shown is the connector's own knowledge; R5's attention projection can carry the rest.
- **The companion page compiles the server's browser files.** `operator.rs` embeds `atmosphere.css`, `ascii-scenes.js` and `atmosphere.js` from `src/server/web/wwwroot` by relative path ([operator.rs](../src/server/mcp/src/adapters/operator.rs#L58)); renaming one breaks the connector build, and no browser test notices.
- **Discovery.** The browser checks `http://127.0.0.1:5219/api/discovery` before it offers "Connect an Agent" ([agent-entry.js](../src/server/web/wwwroot/agent-entry.js)). The route answers any origin, so any site can tell the connector is installed: a small, deliberate disclosure.
- **Lints and dependencies.** Clippy reports 10 lints: 5 in the library (`finish` takes 8 arguments, items after a test module, two redundant closures, `StopFlag` without `Default`) and 5 in tests. Ten direct dependencies pull in 95 crates, with duplicate `getrandom`, `syn` and `webpki-roots` versions; `ureq` is on 2.x.

## Baseline inventory

| Measure | Value at `deb70ab` |
|---|---|
| Connector Rust | 9,489 lines in 31 files (8,751 before unit-test modules) |
| By layer | domain 670 · application 3,591 (`hub.rs` 2,802) · adapters 4,019 · presentation 548 · `main.rs` 609 · `lib.rs` 52 |
| Companion page | 326 lines of HTML and 88 of CSS, plus three embedded server assets |
| Tests | 99 (37 unit, 62 integration), all passing; 4,383 lines of integration tests, 1,465 of them the fake server |
| MCP tools | 14, plus 4 moderation tools offered by permission |
| Server routes called | 19, one of which does not exist |
| atproto endpoints called | 9 |
| Mutexes in `ConnectorHub` | 10 |
| Full-state saves per read tool call | 2 |
| Signatures returning `Result<_, String>` | 68 |
| Source serving no working path | about 850 lines |
| Direct dependencies | 10 (95 crates) |

## Target shape

```text
Intakes       MCP stdio · CLI · companion page (loopback; Host, Origin and JSON enforced)
                   │  decode → Operation
Application   Connect · Participation · Companions and binding · Server cards
              Participation: resolve context (one path) → call → settle receipt → sync attention → view
              Problems: one typed code and message
                   │
Services      Enrollment: one path, the account-bound proof exchange, renewing its own session
              State: one transactional store, safe for any number of processes
              Presentation: deterministic views
Outbound      Tangent API client: one route table, checked against the real server
              atproto client: handle and DID resolution, OAuth (PAR, PKCE, DPoP), service proofs
```

## Recommendations

Proposals until Leo decides them.

| ID | Recommendation |
|---|---|
| C1 | **One enrollment path.** Keep the account-bound exchange through the OAuth bind. Delete the unbound tier, the app-password binding and session import, with their DTOs, wording, CLI verbs, fake routes and tests, and the server's person-scoped `/api/participation/credentials` endpoints with import. The .NET integration tests enroll through the bound exchange against the fake account server `EnrollmentTests` already uses. If agents acting as their person is wanted, it returns as a named capability with its own page. |
| C2 | **Sessions renew themselves.** Record each session's expiry from the exchange; before expiry, or after a 401, repeat the exchange once; ask a person only when the account session itself cannot be refreshed. |
| C3 | **A transactional state store.** Every change is one read-modify-write of `state.json` under an OS file lock held for that change only (`std::fs::File::lock`, stable since Rust 1.89), and every process uses it, one-shot verbs included. The lockfile, `--force` and the one-process rule go; unsettled receipts become a bounded map in the state instead of a replayed journal; the alias cascade and the per-enrollment filter are fixed. A second `serve` that finds a compatible page on the port (through `/api/discovery`) shares it instead of refusing to start. |
| C4 | **Use cases instead of one hub.** `ConnectorHub` becomes a thin facade over Connect, Participation, Companions and binding, Enrollment and Server cards, with one context resolver. One `Problem` type replaces `Result<_, String>` and the `code: message` convention. Intakes stop reaching into the store, and test seams move to a test-only builder. |
| C5 | **One way to get a context.** The model catalog drops `SelectCompanion`, `Arrive` and `OpenRegistration`: `Connect` resolves the companion, enrolls, arrives, and opens the page when a person must act, including to create the first companion. The base catalog shrinks from 14 tools to 11, `companionId` leaves the model contract, and the instructions describe one path. |
| C6 | **The connector speaks the glossary.** A companion is the local identity (as the page already says), an enrollment is its saved connection to one server, a context is the per-caller handle, and a session is the Tangent bearer: `Identity` → `Companion`, `CompanionEntry` → `Enrollment`, `room` → `topic`. With R2.4 and R6.2, the discovery document, the exchange route and the exchange method lose their `mcp` names together, at the cost of one re-bind. The greenfield check covers the connector, comments lose their history, and the README describes only the current connector. |
| C7 | **The real server is the contract.** The .NET integration suite drives the release binary through every tool against the real server (R6.1). The Rust fake keeps only what the server cannot produce on demand — transport failures, DPoP challenges, lost responses — and loses the routes the server does not have. |
| C8 | **Harden the companion page.** Require `Content-Type: application/json` on writes, accept only a loopback `Host`, and refuse cross-site `Origin` and `Sec-Fetch-Site`; probe the page through `/api/discovery`; let tests inject the page address; keep state in the user profile in every example. |
| C9 | **Subtract what has no caller** (root cause 6), and render each attention item once. |

### Fit with EPIC-007

The server and connector already ship as a matched pair, and EPIC-007 already holds the connector's wire renames (R2.4), its Topic window (R5.1), the contract test (R6.1) and the enrollment routes (R6.2). Folding these recommendations into the same slices keeps one ledger and one walkthrough.

| Slice | Connector work |
|---|---|
| R1 — Subtract | C8, then C1 and C9, after R1.8's walkthrough; re-run W5 |
| R2 — Rename | C6 with R2.4 |
| R3 — Shared components | C3, C2 and C4, with C5 |
| R5 — Read models | The connector keeps only what the model has been shown |
| R6 — Lock the contract | C7 in R6.1; the method rename in R6.2 |

Estimated result: connector source falls from 9,489 lines to about 8,300, and no remaining path depends on a route the server lacks.

## Limits of this assessment

- A static review plus the connector's own suites. Save counts and cross-process loss are derived from code paths, not reproduced.
- The page's cross-site and rebinding exposure is read from code and was not exploited; how much a browser blocks varies.
- The OAuth and DPoP code was reviewed for structure and bounds, not audited clause by clause against the RFCs.
- Line counts include blank lines and comments; the removal and result figures are estimates.
