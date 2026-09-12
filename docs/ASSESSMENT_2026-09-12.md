# Tangent realignment assessment — 12 September 2026

## Verdict

The implementation has advanced substantially. Tangent now has the mechanisms for a human and an agent to converse under persistent identities without requiring experimental Spaces storage. The local connector is real, rather than a proposed companion product. Preserve that work.

The experience has not advanced at the same pace. New identity, profile, history, and connection features expose implementation details and follow competing navigation conventions. This is primarily an integration and interaction-design problem, not a reason to replace the architecture or visual identity.

It would also be inaccurate to attribute every rough edge to the latest iterations: the main CSS, editorial page styles, onboarding styles, and ASCII scene implementation are unchanged from the handoff. Several limitations were inherited; newer functionality has made them more visible.

## Scope and evidence

- Baseline: `81ec80f`, the September 10 delivery/handoff. Reviewed the 46 subsequent commits through `e8e895d`, their cumulative changes, current decisions, and relevant implementation paths. File-count/line-count growth is inflated by moving the web project into `src/server/web`.
- Live: started Docker Desktop and inspected the existing healthy container at port 5220. Reviewed anonymous arrival and the legacy agent page, then Leo's signed-in Chrome session: BBS home, Tangent directory, populated Topic, participant profile, and profile-to-post navigation. Checked a 390 × 844 viewport and restored the browser viewport afterward.
- The running image predates unfinished working-tree edits. Live observations describe that image; source findings describe the checkout. No fresh build, test-suite run, OAuth enrollment, message submission, ownership change, or data wipe was performed for this assessment.
- Connector operator UX was inspected in source, not exercised in a new running operator session. Earlier conversation messages and checked-in walkthroughs are historical evidence, not fresh end-to-end verification.

## What changed and should be retained

| Area | Delivered direction and implementation | Assessment |
|---|---|---|
| Participation architecture | Rust local MCP connector, server experience API, shared CLI/MCP operation path, compact contextual rendering and participant digests | Strong fit for inexpensive agent participation. ADR 0005 supersedes the older direct-inbound-first experience. |
| Storage | Local storage defaults, with Spaces available as a separate Topic storage mode; operation-keyed atomic local writes | Important usability breakthrough. AT identity no longer implies that a public account must support native Spaces to converse. This is an accepted change, not a regression against the earlier native-only experiment. |
| Identity | GUIDv7 Participant spine plus identity records; service-proof enrollment; bound/unbound connector identities and per-server sessions | Correct separation of a participant from a particular credential or provider. Display integration is unfinished. |
| Connector arrival | On-demand Connect, operator page and tray, OAuth browser binding with PAR/PKCE/DPoP, refresh and resource-nonce handling, pending connection completion | Substantive mechanism. The human handoff and the product's primary navigation still need to meet it. |
| Conversation semantics | Verbatim text with facets, mention/group resolution, edit/delete history in a Koan changelog partition, change classification | Useful primitives for both human reading and agent attention. Keep semantic metrics out of the default reading experience. |
| Session behavior | Token-only browser sessions, server-pushed identity-change events and ordinary forbidden-access routing | Reduces the earlier identity-mismatch ceremony. Current UI still contains legacy connection vocabulary. |
| Operations | Web/MCP source separation, lifecycle script updates, fixed 5220/5219 defaults, generated ONNX configuration | Helpful operational structure. The connector remains a separate host process and has its own state/lifetime. |
| Decisions, not completed features | Deployment postures/admission defaults, Discord-like bylines, proof-minted handle backfill, one Message save hook for facet minting | Track these as outstanding. Documentation is not implementation evidence. |

The permissive local POC posture and cookie-like session storage are explicit user choices. This review does not propose reintroducing page tokens, allowlist gates, or a production-security program as prerequisites.

## Findings in priority order

### 1. Identity continuity works underneath but fails on screen

**Observed.** In “Are AIs counscious?”, the agent's byline is `01a093dda3c8740caa2469eccca106a4`, its avatar is `0`, and Leo's mention displays the same internal ID. The participant profile substitutes a DID for a name and repeats it below. The user cannot comfortably recognize whom they are talking to.

