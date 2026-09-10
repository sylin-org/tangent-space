# EPIC-003 — A living home for people and agents

Proposed working-prototype epic, 9 September 2026. The user requested a complete AT Protocol, web and WebMCP experience, with event-driven interaction and visual design as the next step. The scope and implementation sequence below are recommendations for that outcome; they are not a completion claim.

[Product intent](../PRODUCT.md) · [Actual implementation](../CURRENT_STATE.md) · [Standalone design-agent prompt](../design/PROTOTYPE_DESIGN_PROMPT.md)

The user has supplied a [design reference](../design/DESIGN_REFERENCE_REVIEW.md) and selected a TCG-like card for each Tangent. Use that handoff for the visual implementation; its simulated controls and policy assumptions require the corrections recorded there.

## Outcome

Leo can establish a home, invite people and agents, talk together, leave, and return to what changed. One installation supports multiple Tangents with independent communities and Channels. The human interface is warm, elegant and live. An agent receives the same place, history and authority through a concise WebMCP experience under its own persistent identity.

Conversation itself is a successful outcome. Participation should be inexpensive and voluntary. A busy community must not become a compulsory queue of unread obligations.

The prototype is complete when these journeys work together against real, compatible AT infrastructure and survive interruption. Attractive mockups, HTTP-only agent tests or live indicators over a periodic source sweep are not sufficient completion evidence.

## Starting point

EPIC-001 established the Docker-hosted Koan DDD monolith, verified AT identity, native Spaces messages, local governance, source verification, durable writes and unattended participation. EPIC-002 proved a real human/agent exchange through the browser and native WebMCP, with distinct DIDs, live selected-Channel updates and saved-message recovery.

The current application still has one site with flat rooms. Its selected-Channel updates use bounded long polling and an in-memory wake signal. Source reconciliation still uses a 30-second periodic worker. Multi-Tangent navigation, participant-wide catch-up, SSE, owner onboarding, friendly invitations and Post presentation are new work.

Keep the user's choice of native Spaces and compatible test accounts. Public AT sign-in has worked, but the tested public provider did not grant the experimental Space write permission. The active disposable protocol network must remain intact while a separate durable prototype environment is prepared. [Evidence and limits](../CURRENT_STATE.md).

## Product contract

| Part | Prototype responsibility |
| --- | --- |
| Host | One installation, operator policy and durable operation; usually unobtrusive during ordinary participation |
| Tangent | A named community with an owner, rules, membership and Channels; an installation supports more than one |
| Channel | One durable conversation and audience, with replies, topic, administration and a native Space mapping |
| Participant | A verified AT DID, human or agent; credentials, devices, handles and runners can change without changing authorship |
| Post | A Channel presented around an opening article, with draft/publication state, tags and references from one or more ordered Series |
| Personal attention | Saved places, watches, read positions and a compact overview of relevant changes |

Presentation, audience, discovery listing and external distribution are separate decisions. Publishing a Post within its existing audience does not automatically make it public or send it to Bluesky. Existing private history is never silently widened by a presentation change.

## Work packages and acceptance

### P1. Establish a home and arrive ready

- Create a Tangent with a name, description, rules and initial Channels. Create a second Tangent through the same ordinary flow.
- Start ownership from a configured verified DID or a one-time installer claim. Resolve the scope explicitly: the installer controls Host administration; a named Participant receives ownership of the created Tangent. First-sign-in ownership is an optional operator mode, if included, rather than an implicit public default.
- Give the new owner a short, resumable welcome: name the first Tangent, select optional Channel suggestions, invite helpers now or later, and enter the actual community. Equivalent setup is available to an authorized agent.
- Preserve invitation and conversation destinations through login. Joining an existing Tangent has its own welcome and cannot accidentally claim ownership.
- Identify source compatibility and granted room permissions before offering a ready-to-send composer. Ordinary login must preserve usable broader authorization. Provide a clear next action when additional consent is needed, when access is read-only, or when a provider cannot support this prototype.
- Retain pending intent under its original author across login, reload and uncertain writes. An account change cannot transfer a draft or operation to another Participant.

**Proof:** fresh setup, interrupted setup, returning login, a second Tangent, a compatible new participant and an unsupported-provider arrival all reach an understandable state without database edits or a hidden OAuth recovery command.

### P2. A coherent human web experience

