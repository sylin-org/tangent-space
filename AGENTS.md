# Tangent Space — project guidance

Follow applicable higher-level instructions and Leo's current direction. [docs/BRIEF.md](docs/BRIEF.md) is the product; [ADR 0014](docs/adr/0014-tangent-beside-your-site.md) anchors every decision and names which earlier records still bind. Read both before product or architecture work. Judge from the code, never from prose about it; where a document and the code disagree, the code wins and the disagreement is reported.

## The tree

The repository root is the product: .NET 10 on Koan for the server, Vue 3 with TypeScript and Pinia for the browser, atproto for identity. `poc/` is the proof of concept, kept only as a harvest source under [docs/HARVEST.md](docs/HARVEST.md): transplant what the map says to transplant, rewrite what it says to harvest, and never reference `poc/` from code or prose outside the map. When the map has no unconsumed item, `poc/` and the map are deleted together; the tag `poc/2026-10-28` is the archive.

[Companion Lobby](https://github.com/sylin-org/CompanionLobby) is a separate product and is never built here. Agents reach a Tangent through ordinary HTTP and the identity-proof bridge; Tangent exposes no operations catalog, MCP server or model-facing envelope.

## Standing rules

- **Cleanup is mandatory.** Removing a capability removes its code, tests, scripts, configuration and documentation in the same change.
- **Code and prose read greenfield.** Names, comments and documents describe the system as it is and never narrate what they replaced.
- **The browser never opens a dialog.** No `alert`, `confirm`, `prompt` or modal `<dialog>`; a destructive action confirms inline, a form opens as a panel in the page.
- **Reads are bounded and take no lock.** Background work runs in background workers on Koan's mechanisms; reads consume compiled state.
- **Ports are fixed.** The server listens on 5220 or the port an operator names.
- **A screen is not done below the visual gate.** Run the `visual-loop` skill on every change a participant can see; the gate is a mean of 8.5 with no dimension below 7, scored by an independent reviewer.
- **Verification is lean.** One relevant build or check plus the affected happy path; focused extra checks only for an observed failure.
- Commits happen when Leo says so; nothing is pushed without his word. Never `git reset --hard`, `git clean` or replace a checkout wholesale.

## Koan issue ownership

Leo requires Koan bugs discovered while developing Tangent to be passed to Koan's agent. The current destination is the existing **Report framework status** task under the **koan-framework** project. Resolve the current task before messaging; do not create a duplicate task or silently patch the framework checkout as the final fix. Include the exact framework revision, expected and observed behavior, a reproducer, evidence, severity and consumer impact. Distinguish confirmed defects from documented capability limits, performance design questions and Tangent's own schema or query mistakes. Record delivery and responses; if the destination is unavailable, ask Leo rather than claiming the report was sent. Keep framework implementation there and consumer adoption here; a provider's native feature does not establish that Koan exposes the same guarantee.
