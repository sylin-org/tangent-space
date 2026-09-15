# Tangent server architecture assessment — 15 September 2026

Requested by Leo to prepare a realignment epic. This evaluates the current server design; it does not record new functionality. The plan is [EPIC-007](epics/EPIC-007.md), and its execution state lives in the [EPIC-007 work ledger](epics/epic-007/LEDGER.md).

## Verdict

The server's local engineering is good: business rules are checked carefully, failures are reported honestly, public reads are size-limited and writes are safe to retry. The architecture is the weak part. It grew by addition — each epic placed a new surface or model beside the old one instead of replacing it — and now has:

- five overlapping authenticated HTTP surfaces over one domain;
- two permission models stacked on each other, reconciled by flags that skip checks;
- one global lock doing the work of an application layer, with the same command sequence hand-copied at about 80 call sites;
- about a fifth of the server code (the inbound MCP transport) with no caller in the repository.

The remedy is mostly subtraction plus two shared components: a command pipeline and one access evaluator. The domain ideas do not need rewriting.

## Scope and method

- **Baseline:** branch `codex/epic-006-stewardship` at `d682c26`; Koan pinned at `21b18c69`.
- **Subject:** the .NET server in `src/server/web` (16,830 lines of C#, counting blank lines and comments) and its browser assets, plus how the browser and the Rust connector call it. The connector's internals were not reviewed.
- **Method:** static review. The core entities and workflows were read, every route and constructor dependency was extracted, and shared mechanisms were counted with repository searches. No build, test run, deployment or data change was made. Cost statements are derived from code paths, not measured.

## Assessment by dimension

| Dimension | Verdict | Evidence |
|---|---|---|
| Architecture | Mixed | The overall shape is right: one process, Koan composition, a local connector for agents. But `TangentServer` is a holder for 11 service references with no behavior, and the Experience layer, public pages and moderation inject services around it. Five authenticated HTTP surfaces serve one domain. A global lock stands in for a unit of work. |
| DDD alignment | Weak to mixed | Entities hold real invariants, and `ModerationCase` is a well-bounded aggregate. But entities also make cross-aggregate permission decisions, with `bool authorized` parameters that callers use to skip them; Koan's own placement rules put that in a workflow. Membership is stored twice. Folders mix business areas, technical layers and protocols. Internal names drift: Room, Channel, Message, Companion. |
| Separation of concerns | Mixed | Some boundaries are clean: consumer registrations, the facet save hook, the public reader. But presentation leaks into the domain (`Message.Permissions`; MCP window types in Conversation), `ExperienceService` both orchestrates and shapes responses (1,261 lines across four files), and an adapter depends on a workflow (`SpacesService → RoomGovernance`). |
| Right-sizing | Over- and under-built at once | Over-built: the uncalled inbound MCP transport, a second storage mode, ONNX change classification, four idempotency mechanisms. Under-built: no application layer, read results computed by scanning, one global sequence row, no connector–server contract check. |

## What to keep

- [`TopicPermissionEvaluator`](../src/server/web/Authorization/TopicPermissionEvaluator.cs): pure, deny-by-default, stable reason codes.
- ADR 0007: a local post is an idempotent upsert at a client-chosen identity, and the row is the receipt.
- ADR 0008: verbatim text with facets, minted in one Koan save hook ([TangentModule.cs](../src/server/web/Infrastructure/TangentModule.cs#L35)).
- Rejected administrative attempts are audited, not only accepted ones.
- `ModerationCase`: explicit caps on testimony and decisions, and revision checks at decision time.
- The public reader's allowlisted fields, size budget and identical 404s for hidden, missing and misplaced items.
- The Experience response shape: identity, place, result, attention, continuation, actions.
- `IRegistration` consumer selection, and the test harness that boots the real application.

## Root causes

### 1. No application layer: the command sequence is copied by hand

Each workflow method repeats the same sequence: take the global lock → open a no-cache scope and a transaction → run a bootstrap check → load site, participant, Tangent, memberships and restrictions → compute policy → mutate → save → audit → journal → report the receipt → commit → signal. [`RoomGovernance.Administer`](../src/server/web/Rooms/RoomGovernance.cs#L284) and [`WithCurrentPolicy`](../src/server/web/Rooms/RoomGovernance.cs#L254) are typical.

| Mechanism | Occurrences |
|---|---|
| Global lock entries (`PolicyGate`) | 49 direct, plus 31 more calls to `WithCurrentPolicy`, which takes it |
| `EntityContext.Transaction` | 41 |
| `ActivityJournal` references | 65 |
| `EnsureHome` bootstrap call sites | 17 |
| `CommandCommit.Report` | 11 |

Consequence: every rule about ordering, auditing, receipts and signaling has to be re-checked at about 80 sites, and reads pay for write machinery.

### 2. Two permission models, stacked

[`Room.CurrentPolicy`](../src/server/web/Rooms/Room.cs#L66) takes 8 arguments and computes the older model — room and Tangent membership rows, admission flags, "creator counts as owner" — in a boolean expression of about 50 lines. [`TangentRoleAccess.ProjectTopic`](../src/server/web/Authorization/TangentRoleAccess.cs#L24) then overwrites `CanRead`, `CanWrite` and `CanManage` from Koan role bags and access maps. Entity mutators still contain their own older checks, so services call them with `authorized: true` (7 sites in `RoomGovernance`). Membership lives in `TangentMembership`, `RoomMembership` and Koan role collections.

Consequence: no single place answers "may this participant do this here, and why?", and a change to either model can silently disagree with the other.

### 3. Every second path stayed

| Concern | Parallel implementations |
|---|---|
| Authenticated HTTP | `/api/rooms` (with `ConversationController`), `/api/tangents`, `/api/v1/tangents`, `/api/v1/experience`, `/mcp`. There are three ways to create a Topic and three wait or update endpoints; the browser uses four of the families. |
| Topic history windows | `ConversationService.History`, `McpWindow`, `PublicConversationReader` |
| Mention detection | `MessageFacets` at write time and `ExperienceMentions` as a digest fallback, with deliberately mirrored rules |
| Live signals | a process-wide static bus in `ActivityJournal` and per-room pulses in `ConversationUpdates` |
| Idempotency | `McpRequests` with the ambient `CommandCommit`, the `PostChange` ledger, `WriteIntent`, client-chosen operation IDs |

Consequence: each feature has to be implemented and kept consistent several times. Drift is already visible; see "Contract drift" below.

### 4. The storage design forces the global lock

Every journaled change increments a single `ActivityHead` row ([ActivityJournal.cs](../src/server/web/Activity/ActivityJournal.cs#L28)), a read-then-write that is only safe under a global lock. Reads hold the same lock:

- directory listings, with several extra queries per room and exact counts;
- public Topic and Post reads, for their whole duration ([PublicConversationReader.cs](../src/server/web/Rooms/Web/PublicConversationReader.cs#L23));
- the attention digest, which walks up to 100 rooms and takes the lock once per room ([ExperienceDigest.cs](../src/server/web/Experience/ExperienceDigest.cs#L51)) and is attached to most Experience responses.

One local post writes five rows ([ConversationService.Writes.cs](../src/server/web/Conversation/ConversationService.Writes.cs#L161)), including a Spaces-era `SourceDecision` even when Spaces is not used.

### 5. Compatibility code the disposable-data rule no longer needs

- `EnsureHome` can write, and it runs inside read paths that then commit ([TangentBootstrap.cs](../src/server/web/Communities/TangentBootstrap.cs)).
- Pre-multi-Tangent branches remain: "flat room" handling, the `home` migration flag, creator-as-owner.
- Admission state is duplicated: `OpenToSignedIn` plus `ApprovalRequired`.
- 41 string literals still say "channel".
- The digest keeps a mention parser for posts without facets, although after a wipe every live post has facets.
- The inbound MCP transport: 3,297 lines (20% of server C#) outside its live infrastructure, plus 1,052 lines of tests. Apart from the 67-line human invitation page, none of it has a caller in the repository.
- Spaces storage: about 1,400 lines, 4 entities, a background worker and `RoomSpaceState` checks in 11 files, for a write path that public Bluesky accounts cannot use.

## Other findings

- **Contract drift.** The connector calls `POST /api/v1/experience/identities/enroll` ([hub.rs](../src/server/mcp/src/application/hub.rs#L425)), as specified in [W2-CONTRACT](handoff/W2-CONTRACT.md) and [W2B2-SERVER-IDENTITY](handoff/W2B2-SERVER-IDENTITY.md). The server has no such route.
- **Presentation on an entity.** `Message` carries a per-viewer `PermissionView` ([Message.cs](../src/server/web/Conversation/Message.cs#L19)).
- **Process-wide state.** The static signal bus ([ActivityJournal.cs](../src/server/web/Activity/ActivityJournal.cs#L12)), `ParticipantDirectory`'s static mint lock and the owner-role guard's once-only flag conflict with Koan's host-owned model. Test fixtures assign `AppHost.Current` globally, and the integration collection disables parallel execution ([ExperienceWebApp.cs](../tests/TangentSpace.Tests/ExperienceIntegration/ExperienceWebApp.cs#L274)).
- **Live code under a legacy name.** `McpRefs`, `McpRequests`, `McpRequestRecord`, `McpConsumerRegistration` and `McpOptions` (400 lines), the `/invite/{invitationId}` page and the `McpWindow` family in Conversation all serve live paths.
- **Configuration under sections due for removal.** `Tangent:Mcp:PublicBaseUrl` (set in [compose.yaml](../compose.yaml)) feeds public references. `Tangent:Spaces:ManagingApp` is the audience for agent enrollment proofs; fresh standalone installs do not set it, which is the README's "current setup limit".
- **A legacy consumer.** The Node client in `clients/participant` calls `/api/rooms` and `/api/connections/authority`.
- **Classification cost.** ONNX change classification ships a 22.1 MB model, runs on every edit inside a service-wide write lock, and its raw scores are shown in the history UI.
- **Large files.** Rust `hub.rs` (2,786 lines, about 2,580 without tests), `rooms.js` (1,161), `ExperienceService.cs` (857), and `CompanionGovernance.cs` (701 lines, 15 operations spanning membership, invitations, join requests, restrictions, watches and classification).

## Baseline inventory

| Measure | Value at `d682c26` |
|---|---|
| Server C# | 16,830 lines in about 230 files |
| Largest areas | Mcp 3,697 · Conversation 2,300 · Communities 2,113 (with web) · Experience 1,797 |
| Persisted entity types | 30 |
| Controllers and route actions | 26 controllers, about 130 actions |
| Authenticated API families | 5 |
| Tests | 7,687 lines of C# tests in 43 files; 15 browser test files; the Rust connector suite |

## Target shape

```text
Adapters      Authenticated API /api/v1 (browser + connector) · Public HTML/JSON · Identity
                                   │
Application   Command: receipt → write lock → transaction → scope snapshot → Access → handler
                       → audit + journal → commit → signal
              Query:   scope snapshot → Access filter → bounded window (no lock)
                                   │
Domain        Hosting · Community · Conversation · Identity · Stewardship
Supporting    Access: one evaluator over Koan role bags, restrictions, classification
              Activity: one journal, attention projection, host-owned live bus
```

[EPIC-007](epics/EPIC-007.md) records the decisions this depends on, the slices and their acceptance.

## Limits of this assessment

- Static review only. Counts come from repository searches at `d682c26` and include comments.
- Digest, directory and lock costs are inferred from code paths; EPIC-007 R5 measures them on the EPIC-005 synthetic dataset.
- The browser and the connector were reviewed only for which server endpoints they call.
