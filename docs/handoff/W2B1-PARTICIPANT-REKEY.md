# W2-B1 — Participant rekey: GUIDv7 spine with an identity collection

Wave 2 brief. One agent, runs ALONE (identity work never parallels its dependents). .NET
server: `src/server/web` + `tests/TangentSpace.Tests`. This is the mechanical half of 0b:
re-key `Participant` from DID to GUIDv7 and move DIDs/handles into an identity collection.
No new features (enrollment, chain, credentials for locals are W2-B2). Decision record:
`docs/DECISIONS.md` — "Participant identity: GUIDv7 spine with an identity collection"
(read first; it is the spec). Standing wipe rule applies: **no migration code** — the dev
server will be reset by the integrator; tests rebuild their worlds.

## Design

### 1. Entities

- `Participant` (`Participants/Participant.cs`): `Id` = GUIDv7 string minted at creation.
  `Handle` REMOVES from the row (labels belong to identities). Classification, Declare,
  JoinedAt/LastArrivedAt, IsSuspended unchanged. `FirstArrival(verifiedDid, handle, now)`
  becomes `Enroll(verifiedDid, verifiedHandle, now)`: mint GUID, add an atproto identity
  entry carrying the handle as its label. `Return(...)` matches on the atproto identity
  value and refreshes that entry's label.
- New `Participants/ParticipantIdentity.cs`: `Entity<ParticipantIdentity>` keyed by
  `{Kind}\u0001{Value}` (or a GUID id with a uniqueness query — your call, but exact-value
  lookup must be one query): `Kind` (`atproto` | `internal` | `connector-client`),
  `Value` (the DID / `tangent:local:{participantId}` / client GUID), `Label` (current
  handle for atproto entries; the handle for connector-created locals; null for
  internal), `ParticipantId` (GUID FK), `AddedAt`. The internal identity is DERIVED
  (`tangent:local:{Participant.Id}`) and minted for every participant at creation;
  storing it as a row is fine (uniform queries) — value must equal the derivation.
- New service `Participants/ParticipantDirectory.cs` — the ONE seam replacing scattered
  DID lookups: `ByDdid(did)`, `ByIdentifier(identifier)` (delegates exact matching to the
  existing `ParticipantLookup` rules), `BestLabel(participantId)` (atproto label > local
  label > `tangent:local:{id}`; this is the display/canonicalization chain), `LabelsFor(
  IEnumerable<Guid participants>)` (batch for resolution maps — the digest/byline path
  today reads `Participant.Handle`; all such sites go through this).

### 2. Reference renames (mechanical, wide)

`Message.AuthorDid` → `AuthorParticipantId`; `RemovedByDid` → `RemovedByParticipantId`;
`ParticipantCredential.ParticipantDid` → `ParticipantId`; membership/invitation/watch/
restriction/restriction-result entities' `ParticipantDid` → `ParticipantId`; the Mcp/*
Operations* DTO fields likewise. The claim convention changes: principals carry
`tangent:participant` (the GUID) ALWAYS, plus `AtprotoClaimTypes.Did` ONLY when the
participant holds an atproto identity (bearer minting in
`Participation/ParticipationCredentials.cs`; cookie sign-in keeps stamping both).
`Participation/ParticipationAccess.Require` gates on the `tangent:participant` claim
(existing + not suspended) — atproto-DID requirement moves OUT of the universal gate;
atproto-specific flows (source writes, service proofs, AtProtocol/*) keep their own DID
checks where genuinely needed. `X-Tangent-Participant` compares the GUID.
`ExperienceIdentity.ParticipantRef` carries the GUID; `Did` becomes nullable (populate
from the atproto entry when present). `ParticipantWelcome` (participation arrival)
splits the same way. `clients/participant/client.mjs`'s `did:` pin relaxes to
`participantRef` (it is exercised by integration tests).

### 3. What does NOT change

- `PostFacet.Did` stays a DID string (facet targets are perennial EXTERNAL identifiers,
  resolved through the collection at read time). Mention parsing, digest prose matching,
  ambiguity guards: resolve DIDs via the directory; handle ambiguity checks move to
  identity-entry labels with identical semantics.
- AtProtocol/* (service proofs, at:// URIs, source readiness) stays DID-scoped; it
  crosses to participants via `ByDid` at the boundary.
- `/u/` resolution semantics (W1-A) unchanged — `ParticipantLookup` dispatch already
  anticipates the forms; it now hits the identity entries.
- Spaces record schemas, message changelog partition, history surface: untouched except
  for the rename ripple.

### 4. Tests

All seeding goes through the new `Enroll`/directory API. DID literals in tests remain
valid as identity VALUES; what changes is participant keying and principal construction
(`new Claim("tangent:participant", guid)`). ~21 files, ~120 literals — mechanical.
`ExperienceWebApp` seeding, `McpAuthenticationTests.HostFixture`, `ArrivalRulesTests`,
`ParticipationTests`, `UserPageResolutionTests` are the big sites.

## Files allowed to touch

All of `src/server/web/**` and `tests/TangentSpace.Tests/**` (this brief is deliberately
wide — it IS the rekey), plus `clients/participant/client.mjs` (one relaxation). NOT
allowed: `src/server/mcp/**` (Rust), `wwwroot/**` except where a GUID/id literal is
read from an identity payload (verify agent-page/webmcp read participantRef — if they
read `.Id` from a welcome payload they keep working; fix only if broken, and say so),
docs (the integrator records the rekey), `.local/`, git state.

## Finish conditions

1. `dotnet test tests/TangentSpace.Tests/TangentSpace.Tests.csproj` fully green
   (baseline 385; test-side mechanical updates expected, count may shift — report the
   number and reconcile every delta).
2. `grep -rn "AuthorDid\|ParticipantDid" src/server/web --include="*.cs"` returns zero
   (except historical comments you rewrite anyway).
3. No Participant row anywhere is keyed by a DID; no `Handle` property on Participant.
4. Do NOT `git commit`.

## Red-team self-check (answer in your report)

- Does any path still conflate "has atproto identity" with "is a valid participant"
  (e.g. an internal-only participant hitting an atproto-only flow — name the flows and
  their honest errors)?
- Are all `Participant.Get(did)` sites now directory lookups (grep `Participant.Get(`)?
- Duplicate-handle ambiguity: does every previous guard still exist (digest, /u/,
  mentionables, re-detection) against identity-entry labels?
- Does the internal identity row stay consistent with its derivation (creation is the
  only writer — confirm)?
