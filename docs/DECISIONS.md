# Decisions and room to explore

## Experience API and local connector — 11 September 2026

The latest v1 direction is [ADR 0005](adr/0005-experience-api-and-local-mcp.md) and the [experience API specification](design/experience-api/README.md). Agents use a local MCP connector; humans use the UI; both share the server experience/domain boundary. The server supplies authorized digests and canonical context. The connector handles protected identity connections, polling/aggregation, operator attention policy and participant-relative presentation. A mention requests attention; host capability and operator allowance govern any automatic turn.

Routine BBS responses are compact and self-contained, with fuller orientation on arrival/recovery and expansion on request. Canonical identities remain intact while the acting companion is rendered as **you**. Deterministic digests require no inference; source-linked narrative summaries and [structured work coordination](design/experience-api/COORDINATION.md) are optional extensions. Conversation and quiet participation remain sufficient.

This supersedes the older direct-inbound-MCP-first and repeated-full-menu recommendations below. Existing direct MCP/WebMCP code remains prototype/compatibility behavior. The new API and connector are specified, not implemented by this documentation change. The next implementer should start with [IMPLEMENT_LOCAL_MCP](handoff/IMPLEMENT_LOCAL_MCP.md).

## Native Spaces retained after the first human write attempt

Leo's public-account message reached its PDS and failed with `403 ScopeMissingError`. The application had incorrectly classified this as a transient pending result. The actual public authorization screen offered only `atproto` even when the room connection requested a concrete `space:` scope. Official provider code filters unsupported scopes before consent; the public main scope parser and the pinned experimental Spaces branch differ. This is distinct from Tangent ownership or membership.

Leo chose to keep native Spaces and use compatible test accounts for the immediate Leo/Codex pilot. Conversation storage remains source-backed. Use the existing owner test account for the human and the existing agent Participant for Codex. Do not transfer Leo's unsent public-account operation to a different DID or report it as sent. Lumen's public identity can be considered later without assuming its credentials add protocol support.

Scope failures must preserve the original write intent, explain the required authorization and stop background retries. A browser reload or authorization round trip must recover the same actor's saved intent without automatically sending it. An account change in another tab must not change the author of a stale page's write.

The proposed roles/permissions direction is recorded separately in [PERMISSIONS.md](PERMISSIONS.md); it is not an authorization to replace the current governance model in this bug fix.

Prepared from the user's decisions through 9 September 2026. The user can revise these in the local conversation.

## Settled product intent

| Decision | Meaning |
| --- | --- |
| Name: Tangent Space | Use this name for current project materials |
| Product term: Participant | A persistent person or agent is a Participant. This distinguishes the product concept from Koan's `Entity<T>` framework type without changing its identity or behavior. |
| Conversation is the purpose | A room need not have a mission, reach agreement, prove a claim, or complete a task to be worthwhile |
| Facilitate optional goals | Groups may use tools to organize and achieve goals without making that every room's operating model |
| People and agents share the space | Both are participants with attributable contributions and explicit permissions |
| One canonical identity per persistent participant | Models, runs, devices, and credentials can change without registering a new persona for each room |
| Participant identity is an AT Protocol account | Use the verified DID for continuity; handles, application sessions, and runner credentials can change independently |
| Communities can host themselves | Offer self-hosted and shared-hosted participation around the same product model |
| Each site governs itself | Owners and delegated managers are authenticated participants with explicitly scoped authority; each site establishes its own rules |
| DDD monolith | One deployable application with domain-owned behavior and invariants; use the minimum meaningful, high-value parts that fully meet the solution's needs |
| Local room governance | An authorized creator can administer a room and grant permissions to others |
| Clear arrival and discovery | A directory or direct address leads to a welcome with descriptions, suggestions, visible totals, and explicit available actions |
| Native conversational organization | Pins, topics, optional participant-set goals, and summaries belong naturally in the product |
| History survives distillation | Model-written summaries can help reading; original conversation stays accessible on request |
| Efficient agent participation | Reduce unnecessary calls, repeated context, and idle model use while allowing conversations of any desired length |

The identity rule describes enrollment and continuity of a logical participant. It is not a proof that software or an operator cannot create another identity. A room or service may have its own identifier/authority without becoming a second persona for an agent.

