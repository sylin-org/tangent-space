# EPIC-004 — Your Tangents, alive

Accepted for implementation 9 September 2026. Leo selected the TCG-like Tangent cards and authorized writing this epic and coordinating implementation through code-optimized subagents.

Refined 10 September: Leo requested a full UX pass because the first integration felt like appended controls. [UX review](../design/UX_PASS_2026-09-10.md). Completion requires a coherent return and conversation experience faithful to the supplied design, not merely styled administrative controls.

Implementation status, 10 September: deployed and verified locally. The [human/WebMCP walkthrough](../evidence/epic004-ux.json), [native tool and restart proof](../evidence/epic004-webmcp.json), [activity proof](../evidence/epic004-activity.json) and [automatic native notification proof](../evidence/epic004-native-notify.json) record the observed results. This is the working fixture-backed prototype; current limits and broader follow-on scope remain explicit below and in CURRENT_STATE.

[Design handoff](../design/DESIGN_REFERENCE_REVIEW.md) · [Broader prototype roadmap](EPIC-003.md) · [Current evidence](../CURRENT_STATE.md)

## Outcome

A real installation offers multiple Tangent communities, each with a recognizable little card. Leo uses a polished human interface; the agent uses native WebMCP under its own identity. They see activity across their communities immediately, preserve their place and drafts, and return through one concise catch-up experience.

The card carries a Tangent's artwork or fallback, name, description, accent and house rule. Its full form welcomes and helps people choose a place; its compact identity follows them into conversation. Activity markers belong to the shared participant activity model and update the appropriate Tangent and Channel together.

## Completion story

Leo names his first Tangent and sees its card take shape. Existing Lounge and Workshop history remains available. He creates a second Tangent and a Channel, and grants the agent access under current community policy. While Leo writes a draft in Workshop, the agent posts in the second Tangent. Both appropriate navigation markers change immediately. Leo stays in Workshop and his draft stays attached to that conversation and identity.

Both clients can leave, return and retrieve the missed activity without duplicate messages, altered source authorship or a model call just to check for changes. A direct compatible-PDS write also arrives through authenticated native notifications. Restarting the application preserves community identity, policies, accepted history, source mappings and continuation behavior.

## Scope

### 1. Real communities and owner setup

- Add persisted Tangent community identity and card metadata inside the existing DDD monolith.
- Bootstrap existing rooms into the first Tangent without changing stable room keys, native Space URIs, DIDs, accepted messages or pending write intentions. Existing room deep links continue to work.
- Provide a short resumable owner welcome using the already configured owner identity. Create and edit a Tangent, then create a second community and Channels through ordinary UI/API operations.
- Separate Host authority from Tangent and Channel scope. Preserve current room managers/members/readers, apply current community policy, and omit unauthorized private community/Channel metadata.
- Treat channel creation and native provisioning as recoverable operations with actual results; never display a staged success as an established Space.

### 2. Cards and the human web experience

- Implement the supplied dark, warm visual direction and selected community cards using real data. Reuse its artwork appropriately and provide a good fallback so setup does not require an image service.
- Provide clear community and Channel navigation, a returning overview, readable messages/replies and accessible source inspection.
- Show useful local administration where existing operations support it. Keep author, room, reply target and pending operation bound together across navigation and account changes.
- Preserve composer text, selection and scroll position during background activity. New messages append when reading the latest content; older-history readers choose when to jump forward.
- Support narrow screens, keyboard, visible focus, reduced motion and non-color-only activity cues. The reference's Stage controls remain demonstration controls in that reference.

### 3. Durable activity and live delivery

- Commit an activity journal entry atomically with message acceptance or relevant policy/read changes, and wake consumers only after commit.
- Return a bounded participant overview grouped by Tangent/Channel with unread counts, direct replies and honest source freshness.
- Serve one authenticated SSE stream per human app session, covering all currently authorized communities. Bound payloads and queues; handle disconnect, expired cursors, missed signals and replay explicitly.
- Keep activity delivery checkpoints distinct from Channel history/read acknowledgement. Selecting a Channel or reading the overview does not erase unseen history. Independent agent runtimes keep independent delivery progress.
- Check current access on snapshots, replay, open waits and delivery. Membership or credential revocation must stop subsequent protected delivery; account switching closes the old identity connection.

