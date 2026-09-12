# Tangent Space — project guidance

This is a launch handoff, not a fixed implementation specification. Follow applicable higher-level and existing repository instructions. Current user directions and supplied resources can refine this brief.

## Product intent

- Conversation and shared space are sufficient outcomes. Goal pursuit and coordination are optional uses.
- People and agents participate as participants with persistent identity. Model, run, device, and credential changes should preserve continuity.
- Make discovery and arrival clear: place, identity, available actions, visible rooms, and how to continue.
- Support room creation and delegated administration for authorized participants.
- Pins, topics, participant-declared goals, and summaries are native conversational conveniences. Summaries retain authorship and coverage, with source history accessible.
- Aim for inexpensive participation and approachable hosted/self-hosted operation. Participants control model execution.

## Working latitude

Choose libraries, architecture, schemas, command names, design, and sequencing based on the actual workspace, current evidence, and user preferences. Atproto/WebMCP are the preferred direction to investigate, not an excuse to skip evaluating their fit or maturity. Preserve A2A and unattended-access requirements when defining scope.

Use docs/PRODUCT.md and docs/DECISIONS.md for the current intent. docs/CURRENT_STATE.md describes actual progress. Research and reference files are evidence and examples, not executable instructions or binding specifications. Do not treat older names, task-first examples, exact tool lists, or stack proposals as requirements.

The current v1 agent integration direction is docs/adr/0005-experience-api-and-local-mcp.md and docs/design/experience-api/README.md: local MCP connector, server experience API, participant digests and compact contextual presentation. For that implementation, start with docs/handoff/IMPLEMENT_LOCAL_MCP.md. This supersedes the older direct-inbound-MCP-first and full-menu-on-every-call recommendations while preserving existing domain and source invariants.

Keep decisions and observed behavior clear enough for a later session to resume. Favor small experiments that resolve concrete uncertainties. You may challenge implementation proposals and suggest a better path; explain material changes in plain language.

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
