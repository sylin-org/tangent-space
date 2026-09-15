# ADR 0011 — Realigned server architecture

Date: 15 September 2026. Status: accepted. Leo accepted every recommendation of [EPIC-007](../epics/EPIC-007.md); the [15 September assessment](../ASSESSMENT_2026-09-15.md) is the evidence.

## Context

The server grew by addition. It has five authenticated HTTP surfaces over one domain, two stacked permission models reconciled by flags that skip checks, a global lock standing in for an application layer, and a fifth of its code without a caller.

## Decision

1. **No inbound MCP transport and no browser WebMCP.** Agents participate through the local connector and the authenticated API ([ADR 0005](0005-experience-api-and-local-mcp.md)). Infrastructure that lived under `Mcp` but serves live paths moves to accurate homes.
2. **No Spaces storage.** Every Topic uses local storage. A future external storage source returns as a module behind a storage interface, built when a second real implementation exists. The tag `archive/pre-epic-007` preserves the removed implementation.
3. **No ONNX change classification.** Edit history stays.
4. **Koan roles are the only membership store.** Removal from a place is a restriction; invitations and join requests are admission records.
5. **One authenticated API**: one route family and one response envelope for the browser and the connector. Public HTML/JSON and the identity endpoints are the only other adapters.
6. **One write lock, owned by the command pipeline and never held by reads.** Multiple server instances remain out of scope.
7. **The code uses the product's words**, as listed in the [glossary](../ARCHITECTURE.md#glossary).
8. **No Node participant client.** The connector is the unattended participation path.
9. **The enrollment proof audience defaults from the public origin**, with an explicit override, so a fresh standalone install can enroll agents.
10. **Delivery on `claude/epic-007-realignment`**, committing at task checkpoints. Pushing requires Leo's authorization.

Two standing rules from Leo govern all work:

- **Cleanup is mandatory.** Removing a capability removes its code, tests, scripts, configuration and documentation together. Nothing deprecated stays in the working tree for reference; Git history and the archive tag are the archive.
- **Code reads greenfield.** No legacy, compatibility, transitional or historical naming, comments or shims. `scripts/check-greenfield.ps1` reports violations and must report none by the end of EPIC-007.

## Consequences

- [ARCHITECTURE](../ARCHITECTURE.md) describes the modules, the glossary and the shared components: a command pipeline, one access evaluator and one activity model.
- This decision partly supersedes ADR 0001 (internal Room/Message names), ADR 0002 (the `TangentServer` service holder), ADR 0005 (inbound MCP and WebMCP kept for compatibility) and ADR 0006 (opt-in Spaces storage). Their other decisions stand.
- The server and connector continue to ship as a matched pair; wire renames change both at once.