`wwwroot/rooms.js:384–390` deliberately falls back to the Participant ID and creates a letter avatar. A profile-decoration service already exists in `Participants/ParticipantProfiles.cs`; the conversation needs to consume an appropriate bounded projection of that information. The September 12 handle/backfill and richer-byline decisions remain unfinished.

**Next:** one participant presentation shared by post bylines, mentions, profile cards, and connector identity confirmation. Display name first, handle as secondary identity, avatar when available, a useful fallback when unavailable. Internal keys belong in optional details. Treat agent/human classification as a declaration, not a conclusion from the absence of a label.

### 2. New profile navigation is internally inconsistent

**Observed and source-confirmed.** The profile page shows the server BBS hero and owner-level server controls above its own content. `wwwroot/pages.js:5–11` defaults `/u/...` to `home`, while `wwwroot/profile.js` independently renders the profile. Two scripts own different interpretations of the same route.

The profile API emits `/tangents/{key}/posts/{id}/` links (`Experience/ExperienceService.Profile.cs:66`), while `Web/PagesController.cs:15–16` serves `/t/{tangent}/{post}`. Clicking “Open in Topic” failed in Chrome with `ERR_BLOCKED_BY_CLIENT`; no browser block was bypassed. The route mismatch is independently evident in source; a specific HTTP status was not established.

**Next:** one route interpretation and canonical URL builder, including profiles and post permalinks. Profile actions should open the actual permitted operation, not merely redirect to an approximate page. Keep the existing page-shell approach; this does not require a frontend-framework migration.

### 3. The conversation is not yet a comfortable reading surface

**Observed.** The large hero, motto, owner controls, identity strip, and Topic details precede the conversation. Each message carries a timestamp, permalink, source disclosure, and separate action buttons. At 390 pixels wide, the document measured **463 pixels wide** and text/byline content clipped horizontally. `rooms.css:120–123` gives the byline a non-wrapping flex layout; long identity labels make that particularly visible.

The architectural promise is “shared conversation”; the visual hierarchy still gives much of its attention to setup and machinery. Some of this predates the new work.

**Next:** retain editorial heroes for arrival and discovery, but give an active conversation a compact Topic header and more reading space. Show recognizable authors, restrained timestamps, reply excerpts that lead to their source, and a clear composer. Put secondary controls in accessible contextual menus. Fix wrapping at the layout level as well as repairing labels. Keep permission availability explicit through discoverable actions and plain explanations.

### 4. The primary agent entry point leads to the superseded flow

**Observed.** “Connect an agent” in the main header opens `/agent.html`, which asks for a Participant credential file and advertises legacy WebMCP tools. That is not the accepted local-connector-first journey.

**Source review.** The new operator UI starts with a live activity feed and tables of local IDs, bindings, enrollments, sessions, and status. Copy includes “cookie-jar posture”, “hub/CLI”, and “Cascade-delete”. An empty-state message says to create an identity “above” although the controls are below. This reads like an operator diagnostic tool rather than a welcoming place to manage companions.

**Next:** the site's agent link explains/launches the current connector route. The human sees the companion, destination, necessary sign-in, and a clear connected result. The model receives one recommended starting action (`Connect`) and an unambiguous continuation. Keep advanced SelectCompanion/Arrive operations if needed, but do not make the small model choose between competing tutorials. Keep technical diagnostics accessible under details.

### 5. Some UI feedback is technically misleading

**Source-confirmed.** `wwwroot/history.js:113–145` collapses request failures, forbidden/empty results, and genuinely absent history into “No recorded revisions.” It marks the disclosure as loaded, so reopening does not retry. Network failure should not imply that history never existed.

History also exposes raw `surface` percentages and `semantic` distance values by default. These are useful machinery, but do not tell an ordinary reader what changed.

**Next:** distinct empty, unavailable, and restricted states; retry where appropriate. Lead with the edited content, author, time, and meaningful mention changes. Keep classification details available for investigation. Audit other connection/readiness copy for the same principle, especially Local Topics still accompanied by “Room access settings”.

### 6. Live behavior needs a precise product promise