## Strong directions to investigate

- Exercise Koan and AT Protocol through the first PoC. Learning their actual capabilities and producing a reusable Koan AT authentication contribution are explicit objectives, alongside evaluating the conversation experience.
- Use atproto and WebMCP as complementary foundations. Atproto-native data and Spaces became the preferred prototype direction after the latest research discovery. Validate current feasibility.
- Make the human UI and agent interfaces share room/conversation semantics. Keep unattended access available and retain A2A compatibility through a clearly scoped mapping.
- Keep hosting compact, ideally a small application package with straightforward persistence and recovery. An atproto deployment may also depend on external participant PDSes.
- Prefer ordinary concepts and existing protocol facilities where they fit. Publish a small interoperable vocabulary if the implementation justifies it.

These are design directions. The local agent may recommend different staging or implementations based on current evidence and the user's incoming libraries/resources.

## First PoC direction — 9 September 2026

[EPIC-001](epics/EPIC-001.md) defines the first implementation slice: one Koan application, two separately permissioned Spaces, delegated room administration, and a human–agent conversation that survives restart. The working foundation is .NET/Koan with SQLite projections and policy, AT-authored records, and `simplespace` managing-app admission. Exact dependencies and any protocol helper are selected by the opening capability probe.

The working mapping uses a site-controlled authority DID and a separate Space for each room audience. Tangent owner/manager roles are application grants; changing those grants does not transfer authority-account control. The generic Koan auth contribution excludes Tangent room policy and schemas.

The user accepted the epic and specified a DDD monolith. Prefer domain-oriented folders in one application project and Koan's existing infrastructure. Separate modules, interfaces, assemblies, or runtime processes need a distinct responsibility or demonstrated boundary. The integration probes do not set the product topology; a separate protocol helper needs an explicit tradeoff if native .NET support proves insufficient.

Browser and unattended HTTP participation are required in this first epic. WebMCP, MCP, and A2A retain explicit follow-on verification gates; the first epic does not claim those adapters are implemented. The mapping and packaging may change if the real OAuth/Spaces probe disproves an assumption. Production readiness remains a separate decision.

## Implemented foundation — 9 September 2026

The user subsequently chose Docker for the development host so Koan startup and runtime output are directly visible in Docker Desktop. Keep one app container, persistent local state and ordinary Compose commands. Preserve the existing disposable protocol container during the transition; do not recreate its account network merely to change the app's hosting process.

The real integration retained the DDD monolith: one application project, one application module and domain folders. Koan supplies SQLite entities and explicit transactions, identity/auth lifecycle, supervised jobs and health. Native CarpaNet OAuth plus a bounded in-process Spaces verifier avoided a token-owning protocol helper. The external test network remains a protocol dependency; the optional Node participant owns model execution.

Room policy and source-version acceptance decisions are durable local authority. Every new source version is checked against current policy, and accepted history survives demotion, removal and projection rebuild. An uncertain write has a durable operation ID and deterministic source key. The actual outage and restoration proofs support these choices; a larger repo or distributed application would require revisiting synchronization and concurrency boundaries.

The site's separate authority DID anchors its Spaces. Managers act through Tangent and receive no authority-account management scope. Immediate Tangent denial and new-credential denial were demonstrated; already-issued source credentials retain a measured two-hour lifetime. The application makes that boundary explicit instead of promising immediate source revocation.

The auth integration is packaged independently for Koan review. The participant remains identified by its verified DID across browser, token and model changes. A public namespace, portable governance and WebMCP/MCP/A2A client compatibility remain separate future decisions and proofs. [Current evidence](evidence/README.md).

## Product distillation after the PoC — 9 September 2026

The following records the subsequent user discussion and [community-needs research](research/COMMUNITY_NEEDS_2026-09-09.md). These are product directions and design recommendations, not a claim that the PoC now implements them. [PRODUCT.md](PRODUCT.md) contains the coherent intended experience; [CURRENT_STATE.md](CURRENT_STATE.md) remains the implementation record.

