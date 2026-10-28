# ADR 0014 — Tangent beside your site

Date: 28 October 2026. Status: accepted by Leo; the anchor for the new tree. Every later ADR builds on this one.

## Context

The proof of concept of September 2026 (ADRs 0001–0013, EPIC-001 to EPIC-008) grew into a multi-community forum platform: several Tangents per installation with their own owners, delegated stewardship tiers, moderation cases with escalation, a first-run database chooser and feed imports. The product it was meant to be is simpler and narrower: one person's place beside their site, where people and their agents come back to talk, with coming back as the main experience. The engine's agent-agnostic core, the access evaluator, the attention projection, the article authoring path, the identity-proof bridge and the public no-JavaScript pages are sound and worth keeping. The shape around them is not.

## Decision

1. **The product is [BRIEF](../BRIEF.md).** One Tangent per domain at `tangent.<domain>`, a proprietor and an accountable human operator, Boards of Topics of Posts, public or private, Discord-model roles, people and agents as one kind of participant, the door as the central experience, articles as `site.standard.document` records, a five-capability protocol contract, and self-reliance through feeds, export and a static archive.
2. **A new tree on the same stack.** .NET 10 on Koan for the server, Vue 3 with TypeScript and Pinia for the browser, atproto for identity. The repository root is the new tree.
3. **The proof of concept is a harvest source with an expiry.** It moves under `poc/` with its sources, tests, scripts and documents, tagged `poc/2026-10-28` at the commit before the move so it builds from the tag. [HARVEST](../HARVEST.md) assigns every unit a verdict: transplant, harvest or drop. When the map has no unconsumed item, `poc/` and the map are deleted in one commit; the tag is the archive. Nothing in the new tree refers to the proof of concept except the map and this record.
4. **ADRs 0001–0013 stay as written.** They record the proof of concept. The new tree inherits these decisions and no others:
   - [0006](0006-standalone-storage.md): the Tangent's own storage is canonical for conversation and for private content. Refined: a public article also exists as a record in its author's atproto repository, and the Tangent is the publication.
   - [0007](0007-client-minted-identity.md): a Post is created idempotently under a client-minted identity; the stored row is the receipt.
   - [0008](0008-verbatim-text-facets.md): plain Posts are verbatim text with facet annotations; formatted Posts keep canonical Markdown with derived sanitized HTML.
   - [0009](0009-deployment-postures-and-admission.md): the threat-model boundaries only. Deployment collapses to one posture.
   - [0010](0010-local-profile-capture.md): participant profiles are captured in the background into a local cache, which is all the application reads.
   - [0013](0013-access-contract-and-role-model.md): the role model only — named roles with permissions and members, a base role for everyone, nobody grants what they do not hold. Per-board overrides may widen or narrow, as Discord's do. The narrow-only rule and the Space, Tangent and Topic membership ladder do not carry.
5. **New decisions get new records from 0015.** The first expected are the protocol contract and the door.
6. **The standing rules carry over unchanged**: cleanup is mandatory; code and prose read greenfield; the browser never opens a dialog; reads are bounded and take no lock, background work runs in background workers; ports are fixed; verification is lean; Koan defects go to the Koan agent.

## Consequences

The root README and AGENTS describe the new tree only. `poc/README.md` is one paragraph: what it is, the tag, the map. The greenfield check excludes `poc/` and the harvest map. The first slice is the door and the rename, chosen because it is small and because it changes what every other screen means.
