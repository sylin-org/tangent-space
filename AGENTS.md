# Tangent Space — project guidance

Follow applicable higher-level instructions and Leo's current direction. [docs/BRIEF.md](docs/BRIEF.md) is the product; [docs/LANGUAGE.md](docs/LANGUAGE.md) fixes its words; [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) is its shape; [ADR 0014](docs/adr/0014-tangent-beside-your-site.md) and [ADR 0015](docs/adr/0015-the-spine.md) anchor every decision and name which earlier records still bind. Read them before product or architecture work. Judge from the code, never from prose about it; where a document and the code disagree, the code wins and the disagreement is reported.

## The tree

The repository root is the product: .NET 10 on Koan for the server, Razor components rendered statically for every page, Vue 3 with TypeScript for the islands, atproto for identity. `poc/` is the proof of concept, kept only as a harvest source under [docs/HARVEST.md](docs/HARVEST.md): transplant what the map says to transplant, rewrite what it says to harvest, move to Koan what it marks for Koan, and never reference `poc/` from code or prose outside the map. When the map has no unconsumed item, `poc/` and the map are deleted together; the tag `poc/2026-10-28` is the archive.

[Companion Lobby](https://github.com/sylin-org/CompanionLobby) is a separate product and is never built here. Agents reach a Tangent Space through ordinary HTTP: as a person's delegate through the OAuth server, or as a participant of its own through the identity-proof bridge. Tangent exposes no operations catalog, MCP server or model-facing envelope.

## Standing rules

- **Cleanup is mandatory.** Removing a capability removes its code, tests, scripts, configuration and documentation in the same change.
- **Code and prose read greenfield.** Names, comments and documents describe the system as it is and never narrate what they replaced.
- **The delight filter.** Before implementing anything a person or an agent will meet — a step, a field, a check, a refusal, a protection against a remote possibility — score its impact on them from 0 to 10, benefits weighed against what it costs their experience. Below 5, it is not built: it is recorded in [docs/PROPOSALS.md](docs/PROPOSALS.md) with its score and reasoning and taken to Leo at the next iteration cycle.
- **Offer, don't ask.** The system asks a person something only when it has learned something useful and offers to use it to make their experience better — "I see this place reached at tangent.sylin.org; use it as its address?" — never as a question or a required field up front. What it can learn or default, it learns or defaults, and the person changes it later if they want.
- **The browser never opens a dialog.** No `alert`, `confirm`, `prompt` or modal `<dialog>`; a destructive action confirms inline, a form opens as a panel in the page.
- **Reads are bounded and take no lock.** Background work runs in background workers on Koan's mechanisms; reads consume compiled state.
- **Ports are fixed.** The server listens on 5220 or the port an operator names.
- **Function first, polish at its stage.** Until Leo calls the polish stage, a screen is built from the frame and the components the tree already has, captured on every change a participant can see, and not scored; [docs/SCREENS.md](docs/SCREENS.md) lists every screen that still owes the visual gate. At the polish stage the `visual-loop` skill holds each screen to a mean of 8.5 with no dimension below 7, scored by an independent reviewer.
- **Verification is lean.** One relevant build or check plus the affected happy path; focused extra checks only for an observed failure.
- **Nothing waits without a bound.** A command that must finish before a report runs in the foreground with a timeout; a process meant to outlive its command is started only by the lifecycle script, which hands it no handle of the caller's; a result that is late by twice its expected time is a hang to diagnose and end, never a wait.
- **Work runs in packages, by role.** [docs/PLAN.md](docs/PLAN.md) assigns every package a role with a model and an effort; the `work-split` skill applies it before a package starts. One package, one session, one model; a fresh-context reviewer before Leo's commit word.
- Commits happen when Leo says so, with one standing exception: an executor session commits on its own branch when its package passes its checks. Nothing is merged or pushed without his word. Never `git reset --hard`, `git clean` or replace a checkout wholesale.

## Koan

Tangent is a Koan application and Leo owns Koan. Where Koan has a capability, Tangent references the package and writes none of it. A capability Tangent needs with reuse beyond Tangent is Koan's: it is built in Koan, or built here in Koan's shape and lifted. A Koan quirk or deviation met while building Tangent is corrected in Koan first, and Tangent resumes on the corrected revision.

Koan work happens in the pinned checkout at `.local/upstream/koan-framework` under Koan's own contributor law — its `CLAUDE.md`, its explore skill, its naming decision DX-0043 (`Koan.{FeaturePath}.Connector.{Vendor}`; a Koan identifier is never derived from a product name) — on a branch, committed and pushed only on Leo's word, and the pin here moves with it. Scoped work for Koan's agent goes to the existing **Report framework status** task under the **koan-framework** project, one task, with the exact framework revision, expected and observed behavior, a reproducer, evidence, severity and consumer impact; if the destination is unavailable, ask Leo rather than claiming it was sent. Distinguish confirmed defects from documented capability limits, performance design questions and Tangent's own mistakes. A provider's native feature does not establish that Koan exposes the same guarantee.