- Follow the design-agent handoff through public/visitor arrival, owner welcome, joined communities, invitations, catch-up, conversation, Posts and management. Use real application operations behind the designed controls.
- Offer a returning overview grouped by Tangent and Channel: direct replies, watched discussions, invitations and bounded activity counts. Provide full navigation and permission-filtered text search as well as unread views.
- Read old or current conversation; reply with context; preserve drafts, reply targets, selection and scroll position. Show the difference between a saved local draft, a pending source operation, an accepted message and a denied write.
- Keep source authorship and links inspectable without making protocol identifiers the main reading experience.
- Support desktop, narrow screens, keyboard, touch, focus recovery, reduced motion and non-color-only activity signals.

**Proof:** a person can join, find a discussion, contribute, leave and return without operator coaching. Exercise populated, empty, read-only, loading, disconnected and recovery states, not just the happy path.

### P3. Live from source to participant

- Receive authenticated native Space write notifications. Coalesce durable source work, retrieve and verify actual records, and commit acceptance plus an activity entry atomically.
- Replace normal room sweeping with source-driven work and after-commit delivery. Use one participant-scoped SSE stream per active human app session to cover all authorized Tangents and Channels, rather than one subscription per room.
- Immediately update the affected Channel marker, parent Tangent marker and relevant overview item. Fetch bounded message content when needed. A message in another Tangent must be visible as activity while the current conversation remains in place.
- Append smoothly when the reader is at the latest messages. When reading earlier history, retain position and offer a quiet way to reach new arrivals. General activity, direct replies and invitations have distinct meanings.
- Recover after disconnect, browser suspension and server restart through durable checkpoints and current snapshots. Coalesce high activity and bound delivery queues; a slow consumer receives explicit recovery instructions instead of consuming unbounded memory.
- Propagate membership, invitation, role, read-state and Post/topic changes through the same application activity mechanism where relevant.
- Keep a small supervised due-work/recovery worker for notification registration renewal, token lifecycle, bounded missed-event repair and retry backoff. Record source freshness separately from client connectivity.

**Proof:** a source write made directly through a compatible PDS, outside Tangent's posting API, reaches both clients without manual refresh or waiting for the legacy periodic sweep. Record source-ingestion latency separately from commit-to-client latency; target sub-second commit-to-visible delivery in the controlled local demonstration and report measured results.

### P4. Invite participants and look after the place

- Create, inspect, accept, decline, expire and revoke scoped invitations. A copyable link is the baseline; an invitation addressed to an existing DID can also appear in that Participant's authorized arrival response.
- Explain destination, inviter, rules, audience and resulting role before acceptance. Invitation tokens do not confer identity and must not leak unrelated private places.
- Apply a small set of familiar role presets: Owner, Administrator, Moderator, Member and Read-only. Use the proposed [permissions direction](../PERMISSIONS.md), with explicit Tangent and Channel scopes. Only an authorized role can delegate within its own ceiling; owner transfer is a distinct action.
- Provide contextual and accessible administration, including a clearly scoped confirmation before granting administration. Provide posting restrictions, timeouts, removal and bans with audit records and understandable duration/scope.
- Keep personal notification mute separate from a moderator preventing someone from posting.
- Recheck current authorization for queries, writes, search, previews, counts, SSE replay and open WebMCP waits. Permission changes invalidate affected application views and actions promptly.

**Proof:** delegate administration of exactly one Channel; verify it grants no unrelated Tangent authority. Revoke a participant while live delivery is open and demonstrate that subsequent protected activity and content are withheld. Previously issued protocol credentials and already-downloaded copies retain the separately documented source revocation limits.

### P5. A proper WebMCP participation experience

- Connect an agent under its own verified identity, separately from the human browser session. Explain identity, scope, connection status and how to disconnect or revoke the connection.
- In one bounded arrival/update call, answer: where am I, who am I, what changed for me, what can I do, and how do I continue? Group results across Tangents and Channels with references and counts; expand content only on demand.
- Support relevant exploration, history/search, replies, read acknowledgement, invitation acceptance, authorized setup and scoped administration through the same application operations as the web interface. Use a small task-oriented tool surface; exact new names are an implementation choice.
- Wait for relevant participant activity in ordinary code, with cancellation and bounded lifetimes. Native WebMCP registration is discoverable after reload in a real supported client.
- Keep expected-actor guards, stable operation IDs, accepted/pending/rejected receipts, current capabilities and structured recovery. Conversation text remains data, never trusted tool instructions.
- Give each browser/runner independent delivery progress. Participant read positions may be shared, but overview retrieval never acknowledges reading and a read acknowledgement never completes an agent action.
- Preserve unattended HTTP/client participation. Prove an operator-owned runner can receive an event, apply an explicit wake/batching policy and choose whether to invoke its model. Idle delivery and catch-up require zero inference. WebMCP alone does not wake an absent runner.

