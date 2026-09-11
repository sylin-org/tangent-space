# Refinement backlog — session handoff

Enumerated 11 September 2026 after shipping ADRs 0005–0008 end to end (experience API,
local Rust connector, standalone storage, atomic upserts, facets/profiles). Baseline: suite
**351/351**, live server running standalone storage at `http://127.0.0.1:5220`, working
human/agent exchange verified in The Lobby / Lounge (`@leo.sylin.org` ↔ `@tangent-agent.test2`
via the connector, artifacts under `.local/live-exchange/`). Items are ordered by expected
value; each names the surfaces it touches.

## 0. User pages with dual resolution (user-designated for the next effort)
**Implemented (wave 1, 11 September) under the superseding design in
[DECISIONS](../DECISIONS.md): `/u/{identifier}` is a kind-dispatched resolver (did: /
tangent:local: / handle) that redirects to the top of the chain current handle → DID →
local, following the Bluesky pattern; `/participants/{did}` was removed outright. The
original design notes below are kept for history — their "DID is canonical" framing was
replaced by the identity-collection model before implementation.**

`/u/{did}` and `/u/{handle}` resolve to the same canonical participant entity. Design
specifics to preserve:
- **The DID is the canonical form.** `/u/{handle}` is a convenience alias resolved via the
  current handle → DID lookup (exact, case-insensitive, one optional leading `@` accepted —
  same matching rules as companion selection); it canonicalizes to `/u/{did}` (redirect or
  `history.replaceState`) so permalinks survive handle changes. A stale handle URL is an
  honest miss or a lookup fallback, never a silent wrong-entity render: handles can be
  reused after rotation, so resolution is point-in-time to the current holder.
- **Exact match only** — never fuzzy. A lookalike handle must not resolve.
- Route disambiguation: segment starts with `did:` → DID; otherwise handle. Both forms
  registered in `PagesController`; the profile API (`/api/participants/{id}/profile`)
  accepts either form and returns the canonical DID.
- Facet mention links and author bylines may display `/u/{handle}` prettily but carry the
  canonical DID target; the server-side alias keeps shared links stable.
- Replaces/absorbs the current `/participants/{did}` route (keep it as an alias or migrate).

## 0b. Agent identity at the connector (user-directed design, 11 September)
Identity lives at the local MCP server. Design as enumerated by the owner, with refinements:
- **1..N identities per connector** (the companion model already isolates them; extend with
  bound/unbound state).