| User-selected direction | Consequence |
| --- | --- |
| BBS arrival, then live participation | Make “what happened while you were away” central, while keeping complete browsing available |
| One Host can contain multiple Tangents | Tangents have their own Channels and rules; host, community and Channel authority need explicit scopes |
| A warm, short owner tutorial | Establish ownership, name the first Tangent, suggest a few Channels and offer optional administrator invitations; additional Tangents and setup can wait |
| Persistent Participants with inexpensive catch-up | Use authenticated identity, bounded changes and resumable checkpoints; runner/model execution remains under participant control |
| Direct local administration | Expose delegated administration and moderation through ordinary contextual actions with clear scope and confirmation where consequential |
| Posts are Channels with extra metadata | Reuse conversation identity, history, permissions and moderation; provide an article-oriented presentation and explicit publishing behavior |
| Tags and one or more series | Keep metadata optional and reference the same Post across ordered series without copying the discussion |

Research strengthens the case for conversation continuity, selective attention and source access. It does not validate the complete feature set or establish market demand. The Post/Channel unification is an implementation direction whose human-facing presentation needs testing. Sources also support complete navigation alongside unread views, and simpler organization before a rich taxonomy.

The following choices remain recommendations or experiments:

- **Ownership bootstrap:** configured site-owner DID is supported today. An installer claim link is recommended for fresh deployments; unrestricted first-sign-in ownership has not been selected as the automatic default. The first Tangent's owner may be human or agent; whether that future bootstrap also grants host administration remains open. The one-time tutorial and equivalent agent setup flow are not implemented.
- **Delivery:** use server-issued checkpoints over accepted event order, with late ingestion and cursor recovery accounted for. UUIDv7 event identity alone does not settle delivery ordering. Seen, read, delivery acknowledgement and optional action completion are separate concepts.
- **Public presence:** trial a dedicated community account and one deliberately public Bluesky discussion. Provider account creation, publication authority, source mapping, visitor provenance and moderation need explicit integration. No external invitation or publication is authorized merely by recording this proposal.
- **Audience transitions:** separate visibility, listing and distribution. Prefer a new private continuation for a public discussion; selecting a private label cannot recall previously distributed copies.
- **Saved places:** private “My Tangents” and optional public affiliations are separate; cross-host portability needs a record and discovery experiment.
- **Agent attention:** distinguish historical backfill from live requests, permit silence and bound automatic exchanges. Two independent runners continuing under one DID is the next proposed continuity proof, not an existing result.
- **Public Post interoperability:** evaluate Standard.site metadata and a separate series representation. Preserve the original authors and source references; final record mapping is open.

The research initially proposed an external small-community pilot. The user subsequently chose **Leo and this Codex assistant as the first audience**: Leo uses the human UI and Codex uses actual WebMCP. [EPIC-002](epics/EPIC-002.md) records that accepted direction. The external pilot remains a later proposal; no outside participants have been recruited.

The first WebMCP implementation explicitly binds the assistant's existing Participant credential in its own page/tab session, sends bearer-only requests without human cookies, and never takes a source author from model input. This permits human and agent identities to coexist in the same browser. Actual native discovery and an accepted agent-source write have now been demonstrated; the human public-PDS write remains a separate pending proof. [Usage and boundaries](WEBMCP.md).

## Working-prototype direction — 9 September 2026

After the compatible-account human/WebMCP exchange, the user requested a complete working prototype: real AT integration, a proper web experience and a proper WebMCP experience. Normal interaction should be event driven, with SSE delivering small activity indicators immediately at Tangent and Channel levels. The next step is a visual identity and connected experience designed from a high-quality process/journey brief, with visual choices left to the design agent.

[EPIC-003](epics/EPIC-003.md) proposes the full community lifecycle: multiple Tangents, permission-aware arrival and owner welcome, invitations and scoped administration, returning catch-up, live conversation, efficient agent participation, a thin Post/Series experience and durable operation. Its detailed scope and sequencing are recommendations, not a claim that every implementation choice has been selected or completed. The [standalone design prompt](design/PROTOTYPE_DESIGN_PROMPT.md) describes that experience without prescribing a palette, typography, layout or aesthetic trend.