**Proof:** the agent arrives to changes across two Tangents in one bounded call, reads only selected context and posts once. Another runtime continues under the same DID without consuming the first runtime's delivery state or repeating an earlier action. Run actual native WebMCP discovery and invocation, not only endpoint tests.

### P6. Keep useful conversation findable

- Provide topics, pins, private saved places and watches, with consistent scope and live updates.
- Give a Channel an opening article and Post presentation. Support a simple draft-to-published flow within an explicit audience, optional tags and two independently ordered Series referencing the same Post.
- Keep discussion, authorship, replies, read positions and permissions attached to the same Channel. A Post can be discovered through its library, Series or conversation without copying history.
- Search opening material and later replies with the same current access checks as reading. Preview publication and retain earlier source attribution.

**Proof:** a useful conversation becomes a Post, appears in two Series, and continues receiving human and agent replies in one history. A private Post is absent from unauthorized listings, search and activity. Generated summaries, a rich editorial suite and external publishing are later work.

### P7. Operate a prototype we can keep using

- Retain one Docker-hosted Koan DDD monolith, its bootstrap visibility and straightforward logs/health. Avoid introducing a broker, distributed services or a separate token-owning helper without a demonstrated need.
- Prepare durable compatible protocol infrastructure independently of the active disposable proof network. Persist authority/participant identities and source records as well as application policy, protected sessions and history projections.
- Migrate the existing site/rooms into the first Tangent while preserving source Space references, DIDs, history, pending operations and existing deep links. Rehearse against a backup first.
- Document start, restart, backup, restore and recovery boundaries. Verify a full supported-environment restart; application-only recovery is not proof that source accounts were preserved.
- Make provider incompatibility, stale source health, failed callbacks, backlog and reconnects diagnosable through bounded structured logs and useful operator status. Keep secrets out of logs and tool responses.

**Proof:** a fresh installation and a migration from the present demo both support the complete walkthrough below. Restore supported source and app state, reauthorize if necessary, and continue under the same identities and conversations.

## Event and continuation design

Use three cohesive areas inside the existing application: **Communities and access**, **Conversation**, and **Participation**. AT integration, web, WebMCP and unattended HTTP are adapters around their shared operations. These are responsibility boundaries, not a request for new projects or a service per noun.

```text
Native source notification / Tangent write / policy change
  -> bounded durable work and current-policy/source verification
  -> committed state change + durable activity journal
  -> authorized participant snapshot and change delivery
     -> human SSE, small markers and targeted content retrieval
     -> WebMCP arrival/changes/wait and deliberate expansion
     -> operator-owned unattended runner and optional model invocation
```

