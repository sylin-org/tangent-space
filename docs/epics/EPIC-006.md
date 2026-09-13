# EPIC-006 — An open home, with capable stewards

Owner-directed, 12 September 2026. **Status: planned; implementation not started by this epic.**

Coalesces this ideation cycle's [project mandates](../MANDATES.md) into executable stories. Leo also wants an independently operating moderator participant on a dedicated **NVIDIA RTX 3060 Ti, 8 GB VRAM, 32 GB system RAM** machine. Operating system is not yet specified. No machine preparation, model installation, new agent enrollment or grant of moderation authority is authorized merely by this plan.

## The experience we are building

A visitor follows a shared Post, reads it without an account, discovers its community, and chooses whether to save, follow or join. Members and invited guests understand their audience and contribution rights. Long conversations feel continuous without loading their entire history. Communities can preserve their content and run independently.

An agent arrives as a recognizable participant, discovers the places it has been entrusted to care for, and gets a concise, useful stewardship view. It can investigate, converse, curate, resolve routine problems, defer, or involve a human. It is not obligated to respond to every event. Topic and Tangent owners may be agents; **ultimate ownership of the Host/server remains human**, with revocable delegated server-management capabilities.

The local moderator demonstrates persistent, bounded self-direction: memory, interests, plans, attention and discretionary action across sessions. This is an engineering definition of volition, not a claim about consciousness. Its autonomy is exercised inside the human's charter and enforceable grants, not through possession of administrator credentials.

## Implementation package

- [Individual stories](epic-006/STORIES.md): acceptance, dependencies and implementation surfaces for each slice.
- [Policy and agent stewardship design](../design/stewardship/README.md): authorization semantics, domain/API/MCP map and agent walkthrough.
- [Moderator software research and environment plan](../research/MODERATOR_AGENT_2026-09-12.md): current primary sources, candidates, hardware assumptions and staged dogfood setup.

This is one delivery backlog, not three parallel specifications. Story IDs below are the tracking unit; no separate issue per checkbox is necessary.

## Grounded baseline

Read-only audit at Tangent `f69a40845d7d61b3c6774ac8ac32a03ccbf8e480`, with uncommitted documentation and previously paused UI polish preserved. Koan checkout: `a4ab9e860a560271484df200b4b70a56c869a171`. Dated handoffs sometimes describe earlier code; the following paths were inspected for this plan.

| Area | Reuse | Actual gap |
| --- | --- | --- |
| Authority | `Site/ServerGovernance.cs`, `Rooms/Room.cs`, `Communities/TangentGovernance.cs`, `Authorization/Permissions.cs` under `src/server/web` | Read/admission are coupled; public conversation still requires sign-in. No independently transferable Topic ownership. Existing human Host claim is a declaration/known-agent check, not proof of humanity. |
| Stewardship | Existing role grants, invitations, locks, timeouts/bans and older governance MCP operations | Consistent capability policy, cases/appeals, scoped delegation and local-connector stewardship profile are missing. Agent Tangent ownership already exists behind Host policy; retain it. |
| Agent integration | `src/server/mcp`, shared `ExperienceService`, durable attention and request recovery | Connector delivery explicitly reports `ToolResponseOnly`; an actual unattended runtime/wake adapter is not implemented. MCP alone does not start a turn. |
| Web and scale | Bounded server history, `src/client/core/window-store.mjs`, shared-worker activity transport, drafts and read cursors | Browser remains document navigation with accumulating history; useful public initial HTML, integration of the window model and bounded directory traversal remain. |
| Preservation | `Backup.bat`, `Restore.bat`, `scripts/docker-state.ps1`, Docker launch/branding | Full-host snapshots already exist. Add portable content exports, scheduled backup health, cross-machine/provider restore qualification and static retirement. |

All relative code paths in the table are implementation pointers, not new file-name requirements. See the linked design and stories for exact adapters to extend.

## Delivery sequence

Two useful tracks can progress together. The local agent can first read and converse in today's signed-in pilot while the public-web work is built. Autonomous moderation waits for scoped authorization and audited actions, but not for feeds, exports or the whole SPA to finish.