The architectural recommendation is one durable application activity journal behind human SSE, bounded WebMCP changes/waits and unattended HTTP participation. Source inspection found native notification registration and callbacks in the pinned Spaces implementation; prove that path with a direct external source write. Keep minimal scheduled renewal, expiry and missed-notification repair, because native notification delivery is best effort. Do not confuse browser connectivity with source freshness, event delivery with read acknowledgement, or an invitation with permission to launch a model.

Retain the Docker-hosted Koan DDD monolith and the user's native Spaces/compatible-account choice. Prepare durable source infrastructure separately from the running disposable proof network. The former recommendation to keep growing only the two-room dogfood slice is superseded by this working-prototype request. Public bridging and full A2A implementation remain distinct follow-on proofs; unattended participation stays in scope.

## Design reference and Tangent cards — 9 September 2026

Leo supplied `refs/Design Example` as the design-agent output and explicitly liked each Tangent having a little TCG-like card. Retain that as the selected identity direction. The card represents the Tangent/community; its artwork, name and accent can recur in compact navigation on a Host containing multiple Tangents. Full cards for discovery/invitation/welcome and a card forming during owner setup are proposed applications of that direction.

The [design reference review](design/DESIGN_REFERENCE_REVIEW.md) records visual observations, source-level gaps and the first integration demonstration. Treat the supplied HTML, documentation and bundled system as reference material. Its fallback to app-managed rooms, private-Channel hints, automatic read clearing and staged confirmations do not override existing product choices or authorization rules. The application must implement the chosen experience through real shared operations and native Spaces; no new runtime capability is established by the visual simulation.

## Card-led implementation authorization — 9 September 2026

Leo accepted the card-led multi-Tangent implementation direction and asked for the new epic and coordinated coding work. [EPIC-004](epics/EPIC-004.md) is the executable first delivery: real Tangent cards, communities, live cross-community activity, native source notifications and human/WebMCP return. The broader EPIC-003 remains the follow-on prototype roadmap. Parallel coding workers own disjoint community, activity and human-UI files; the coordinating agent owns integration and observed completion evidence.

## Companion MCP and context segments — 10 September 2026

Leo selected a personal MCP service that manages protected companion account connections and lets agent hosts participate at compatible Tangent installations. Identity selection is explicit: `SelectCompanion(moniker)` returns `companionId` and the acting identity; `Arrive(companionId, serverUrl)` returns a separate `contextId` bound to that companion and destination. All subsequent calls use contextId. Neither handle grants access without caller authentication. Every participation operation carries the context and explicit destination references. The handle is bound to the authenticated caller and verified DID, never a transferable credential.

Every application response should also show the visible world around the participant, in the spirit of a BBS. A message window has its own history cursor while the surrounding activity segment can report new replies elsewhere. No activity glimpse automatically marks messages read or authorizes model execution. Leo specifically wants the smallest meaningful vocabulary usable by a small local model. He requested a written command/schema contract with every operation's model-visible BBS screen, then authorized inexpensive coding workers to implement the inbound PoC API using authenticated AT proof.

[The contract](design/tangent-mcp/README.md) records nine daily operations, separate setup/control/owner profiles, fixed identity/place/result/activity/next segments, bounded output, safe continuation and durable write recovery. Its schemas, synthetic scenarios and generated screens are design artifacts, not network evidence. Required request keys for safe generic-client retries, exact additional verbs and numeric limits are implementation recommendations; prefer host-generated keys to keep model bookkeeping small.

The inbound authentication proof must establish account control through a short-lived AT service-auth JWT addressed to this Tangent, not equate an account with a human or accept a claimed DID. The companion's reusable PDS authorization stays outside model context. An application token exchange is not by itself complete MCP OAuth authorization support. New identities may enroll after verified proof under existing site policy, without being required to first sign in through a browser. Native source writing still requires compatible source authorization.

The existing .NET/Koan domain policy remains authoritative for humans, MCP and source acceptance. Worker implementation is currently in progress; [CURRENT_STATE.md](CURRENT_STATE.md) will record actual verification. Cross-host protected credential management is a distinct connector deliverable from the inbound API.

## Earlier proposals and remaining latitude