**Source-confirmed.** Web activity and identity changes use push events. The connector operator feed also uses SSE. However, `src/server/mcp/src/adapters/poller.rs` starts a polling thread per auto-check enrollment and uses backoff. The connector does not autonomously invoke a model; attention is surfaced through subsequent tool responses.

This is a workable POC tradeoff, not a newly discovered failure. It does mean that “all event-driven” and “an agent wakes immediately on every reply” are not current capabilities.

**Next:** define live conversation updates, catch-up checkpoints, read acknowledgement, and host/model wake behavior separately. Preserve the reader's position when updates arrive. If idle polling becomes material, replace or complement it with a server activity subscription rather than adding more schedulers.

### 7. The connector has a single-client process boundary

**Source-confirmed limitation, not live-tested.** `src/server/mcp/src/main.rs:113–174` gives long-running verbs an exclusive data-directory lock and states that one process serves one MCP client. `serve` and standalone `operator` share the same default port and cannot run independently against the same state at once. A second host that spawns another default stdio connector therefore cannot simply share the first one's identities.

This matters to the eventual “several agents, one personal connector” story. For the immediate POC, document and present the one-host arrangement clearly. If concurrent hosts become necessary, decide on one resident service with thin client attachments; do not remove the lock or use `--force` as the normal experience.

## Finish the current implementation before another broad delegation

1. **Unfinished local edit:** `ParticipantDirectory` now takes `IAtprotoHandleSource`; the new `AtprotoHandleResolver.cs` is untracked, and no DI registration for that interface was found. The parameter is not yet used. This is a working-tree integration blocker to resolve before rebuilding, not a committed regression or a diagnosis of the currently running container.
2. **Facet boundary:** `ConversationService.PostChanges.cs:243–244` still uses a per-service helper, and explicitly empty facets trigger detection. Message writes still assign facets at call sites. Implement the latest accepted save-hook rule once, preserving explicit empty packages, tombstones, snapshot-era facets, and idempotency semantics. Do not create another parallel parser.
3. **Profile visibility review:** posts are filtered through readable Topics, but the profile's role loop includes all Tangent memberships without an equivalent visibility check (`ExperienceService.Profile.cs:41–48`). Before exposing profiles beyond this trusted POC, apply the intended shared-space visibility rule to role labels too. This is a source finding, not a demonstrated private-data exposure in this review.
4. **Status documentation:** `docs/CURRENT_STATE.md` still leads with Wave 1, describes Wave 2 as upcoming, and says twelve tools/platform credential storage. The connector catalog has fourteen tools and its README describes local session-state storage. Give the next model one short implemented/unfinished matrix, with old handoffs clearly treated as snapshots.

The Rust application hub has also grown to roughly 2,500 lines. Keep the monolith and its shared domain decisions. Extract cohesive use-case code only where the next work benefits; do not launch an architectural rewrite to reduce a line count.

## Proposed next epic: a place worth returning to

**Outcome:** Leo and one bound companion can recognize each other, arrive at meaningful activity, follow a conversation, reply, and continue from either interface without interpreting storage or authentication internals.

Sequence:

1. **Continuity first:** finish identity labels/profile projection, canonical navigation, and facet integration; correct mobile overflow. These are dependencies of the experience, not a separate infrastructure epic.
2. **One human conversation journey:** returning BBS with useful catch-up, preserved TCG Tangent cards, editorial discovery, compact active Topic header, readable posts and reply context, calm live indicators, sensible contextual controls. The same layout serves members and administrators, with permission-driven actions.
3. **One companion arrival journey:** align the website entry, local companion management, OAuth handoff, connected state, and compact MCP continuation. Make the identity and destination unmistakable. Keep model-visible responses bounded and actionable.

Keep the ASCII atmospheres and mouse spotlight subtle. Retain the Gposingway-inspired discovery language and the cards. Do not add more decoration to compensate for unclear information hierarchy.

**Lean acceptance:** a single human/agent conversation walkthrough; direct profile/permalink navigation; one narrow viewport; one identity switch; one failed request with truthful recovery; and a reconnect/read-checkpoint check. Add targeted automated coverage only for changed domain/authentication invariants. A broad new test campaign is not the next deliverable.

This assessment adds documentation only. Application code and the unfinished identity edits were left untouched.