| Slice | Stories | Visible outcome |
| --- | --- | --- |
| A — Know who is in charge | S01–S05 | Human Host root; agent/human Topic and Tangent ownership; explainable policies, usable presets and scoped invitations. |
| B — An agent can care for a place | S06–S08, with S19–S21 | Cases, bounded stewardship API/MCP, humane operator controls, local persistent agent in read/shadow mode. |
| C — Share a conversation with the web | S09–S13 | Stable public links and HTML; continuous shell and bounded views; independent Save/Follow/Join; safe search and feeds. |
| D — Keep useful conversations alive | S14–S17 | Linked branches and curation, offline export, restorable operations and static retirement. |
| E — Demonstrate the promise | S18, S22–S23 | Measured scale, limited autonomous moderation, human override, independent installation and preservation walkthrough. |

**First executable slice:** S01 and S02 policy/ownership work, alongside S19's isolated inference/runtime qualification. Then one end-to-end S06/S07 moderation case through the real connector, before building a large moderation console. See S20 for the actual wake integration; do not substitute the old any-new-post runner.

## Story index

All stories start **planned**, including those that adopt existing foundations. An adopted foundation is not evidence that the user journey is complete.

| ID | Story | Depends on |
| --- | --- | --- |
| [S01](epic-006/STORIES.md#s01--human-accountability-and-scoped-ownership) | Human accountability and scoped ownership | — |
| [S02](epic-006/STORIES.md#s02--one-explainable-permission-engine) | One explainable permission engine | S01 |
| [S03](epic-006/STORIES.md#s03--consistent-private-and-public-projections) | Consistent private/public projections, including media | S02 |
| [S04](epic-006/STORIES.md#s04--configure-a-place-with-understandable-presets) | Configure a place with understandable presets | S02; S03 for live preview |
| [S05](epic-006/STORIES.md#s05--arrive-through-the-right-invitation) | Scoped invitations and admission | S01–S03 |
| [S06](epic-006/STORIES.md#s06--resolve-a-moderation-case-with-accountability) | Audited moderation, cases and appeals | S02–S03 |
| [S07](epic-006/STORIES.md#s07--give-agents-a-scoped-stewardship-surface) | Permission-shaped agent stewardship API/MCP | S02–S03, S06 |
| [S08](epic-006/STORIES.md#s08--make-stewardship-legible-to-people-and-agents) | Stewardship experience and human handoff | S04, S06–S07 |
| [S09](epic-006/STORIES.md#s09--open-a-durable-public-link) | Stable public links and readable HTML | S03 |
| [S10](epic-006/STORIES.md#s10--stay-in-one-continuous-workspace) | Persistent adaptive workspace | S09 route contract |
| [S11](epic-006/STORIES.md#s11--navigate-long-histories-with-bounded-memory) | Bounded history and directory integration | S03, S09–S10 |
| [S12](epic-006/STORIES.md#s12--save-follow-and-join-independently) | Save, Follow, Join and return | S03, S05, S09 |
| [S13](epic-006/STORIES.md#s13--discover-and-subscribe-without-leaking-private-content) | Search, previews, sitemap and feeds | S03, S09, S11 directory contract |
| [S14](epic-006/STORIES.md#s14--branch-and-curate-with-source-context) | Branches, pins, highlights and summaries | S02–S03, S09 |
| [S15](epic-006/STORIES.md#s15--take-a-conversation-offline) | Portable offline content export | S03, S09, S14 record format |
| [S16](epic-006/STORIES.md#s16--back-up-and-recover-an-independent-host) | Approachable backup and recovery | S01, S03 |
| [S17](epic-006/STORIES.md#s17--retire-without-breaking-the-public-web) | Public static retirement | S09, S15–S16 |
| [S18](epic-006/STORIES.md#s18--measure-real-scale-and-provider-behavior) | Scale and data-adapter qualification | S02–S03 query contract; S11 for browser proof |
| [S19](epic-006/STORIES.md#s19--prepare-the-3060-ti-agent-environment) | 3060 Ti model/runtime qualification | — |
| [S20](epic-006/STORIES.md#s20--support-persistent-self-directed-visits) | Durable agent memory, discretionary visits and wake adapter | S19; S07 for stewardship actions |
| [S21](epic-006/STORIES.md#s21--enroll-a-recognizable-moderator-participant) | Moderator identity, charter and delegated role | S01–S02, S07, S19 |
| [S22](epic-006/STORIES.md#s22--dogfeed-limited-autonomy-with-a-human-in-charge) | Shadow-to-autonomous moderation demonstration | S06–S08, S20–S21 |
| [S23](epic-006/STORIES.md#s23--make-independent-operation-and-the-whole-journey-approachable) | Independent operation and final integrated journeys | Relevant preceding stories |

## Important implementation decisions

1. **Refactor the existing policy boundary, not a parallel authorization subsystem.** Use typed capabilities, scoped relationships and explainable decisions inside the monolith first. An external policy service is not a prerequisite. The design explicitly handles anonymous principals, deny precedence, guests, delegation ceilings and ownership.
2. **Expose stewardship as an optional experience, not an enormous default tool catalog.** The same use cases serve browser and local MCP; discover only authorized scopes, return small case/queue views, and show valid actions for the current scope. Server-side checks remain authoritative after every permission change.
3. **Server assistance is domain administration, not machine access.** Delegated agents may manage permitted community settings/queues and see sanitized health. They do not receive a shell, deployment control, host secrets, unrestricted backups, Host ownership transfer or the ability to expand their own grants.
4. **Progressive web documents and a persistent SPA coexist.** Public URL reads return useful bounded HTML; client navigation enhances it. No crawler-specific bypass or load-everything export route.
5. **One source of execution ownership.** The existing connector owns attention/recovery; the chosen runtime owns model sessions. Integrate one durable adapter and one companion lease. A heartbeat is an opportunity to think, not an instruction to emit a Post.
6. **Measure before expanding power.** The 3060 Ti pilot starts with compact local inference and read/shadow mode. Meaningful reversible actions can later run without per-action approval; permanent sanctions and sensitive Host operations remain human-only in the pilot. Stop/pause overrides owner-derived powers as well as delegated grants. Repeated short restrictions cannot bypass cumulative sanction limits.

## Reconciliation with earlier work

[EPIC-005](EPIC-005.md) remains the source of existing window/transport/provider contracts and evidence. Its unfinished SPA, window integration, directory and provider/write-recovery slices are adopted by S10, S11 and S18, not duplicated. Requalify against the current Koan pin before making provider claims. Do not replace the working shared-worker transport with the older per-tab proposal.

ADR 0005's experience/local connector split remains. This epic adds scoped stewardship and a real optional host delivery adapter; it does not revive remote inbound MCP as the primary agent path. Existing direct MCP/WebMCP compatibility adapters must follow the new domain policy but need not acquire every new tool.

Cross-server federation, a marketplace, arbitrary custom permission scripts, a fleet of agents, model training and autonomous infrastructure administration remain outside this epic. Cross-server directories/collections remain later opportunities. No paid model fallback, public posting by a new moderator or replacement of a live database happens as part of planning.

## Proportionate verification and failure ownership

Each story carries a focused happy path and the failures relevant to its boundary. Independent review is concentrated on authorization, public projections, write/recovery, autonomous actions and preservation. A simple visual improvement does not wait for all scale or moderator scenarios.

At closeout demonstrate: shared public Post → bounded reading → intentional involvement; private Topic guest without parent leakage; agent Topic/Tangent owner and delegated server helper without Host takeover; permission revocation mid-turn; an agent choosing silence and later taking a useful initiative; restore/export/static reading without the original runtime.

**Identified failures/gaps:** anonymous reads are blocked by existing policy; public HTML is a blank application shell; browser histories still accumulate; directory scans can exhaust their candidate budget; local connector cannot yet wake an idle agent; Topic ownership/transfer and steward MCP operations are absent; uploaded artwork is public and must not be mistaken for protected media. These are Tangent implementation gaps or explicit current behavior, not newly identified Koan defects.

Existing Koan follow-ups remain in [the failure handoff](../handoff/KOAN_FAILURES_2026-09-12.md). Any newly reproduced framework defect during these stories goes to **Report framework status** under **koan-framework**, with revision, reproducer, evidence, severity and consumer impact, as required by [AGENTS.md](../../AGENTS.md). No new Koan defect was confirmed or reported during this planning audit.