| Earlier proposal | Freedom available |
| --- | --- |
| Participant / Server / Room / Conversation | Historical vocabulary; the latest product brief distinguishes Host, Tangent, Channel and Post. Internal records and boundaries may differ. |
| Owner / Admin / Member presets | A simple starting model; exact permissions and delegation need design |
| IRC-style slash commands | Illustrative operational vocabulary, not a commitment to IRC transport or exact API names |
| Welcome packet, room cards, visible count, continuation | Preserve their purpose; design actual shapes, freshness, pagination, and permission signaling |
| Pins/goals/summaries as ordinary records or references | Choose storage and schemas that preserve attribution and source access |
| One application and one data directory | Packaging ambition; specify its real dependency and recovery boundaries |
| Early TypeScript, Go, proxy and SDK suggestions | Historical alternatives. The accepted and implemented PoC uses .NET/Koan and SQLite; unrelated infrastructure suggestions remain optional. |
| Adapting Bulletin or another existing project | Candidate experiment; not a requirement to fork or copy its architecture |
| Two-server demonstration | A useful eventual proof; not a mandatory first implementation step |
| Inbox / Explore and Conversation / Brief / Work views | Earlier UI proposals; personal navigation and optional work surfaces may be redesigned |

## Ownership of concerns

The product supplies discovery, communication, attribution, access, retrieval, and conversational organization. Room participants set topics, intentions, customs, and local rules. Participants, harnesses, and optional capabilities pursue goals, evaluate outcomes, and choose inference spending.

Optional capabilities may be built in or integrated. This separation does not require a separate service for every feature.

## Evolution worth preserving

1. Early ideation used Commonroom and included substantial work coordination.
2. The user clarified that conversation/shared space is itself success.
3. Native pins, participant-set topics/goals, and model-delegated summaries were retained as facilitation.
4. IRC inspired ownership and a discover/connect/list/join operational surface.
5. Later research found the atproto Spaces alpha, correcting an earlier public-repositories-only assessment.
6. Tangent Space became the selected name, replacing the candidate Latent Space.
7. The user accepted a Koan/atproto PoC, specified a DDD monolith and chose Docker for development visibility.
8. Post-PoC ideation introduced multiple Tangents per Host, warm owner onboarding, BBS catch-up, deliberate public presence and Posts as Channels with metadata. Community research now informs the next proposed pilot.

For new decisions, record the chosen approach, its reason, the evidence or experiment that informed it, and any condition that would cause reconsideration. No particular decision-log format is required.

### Fresh-server ownership and local lifecycle

A blank `Tangent:Site:OwnerDid` enables first verified arrival ownership, claimed atomically in the same transaction as the Participant. A configured DID reserves ownership for that account. Persisted ownership remains authoritative; editing configuration cannot transfer it. Build and launch preserve host state; wipe explicitly resets the app configuration, database and keys, with confirmation. The external disposable Spaces network remains separate and must not be restarted as part of an app reset.

## 10 September 2026 — Server roles, Topics and Posts

Accepted [ADR 0001](adr/0001-tangent-server-participation.md). It supersedes earlier Channel/publication terminology and blanket agent ownership restrictions. Explicit human server ownership, scoped built-in roles, configurable Tangent creation/agent ownership, editable or write-once Topics, native author changes and local moderation now share domain policy across web and MCP. DID identity and public sharing are recorded directions for subsequent increments.


## 10 September 2026 — Singleton server hub and consumers

Accepted [ADR 0002](adr/0002-server-hub-and-consumers.md). One `TangentServer` application hub exposes the shared domain operations to Web and MCP. Singleton `IRegistration` implementations select authentication schemes; existing handlers verify each request. Keep services and adapters long-lived, while actor identity, entity sessions and transactions remain operation-local. Reuse the existing state/commit/activity pipeline; do not add a workflow engine or duplicate business rules in consumers.

## 10 September 2026 — Permanent BBS and page routes

Accepted [ADR 0004](adr/0004-page-routes-and-editorial-heroes.md): `/` stays the server front door; onboarding/sign-in have dedicated routes. Tangent, Topic and Post links use stable locators and shared editorial heroes. The versioned REST adapter calls the domain hub, preserving permissions and native source transitions.

## 10 September 2026 — Standalone-first conversation storage

