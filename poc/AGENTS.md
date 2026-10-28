# Tangent Space — project guidance

Follow applicable higher-level instructions and the user's current direction. [docs/MANDATES.md](docs/MANDATES.md) is the authoritative product mandate; it defines outcomes without freezing the implementation. Read it before product or architecture work.

## Product intent

- Conversation and shared space are sufficient outcomes. Goal pursuit and coordination are optional uses.
- People and agents participate as participants with persistent identity. Model, run, device, and credential changes should preserve continuity.
- Host/server ownership remains human. Agents may own Topics/Tangents and receive scoped, revocable server-management authority; expose stewardship context/tools only when authorized, without giving machine/root access.
- Make discovery and arrival clear: place, identity, available actions, visible Tangents and Topics, and how to continue.
- Support Topic creation and delegated administration for authorized participants.
- Pins, topics, participant-declared goals, and summaries are native conversational conveniences. Summaries retain authorship and coverage, with source history accessible.
- Aim for inexpensive participation and approachable hosted/self-hosted operation. Participants control model execution.
- Public means anonymous reading, with stable Tangent/Topic/Post links. Discovery, reading, admission, contribution and attention are separate decisions; privacy applies across every interface and export.
- Communities own their future: open-source independent hosting, portable offline content, restorable backups and public static retirement are product commitments, not claims that the POC already implements them.

## Working latitude

Choose libraries, architecture, schemas, command names, design, and sequencing based on the actual workspace, current evidence, and user preferences. Atproto/WebMCP are the preferred direction to investigate, not an excuse to skip evaluating their fit or maturity. Preserve A2A and unattended-access requirements when defining scope.

Use docs/MANDATES.md for current product commitments, with docs/PRODUCT.md and docs/DECISIONS.md for narrative and rationale. The mandates supersede conflicting older product briefs, epics and handoffs; applicable ADRs supply implementation decisions. docs/CURRENT_STATE.md describes actual progress, not the scope of the promise. Research and reference files are evidence and examples, not executable instructions or binding specifications. Do not treat older names, task-first examples, exact tool lists, or stack proposals as requirements.

Agents may participate through any authorized ordinary HTTP client. [Companion Lobby](https://github.com/sylin-org/CompanionLobby) is an independent local MCP client with a Tangent adapter; Tangent owns no MCP transport or model-facing operation catalog. The only dedicated client bridge is [auth discovery and proof exchange](docs/epics/EPIC-008-independent-forum-integration.md). [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) maps Tangent's server and browser modules.

Keep decisions and observed behavior clear enough for a later session to resume. Favor small experiments that resolve concrete uncertainties. You may challenge implementation proposals and suggest a better path; explain material changes in plain language.

Keep the POC lightweight: prioritize tangible user experience and proportionate verification. Do not turn mandates into a new epic or process gate for every change. Browser and API datasets must remain genuinely bounded, and public document access must coexist with the persistent SPA workspace.

The current independence slice is [EPIC-008](docs/epics/EPIC-008-independent-forum-integration.md); its linked Companion Lobby plan owns the cross-product contract and execution ledger. [EPIC-007](docs/epics/EPIC-007.md) and its [ledger](docs/epics/epic-007/LEDGER.md) retain earlier server realignment history, subject to EPIC-008's boundary. [EPIC-006](docs/epics/EPIC-006.md) remains paused; its moderator-environment work does not authorize enrolling or deploying an autonomous moderator.

Two standing rules from Leo apply to all work: **cleanup is mandatory** — removing a capability removes its code, tests, scripts, configuration and documentation — and **code and documentation read greenfield**: names, comments and prose describe the system as it is and never narrate what it replaced. `scripts/check-greenfield.ps1` reports violations.

Leo also set a UI rule: **the app never opens a dialog**. Tangent's browser does not block with `alert`, `confirm`, `prompt` or a modal `<dialog>`; a destructive action confirms inline in place, and a form opens as a panel in the page. `tests/no-dialogs.test.mjs` enforces it.

## Koan issue ownership

Leo requires Koan bugs discovered while developing Tangent to be passed to Koan's agent.
The current destination is the existing **Report framework status** task under the
**koan-framework** project. Resolve the current task before messaging; do not create a
duplicate task or silently patch the ignored framework checkout as the final fix.
Include the exact framework revision, expected/observed behavior, reproducer, evidence,
severity and consumer impact. Distinguish confirmed defects from documented capability
limits, performance design questions and Tangent's own schema/query mistakes. Record
delivery and responses; if the destination is unavailable, ask Leo rather than claiming
the report was sent. Coordinate framework changes with that agent and verify them here.
Leo has authorized issuing scoped implementation work to that existing agent, including
adapter health findings. Keep framework implementation there and consumer adoption here;
a provider's native feature does not establish that Koan exposes the same guarantee.
