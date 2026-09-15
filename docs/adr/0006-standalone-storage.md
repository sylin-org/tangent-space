# ADR 0006 — Standalone-first conversation storage; Spaces optional

Date: 10 September 2026. Status: accepted; implements the "Identity and sharing direction" of [ADR 0001](0001-tangent-server-participation.md). Partly superseded by [ADR 0011](0011-realigned-server-architecture.md): Spaces storage is removed and every Topic uses local storage.

## Decision

A Tangent server's minimal operational model is **standalone**: it must run as a plain
service with local entity persistence, no Atmosphere dependency, no authority account and
no per-participant source grants. Conversation works out of the box.

Identity is unchanged and non-negotiable: participants are AT DIDs verified through the
existing atproto authentication; identity verification never depends on the storage scope.

Source-backed Spaces storage is an **opt-in addition**, not a prerequisite, matching ADR
0001's "an Atmosphere account adds a repository and possible public persona":

- **Local storage (default, `Tangent:Conversation:Storage = "Local"`).** New Topics are
  storage-complete at creation. Posts settle in one transaction (durable decision,
  projection, settled intent) under current room policy; the record identity is
  server-local (`local://{topicKey}/{operationId}`). Author edits and deletions update the
  entity directly; moderation remains local suppression as before. Reads, digests,
  acknowledgements and the experience API are unchanged — they consume the projection.
- **Spaces storage (`"Spaces"`).** The existing authority-backed pipeline is retained
  without redesign: Pending Topics, provisioning through the Spaces authority, native
  per-author records, verified acceptance, and honest pending outcomes when a source is
  unreachable. The authority account remains a manual operator connection today; the
  recorded follow-up (automated service-DID lifecycle) would automate it.

The storage scope is recorded per Topic at creation from the configured default. On
startup with Local as the default, Topics that are still unprovisioned **and have never
held a message or write intent** adopt the local scope; anything with content, an intent
or a mapped Space keeps its recorded scope. No silent migration of real data occurs.

## Consequences

- A fresh server needs no Atmosphere anything for full conversation: participants sign in
  with verified DIDs and post. Public-provider accounts (whose PDS cannot perform Spaces
  writes) can participate fully in local storage.
- `RoomSpaceState` gains `Local`; local Topics are policy-ready without a Space URI and
  can never be mapped to a Space afterward. Space reconciliation and provisioning skip
  them.
- Spaces-mode behavior, including the honest pending semantics and the authority
  connection, is unchanged and remains covered by tests that boot the app in that mode.
- Local records have no portable source identity; moving a Local Topic to Spaces is a
  deliberate future migration, not offered silently.
- The MCP inbound path, WebMCP, the experience API and the local Rust connector required
  no contract changes; they consume domain outcomes.