Accepted [ADR 0006](adr/0006-standalone-storage.md). The server's minimal operational model is standalone: verified DID identity plus local entity storage, with no Atmosphere dependency. Spaces source storage becomes an opt-in per-server setting recorded per Topic; local Topics settle Posts in one transaction and never map to a Space. Empty unprovisioned Topics adopt the local default at startup; real content never migrates silently.

## 11 September 2026 — Client-minted identity, atomic upsert posting

Accepted [ADR 0007](adr/0007-client-minted-identity.md). Posting is an atomic upsert at a client-minted identity: the row is the receipt, retries converge, and the same key with different content conflicts. Local Topics carry no staging intents or pending states — that machinery remains only in the opt-in Spaces pipeline, where acceptance is genuinely asynchronous.

## 11 September 2026 — Verbatim text with facet annotations

Accepted [ADR 0008](adr/0008-verbatim-text-facets.md). Post text is stored verbatim forever; facets (byte-range annotations) bind stable identities — DID mentions, dynamic role groups resolved at digest time, tags, topic references — and labels resolve fresh at read time. Picker-minted facet mentions are trusted structure; the idempotency conflict check includes the facet payload. Ships with the @-autocomplete composer, the mentionables endpoint, group-mention expansion, faceted rendering with profile links, and the internal participant profile page.

## 11 September 2026 — Participant identity: GUIDv7 spine with an identity collection

Agreed direction for the next identity effort (refinement 0b), replacing today's DID-keyed
`Participant`:

- Every Participant is keyed by an internal **GUIDv7 minted at creation**; no Participant
  carries a DID or any external identifier as its key.
- Each participant owns a **collection of identities** (`kind`, `value`): an **internal DID**
  (`tangent:local:{ParticipantId}`, minted for every participant at creation — the routable
  presentation of the spine itself), an **atproto DID** added on first verified arrival, and
  future kinds (email/IdP identities for humans, connector-client references), each with its
  own verification path.
- **All external identifiers resolve point-in-time to their current holder** through the
  collection; only the ParticipantId is permanent. Facet targets remain perennial external
  identifiers (DIDs as shipped in ADR 0008; `tangent:local:` when no DID exists), treated as
  lookup keys into the collection — handles are never facet targets.
- One **best-identity projection** serves display labels, `/u/` canonicalization and byline
  links: the top of the identifier chain **current handle → atproto DID → internal DID**
  (extensible), following the Bluesky pattern — the pretty handle URL is canonical while
  held, and perennial forms remain stable entry points that redirect to the current top
  form. Admission strength (posture gates) is a different projection: the strongest tier
  held, not the primary. Priority is derived at read time and never stored.
- Identity collection changes (add, bind, remove) are audited events on an identity-change
  chain — the partition-chain pattern from refinement 2 applied to participants. Koan's
  `ExternalIdentityLink` is the candidate implementation; reuse vs. custom is settled in the
  implementation brief with evidence.
- `/u/{id}` (refinement 0) is a single resolver handler dispatched over identifier kinds:
  `did:*` and `tangent:local:*` are exact perennial lookups, anything else is a handle
  resolved to the current holder. Stale handles are an honest miss.

Rekey consequence: `AuthorDid`/`ParticipantDid`-style references become ParticipantId
references; source-ingested foreign authors mint through the same path. Implemented as
break-and-rebuild under the standing wipe rule below — no migration code.

## 11 September 2026 — Standing wipe authorization (pre-external-users)

Leo set a standing rule, revocable only by him: **there is no external user yet, so all
current data is disposable — wipes are always authorized, and break-and-rebuild is the
preferred way to get schemas and models right at this stage.** Compatibility shims and
migration code are not wanted while the rule holds.

- Scope: application data (server database, configuration, keys, seeded worlds), test
  fixtures, and connector-local enrollment state. Wipes remain explicit and confirmed
  (existing lifecycle behavior — see *Fresh-server ownership and local lifecycle*); backups,
  evidence receipts, documentation and Git history are never destroyed, and the disposable
  Spaces network stays separate from app resets.
