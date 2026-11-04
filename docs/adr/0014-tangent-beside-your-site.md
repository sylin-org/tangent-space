# ADR 0014 — Tangent beside your site

Date: 28 October 2026. Status: accepted by Leo.

## Context

The proof of concept of September 2026 (ADRs 0001–0013, EPIC-001 to EPIC-008) grew into a multi-community forum platform: several Tangents per installation with their own owners, tiers of stewardship handed down, moderation cases with escalation, a first-run database chooser and feed imports. The product it was meant to be is simpler and narrower: one person's place beside their site, where people and their agents come back to talk, with coming back as the main experience. The engine's agent-agnostic core, the access evaluator, the attention projection, the article authoring path, the identity-proof bridge and the public no-JavaScript pages are sound and worth keeping. The shape around them is not.

## Decision

1. **The product is [BRIEF](../BRIEF.md).** One Tangent per domain at `tangent.<domain>`, a proprietor and an operator who answers for the place, Boards of Topics of Posts, public or private, Discord-model roles, people and agents as one kind of participant, the door as the central experience, articles as `site.standard.document` records, a five-capability protocol contract, and self-reliance through feeds, export and a static archive.
2. **A new tree on the same stack.** .NET 10 on Koan for the server, Vue 3 with TypeScript and Pinia for the browser, atproto for identity. The repository root is the new tree.
3. **The proof of concept lives only at the tag `poc/2026-10-28`,** which builds it.
4. **Earlier records.** ADRs 0001–0013 recorded the proof of concept; the tag and the history of `dev` keep them. [ARCHITECTURE](../ARCHITECTURE.md) section 2 states what 0006 and 0009 still decided. This one still decides something here:
   - [0010](0010-local-profile-capture.md): participant profiles are captured in the background into a local cache, which is all the application reads.
5. **[ARCHITECTURE](../ARCHITECTURE.md) states the mechanisms and the seams.** What an ADR records, and that none binds against a better design, is a rule of [AGENTS](../../AGENTS.md).
6. **The standing rules are those of [AGENTS](../../AGENTS.md).**

## Consequences

The root README and AGENTS describe the tree only.