- **Resolution policy per caller**: single-identity connectors may auto-resolve ("this
  machine's only openclaw is Jeff") or require explicit selection ("assume agent-04 this
  session"). Auto-resolution MUST be keyed to the operator's client allowlist (clientInfo),
  not machine-wide — any MCP client on the machine must not silently become Jeff.
- **Composable minimal identities**: handle + optional display name, no atproto DID needed.
  Two-tier verification: bound = cryptographic DID proof (portable); unbound = operator
  vouching within a server relationship (scoped like localhost — never a portable proof).
  Refinement: hybrid minting — the SERVER mints its own GUIDv7 local participant record for
  an unbound companion (server-authoritative), while the connector's stable client-side
  identity provides cross-server continuity of experience, not proof. Same client GUID at two
  servers = two distinct server-scoped participants. Use a clearly-non-DID form
  (e.g. tangent:local:<guidv7>) — avoid a fake did: method name that may collide with a real
  DID method.
- **Binding is identity migration**: "sign in with the Bluesky account for this identity"
  (operator action on the connector's operator page — new surface, must be loopback-bound
  and authenticated per the ADR 0005 boundary) re-anchors each server's local record to the
  verified DID. Authorship continuity per server; audited via an identity-change chain (the
  partition-chain pattern from item 2 applied to participants).
- **Server-side sign-in research** (the connector presenting identities / holding atproto
  credentials): candidate paths — (a) atproto OAuth with the connector as native app
  (loopback redirect, operator consents once in the browser); (b) delegated signing key in
  the DID document feeding the existing service-proof exchange (/mcp/token already verifies
  DID-keyed JWTs and mints credentials); (c) formalized operator-browser consent handing the
  connector a scoped token (today's manual enrollment, made first-class). Also: Arrive
  advertises the connector's identities and the server issues per-identity credentials
  (experience API addition); server policy for accepting unbound identities (per-server
  setting with honest UI labeling).

## 0c. Deployment postures and admission defaults (user-directed, 11 September)
One composability model under a first-class posture dial (not two hardcoded modes):
- **Local posture** (single operator / swarm): the operator IS the identity provider.
  Minted agent identities in the `tangent:local:` form (0b), email/nickname accounts for
  humans who skip atproto, optional atproto binding for anyone wanting portability.
  Koan.Identity (Identity/Session/
  ExternalIdentityLink — currently bypassed for the atproto connector) is the natural
  machinery for local password/email accounts. All trust flows from the operator.
- **Public posture** (internet-exposed): same tiers, secure-by-default — approval-required
  admission, agents must present verified/bound identity (no self-minted enrollment), rate
  limits, classification gates enforced. Overridable by an operator who insists; the safe
  posture is what you get without configuring anything.
- **Small-trust-group** (LAN/VPN/team): invitation-based, minted identities fine — the dial
  must not assume exactly two settings.
Threat model to record (sharpened from the owner's "escaped instances" scenario): an open
Tangent is a purpose-built agent-C2 surface — rendezvous on third-party infra, persistent
dead-drop, Topics as task queues, free storage. Defense is economics, not detection: closed
admission makes rogue enrollment require an auditable operator action. Triad: admission
posture, identity strength, rate limits. Restate explicitly: connector sign-in flows are
never auto-executable from server discovery without operator consent (existing invariant,
now named in the threat model).

Recorded as [ADR 0009](../adr/0009-deployment-postures-and-admission.md); this item is the
design source.

## 1. Connector mints facets (agent-side picker parity)
The connector's `CreatePost` tool vocabulary has no `facets` argument; agent posts are plain
text the parser resolves. Add the argument (schema + decode + journaled body), and optionally
render facets in the connector's expanded views using the experience API's `resolved` map.
Surfaces: `src/server/mcp/src/application/operations.rs`, `hub.rs`, `src/adapters/mcp.rs`.

## 2. Edit history chain + facet recompute (user-directed design, 11 September)
**Implemented (wave 1, 11 September): shared `changelog` partition with write-once
snapshots on every change, facet-bearing edit packages with deterministic re-detection,
both classification layers computed at edit time on the ONNX connector, and history reads
on the generic entity surface (`?set=changelog`) gated per-row by an `EntityAccess<Message>`
realization. The UI viewer (wave 2) and the connector's ReadTopic markers (wave 3) remain.
See CURRENT_STATE. Original design notes:**

Edits preserve change history via Koan partitions: on every edit (author edit, delete,
moderation removal), copy the full pre-edit row into a single shared `changelog` partition
(`Data<Message,string>.WithPartition`) with a minted GUIDv7, an `OfMessageId` index, and the
`PreviousChangeId` the live row held; the live row's pointer moves to the new snapshot.
Backward-linked chain (original has null); snapshots are write-once appends. One shared
partition, never per-post (SQLite partitions are physical tables under a repository plan
cap). Reads query the partition by `OfMessageId` ordered by GUIDv7; the chain remains the
integrity spine. `PostChange` records stay the operation ledger; the changelog is the state
ledger; Spaces mode already versions via its SourceDecision ledger (unified history reader
optional). Facets ride their era's snapshot; the *new* current version gets re-detected
facets (replacing today's honest drop). UI: "Edited · view history" viewer. Optional later:
content-hash chaining per snapshot for tamper-proofing. History starts at adoption — no
retroactive recovery of never-stored prior text.
Surfaces: `ConversationService.PostChanges.cs`, `Message`, new partition access, history
endpoint + UI viewer, tests.

Framework-verified specifics (author's repo, 11 September):
- Snapshot writes use `Entity<Message,string>.Insert(snapshot, "changelog")` — insert-only
  semantics make snapshots write-once by construction (fresh GUIDv7 never collides).
- History reads can ride the generic entity surface natively: `?set=changelog` routes through
  `EntityContext.With(partition:)` (EntityEndpointService), gated by an `EntityAccess<Message>`
  realization (author-or-moderator visibility) — converging with refinement #3's direction.
- SQLite materializes the partition as a `#`-suffixed table
  (`TangentSpace.Conversation.Message#changelog`); DATA-0094 keeps the design portable to
  adapters with native partition containers.
- Bulk backfill, if ever wanted, has first-class machinery: `Copy(predicate).To("changelog")`
  transfer builders with batching.

Change classification for agents (user-directed, 11 September): an agent dropped on a thread
must be able to tell a spelling fix from a meaning change. Layered design:
1. Deterministic, free: surface metrics (Levenshtein/token overlap) + **facet diffs** —
   mention added/removed/retargeted is structural meaning change detected exactly, no model.
2. Semantic axis: a pinned small local embedding model (MiniLM-class, ONNX CPU) computing
   version-to-version distance. The valuable signal is the 2x2 of text-distance x embedding-
   distance; the small-text/large-shift cell (negation/reversal) is the dangerous one worth
   flagging. Known limit: tiny models are weak on fine negation — the classifier triages
   ("review advised"), the agent judges; verdicts are advisory, content stays authoritative.
3. Compute once at edit time and store on the snapshot ({surface, semantic, facetDelta,
   model id+version}): reads are pure lookups (deterministic, no model drift rewriting
   history's interpretation, no inference at read time per the digest rule).
4. MCP surface: edited posts carry a compact marker in ReadTopic (revisions, change class,
   meaning class, mentionsChanged); bounded history retrieval walks the chain and returns
   each version with its snapshot-time verdict. Compact shows the flag; history is explicit.

## 3. Composer-simplicity cleanup (the "EntityController" direction)
Composer visibility derives purely from the topic response; the Spaces-only readiness probe,
reconnect link and consent copy move behind the `spaceState === 'Ready'` branch so the Local
path has zero bespoke state (the bug class that produced the disappearing composer).
Later: adopt Koan `EntityAccess<T>` + `GET /new` verb projection for read surfaces
(mentionables already has the right shape). Surfaces: `rooms.js`, optionally controllers.

## 4. Search over facets
`author=did`, `tag=value`, `mention=did` queries indexed off `Message.Facets`; powers profile
"all posts" pagination (`morePosts` exists) and tag/topic navigation. Surfaces: conversation
queries, profile page, `#tag` rendering links.

## 5. Group semantics refinement
`@moderators` currently resolves to the same holders as `@admins` (owner + Tangent admins).
Distinguish per-Topic moderators (Room managers) as their own group; expose distinct counts
in the popup. Consider `@members` including readers. Surfaces: `ExperienceDigest.HoldsAnyRole`,
`ExperienceService.Mentionables.MembersOf`, tests.

## 6. Roster panel and presence
A "who's here" panel reusing the mentionables endpoint (participants + classifications +
badges). Presence needs a presence *concept* (SSE exists; do not fake it from connections);
typing indicators ride the same decision. Surfaces: new UI panel, optional server presence.

## 7. E10 — modest-model walkthrough with a named host
Still open from IMPLEMENT_LOCAL_MCP: attach the connector to a real agent host (Claude Code
channels noted incompatible with 2026-07-28 negotiation; verify the installed host), run the
select → arrive → read → respond → resume loop, record errors/sizes/calls. Also: second
companion + second server live (E02/E12 live variants).

## 8. Spaces record schema v2
Carry facets in CBOR records (lexicon bump: `MessageContent.FromCbor` is strict 3-or-4
fields); stamp facets at `Accept` for source-authored history so Spaces topics render
identically. Deliberate change, not silent. Surfaces: `MessageContent`, `Acceptance.cs`.

## 9. Automated service-DID lifecycle (Spaces mode)
The manual `tangent-authority.test` connection is the stand-in for the recorded "verified
service DID" idea (ADR 0001 direction): automate acquisition/renewal of the server's own
identity when Spaces mode is enabled. Large; needs its own ADR.

## 10. Connector polish
`check --wait` using the experience `wait` endpoint (long-poll instead of fixed interval);
an `inspect` CLI for operator visibility into state/attention/pending writes; background
check already reconciles receipts.

## 11. Known v1 characteristics (not bugs, worth measuring before scaling)
Digest assembly scans per response; mentionables queries per keystroke (30 s cache client
side); renderer decorates ranges without verifying segment text (guarded by composer-side
verification and the edit-drop rule).
