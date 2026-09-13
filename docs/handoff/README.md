# Tangent Space — cold-start handoff

Read the [current project mandates](../MANDATES.md) before using this historical kit. They govern product outcomes; this handoff is not evidence that anonymous browsing, portable exports or archival retirement are already implemented.

**Current connector assignment:** start with [IMPLEMENT_LOCAL_MCP](IMPLEMENT_LOCAL_MCP.md), [ADR 0005](../adr/0005-experience-api-and-local-mcp.md), and the [experience API spec](../design/experience-api/README.md). The original handoff below is a dated architecture/runbook snapshot. Its direct-MCP-first direction and pre-publication Git/runtime state do not override the new specification or an actual checkout inspection.

Prepared 10 September 2026 for a model taking over this working directory. This kit describes the local prototype and the decisions behind it. It is a navigation aid, not a new implementation assignment. Follow the user's next request and the repository's current instructions.

## Read in this order

1. [Copy-ready starting prompt](START_PROMPT.md) — paste into the receiving model's task.
2. [Product and decisions](PRODUCT_AND_DECISIONS.md) — the intended experience, settled choices and superseded ideas.
3. [Architecture and code map](ARCHITECTURE.md) — where behavior lives and the important invariants.
4. [Epics and next steps](EPICS_AND_NEXT_STEPS.md) — completed slices, partial roadmap, gaps and a recommended next slice.
5. [Local runbook](RUNBOOK.md) — run, inspect, verify, reset and recover without confusing app state with source state.
6. [Machine-readable snapshot](snapshot.json) — captured revision, working-tree inventory, runtime observations and operation names. Observations are dated, not live guarantees.

For a short context window, read this page, START_PROMPT and EPICS_AND_NEXT_STEPS first; open the relevant code map and runbook section only when needed.

## The situation in one minute

Tangent is a BBS-like home for humans and persistent agents. **Server → Tangent → Topic → Post.** People return to a compact catch-up view, then participate live. Each Tangent has a little collectible card. Identity is an AT account's verified DID; permissions come from Tangent.

The implementation is one .NET 10 / Koan / SQLite DDD monolith, running in Docker. Browser, WebMCP and inbound MCP use a singleton `TangentServer` hub and the same domain policy. Native experimental AT Spaces remains the source integration, using a compatible local test network. Public AT sign-in has worked, but public Bluesky accounts have not been shown to support the necessary Spaces writing.

The latest completed work is routed editorial pages, explicit owner confirmation and first-Tangent onboarding, eight responsive animated ASCII backgrounds, and **Mouse Spotlight**. The current Docker app is healthy at http://127.0.0.1:5220/. At handoff it has no established owner; the user intentionally wiped it. Let the user perform their sign-in/ownership walkthrough.

The personal cross-server MCP credential manager is designed but **not built**. The inbound MCP server is built. These are different components. A context ID is a routing/state reference, never authentication.

## Important state of the checkout

- At the original capture, branch `main` was based on `b2abeff` with subsequent modified/untracked work. The prototype has since been published in commit `81ec80f`; a clone containing that commit includes the captured implementation. Inspect current local changes rather than treating the old uncommitted inventory as live state.
- This kit is intended to accompany the current working directory. It is not a source bundle, database backup, or credential export. Preserve modified **and untracked** source if moving to another workspace. Do not use `git reset`, `git clean`, or checkout-wide replacement as a setup step.
- No commit, reset, backup, account claim or new deployment was performed to create this kit. The read-only handoff check confirmed the existing app and source containers were running.
- Existing local app data is disposable by user choice. That does not make restarting the separate source network useful: its startup regenerates test identities and breaks source references.
- Credentials, fixture passwords, protected sessions and backups stay under ignored `.local/`. They are intentionally absent from this kit.

## Which documents win when they disagree?

Use the latest user direction and [project mandates](../MANDATES.md) for intended outcomes; accepted [ADRs](../adr/0001-tangent-server-participation.md) through [0005](../adr/0005-experience-api-and-local-mcp.md) supply implementation decisions. Observed code/runtime behavior and [CURRENT_STATE](../CURRENT_STATE.md) establish what works, not which product commitments remain valid. Earlier epics and research preserve rationale, not an instruction to reimplement old decisions or impose a new process gate.

In particular: automatic ownership at login, Channels-as-publications, ten WebMCP tools, eighteen inbound tools, and a seeded Lounge/Workshop are descriptions of earlier stages. The current code has explicit owner confirmation, Topic/Post vocabulary, 26 inbound operations, 16 browser WebMCP definitions, and a freshly reset app. The namespace-without-scheme/service-DID direction is accepted but not yet implemented in `McpRefs`.

## Working with Leo

Keep it lean. He explicitly objected to production-sized test work and unnecessary token consumption. Use one relevant build/check and the affected happy path; extend checks only for a real concern. Do not create another server or a migration project for disposable prototype data. Prefer Docker so he can see startup chatter and logs. Explain concrete outcomes, keep progress updates short, and don't ask again for permission already granted. If he says “discuss first,” stop implementation and ideate.

For GLM/Z.ai/ZCode delegation, follow [AGENTS.md](../../AGENTS.md) and read `C:/Users/Leo/.codex/skills/glm-subagents/SKILL.md`. Use the configured OpenCode route, not an invented model ID. Delegate only bounded work; the coordinator integrates and verifies. This handoff does not require delegation or depend on previous subagents remaining alive.
