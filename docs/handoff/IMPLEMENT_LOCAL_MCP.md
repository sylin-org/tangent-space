# Implementation handoff — local MCP connector and experience API

This is the entry point for the model implementing the design recorded on 11 September 2026, including GLM 5.3 when selected by Leo. It is a task handoff, not a skill or a request to start other agents. The specification commit itself changes documentation only.

## Copy-ready assignment

Implement Tangent's local MCP connector and the server experience API described in docs/design/experience-api/README.md and accepted in docs/adr/0005-experience-api-and-local-mcp.md. Read AGENTS.md first. Inspect the current checkout before choosing a runtime or SDK; use the existing TangentServer hub, permissions, AT identity, native source pipeline and activity machinery. Follow the implementation sequence below and complete the core path before optional features.

For v1, agents always participate through the local connector; humans use the UI. The connector consumes the server's HTTP experience API. The old direct inbound MCP and browser WebMCP are compatibility/prototype paths. The old generated tools.json describes a running implementation and is imported by it: do not overwrite it with a design-only replacement. The new experience documents take precedence over the old fixed-full-menu and direct-MCP-first design.

Provide a small stable tool vocabulary, deterministic participant digests, ordinary background polling, truthful queued attention, and orientation/compact/expanded model-facing responses. Render the verified acting companion as you while preserving source words and canonical identity. Keep credentials and mechanical state in the connector. Persist mutation keys and delivery checkpoints. A notification does not itself guarantee a model turn; unsupported hosts must remain a working tool-response-only integration.

## Read in this order

1. [ADR 0005](../adr/0005-experience-api-and-local-mcp.md): accepted scope and superseded ideas.
2. [Core spec](../design/experience-api/README.md): responsibilities, interfaces and requirements.
3. [Examples](../design/experience-api/examples.json): synthetic canonical data and expected presentation.
4. Relevant entries in [architecture](ARCHITECTURE.md) and [runbook](RUNBOOK.md), then the actual code identified by the core spec.
5. [Coordination](../design/experience-api/COORDINATION.md) only when implementing the optional work-coordination slice.

The older handoff's uncommitted-work and Docker-state statements were captured before the 81ec80f commit. Check Git and the actual runtime; do not assume an unclaimed or running server, seeded conversations, usable credentials or an incomplete clone. Do not rebuild the repository from a narrative snapshot.

## Implementation sequence

| Step | Deliverable | Finish condition |
| --- | --- | --- |
| 1. Inspect and map | Short decision on connector runtime/SDK, precise route/DTO mapping, existing authentication path and source paths to reuse | A small implementation map is recorded; selected SDK/host compatibility is checked. No speculative full rewrite. |
| 2. Read experience | Extract the application experience assembler; expose arrival, directory, Topic read and updates through HTTP | A real authorized read includes correct identity/place, bounded data, coverage and independent continuations. Equivalent UI reads preserve domain policy. |
| 3. Local tools and presentation | Protected local setup/import, caller/companion/server bindings, basic MCP tools and three deterministic views | A real MCP client selects, arrives, reads and reorients. Two identities cannot cross-contaminate references or you-rendering. |
| 4. Reliable participation | Route Post/reply, membership, watch, read acknowledgement and receipt recovery to the shared domain | A native-source reply is accepted; a lost response/restart reconciles it without a duplicate. Source-consent failure remains honest and recoverable. |
| 5. Digest and polling | Validated mentions/direct replies, activity aggregation, saved cursors, backoff and pending attention | Empty/unchanged/self-only checks invoke no model. Duplicate signals coalesce. Revoked content disappears before delivery. Unsupported hosts receive attention during later tool calls. |
| 6. Host delivery and walkthrough | Detect/report effective host support; optionally implement one authorized wake adapter; document setup and limits | A modest model completes a small real participation flow. Automatic-wake claims are tested against a real host, or explicitly listed as unavailable. |
| 7. Optional coordination | Source-linked work requests, explicit acceptance, blockers, results and decisions | Complete the separate coordination acceptance walkthrough; never mark it implemented merely because ordinary Posts can describe tasks. |

Keep each slice runnable and reviewable. Record progress and remaining work as you finish slices so another model can resume. Runtime, storage layout, exact HTTP routes and numeric defaults are implementation choices within the invariants; explain a material deviation and update consumers together. Do not silently redesign the product to fit an SDK limitation.

## Focused verification and evidence

- Use focused checks for identity/policy isolation, bounded rendering, cursor/retry behavior and attention dispatch. The spec's E01–E12 list is an outcome checklist, not a requirement for twelve full deployments or a new test framework.
- Use synthetic fixtures for deterministic rendering and transition edge cases. Label them synthetic. Exercise the actual MCP transport with a real client and reuse the existing compatible native Spaces setup for the genuine human/agent exchange when available.
- Record the connector runtime/SDK, target host, negotiated MCP version, actual accepted source reference and what was verified. Record response sizes and unexpected extra calls in the modest-model walkthrough.
- If credentials, source grants, a model account or a host capability are unavailable, complete the unaffected implementation and checks, then state the exact missing prerequisite. Never fake model output, source acceptance or universal wake compatibility.
- Use the relevant build and affected happy path. Avoid unrelated full-suite reruns, new test servers and production-scale hardening exercises. Broaden verification only for a concrete risk introduced by the change.

## Preserve these boundaries

No source-network reset, account claim, data wipe, credential export, public deployment or upstream framework publication is required by this assignment. Preserve unrelated local work. Keep source grants distinct from Tangent permissions; pending native writes must not become fake local Posts. A coordinator role and a mention do not authorize another model's execution. Do not introduce model-specific personas or guessed GLM/provider identifiers into the API.

At completion, report what works, how to configure/use the connector, the paths changed, focused verification results and exact remaining limits. Update CURRENT_STATE and relevant setup documentation to reflect observed behavior, not the target specification alone.
