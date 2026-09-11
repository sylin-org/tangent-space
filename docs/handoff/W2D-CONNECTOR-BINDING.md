# W2-D — Connector atproto binding and bound enrollment (the round's core build)

Wave 2 brief. One agent, launches ONLY after W2-B1 (the rekey) has been committed by the
integrator. Rust only: `src/server/mcp`. Decision record: `docs/DECISIONS.md` — "Round
objective: bound atproto exchange", "Bound-identity enrollment direction",
"Session-token semantics", "Bound-baseline sequencing principle". Read all four first.

## Investigation facts (verified against the code, commit a3a72a2)

- The server's bound enrollment is `POST /mcp/token` (no server-issued challenge): the
  client (1) `GET {origin}/.well-known/tangent-mcp` → `{serviceProof: {audience, method}}`;
  (2) mints a service-auth JWT from its own PDS:
  `GET /xrpc/com.atproto.server.getServiceAuth?aud={audience}&lxm=local.tangent.mcp.exchange&exp={now+120s}`
  with the PDS session's `accessJwt`; (3) `POST {origin}/mcp/token` with
  `Authorization: Bearer {proof}` and body `{name, lifetimeDays, grants}` (grants bounded
  to welcome/read/post; manage only if explicitly requested — do not request it) →
  `{profile: "atproto_service_proof_exchange", credential, token}`. Errors: 503
  `exchange_unavailable` (audience unconfigured), 401 invalid/replayed, 403 suspended.
  Reference implementation to port: `probes/scripts/probe-mcp-inbound.mjs` (repo root).
- PDS session acquisition: `POST {pds}/xrpc/com.atproto.server.createSession` with
  `{identifier: <handle>, password: <app password>}` → `{did, handle, accessJwt, ...}`.
  The PDS endpoint comes from the response's `didDoc`/`response` fields or the configured
  PLC resolution — port the minimal path the probe uses; public bsky.social handles
  resolve via `https://plc.directory` (outbound HTTPS from the connector is fine).
- The audience on the live dev server is already configured (fixture-registered PLC
  service DID); nothing server-side blocks this round.

## Design

### 1. Atproto binding per identity (operator page + API)

- Operator API + page section "Atproto binding" per identity: fields handle + app
  password → the connector calls `createSession` → on success stores an
  **atproto session** `{did, handle, access_jwt, obtained_at}` per identity in connector
  state (a dedicated state map — cookie-jar posture, same exposure class; documented),
  sets the identity's `bound_did`, and NEVER persists or logs the password (it exists in
  memory for the one request). Binding again replaces the session (re-bind on expiry —
  honest error when the PDS session has expired, message says to re-bind).
- The operator page shows bound state (`bound_did`, session age) and an Unbind action
  (clears session + bound_did).

### 2. Bound enrollment (primary path)

- `enroll_bound(identity, origin)`: requires `bound_did` + a live atproto session; runs
  the 3-step recipe above; stores the returned `ts_` token as the per-enrollment session
  (existing sessions map, keyed by companion id — W2-A shape). Add it to the operator API
  and page as the primary enroll action ("Enroll with bound atproto identity");
  the unbound flow (W2 contract) stays available as the secondary path; manual token-file
  import stays.
- Honest errors mapped from the server outcomes (503 → "this server has no proof
  audience configured"; 401 → proof rejected, name the reason; 403 → suspended).

### 3. Operator server under `serve` + the OpenRegistration tool

- The `serve` verb hosts the SAME loopback operator server (the W2-A adapter) in-process.
  The startup token goes to **stderr and the diagnostics journal — never stdout** (stdout
  is protocol-owned JSON-RPC). The standalone `operator` verb is unchanged; the existing
  lockfile discipline carries over (one process owns the data dir).
- New MCP tool **`OpenRegistration`** (no arguments): browser-opens the operator page's
  identity-creation view with the token appended — URL constructed internally, NEVER
  rendered into the tool response or any view (the model must not see the token). Tool
  response: a short instruction ("Opened the local operator page for the operator to
  create or bind an identity; ask the operator when done."). This is attention, not
  execution — the human acts in the browser; nothing auto-runs.
- Browser-open in tests/headless: honor a `TANGENT_CONNECTOR_NO_BROWSER=1` env guard that
  skips the spawn (the URL is still constructed and asserted).

### 4. Catalog and presentation

- Tool catalog 12 → 13 (`OpenRegistration`); update the connector's own catalog tests and
  README. The .NET Tier-B test asserting the negotiated tool count will be updated by the
  integrator — flag it, do not touch C#.

## Files allowed to touch

Everything under `src/server/mcp/` (src, tests, Cargo.toml if truly needed, README).
Off-limits: the .NET server, wwwroot, docs, `.local/`, git state (no commits).

## Invariants

- The app password never touches disk or logs; only the resulting atproto session tokens
  are state (documented exposure class, like the enrollment sessions).
- `lxm` is exactly `local.tangent.mcp.exchange`; `aud` comes from the server's discovery
  document, never hardcoded; `exp` within the server's accepted window (now+120 s).
- The page token and all session tokens never enter model-visible output.
- No sign-in or enrollment is ever triggered by server discovery or without the operator
  (ADR 0009 invariant): OpenRegistration opens a page; binding and enrolling are operator
  actions on the operator page/API.
- Wipe rule: no migration for the new state fields (serde defaults).

## Finish conditions

1. `cargo test` green from `src/server/mcp` (existing + new) and `cargo build --release`.
2. New tests (FakeServer): the full bound journey (fake discovery → fake getServiceAuth
   → fake /mcp/token 200) storing the session per enrollment; 503/401/403 honest errors;
   createSession acquisition with password-never-logged assertion; re-bind replaces the
   session; OpenRegistration URL construction under the no-browser guard; serve-mode
   operator server reachable on loopback with stderr-token (stdio journey extension).
3. README: binding, bound enrollment, OpenRegistration, the no-browser guard.
4. Do NOT `git commit`.

## Red-team self-check (answer in your report)

- Is there ANY path where the app password, atproto session token, page token, or proof
  JWT reaches stdout, a view, a tool response, the journal, or state besides the designed
  session fields?
- Does a malicious server in the discovery document influence anything beyond the proof's
  `aud` (URL parsing: can a crafted audience or origin inject query params/headers into
  the getServiceAuth or /mcp/token calls)?
- Does serve-mode keep the stdio protocol pure (nothing but JSON-RPC lines on stdout)?
- Re-binding under an existing enrollment: does the old enrollment keep its Tangent
  session (it must — the atproto session is per identity, Tangent sessions are per
  enrollment)?
