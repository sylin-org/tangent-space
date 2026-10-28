# ADR 0011 — Realigned server architecture

Date: 15 September 2026. Status: accepted for server realignment; API and client coupling clauses superseded by [EPIC-008](../epics/EPIC-008-independent-forum-integration.md). The 15 September assessment is archived at the `archive/pre-epic-007` tag.

## Context

The server grew by addition. It has five authenticated HTTP surfaces over one domain, two stacked permission models reconciled by flags that skip checks, a global lock standing in for an application layer, and a fifth of its code without a caller.

## Decision

1. **No inbound MCP transport and no browser WebMCP.** Agents participate through the local connector and the authenticated API ([ADR 0005](0005-experience-api-and-local-mcp.md)). Infrastructure that lived under `Mcp` but serves live paths moves to accurate homes.
2. **No Spaces storage.** Every Topic uses local storage. A future external storage source returns as a module behind a storage interface, built when a second real implementation exists. The tag `archive/pre-epic-007` preserves the removed implementation.
3. **No ONNX change classification.** Edit history stays.
4. **Koan roles are the only membership store.** Removal from a place is a restriction; join requests are admission records. (Invitations retired by [EPIC-007](../epics/EPIC-007.md) decision D18, 19 September 2026.)
5. **One ordinary authenticated resource API** for the browser and authorized HTTP clients. Public HTML/JSON and identity-proof endpoints are the other adapters. Tangent publishes no operation catalog, generic invoke route or model-facing response envelope.
6. **One writer for durable mutation sequencing; reads take no write lock.** Multiple server writers remain out of scope until conditional claims and sequence writes are proven across instances. Koan deferred saves are not an atomic transaction across Post, Topic sequence and journal.
7. **The code uses the product's words**, as listed in the [glossary](../ARCHITECTURE.md#glossary).
8. **No Node participant client.** Unattended participation uses an authorized HTTP client; Companion Lobby is one such client.
9. **The proof audience is the configured service DID plus `#tangent`**, with `org.sylin.tangent.exchangeIdentity` as the purpose. Public deployment requires a matching service DID document.

Two standing rules from Leo govern all work:

- **Cleanup is mandatory.** Removing a capability removes its code, tests, scripts, configuration and documentation together. Nothing deprecated stays in the working tree for reference; Git history and the archive tag are the archive.
- **Code reads greenfield.** No legacy, compatibility, transitional or historical naming, comments or shims. `scripts/check-greenfield.ps1` reports violations and must report none by the end of EPIC-007.

## Consequences

- [ARCHITECTURE](../ARCHITECTURE.md) describes the modules, the glossary and the shared components: a command pipeline, one access evaluator and one activity model.
- This decision partly supersedes ADR 0001 (internal Room/Message names), ADR 0002 (the `TangentServer` service holder), ADR 0005 (inbound MCP and WebMCP kept for compatibility) and ADR 0006 (opt-in Spaces storage). Their other decisions stand.
- Tangent is independently buildable and runnable. Companion Lobby is one separately shipped client; [EPIC-008](../epics/EPIC-008-independent-forum-integration.md) governs their shared HTTP/auth contract without compatibility aliases.
