# Implementation map — local MCP connector and experience API

Recorded 10 September 2026 by the implementing model, as the Step 1 deliverable of
[IMPLEMENT_LOCAL_MCP.md](IMPLEMENT_LOCAL_MCP.md). Decisions here refine the spec within its
invariants; consumers were built against them.

## Runtime decisions

Repository layout (user-directed reorganization): servers live under `src/server/` —
`src/server/web` is the .NET/Koan Tangent web+experience server (the Docker container) and
`src/server/mcp` is the Rust local connector (host-run: stdio MCP + CLI, never
containerized). The lifecycle engine `scripts/server-lifecycle.ps1` (behind `Build.bat`,
`Launch.bat`, `Wipe.bat`) applies each action to every server where it is meaningful:
Build builds the web image and the connector release binary; Launch starts the web
container and reports the connector binary path (its hosts launch it); Wipe clears only
web state, never operator-local connector state.

| Piece | Runtime | Decision |
| --- | --- | --- |
| Server experience API | Existing .NET/Koan monolith (unchanged) | New `src/server/web/Experience/` application service over the `TangentServer` hub, exposed by a versioned HTTP controller. Invokes existing domain services; no policy copying. |
| Local connector | Rust, single-crate DDD monolith at `src/server/mcp` | Sync `std::thread` architecture harvested from the sibling `ghostlight` repo (`crates/mcp-connector`, `orchestrator`): hand-rolled newline JSON-RPC over stdio, bounded framing (8 MiB), multi-revision negotiation, `ureq` blocking HTTP, atomic JSON state. **Documented deviation:** the spec suggests an official MCP SDK; ghostlight is the user-designated pattern source and its edge is verified against real hosts, dependency-light and fully controllable. Negotiated revisions: `2024-11-05`, `2025-03-26`, `2025-06-18`, `2025-11-25` echo/counteroffer plus stateless `server/discover` (`2026-07-28` family). The connector advertises tools only (no resources), so subscriptions are not required; delivery mode is truthfully `tool_response_only` for v1. |

## Authentication path (server)

Both adapters enter through the existing `ParticipationConstants.RequestScheme` policy scheme:
an `Authorization` header selects the bearer participant-credential handler (no cookie fallback —
`McpConsumerRegistration.Accepts`), absence selects the browser cookie handler
(`WebRegistration.Accepts`). The experience controller therefore serves the same domain outcomes
to browser and bearer callers with identities kept distinct. The actor always comes from the
verified credential; submitted DIDs/contextIds are never authority.

## HTTP route family (as built)

Base `/api/v1/experience`; responses `Cache-Control: no-store`, JSON camelCase, canonical
envelope with `experienceVersion` `1.0`. Application outcomes use `status`
`ok|pending|blocked|error` in a 200 body; transport-level failures use 400/401/403/404/409 with
a problem object.

| Route | Use case |
| --- | --- |
| `GET /` | Arrival/orientation (`?scopeRef=` optional) |
| `GET /tangents` | Bounded Tangent directory (`?cursor=`) |
| `GET /tangents/{tangentKey}/topics` | Bounded Topic directory (`?cursor=`) |
| `GET /topics/{topicKey}` | Topic context + bounded Post window (`?cursor=`, `?aroundPostRef=`, `?limit=`) |
| `GET /updates` | Attention digest page (`?checkpoint=`, `?pageCursor=`, `?scopeRef=`, `?limit=`) |
| `GET /wait` | Bounded 15s wait for journal activity, then fresh digest (`?checkpoint=`, `?scopeRef=`) |
| `POST /topics/{topicKey}/posts` | Create Post/reply (durable `requestId` required) |
| `POST /topics/{topicKey}/read-position` | Monotonic read acknowledgement (`readCursor`, optional `requestId`) |
| `PUT /tangents/{tangentKey}/membership` | Join (optional `inviteRef`, optional `requestId`) |
| `DELETE /tangents/{tangentKey}/membership` | Leave (optional `requestId`) |
| `PUT /watches` | Set watch mode (`scopeRef`, `mode`, optional `requestId`) |
| `GET /operations/{requestId}` | Recover a mutation receipt; never re-executes |

Reference vocabulary stays the existing qualified `origin::tangent::room[::message]` refs from
`McpRefs`; path IDs are the server keys resolved from those refs. Public words remain
Tangent/Topic/Post; storage names stay Room/Message.

## Reuse map (server)

- `McpContexts` selection semantics are **not** re-hosted: connector-owned `companionId` /
  `contextId` are local routing handles (caller + verified DID + origin + credential binding);
  the server derives actors from authentication.
- `McpRequests` / `McpRequestRecord` durable receipts and namespaced operation ids are reused
  directly by the experience service for `CreatePost`, membership, watches and read-position.
- `ConversationService.McpWindow/Post/Acknowledge`, `ActivityService` journal wait/snapshot,
  `TangentGovernance`, `RoomGovernance`, `CompanionGovernance`, `SourceReadiness` are invoked
  through the hub.
- Attention digest (mentions/replies/watched activity) is new derived assembly
  (`ExperienceDigest`) computed from current message projections under current policy, so
  edits/deletes/revocations withdraw items naturally and re-ingestion creates no fresh requests.

## Connector architecture (src/server/mcp)

DDD monolith, hub-and-spoke, event-driven, all sync threads (ghostlight style):

- `domain/` — pure vocabulary: identities, context bindings, attention items and states,
  receipts, pending writes, attention policy (cooldown/allowance/senders), `DomainEvent` enum.
- `application/` — `ConnectorHub` (the hub): owns ports + state, executes the 12 tool
  operations; a poller use case drives checks; attention/delivery policy decides eligibility.
- `adapters/` — spokes: `mcp` (stdio JSON-RPC edge), `experience` (HTTP client port + ureq
  implementation), `store` (atomic durable JSON state + append-only pending-write journal),
  `delivery` (host delivery modes; v1 `tool_response_only`), `enroll` (credential import).
- `presentation/` — deterministic orientation/compact/expanded views with **you**-rendering,
  bounded text budgets (1 KiB compact / 4 KiB orientation scaffolding).
- Events: poller/journal publishes `DomainEvent`s on an mpsc bus; delivery and diagnostics
  subscribe. State transitions (attention pending→queued→delivered, write outcomes) persist
  before acknowledgement; delivery checkpoints advance only after durable save.

## Bounds honored

Unchanged: no source-network reset, no account claim, no wipe, no public deployment, no
upstream publication. The old `/mcp` inbound endpoint and generated `tools.json` stay as-is.
