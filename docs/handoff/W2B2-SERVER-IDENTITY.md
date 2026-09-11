# W2-B2 — Server identity consumption: unbound enrollment, chain, per-identity credentials

Wave 2 brief. One agent, runs ALONE after W2-B1 (and after W2-A; this brief assumes the
W2-A connector exists and the [W2 contract](W2-CONTRACT.md) is implemented client-side).
.NET server: `src/server/web` + `tests/TangentSpace.Tests`. Decision record:
`docs/DECISIONS.md` (0b entry, D7/D8) — read first.

## Design

### 1. Unbound enrollment endpoint (the frozen contract, server side)

New `POST /api/v1/experience/identities/enroll` (route on the existing
`ExperienceController`; no Authorization header required — consent is the setting):

- Validate `client.localId` (32-hex guid v7), `handle` (2..253). Look up an existing
  `connector-client` identity with that value: found → idempotent
  `already_enrolled` outcome with the existing participant, NO credential.
- Setting off → blocked `unbound_enrollment_disabled`. Setting on → mint participant
  (GUIDv7) + internal identity + connector-client identity (value = localId) + a scoped
  **session** (reuse the `ParticipantCredential` machinery internally — name
  "connector", grants `[welcome, read, post]`, 7 days; the wire shape and vocabulary are
  `session` per the contract: a server-scoped bearer session, cookie-equivalent,
  re-mintable by re-enrollment), return the contract's ok shape. Handle deconfliction is
  DISPLAY-only: the stored identity label is the requested handle verbatim; uniqueness
  of labels is NOT enforced (handles are labels, not identifiers) — the digest/`/u/`
  ambiguity guards already treat duplicate labels honestly.
- Suspension: if the resolved participant is suspended → blocked
  `suspended_participant`.

### 2. The setting — honest labeling

Runtime, owner-tunable, persisted — follow the `TangentSite`/`ServerGovernance`
`Read/Update` pattern (NOT appsettings): a new boolean
`AllowUnboundEnrollment` (default **false**) with label "Allow unbound connector
identities (server-minted local participants)" and an honest description line in the
settings form (`wwwroot/rooms.js` renders it; one field, no new sections). Flipping it is
the auditable operator action (threat model, ADR 0009).

### 3. Identity-change chain (audit)

Same pattern as the message changelog (W1-B): a shared `identity` partition of
`Participant` — write-once snapshots inserted on participant creation (`created`) and
every identity add/remove (`identity-added` / `identity-removed`), fields:
full participant field copy + the identity-entry snapshot + `ChangeKind`, `ChangedBy`
(actor or "system"), `PreviousChangeId`, `OfParticipantId`; live row gains `ChangeId`
pointer. Insert-only, same-transaction, no reads infer (digest rule).
Read surface: an `EntityController<Participant, string>` mount at
`api/history/participants` with the W1-B shape — every mutating action denied, set
pinned to `identity`, reads gated by an `EntityAccess<Participant>` realization to the
**site owner only** (reuse W1-B's realization patterns; viewer = `TangentSite.IsOwner`).

### 4. Arrival/identity view + credentials parity

- `ExperienceService` arrival identity segment now carries the full identity view per
  the contract (identities array, nullable did, bestLabel) — extend the W2-B1 seam, do
  not fork it.
- `ParticipantCredential.Issue` accepts internal-only participants (the W2-B1 rekey
  already relaxed `Require`; verify issuance + `Authenticate` + suspension checks work
  end to end for a `tangent:local:` bearer).
- Sign-in (`ParticipantSignIn`) is UNCHANGED — atproto only. Do not build local human
  accounts (D11 follow-up).

### 5. End-to-end proof

Extend `ExperienceIntegration/ConnectorIntegrationTests` with an unbound journey: with
the setting off → connector-side enroll fails `unbound_enrollment_disabled`; owner
enables it via the settings API; connector enrolls (real compiled binary, both the CLI
operator path and/or the enrollment hub call) → participant + identities + credential;
the connector arrives as the minted identity (**you** renders its handle); it posts into
a local-storage topic; the identity chain shows `created` + two additions; a second
enroll of the same localId → `already_enrolled`, no second participant.

## Files allowed to touch

`src/server/web/**` and `tests/TangentSpace.Tests/**` (identity wave — deliberately
wide), EXCEPT: do not touch `src/server/mcp/**` (if the connector binary misbehaves,
report — the integrator owns the Rust side), `docs/**` (integrator records), `.local/`,
git state.

## Invariants

- Enrollment NEVER auto-executes anything beyond its own participant/credential mint; no
  sign-in flows triggered; connector sign-in remains operator-initiated only.
- The identity chain is insert-only; `PostChange`/message-changelog semantics untouched.
- Write-once, same-transaction, no read-time inference (digest rule).
- Default posture: setting OFF on any server without explicit owner action (secure
  by default, ADR 0009).
- No migration/compat code (wipe rule).

## Finish conditions

1. Full suite green (`dotnet test tests/TangentSpace.Tests/TangentSpace.Tests.csproj`;
   baseline = W2-B1's reported count; reconcile every delta).
2. The end-to-end unbound journey passes against the real connector binary.
3. Do NOT `git commit`.

## Red-team self-check (answer in your report)

- Can the enrollment endpoint be used to enumerate participants or mint credentials
  while the setting is off (every blocked path returns no participant data)?
- Is `already_enrolled` truly idempotent (same localId, concurrent requests — the writes
  semaphore/transaction discipline)? Can an attacker claim another localId's participant
  by replaying with a different handle (label changes must NOT rebind identity)?
- Does the identity-history surface leak to non-owners (owner-gate realization, set
  pinned, mutations denied — mirror W1-B's controller shape)?
- Does a `tangent:local:` bearer pass anywhere it must NOT (atproto-scoped flows list —
  source writes, service proofs, PDS connections)?
