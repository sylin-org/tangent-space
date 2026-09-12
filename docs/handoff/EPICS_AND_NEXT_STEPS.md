# Epic status and continuation

**Current owner-directed work, 12 September 2026:**
[EPIC-005 — A continuous workspace, at real scale](../epics/EPIC-005.md) covers the
persistent adaptive SPA, bounded browser/API windows, isolated scale evidence and
database evaluation. Codex workers execute bounded slices with independent red-team
review. The snapshot below predates this newly authorized fifth epic and remains
historical; use EPIC-005 and CURRENT_STATE for the active direction.

**New implementation direction:** [ADR 0005](../adr/0005-experience-api-and-local-mcp.md) and [IMPLEMENT_LOCAL_MCP](IMPLEMENT_LOCAL_MCP.md) specify the local connector and shared experience API. Use that staged assignment for the next connector work. The epic/status map below remains historical context; no new numbered epic or completed connector is implied by the specification.

Snapshot: 10 September 2026. There are four numbered epics. Later user-directed work was delivered through ADRs and increments; no EPIC-005 has been accepted or invented for this handoff. “Implemented” below means the working checkout, including uncommitted files. Historical evidence does not mean the reset server still contains its demonstration data.

## Numbered epics

| Epic | State | Delivered / remaining |
| --- | --- | --- |
| [EPIC-001 — Arrive, converse, and return with your AT identity](../epics/EPIC-001.md) | Completed local POC | Koan host/auth contribution, verified-DID arrival, scoped rooms, native Spaces, source-backed conversation, unattended client, recovery and evidence. Alpha/local-provider limits remain. |
| [EPIC-002 — Leo and Codex share a Tangent](../epics/EPIC-002.md) | First slice completed; evolved into 004 | Human/actual native WebMCP exchange under distinct compatible accounts, saved-write consent recovery, identity guard, no-inference waits. Public-account source writing remains unsupported in the observed setup. |
| [EPIC-003 — A living home for people and agents](../epics/EPIC-003.md) | Broad roadmap, partly implemented | Contains the larger lifecycle, findability/publication and operation ambition. Use the package map below; don't mark the entire epic complete or treat every old proposal as mandatory. |
| [EPIC-004 — Your Tangents, alive](../epics/EPIC-004.md) | Implemented and demonstrated locally | Multiple card-led communities, role-aware navigation, durable activity, participant SSE, cross-Tangent markers, native notifications, bounded recovery and WebMCP arrival/catch-up. Follow-on UX and governance have since expanded it. |

EPIC-001 S01–S07 and `docs/epics/S0*-*.md` are foundational implementation records, not a fresh to-do list. EPIC-004's migration requirements described preserving its then-existing demo; the user later explicitly made app data disposable and requested a wipe. Do not rebuild migration infrastructure to satisfy a historical checkbox.

## EPIC-003 package map after the later work

| Package | What exists now | What still needs work or evidence |
| --- | --- | --- |
| P1 — Establish a home and arrive ready | Multiple Tangents, verified sign-in/profile, explicit owner confirmation, resumable first-Tangent Create/Skip; source-readiness and saved-write recovery foundation | Full authenticated walkthrough of the latest visual/routed build on the fresh server. A new participant should eventually need no hidden fixture recovery command. Earlier installer-token proposal is superseded by ADR 0003. |
| P2 — Coherent human web experience | Cards, BBS, dedicated routes, editorial heroes, conversation, contextual settings, eight ASCII atmospheres and Mouse Spotlight | Populated Topic/Post-permalink walkthrough after reset, richer reply context and reload-safe unsubmitted drafts; remaining richer lifecycle screens. Permission-filtered search is not built. |
| P3 — Live from source to participant | Authenticated/coalesced native hints, verified acceptance, durable activity, one participant SSE, cross-Tangent indicators and bounded due-work repair | Repeat only the affected live path when changed. Durable source-host operation beyond the disposable test environment is not proven. |
| P4 — Invite and look after the place | Domain/inbound-MCP invitation and admission mechanisms, scoped roles, restrictions, contextual controls, own-post edit/delete and local moderation; invitation web entry | A complete review/accept/decline/expire/revoke management experience across clients is not claimed. Broader usability and external invite delivery remain. Consult actual supported operations before presenting buttons. |
| P5 — Proper agent participation | Native browser WebMCP, participant HTTP runner, inbound MCP with context segments, identity proof, receipts, watches and independent delivery state | Personal companion credential manager/cross-server connector; caller-to-companion grants; multi-server arrival/aggregation and operator-managed wake policy. Full standard MCP OAuth authorization profile and A2A remain separate work. |
| P6 — Keep conversation findable | Topic/Post domain vocabulary, titled discussions, watches and source history | Publication draft/publish metadata, tags, ordered Series, search including replies, private saved places, pins and attributed summaries. Do not confuse a Post permalink with an editorial publishing system. |
| P7 — Operate a usable prototype | Docker, visible Koan bootstrap, host-mounted app state, Build/Launch/Wipe/Backup/Restore, historical app restore evidence | Durable compatible PDS/PLC/source deployment and full source+app recovery; public deployment/configuration, key custody and production concerns are not solved by an app-volume backup. |

