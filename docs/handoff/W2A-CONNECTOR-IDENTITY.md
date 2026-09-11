# W2-A — Connector identity model and operator web server

Wave 2 brief. One agent. Rust only: `src/server/mcp`. The server side (W2-B2) lands after
this, so the enrollment wire flow is implemented client-side and proven against the test
fake server per the frozen [W2 contract](W2-CONTRACT.md). Decision record:
`docs/DECISIONS.md` — "Refinement decision round" (D7, D8, D9) and the ghostlight patterns
entry. Read both plus the contract before coding.

## Design

### 1. Identities (1..N per connector)

New domain concept in `src/domain/identity.rs`:

```
Identity { local_id: String (guid v7, minted at creation, immutable),
           handle: String (2..253, unique within the connector),
           display_name: Option<String>,
           bound_did: Option<String> (atproto DID when bound; binding itself is a later
                                      wave — the field is present, normally None),
           created_at: i64 }
```

- Enrollments (today's `CompanionEntry`) become **per (identity, server origin)**: an
  enrollment is the credential + server binding for one identity. Re-key
  `CompanionEntry` to carry `local_id` (keep the `companion_id` handle and the credential
  machinery as-is otherwise; the moniker selection resolves identity first, then
  enrollment).
- `LocalContext` binds caller + enrollment (+ origin) as today; the acting identity rides
  the enrollment.
- Under the standing wipe rule, state-file changes are additive serde defaults with NO
  migration code: a state.json without identities simply starts empty; companions from an
  old state fail the identity join honestly — drop them at load with a log line
  (`EnrollmentDropped`, new event variant) since re-enrollment is the documented path.

### 2. Per-caller resolution keyed to the clientInfo allowlist

- `adapters/mcp.rs` currently extracts `clientInfo.name` at initialize (~line 127-133)
  and discards it. Capture it into the hub instead (thread through `serve`'s hub
  construction — this replaces the hardcoded `CallerId("mcp")` semantics without changing
  the per-process single-caller rule).
- New persisted allowlist: `Vec<ClientRule { client_name: String, local_id: Option<String> }>`
  (a rule with `local_id: None` = "ask, never auto-resolve").
- Resolution policy: a tool call needing an identity resolves by (a) explicit identity
  argument/selection, else (b) the allowlist entry for the connecting client — a single
  exact `local_id` auto-resolves; `None` or missing rule → the call fails with a clear
  instruction to select an identity (never a guess, never machine-wide: an unlisted MCP
  client on this machine resolves NOTHING even if only one identity exists).
- CLI verbs (`call` etc.) keep an explicit `--identity`/moniker path; no CLI
  auto-resolution.

### 3. Operator web server (`tangent-connector operator`)

New long-running verb, separate from `serve` (which stays pure stdio):

- **Loopback listener**: raw `std::net::TcpListener` bound to `127.0.0.1` (ephemeral
  port, or `--port`). Hand-rolled minimal HTTP/1.1 in the house style (precedent:
  `tests/common/mod.rs` FakeServer): request line + headers + Content-Length body only,
  1 MiB body cap, 8 KiB header cap, `Connection: close` on every response, GET/POST
  only, 30 s read timeout. Only paths under `/` serving embedded static assets
  (`include_str!` — no filesystem webroot) and `/api/*` JSON crossing the SAME hub as
  the CLI/MCP intakes (attribution channel `Operator`, a new `IntakeChannel` variant).
- **Auth**: a token generated at startup (32 random bytes hex), printed once to stdout
  (this process owns stdout — it is not the MCP edge) and included in the auto-opened
  URL `http://127.0.0.1:{port}/?token={token}`. Every `/api/*` request must carry it
  (`X-Tangent-Token` header or `?token=`); static assets are served without it (they are
  inert HTML/JS). Constant-time compare.
- **API** (JSON, camelCase): list identities; create/update identity (handle/display
  name; handle uniqueness enforced); delete identity (only when it has no live
  enrollments, or cascade-with-confirmation flag); list/set the client allowlist; list
  enrollments per identity (origin, participant ref, handle, credential status,
  auto-check); **enroll unbound** (`POST /api/identities/{localId}/enroll {origin}` →
  performs the W2-contract enrollment against that server: when the identity has no
  credential for that origin, call `POST {origin}/api/v1/experience/identities/enroll`
  per the contract, store the returned credential in the existing custody, create the
  enrollment; `already_enrolled` → honest error telling the operator a credential
  already exists or re-forget first); forget enrollment; view attention/pending-write
  state per enrollment (read-only reuse of existing hub state).
- **UI**: one embedded page (plain HTML + vanilla JS + fetch, no framework, no CDN).
  Sections: Identities, Client allowlist, Servers/enrollments, Status. Honest empty
  states; no sign-in buttons that do anything (a disabled "Sign in with atproto —
  coming in a later wave" control with that exact copy is required, per D8 deferral).
- **Tray icon**: `tray-icon` crate. Menu: "Open operator page" (browser-open), status
  line (identities/servers counts), "Quit". On Windows the tray needs a Win32 message
  pump on its creating thread; this crate forbids `unsafe` — you MAY change the lint
  from `forbid` to `deny` in Cargo.toml and add exactly ONE `#[allow(unsafe_code)]`
  module containing only the message pump, with a safety comment naming why the calls
  are sound — if and only if tray-icon cannot be driven without it (investigate
  `tray-icon`'s own event delivery first; if it can, use that and keep `forbid`).
  Non-Windows: build the tray behind `#[cfg(target_os = "windows")]` with a documented
  no-op elsewhere (the keyring dep is already windows-native).
- **Browser-open**: the ghostlight `browser_command()` shape — Windows
  `rundll32.exe url.dll,FileProtocolHandler <url>`, Linux `xdg-open`, macOS `open`,
  spawned detached with null stdio (`Command`, `.spawn()`, stdio null).
- **Threading**: operator server + tray each on named threads; state mutations go
  through the hub under its existing locking discipline (`ConnectorHub` must be sharable
  — `Arc`; poller threads already do this). Nothing may write to stdout after startup
  banner except the MCP edge when serving (the operator verb owns stdout; if `serve` and
  `operator` ever run together in one process they don't — separate processes).

### 4. Arrival DTO relaxation

`application/contract.rs`: `identity` gains `identities` (Vec, default empty) and
`did` becomes optional; validation requires non-empty `participantRef` instead of
`did.starts_with("did:")`. `hub.rs enroll()` (manual credential import) keeps working:
validate against `participantRef`; store the server-reported identity view on the
enrollment. Update `tests/common/mod.rs` `envelope()` to the contract shape.

## Files allowed to touch

Everything under `src/server/mcp/` (src, tests, Cargo.toml, README) — this is the sole
worksite. `Cargo.toml` gains `tray-icon` (and nothing else without justification in the
report). Off-limits: the .NET server, wwwroot, docs except `src/server/mcp/README.md`,
`.local/`, git state (no commits).

## Invariants

- Attribution only: clientInfo selects identity; it never changes domain outcomes.
- An unlisted MCP client resolves nothing — even with one identity present.
- Credentials only in the existing custody (platform store / plaintext-dev flag); never
  in state.json, never in operator-page HTML responses (the API returns credential
  STATUS, never the token; the one-time enrollment credential is stored directly).
- The 12-tool MCP surface, budgets, you-rendering and journaled writes are unchanged in
  behavior; existing journeys must still pass (allowing mechanical adaptation to the
  identity join).
- `unsafe`: `forbid` stays unless the single-pump exception is required; if used, it is
  ONE module, `deny` elsewhere, documented.
- Wipe rule: no state migration code; additive serde defaults only.

## Finish conditions

1. `cargo test` green (existing + new) from `src/server/mcp`; `cargo build --release`
   succeeds.
2. New tests: identity CRUD + handle uniqueness; allowlist resolution (listed→resolve,
   listed-None→instruction, unlisted→instruction even with one identity); enrollment
   journey against FakeServer implementing the W2 contract (ok path stores credential +
   enrollment; `already_enrolled` honest error; blocked `unbound_enrollment_disabled`
   honest error); operator HTTP server auth (wrong token → 401-ish JSON refusal; correct
   token works) via the loopback listener in-process; browser-open helper unit (command
   construction per-OS cfg).
3. README updated: `operator` verb, token, tray, allowlist semantics, unbound
   enrollment flow.
4. No `git commit`.

## Red-team self-check (answer in your report)

- Can the operator API be reached from another machine (bind check) or without the token
  (static vs api split, query/header both accepted but always compared)?
- Can identity selection be spoofed via clientInfo (client sends attacker-chosen
  `clientInfo.name`)? The allowlist keys on it — state exactly what an attacker who can
  run a local MCP client gains (they are already the operator's machine; the allowlist
  is a guard against silent wrong-identity action, not an auth boundary — confirm the
  README says so).
- Does the tray pump module (if used) contain anything beyond the pump? Any other unsafe?
- Can an enrollment write to state.json corrupt it under concurrent operator+poller
  access (all mutations under the hub's lock; atomic_write preserved)?