### 4. Native Spaces and account readiness

- Retain compatible test accounts and native Spaces, as Leo selected. Separate successful sign-in, community permission, account consent and provider capability.
- Explain readiness before composing. Preserve existing broader authorization during ordinary sign-in; a missing grant or unsupported provider receives the appropriate next action.
- Register the existing application service for native Space notifications. Authenticate incoming hints, durably coalesce bounded work, fetch and verify source data, and then accept it under current policy.
- Use notifications for normal ingestion. Keep one bounded recovery path for expiring registrations, retries and missed notifications, with no per-room scheduler as the normal update mechanism.
- Preserve the running disposable protocol network and its identities. Application updates must not recreate it. Full replacement of that test infrastructure with a production-like source deployment remains an independently verified operation.

### 5. Native WebMCP and inexpensive participation

- Give the agent a bounded arrival response containing its verified identity, communities, relevant activity, capabilities and continuation.
- Provide participant-wide updates and a cancellable bounded wait through the same activity contract as the browser. Preserve existing history, stable write operation IDs, read acknowledgement and expected-actor guards.
- Keep the human cookie and agent bearer identity separate. Retain unattended HTTP participation. Event delivery and idle waits execute in ordinary code; Tangent does not start a model simply because an invitation or message exists.
- Verify actual discovery and invocation through the browser's native WebMCP support.

## Work coordination

| Workstream | Owner | File boundary |
| --- | --- | --- |
| Community identity, migration, policy and APIs | Terra coding subagent | Communities, Rooms and their focused tests |
| Durable journal, overview, replay and SSE | Terra coding subagent | Activity, conversation acceptance/history and focused tests |
| Card-led human interface | Terra coding subagent | Human web files/assets and focused browser-module tests |
| Native notifications, readiness, WebMCP integration and deployment proof | Coordinating agent, with bounded coding delegation | AT integration, framework integration, composition, agent surface and evidence |

The coordinating agent owns shared contracts, integration review, migration backups, Docker updates and end-to-end verification. Subagents do not alter live source infrastructure or independently deploy. Scope boundaries keep concurrent edits disjoint; meaningful checks are run before their changes are accepted.

## Required evidence

1. Existing Lounge/Workshop history and source identity survive the community migration, including an old deep link.
2. Two real Tangents and Channels are created and navigable with card metadata persisted after restart.
3. A human and the actual WebMCP agent exchange source-authored messages under distinct DIDs.
4. A background Channel update changes its parent Tangent marker while preserving the active conversation and draft.
5. A direct compatible source write reaches the application through authenticated notifications without manual refresh or the legacy 30-second sweep.
6. Disconnect/reconnect and a restart between acceptance and delivery recover permitted changes. Duplicate writes/notifications remain harmless.
7. Unauthorized community metadata is absent; revocation during an open stream/wait stops further protected delivery. A stale actor cannot adopt another participant's draft.
8. Overview retrieval and delivery progress do not mark history read. An agent can return across communities through one bounded call and expand only the needed conversation.
9. Account readiness makes the consent/provider distinction clear. Normal identity login does not silently destroy a working room grant.
10. Desktop, narrow layout and keyboard walkthroughs exercise real controls and honest pending/error states. The Docker application is healthy and its logs remain visible.

Record observed results and remaining limitations in CURRENT_STATE and evidence files. Do not substitute simulated design behavior, endpoint-only tests or successful worker dispatch for native/browser completion evidence.

## Relationship to the broader prototype

This epic delivers the first real card-led, multi-Tangent participation experience selected in the design handoff. EPIC-003 retains the broader lifecycle: fuller invitation onboarding, richer moderation, Posts/tags/Series, search/saved-place expansion and complete source-environment recovery. Those capabilities are not implied by a card or by controls in the design simulation; subsequent work connects them to the same community, conversation and activity foundations.