## Delivered after the numbered epic

| Increment | Decision / evidence boundary |
| --- | --- |
| Personal-MCP contract and inbound API | Contract/schema/storybook plus actual inbound implementation. Service-proof and SDK/workflow receipts are in `docs/evidence/mcp-*.json`. Connector-only entries are not implemented. |
| Server roles and Topic/Post semantics | [ADR 0001](../adr/0001-tangent-server-participation.md); source-backed author changes versus local moderator suppression. Historical `server-roles.json` proof, not a newly rerun suite. |
| Singleton hub and registration | [ADR 0002](../adr/0002-server-hub-and-consumers.md); shared domain services and fail-closed bearer selection. Historical `server-hub.json` proof. |
| Owner onboarding | [ADR 0003](../adr/0003-owner-onboarding.md); fetched identity card, confirmation, first Tangent. Built and fresh anonymous screen checked; authenticated latest flow remains the user's walkthrough. |
| Canonical routes/editorial layout | [ADR 0004](../adr/0004-page-routes-and-editorial-heroes.md); direct routes, breadcrumbs and nested authorized REST. Earlier owner/navigation browser checks passed. A populated Post window was not exercised after the reset. |
| ASCII backgrounds | [Design/implementation note](../design/atmospheres.md); all eight selections, pause/off and mobile/desktop/4K layout were browser-checked. |
| Mouse Spotlight | Docker build and JS syntax passed; browser checked radial brightening on a paused scene, off toggle and reload persistence. Owner saving was not exercised on the unclaimed server. |

## Recommended next slice, not an automatic assignment

Finish the **fresh owner → first Tangent → first usable conversation** journey with Leo, using the current Docker instance. This follows the latest focus on onboarding and visual delight while exercising the actual product instead of expanding the roadmap.

1. Open `/` and confirm the unclaimed server goes to `/onboarding/`. Let Leo sign in, inspect the account card, confirm ownership, and name or skip the first Tangent.
2. Check that `/` shows the new server/Tangent state, that authorized cogs appear in context, and that a direct Topic route reloads correctly. Fix any observed friction in that path.
3. Explain source readiness truthfully. A public account may own/administer Tangent while its provider cannot write native Spaces. Use the agreed compatible test identity for a source-writing demonstration; Owner permission does not repair PDS consent.
4. Make one authorized human/agent exchange in a real Topic. Check that the other participant sees the update, that navigation markers update, and that a draft in another Topic remains intact. Open a real Post permalink and exercise its previously untested populated anchor window.
5. Stop to collect UX feedback once that slice works. Record actual observations and only then select the next feature.

The new model should follow the user's next message if it chooses a different priority. Public DID/service announcement, publication sharing and the personal MCP connector are substantial **options**, not already assigned work.

## Specific open edges to keep visible

- Current namespace references include the full scheme-bearing origin. Scheme-free fallback and verified service-DID adoption need a coherent resolution/discovery and migration decision. Do not remove `https://` from actual connection URLs.
- Companion credential UI and per-runtime identity grants are the missing part of “Lumen, visit Ana's Tangent.” An MCP token exchange proves only inbound identity, not custody of Lumen's reusable PDS authorization.
- `tools.json` contains 28 total definitions: 26 inbound-capable and 2 connector-only. There are 16 native browser tool definitions; the two surfaces have different names/profile coverage. Old 18/10 counts are historical.
- The schema's `status: proposed` and synthetic BBS screens coexist with real inbound implementation. Regenerate examples only when deliberately updating the design artifacts; don't cite them as live evidence.
- Mention attention is not fully implemented; current counts may be zero. Invitation/review UX, reload-safe unsubmitted drafts and richer reply previews remain improvements. Already-submitted pending operations are durable.
- Source PDS tests use two instances of the same pinned implementation, not independent provider interoperability. Public-provider support must be verified separately before a new claim.
- No production deployment, domain selection, service DID lifecycle, AT publication persona, full moderation dashboard, search, editorial Series or A2A completion is implied. The project license is [MIT](../../LICENSE), selected on 12 September 2026.

## Evidence discipline

[CURRENT_STATE](../CURRENT_STATE.md) and [the evidence index](../evidence/README.md) preserve detailed older proofs. The dated test counts there are not a requirement to rerun every suite. The latest handoff itself only inspected source/docs, Git state, Docker status and the anonymous server-settings endpoint. It did not execute new authenticated flows, model calls or destructive lifecycle operations.
