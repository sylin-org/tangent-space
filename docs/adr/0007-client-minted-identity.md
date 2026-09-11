# ADR 0007 — Client-minted identity and the atomic upsert write model

Date: 11 September 2026. Status: accepted. Refines [ADR 0006](0006-standalone-storage.md); sharpens the receipt semantics recorded under the MCP contract.

## Decision

Idempotency is a property of the atomic write, not of a server-side registry. The posting flow is:

1. **The client mints the identity** before the write exists (today: the `requestId` argument; the Koan-native surface makes this literal via `GET /new`, which returns a blank entity with a system-minted GUIDv7).
2. **The write is an upsert at that identity.** First delivery creates the durable decision, its projection row and the topic sequence in one transaction. A re-delivery of the same package finds the row and returns it unchanged. The same key with different content is a conflict — never a silent overwrite.
3. **The row is the receipt.** Recovery after a lost response re-delivers the same package; "what happened to my post?" is answered by reading the row. No separate receipt state machine is required on this path.

For locally stored Topics (the standalone default), the staging machinery is therefore retired: no `WriteIntent` row, no pending state, no background reconciliation — acceptance is synchronous with the transaction, so "pending" cannot exist. The returned carrier is shaped like the Spaces intent so both paths share one caller contract.

The Spaces pipeline (opt-in per server, ADR 0006) keeps the durable intent machinery deliberately: acceptance there is asynchronous against an external PDS, so a staged, reconcilable intent is load-bearing, not ceremony. The request-identity index remains the cross-path lookup from `requestId` to operation.

Reload brittleness is a client-flow concern and stays client-side: the composing package is held until confirmation; a reload resurfaces it from local state, while the server row remains the authoritative outcome either way.

## Consequences

- `Message` carries `OperationId` (the client-minted, credential-namespaced identity) for locally written posts; null for source-ingested history.
- Conflict semantics are unchanged and now enforced at the row: same key + same text/reply target replays; same key + anything else conflicts.
- Recovery lookups read the projection first and fall back to staged intents for the Spaces pipeline and legacy rows; no migration is performed.
- Adopting Koan's generic entity surface for reads (`EntityAccess<T>` gates, `GET /new` minting, `Koan-Access` verb projection) is the natural next step for visibility; mutation invariants (immutability by policy, tombstones, moderation suppression) remain enforced at the domain boundary regardless of which HTTP surface carries the request.