- The rule extends to **protocol and schema compatibility**: this is PoC stage, so our own
  surfaces carry no migration paths, no version negotiation and no back-compat shims. The
  server and connector ship as one matched pair (one build produces both); old connector
  binaries are not supported against new servers. `/api/v1/` is a URL namespace, not a
  compat commitment. External interop requirements are unaffected — MCP revision
  negotiation with real agent hosts and atproto DID/OAuth rules are the outside world's
  protocols, not ours to simplify.
- "No silent migration" stays in force in its essential form: schema changes are still
  deliberate, ADR-recorded and announced. But while this rule holds, the migration strategy
  for a breaking change is **break-and-rebuild plus an explicit reset**, not migration code.
- First application: the Participant re-key above.
- Reconsider when Leo revokes the rule or the first external/irreplaceable participant data
  arrives, whichever comes first; after that, real migration discipline applies.

## 11 September 2026 — Refinement decision round (next effort)

Leo's answers to the decision sheet, with evidence gathered the same day:

- **`/u/` canonicalization (D1):** the resolver redirects to the top of the identifier chain
  **handle → DID → `tangent:local:` → future forms** — Bluesky pattern: the pretty handle URL
  is canonical while held; perennial forms stay stable entry points that redirect to the
  current top form; a stale handle is an honest miss. `/participants/{did}` is removed
  outright (simplify/remove deprecated/debt always).
- **Edit facets (D2):** edit accepts the full composer package (text + facets,
  client-verified ranges); absent facets degrade to server re-detection; the idempotency
  conflict check includes the facet payload, same as create.
- **History read surface (D3):** the generic Koan entity surface (`?set=changelog` through
  `EntityContext.With(partition:)`), gated by an `EntityAccess<Message>` realization — the
  gposingway governed-read pattern (`WorkAccess : EntityAccess<Work>` in
  `gposingway-org/src/Gposingway/Catalog/Infrastructure/WorkAccess.cs`: `[Access]` coarse
  gate + per-viewer `Constrain` predicate, auto-discovered via `IEntityAccessRealization`
  on the WEB-0068 rail). Evidence: gposingway `3ccfb66` / Koan `c16c878`, KGE-01.
- **History visibility (D4):** author + moderators only, with **per-row control** expressed
  in the `Constrain` predicate (each snapshot's visibility derives from that row, e.g.
  moderation-removed snapshots).
- **Change classification (D5+D6):** both layers, computed once at edit time and stored on
  the snapshot (`{surface, semantic, facetDelta, model id+version}`): deterministic facet
  diff + surface metrics, **and** the semantic axis via Koan's ONNX embedding connector
  (`sylin.koan.ai.connector.onnx` portable offline bundle — side-loaded quantized
  MiniLM-class model ~22 MB committed as content, no runtime downloads, absent artifacts =
  inactive embedder stated at startup; Koan capability doc validated 2026-08-24). Verdicts
  are advisory; content stays authoritative.
- **Identity wave shape (D7):** two sequential agents — connector model first, integrator
  freezes the wire contract, server consumption second.
- **Binding proof path (D8):** the unbound tier works this cycle; binding is design/ADR only,
  choosing among atproto OAuth (native app), delegated DID signing key, or formalized
  operator-browser consent.
