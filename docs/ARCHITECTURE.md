# Tangent server architecture

Adopted by [ADR 0011](adr/0011-realigned-server-architecture.md) and delivered by [EPIC-007](epics/EPIC-007.md). Until the epic closes, this page describes the destination; the [work ledger](epics/epic-007/LEDGER.md) records which parts are in place.

## Rules

1. **One way to do each thing.**
2. **Complexity lives in named shared components**; domain code stays plain.
3. **Delete before abstracting.** An interface needs a second real implementation.
4. **Reads never take a lock; writes stay short.**
5. **The code uses the product's words.**
6. **Cleanup is mandatory.** Removing a capability removes its code, tests, scripts, configuration and documentation.
7. **Code reads greenfield.** No legacy, compatibility or historical naming, comments or shims. `scripts/check-greenfield.ps1` finds violations.

## Shape

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
Outbound      Koan data and identity adapters · atproto gateway (OAuth, DID/handle, profiles, proof keys)
```

## Modules

Each module is a folder under `src/server/web` and a namespace `TangentSpace.<Module>`.

| Module | Owns | Built from |
|---|---|---|
| `Application` | Command pipeline, operation receipts, references, API problems | `Infrastructure/PolicyGate`, `Infrastructure/CommandCommit`, `Mcp/McpRequests`, `Mcp/McpRefs` |
| `Access` | The access evaluator and its single adapter over Koan roles | `Authorization/*`, `Room.CurrentPolicy`, `TangentRoleAccess` |
| `Activity` | Journal, attention projection, live bus | `Activity/*`, `Experience/ExperienceDigest`, `Conversation/ConversationUpdates` |
| `Hosting` | The Host: settings, accountable human owner, posture, artwork | `Site/*` |
| `Community` | Tangents, Topics, invitations, join requests; membership as Koan roles | `Communities/*`, `Rooms/*` |
| `Conversation` | Posts, facets, edit history, read positions, Topic windows | `Conversation/*` |
| `Identity` | Participants, identities, sessions, enrollment proofs, profiles, the atproto gateway | `Participants/*`, `Participation/*`, `Mcp/Authentication/*`, identity parts of `AtProtocol/*` |
| `Stewardship` | Reports, moderation cases, restrictions, audit | `Moderation/*`, `Rooms/ScopedRestriction`, `Rooms/RoomAudit` |
| `Api` | HTTP adapters: authenticated API, public pages and JSON, identity endpoints, consumer registrations | `Web/*`, `*/Web/*`, `Hosting/*`, presentation in `Experience/*` |
| `Infrastructure` | Koan module composition, options, constants | `Infrastructure/TangentModule` |

## Glossary

| Product word | Code name | Replaces |
|---|---|---|
| Host | `TangentHost` (avoids `Microsoft.Extensions.Hosting.Host`) | `TangentSite`, "site" |
| Tangent | `Tangent` | `TangentCommunity` |
| Topic | `Topic`; its description text is `Topic.Description` | `Room`, "channel", `Room.Topic` |
| Post | `Post` | `Message` |
| Participant | `Participant` | "companion" on the server |
| Session | `Session` | `ParticipantCredential` |
| Operation receipt | `OperationReceipt` | `McpRequestRecord`, the `PostChange` ledger, `WriteIntent` |
| Reference | `References` (issues and parses API references) | `McpRefs` |
| Topic window | `TopicWindow` | `McpWindow`, `ConversationService.History`, the public reader's window |
| Restriction | `Restriction` (timeout, removal, ban) | `ScopedRestriction`, `RoomRole.Removed` |
| Audit record | `AuditRecord` | `RoomAudit` |
| Invitation · join request | `Invitation` · `JoinRequest` | `TangentInvitation` · `TangentJoinRequest` |
| Moderation case | `ModerationCase` | — |

"Companion" remains the connector's word for a local identity it manages.

## Shared components

- **Command pipeline.** The only code that takes the write lock, opens transactions, stages operation receipts, writes audit records and journal entries, commits and signals. Handlers load aggregates and call domain methods that check only their own invariants.
- **Access.** One evaluator returns `{allowed, reasonCode, revision, limits}` for every read and write, from the scope chain (Host → Tangent → Topic), the participant's Koan role bag, restrictions and classification. It is the only code that reads Koan roles.
- **Activity.** One journal whose sequence needs no shared counter row; attention items written when a post is accepted, with group mentions resolved at read time; a host-owned live bus behind SSE and waits.
- **Adapters.** Map HTTP to commands and queries, and results to one response envelope. The public adapter serves unauthenticated readers through the same queries.