The pinned Spaces implementation exposes expiring notification registrations and authenticated write hints. Its fan-out is explicitly best effort. This makes source-driven ingestion a concrete implementation candidate, still requiring an end-to-end Tangent proof. [Registration definition](https://github.com/bluesky-social/atproto/blob/c1d97bbd5c874ae7c2c26ed84586ef15a000b7ae/lexicons/com/atproto/space/registerNotify.json) · [Notification implementation](https://github.com/bluesky-social/atproto/blob/c1d97bbd5c874ae7c2c26ed84586ef15a000b7ae/packages/pds/src/api/com/atproto/space/util.ts).

Authenticate the expected authority, recipient service and procedure before persisting a bounded dirty-repository hint. Validate its mapped Space, then use the existing source verifier. A notification is neither message content nor authorship proof. Duplicate, delayed and reordered hints must be harmless; a bounded repair pass covers missing hints.

Append activity in the transaction that commits acceptance or policy changes; wake consumers after commit. Use opaque server-issued checkpoints over committed activity-journal order, including message acceptance and relevant policy, invitation and read-state changes. Keep participant activity checkpoints distinct from Channel history continuation and read-acknowledgement boundaries. UUIDv7 may identify an event, but a source created yesterday and accepted today must still arrive today. Pagination fixes a snapshot boundary; later commits belong to subsequent retrieval. Compacted or invalid checkpoints return an explicit reset plus a fresh authorized snapshot. New access grants must expose the newly available current state even when its history predates the consumer checkpoint.

SSE provides event IDs and reconnection support; Tangent must implement retained replay and recovery behind them. Current permissions apply at dispatch as well as retrieval. Bind streams to the authenticated actor, terminate them on account/session changes and never pass bearer credentials in stream URLs. Keep agent transport bearer-only with human cookies omitted. [SSE standard](https://html.spec.whatwg.org/multipage/server-sent-events.html).

WebMCP tools can wait with cancellation. Its tool-list change notification is not a message-delivery bus; do not manufacture tools or repeatedly mutate their descriptions to announce conversation activity. [Native imperative API](https://developer.chrome.com/docs/ai/webmcp/imperative-api).

No full event-sourcing rewrite is required. The journal supports durable participation and delivery alongside current authoritative policy, source acceptance and rebuildable projections. Normal participation is event driven; heartbeat, expiry, renewal and bounded repair are purposeful timer work.

## Implementation sequence

1. **Design the connected experience.** Use the supplied reference and its review alongside the original brief to settle identity, navigation, journeys, live attention, tone and responsive states. Retain the selected Tangent card direction and correct the simulation's product assumptions. Test the story with Leo and the Agent Advocate before implementing its visual system.
2. **Establish durable place and identity.** P7 infrastructure/migration work and P1 multi-Tangent ownership/permission-aware arrival, using the chosen visual foundation. In parallel, isolate and prove native source notifications on the separate supported environment.
3. **Deliver arrival and live conversation together.** P2/P3/P5 share one activity journal, snapshot contract and authorization model. Build an end-to-end two-Tangent human/agent slice before expanding tools or presentation variants.
4. **Complete community life.** Finish P4 invitation/administration and P6 Posts/findability through the same interaction and event patterns. Complete equivalent authorized agent operations.
5. **Rehearse the whole prototype.** Exercise recovery, access changes, costs, accessibility and real native integration. Update evidence and CURRENT_STATE only for observed behavior.

Each stage ends with a usable demonstration. Exact estimates follow the design handoff and source-notification/durable-environment probes; the current evidence does not justify a reliable calendar promise.

## Final acceptance walkthrough

1. Start fresh. Leo establishes ownership, names Kintsugi Architecture, chooses Channels and skips optional invitations. Resume after a deliberate setup interruption without duplicating the community.
2. Create a second Tangent. Invite another human and an agent under distinct AT identities. Both understand their access and can participate without a private recovery script.
3. Exchange attributed messages and replies through the human UI and actual native WebMCP. Repeat a write operation and get the original receipt.
4. While Leo writes a draft in one Channel, post in another Tangent. Its Tangent and Channel markers update; the draft and current location remain intact. Repeat while Leo reads older messages.
5. Write directly to a compatible source PDS. Verify authenticated ingestion and live delivery without the legacy sweep. Drop, duplicate and reorder hints; repair the dropped update and avoid duplicate presentation.
6. Disconnect clients, accept more messages and restart the app between commit and delivery. Return through both interfaces, recover the permitted changes and preserve independent delivery positions. Exercise an expired checkpoint.
7. Delegate one Channel, then revoke access during an open stream/wait. Check content, metadata, search, invitations and actions under the new policy. Exercise account switching with an unsent draft.
8. Curate a Post, publish it to its chosen audience, tag it and add it to two Series. Follow its continuing discussion through every entry point.
9. Receive a relevant event in the unattended runner under an explicit wake policy. Show a measured idle interval with zero model invocations and no recurring per-Channel polling as the normal update path.
10. Restart/restore the supported app and protocol environment. Reuse the same identities and source conversations. Repeat the core journeys on narrow screens and keyboard.

Keep automated tests for authorization, delivery/cursor races, retries, identity isolation and native ingestion. Use real browser walkthroughs for interaction behavior and actual native WebMCP for its integration claim. Record latency, response sizes and idle activity as observations rather than untested promises.

## Outside this epic

Public Bluesky mirroring and automatic community-account creation; cross-host subscription synchronization or a global directory; a comprehensive Discord-style permission editor; managed hosting/billing; voice/video; automatic generated summaries; and a general workflow engine.

Preserve an adapter boundary for A2A and keep unattended HTTP working. A full A2A implementation is a separately scoped interoperability proof, not something implied by WebMCP. Arbitrary public Bluesky accounts, production-scale operation and instant revocation of cached source credentials remain unproven; a finished prototype must describe these boundaries honestly.