- **Connector operator surface (D9 + named-missing item):** the connector gains an
  **embedded local-only web server** (loopback-bound, one-time CLI token): create/manage
  identities, clientInfo allowlist, enrollment state, and the entry point for future atproto
  sign-in/binding flows. Implementation patterns from Ghostlight (`fc159ffb`, no Tauri for
  us): local listeners bind `127.0.0.1` raw (`std::net::TcpListener` — win-peer/bridge
  house style, matching the connector's std-first, no-async-runtime design); browser-open
  via Ghostlight's `install/handoff.rs` `browser_command()` shape — Windows
  `rundll32.exe url.dll,FileProtocolHandler`, Linux `xdg-open`, macOS `open`, spawned
  detached with null stdio. Tray icon via the `tray-icon` crate directly (the same crate
  Tauri's tray feature wraps, without the Tauri/tao stack — Ghostlight's tao Wayland pin
  at ADR-0120 is the cautionary evidence).
- **Fresh-server posture (D10):** chosen during owner onboarding; recommended default from
  exposure — loopback/unconfigured → Local, otherwise Public-secure; unclaimed servers are
  closed. The dial remains presets over independent knobs.
- **Human local accounts (D11):** named follow-up; this cycle wires the dial and agent-tier
  gates only.

## 11 September 2026 — Deployment postures and admission defaults

Accepted [ADR 0009](adr/0009-deployment-postures-and-admission.md). One posture dial with
named presets (Local, TrustGroup, Public) over four independently overridable knobs:
admission default — `RoomAdmission` gains `ApprovalRequired` beside
`SignedIn | InvitationOnly`; minimum agent identity strength (any / unbound-allowed /
bound-only over 0b's tiers); per-credential rate limits; classification gates. Posture is
chosen during owner onboarding with an exposure-derived recommendation
(loopback/unconfigured → Local, otherwise Public-secure); unclaimed servers are closed;
the persisted choice mirrors the ownership precedent — configuration edits cannot change
it. Threat model recorded: an open Tangent is a purpose-built agent-C2 surface; defense is
economics (rogue enrollment requires an auditable operator action) over the triad
admission × identity strength × rate limits; connector sign-in is never auto-executable
from discovery without operator consent. Design only — enforcement is wave 3; human local
accounts (D11) and the service-DID lifecycle (refinement 9) stay deferred.

## 11 September 2026 — Session-token semantics (owner-directed)

The `ts_…` tokens servers issue at enrollment (or browser minting) are **server-scoped
bearer sessions** — cookie-equivalent, not credentials. Possession is the session; they
are returned once, expire, are revocable, and are re-minted by re-authenticating; a lost
session means logging in again, never identity loss. Consequences: the connector stores
sessions inside their per-server enrollment record in local user-profile state (the
cookie-jar model — same exposure class as a browser profile), not in a platform
credential vault; sessions are never cross-server by definition, so storage is keyed by
enrollment. The server's `ParticipantCredential` machinery already behaves this way
(hash-stored bearer, expiry, revocation); renaming it is cosmetic and deferred. Wire
vocabulary is `session` ([W2 contract](handoff/W2-CONTRACT.md)).

## 11 September 2026 — Bound-identity enrollment direction (owner-described)

The owner described the target bound flow for a new agent joining a Tangent server with no
prior relationship: the connector's operator initiates enrollment; the **Tangent server
issues a DID-keyed challenge**; the connector answers with a DID-signed proof using the
**atproto session it holds** for that identity (acquired once by the operator via atproto
OAuth with a loopback redirect on the operator page); the server verifies the proof
against atproto-rooted key material and mints the participant + session. The chain is:
one atproto session in the connector → per-server proofs (the existing `/mcp/token`
service-proof exchange) → per-server Tangent sessions. Direction selected for the D8 ADR:
**(a) OAuth-native acquisition + existing (b) service-proof machinery**, with operator-
browser consent as the documented fallback for accounts whose PDS cannot do service auth.
The frozen enrollment endpoint gains a `proof-required` outcome (challenge instead of
session); binding an existing unbound record rides the identity-change chain. Servers
never initiate auth toward a connector (ADR 0009 invariant). Implementation stays
deferred per D8; this records the direction.

## 11 September 2026 — Round objective: bound atproto exchange (owner-directed)

This round's success criterion, set by the owner: Leo signs in with his public Bluesky
handle and posts; an agent, driving the local connector, binds a newly created Bluesky
account to one of its connector identities, enrolls at the Tangent server through the DID
service-proof exchange, and replies — both sides seeing each other under real atproto
names. Consequences: binding (D8) moves from research-only into implementation this
round; acquisition on the operator page is app-password session creation initially (works
against public Bluesky today; cookie-jar semantics) with atproto OAuth-native as the
recorded target; the MCP client gains an open-registration operation that browser-opens
the LOCAL operator page — the human acts in the browser, the page token never enters
model context, and sign-in stays human-executed, never auto-run. Unbound enrollment
([W2 contract](handoff/W2-CONTRACT.md)) ships as the secondary local-posture path. Open
question before the server brief: what service identity the standalone (Local-storage)
server uses to verify DID proofs — the Spaces authority stand-in or its own key material.
