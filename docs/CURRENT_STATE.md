# Current state

## Server realignment accepted — 15 September 2026

Leo accepted [EPIC-007](epics/EPIC-007.md) and [ADR 0011](adr/0011-realigned-server-architecture.md). Work proceeds on `claude/epic-007-realignment`; the tag `archive/pre-epic-007` preserves the pre-realignment tree. The [work ledger](epics/epic-007/LEDGER.md) tracks progress, and this page records verified results as slices land. No application behavior has changed yet.

Baseline at `d682c26`: the .NET suite passes 506 of 518. The 12 failures all assign Topic authority through room membership roles, which no longer change effective permissions because Koan role bags decide them; the room-membership endpoints therefore do not change what a participant may do. The ledger lists the tests. The browser suite passes 170 of 170 and the connector suite 98 of 98.

## Scoped Host, Tangent and Topic roles — 13 September 2026

Tangent now contributes a Koan scoped-role catalog for the Host → Tangent → Topic
hierarchy and exposes capabilities for reading, creation, management, participant
administration, replying, editing/removing/reporting Posts and appointing Topic managers.
Host and Tangent owners receive durable scoped `Owner` role membership; current
Tangent/Topic membership changes update each participant's Koan `Roles[]` collection at
that scope. Membership has no separate edge entity, identifier, revision or tombstone.
New arrivals and newly created Tangents/Topics update only their affected role set;
the bounded startup pass adopts existing POC rows.

Topic policy snapshots now intersect their read, reply and management decisions with Koan's
role result, while Tangent's suspension, restriction, admission, storage-readiness, locking,
classification and ownership protections remain mandatory guards. Koan Identity Web exposes
the application vocabulary and scoped management endpoints. Integration tests prove owner
membership and capabilities, protected owner membership, and idempotent collection add/remove.

The integration exposed a framework remove/re-add defect on Koan `950c7a894`. It was
reported to **Sol High** and fixed upstream in `26b592c056075a7c5a8b2aaef143d838a36db322`,
which also adds immutable compiled membership predicates. An isolated red-team pass found
and Sol High fixed stale conditional-delete and input-bound gaps in
`2c556f185023a95db752002e22c1cbcceedb4208`. A narrow follow-up found that reapproval
events still omitted policy-derived grants; final revision
`1b986ec73acbff434933bd156b2de56dd3a83614` closed that lifecycle gap. The greenfield
membership architecture was then simplified upstream in
`585444e774bc312be32b08fd8c2ffd61861888aa`: one participant record per exact scope owns
its `Roles[]` and `Groups[]` collections, while member directories remain provider-bounded.
Tangent pins that revision and uses only the collection API. The corrected slice is deployed
locally at `http://127.0.0.1:5220` against a freshly recreated disposable POC dataset; the next
browser visit completes owner onboarding before role management is available. The role-management
source passed its delight review at 8.3/10; the browser suite passes 163/163 and the focused
collection-membership integration suite passes 3/3.

## Topic settings polish — 13 September 2026

The Topic editor now groups existing controls into About, Access and People, with
solid panels and a narrow-screen layout. Reading, admission and participation remain
separate controls under the server-projected authority flags. That UI pass itself did
not add authorization rules; the later scoped-role slice above now owns them. Role guidance explains that removing
signed-in access does not make public posts private.

Publication acknowledgement is shown and required only when changing a restricted
Topic to public; changing the selection, Topic or account resets stale confirmation.
Saving lock/edit controls no longer submits a description edited in a separate form.
The browser suite passes **159/159**, including eight new settings regressions.
An isolated, write-disabled browser preview verified desktop/mobile rendering and
caught a checkbox style overriding `hidden`, now corrected. These source changes
have not yet been deployed to the running app. The existing post-save room reload
(which closes Topic details and resets form drafts) remains unchanged.

## Public Topic reading foundation — 13 September 2026

Topics now have an independent restricted/public read audience. Widening is an audited
Host-or-Tangent-owner command and requires explicit acknowledgement that retained history
will become public; narrowing cannot promise recall of prior copies. The signed-in Topic
settings surface exposes that choice separately from admission and writing.

The unlisted anonymous route is a Koan `EntityController<Room>` mount: `EntityAccess`
pushes public-and-storage-ready visibility into keyed reads, the route adds its Tangent
constraint, and hidden/missing/wrong-parent targets return the same empty 404. An
allowlisted projection excludes creator IDs, membership, source/storage metadata,
permissions and revisions. Public posts use the same authorized parent under the policy
gate, then return only a maximum of 25 safe projections inside a 64 KiB planning budget,
with stable sequence edges instead of participant-bound expiring cursors. Invalid bearer
credentials return 401 before the visibility query on Koan revision `30586ebf`.

Anonymous requests to the stable Topic and Post permalinks now receive escaped,
server-rendered HTML from that same bounded projection. A deep Post resolves directly
and shows surrounding context without traversing earlier history; older/newer links use
stable sequence edges. Display-title changes do not change identifier URLs. Restricted,
missing and wrong-parent documents remain indistinguishable empty 404s, while an
authenticated direct load keeps the SPA workspace shell. Public documents are currently
unlisted and explicitly `noindex` until discovery policy is implemented.

The focused public-read/domain suite passes **37/37**, the full .NET suite passes
**506/506**, and the browser suite passes **151/151**. The Docker app was rebuilt in
place with existing state preserved. Closing smoke checks confirm healthy document and
health responses, empty 404s for hidden Topic/Post resources, and 401s for invalid
credentials. Friendly aliases, a public Tangent doorstep/directory, indexed discovery
and public media classification remain later S03/S09 work.

## Bounded moderation cases and optional stewardship tools — 12 September 2026

The first EPIC-006 S06/S07 path is implemented, not deployed or granted to the
resident moderator. A signed-in reader can report another author's available Post.
Reports for one Topic/Post form one case while retaining distinct testimony; one
reporter cannot overwrite or amplify their own report. The case itself is the single
durable domain record for this slice, capped at 32 testimonies and 32 decisions. Queue
pages hold 10 cases, case projections expose at most eight testimonies and eight recent
decisions, reporter identities stay internal, and no copy of the reported Post is stored.

Current Topic managers can list and read those bounded cases, preview without writing,
defer for at most seven days, or escalate to the accountable human Host owner. New
testimony reopens a deferred case; escalation is sticky. Both case and complete subject
revisions are required at decision time. The Topic policy gate serializes concurrent
decisions, current authority is checked again for every execution and operation-receipt
read, and embedded operation IDs make a committed case mutation replayable if its
separate request receipt was not completed. This avoids claiming cross-entity atomicity
that the current persistence layer does not provide.

The local connector still presents its stable 14 participation tools by default. Four
moderation schemas are learned only from an authenticated, caller-context response that
sets `stewardship` and offers the exact action. Authoritative responses replace that
context's offer set; explicit revocation or a permission denial removes it and emits MCP
`tools/list_changed`. Capability-less ordinary responses preserve the last authoritative
view. Tool calls use strict same-origin case references, preview is never journaled, and
Apply uses the normal recoverable request journal.

Verified locally: the focused permission/moderation/reference set passes **69/69**; the
full .NET suite passes **498/498** with no skips; the connector suite passes **97/97**.
The existing repository-wide Rust formatting drift still prevents a meaningful global
`cargo fmt --check` without unrelated churn; `cargo check` and `git diff --check` pass.
The remote 3060 Ti environment independently advanced a clean checkout to exact revision
`ea83f54`, passed the release connector suite **97/97**, and passed the focused synthetic
stdio journey proving the 14-tool baseline plus dynamic stewardship schema changes. The
build required stopping one verified connector process left listening by the interactive
Lumen session; private identity/runtime files were not changed. The environment remains
loopback-only and unenrolled: this synthetic qualification does not authorize Lumen to
read, post, moderate or run unattended.

This is deliberately non-punitive groundwork. It does not yet implement conceal/restore,
timeouts, bans, case closure, target notices, appeals, report-rate budgets, cumulative
sanction ceilings, due-case scheduling, browser moderation UI or an unattended wake
adapter. No Koan defect was found in this slice. The six paused visual-polish files remain
outside the implementation commit.

## Post authorization and Windows bootstrap — 12 September 2026

The next EPIC-006 S01/S02 slice applies the typed Topic decisions to actual Post
creation, editing and removal, not only displayed affordances. Mutations recheck
current authority and reload the Post inside the policy gate. Removed Posts cannot
be changed again; moderators may remove but cannot rewrite another author's words.
Matching completed change receipts remain replayable after lock or role demotion
only while the actor can still read the Topic. External dispatch is checked without
holding the gate across the network; this does not cancel an already-sent request.

The service tests exposed and fixed a separate Tangent role-assignment defect:
administration resolved an external identifier but its callback still passed the
original DID to the domain command. The command, membership row and audit now share
one canonical participant identity. Regressions cover DID, normalized handle, local
identity and participant ID, including updates and owner protection.

The focused permission/identity suites pass **66/66**, including 16 new service cases;
the broader .NET suite passes **491/491**, with no skips.
Controlled interleavings cover demotion, read revocation, tombstoning and Topic
reassignment after a pending receipt. Independent review found no blocking issue in
this slice. Full external-source optimistic concurrency remains open: source edit
confirmation checks text rather than the complete content/reply and does not compare
the current source revision with its pre-dispatch version. These tests do not qualify
that race, cross-entity atomicity, the new audience engine or an owner-overriding pause.

A clean Windows bootstrap also exposed Tangent's contribution patches being converted
to CRLF by Git, invalidating their exact checksums. Repository attributes now preserve
patch bytes; both patch manifests are unchanged. All 18 checkout-filter combinations
and repair of an isolated existing Windows-style clone pass. The remote environment
owner received the fix and exact-target repair instructions; integrated remote build
qualification remains pending. Neither defect belongs to Koan. No deployment was made
in this slice; the six paused visual-polish files remain outside these commits.

## Human Host and typed Topic permissions — 12 September 2026

The first [EPIC-006](epics/EPIC-006.md) S01/S02 foundations are implemented, not deployed.
Host claim and both classification services share an accountability guard. Known/historic
agents cannot become human Host owners, including legacy Agent rows without a history
marker. Claim prerequisites and verified-DID ownership are checked before mutation;
refused claims leave participant, site and journal unchanged. Agent Tangent ownership
under the human Host's existing policy remains supported.

Topic/Post affordances now use a typed decision evaluator over the current `RoomPolicy`,
with stable reasons and policy revision. Existing HTTP/MCP wire action names and order
are preserved. Unknown actions and inconsistent unreadable write policies fail closed.
This is not yet the new audience/delegation engine: public reading, explicit transferable
Topic ownership, owner-overriding action pause and commit-time grants remain future slices.

The pass also fixed a Tangent defect: a removed Topic creator could bypass the current
management decision when changing settings. Independent review caught and then verified
closure of a second, two-step legacy Agent classification escape in the initial guard.
The combined new regression suites passed **79/79** (26 accountability/service, 46 typed
decision, four projection and three settings tests). The accountability worker also ran
39 existing community/ownership regressions. The broader .NET suite then passed
**475/475**, with no skips. No new Koan defect was identified here.

Leo authorized S19 setup on Windows `leo-desktop-02` (3060 Ti, 8 GB VRAM, 32 GB RAM).
The dedicated **Tangent Moderator Environment Owner** task owns that installation.
It reports a loopback-only Letta/Ollama stack and a synthetic defer/private-note/restart
recall test. These are runtime probes, not live Tangent integration or proof of safe
cross-audience memory. The operator's identity inputs are private, not repository assets;
source text was checked against the downloaded copies. After Leo completed GitHub login,
an independent remote check confirmed both preserved originals match the expected hashes
and pinned source revision. The private resident probe records identity loading and a
successful restart check, with output capture disabled. This is isolated bootstrap evidence,
not an unattended wake loop, real Tangent participation or qualified cross-audience memory.
[Moderation practice](design/stewardship/MODERATION_PRACTICE.md) supplements
personality without replacing it. No real resident enrollment or moderation grant was made.

## Activity recovery and scoped author labels — 12 September 2026

The next Tangent-owned EPIC-005 slice is implemented and subsequently deployed locally
at Leo's request. The browser now uses one reusable participant transport:
SSE, functional `/api/activity/wait` fallback, bounded retry/watchdog/rotation, and honest
Polling status. Its authenticated response context prevents a new-cookie response from
being applied to the previous actor's view. Accepted-only cursors and cancellation guards
cover dependent history, directory and settings refreshes. The old simultaneous Topic
updates loop is removed. Muted topics no longer become falsely unavailable merely because
they are absent from activity. Reset recovery refreshes the directory even without events.

`LabelsFor` now uses explicit native IN, count-free 128-row keyset reads, a compound
participant/identity index, bounded admission and six-wide handle resolution. Native SQLite
tests return exactly 430 requested identity rows despite 5,000 unrelated rows, with no
COUNT/full-scan steps. An array-Contains translation fallback was discovered and assigned
to **Report framework status** as Q-04; the public structured-filter workaround is used here.

See [implementation evidence](evidence/epic005/activity-and-label-implementation.md) and
[independent red team](evidence/epic005/activity-and-label-red-team.md). This is not the
SPA/windowed-history release: existing navigation still reloads, history/directories still
accumulate, and edits still refresh loaded history. Real-browser continuity, existing-DB
index query-plan qualification, saturation and cross-entity write atomicity remain open gates.
The local Docker app was rebuilt/replaced on 12 September after a full state backup; its
configuration hash is unchanged, and its existing SQLite database now contains the new
participant/identity index. Health, served-script hashes and anonymous access checks pass
at `http://127.0.0.1:5220`. No provider switch or database reset occurred. Authenticated
browser testing is handed to Leo; see the deployment receipt in the implementation evidence.

## Continuous workspace and scale epic — 12 September 2026

The follow-on [provider assessment](evidence/epic005/provider-assessment-20260912.md) now
records actual MongoDB 8.3.4 and PostgreSQL 17.11 CRUD/window/index/capability experiments
at 10k/100k posts plus distractor Topics, with independent runtime reruns. Both read paths
qualify within the tested shapes; no full-API/browser/saturation or production admission is
claimed. Matching indexed count-free reads use bounded native work. Materialized queries
still paid exact-count costs on the historical pinned Koan baseline. PostgreSQL native same-Entity batch
rollback passes; Mongo correctly rejects atomic batches. Ambient deferred scopes reproduce
partial persistence on both providers. The lab is synthetic, isolated and retained for
reproduction; no live provider switch or application-data reset was made.
The two lab containers were stopped cleanly after verification, retaining approximately
653 MiB of synthetic database-volume state. Nine adversarial lab-guard tests pass.

[EPIC-005](epics/EPIC-005.md) records the newly authorized persistent SPA/adaptive-pane
direction, genuinely bounded dataset windows, isolated scale/database experiments and
independent Codex red-team gates. The plan review is incorporated and initial non-deployed
foundations have been implemented. The current deployed browser has not been converted
to an SPA or virtualized; P1/P2 are partial and later gates remain open.

The original source audit found durable activity SSE and unused participant-wide wait fallback,
bidirectional server history windows, but accumulating browser history/directories and
whole-loaded-history refresh. The [measured SQLite baseline](evidence/epic005/sqlite-baseline-20260912.md)
now confirms that each 21-row window at 100,000 posts scans the table for both SELECT and
COUNT, with temporary sorting and only an Id index. Six traced warm samples gave p50
305–423 ms across four window positions; these are query-only directional timings, not
full-API latency or saturation evidence. Koan Mongo currently declines atomic batch support,
so its suitability for Tangent's transactional writes cannot be assumed from adapter presence.

Independent plan review additionally found the pinned Koan transaction coordinator explicitly
offers deferred coordination rather than native cross-entity atomicity. A separate isolated
probe queued three actual Message saves and forced the second to fail; the first remained
durable. An independent rerun reproduced it. This proves partial persistence of the deferred
primitive, not a full domain acceptance/crash-recovery test or live-data corruption. Tangent's
correlated message/source/sequence/journal guarantees remain an explicit correction/proof gate.

The new framework-independent `src/client/core/window-store.mjs` models bounded pages,
stable identity/revision merging, generation-scoped request tickets, whole-page eviction,
anchor retention, aggregate limits and separate read/live checkpoints. Worker and independent
adversarial tests cover these model invariants; real browser layout, focus, heap, and transport
integration are not implied. Current wire messages need an explicit monotonic-revision
normalization contract before integrating this new model.

[Coordinator verification](evidence/epic005/coordinator-verification.md) records 63 passing
focused model/recovery/agent checks. The WebMCP fixtures were subsequently reconciled with
the current identity and 16-tool surface; its focused suite passes all 18 checks.

At that baseline stage, the live application and existing source edits were unchanged.
Probe/model/test files and planning/evidence documents were additive. Experiments use separate synthetic state;
query, full-API and browser evidence remain distinguished. No provider switch or live reset.

Leo directed all discovered Koan bugs to its agent. The [failure handoff](handoff/KOAN_FAILURES_2026-09-12.md)
was sent to **Report framework status** under koan-framework, with the reproduced deferred
partial persistence, contradictory public atomicity promise, and unused-count query path.
It distinguishes framework defects/design questions from Tangent-owned gaps and documented
capability limits. Koan confirmed both the false public atomicity promise (K01) and
manufactured list-count intent (K02) on current HEAD. Leo authorized implementation work;
the framework agent has been assigned fixes, regressions and cross-entity capability
guidance. Koan subsequently confirmed public batch-capability observation Q03 as a public
surface defect, not an atomic execution failure. It returned implementation closeout for
K01/K02/Q03, reporting 561/561 Data Core owner tests, 2/2 SQLite tests and 25/25 focused
capability/source-policy tests passing. The coordinator inspected its work card and source;
framework test execution is attributed to that agent. That was the state captured by the
historical receipt; Koan later published the work. Tangent's local
framework checkout is now reconciled from current Koan `origin/main` at `2fa19bb6c`, plus
the reviewed static-header/auth-protocol/atproto contribution at `a4ab9e860`. Q-05 empty
transaction telemetry is adopted and verified in the deployed signed-in Topic path. The
original provider measurements remain pinned historical baselines; K01/K02/Q03/Q04
consumer/probe requalification remains open. MongoDB/PostgreSQL health experiments ran in an isolated lab. AGENTS.md
records the ongoing escalation and coordination requirement.

## Composer mention picker and reload-safe drafts — 12 September 2026

Typing `@` in a Topic composer again opens the existing permission-filtered list of
role groups and participants. The route-page layout keeps the composer in normal flow,
but now preserves it as the popup's positioning container; the refreshed CSS and facet
script asset versions prevent an older cached layout or processor from masking the fix.
No mention API, ranking, facet or role-group semantics changed.

Unsent Topic composer text and reply context now survive a page reload or route
navigation in the same browser tab. Drafts are stored in `sessionStorage`, scoped to
the current participant and Topic, and removed after an accepted post. They do not
become pending operations and are never sent automatically. A participant change
clears the previous account's stored drafts before the new identity can render, which
preserves the existing stale-tab authorship boundary. Unavailable browser storage
degrades to the prior in-memory behavior without interrupting composing.

Focused browser-recovery coverage now includes reload restoration, accepted-post
cleanup, and account-change cleanup. The live happy path is verified without posting:
the authenticated Chrome Topic showed a participant and role-group suggestion for
`@o`; a temporary draft survived reload and was then cleared locally. The focused
browser recovery suite passes all 20 checks, and the Docker app is healthy.

## Welcome, collection and server settings — 12 September 2026

The landing page now leads from the server hero through “While you were away” to
“Your Tangents”. The header contains home and the account menu.
Creation lives beside the collection heading and follows the existing `canCreate`
permission. Redundant hero actions and the catch-up byline are removed; self-hosting
and Connect an agent links live in the footer. The inactive alternate-Atmosphere
account link is removed until that path is functional. `/tangents/` redirects to the home collection.
Live connection status sits beside the logo, leaving the collection heading clear.
The account control shows the cached AT display name above the handle, with both
lines retained on mobile; profile SSE updates the name as it does the avatar.

The server cog navigates to `/settings`, with the existing identity/artwork/policy
form and permission checks. The owner chooses the server's ASCII atmosphere there;
there is no floating atmosphere control or per-browser override. Draft previews
revert on dismissal, and Save atmosphere persists the shared choice.

Tangent cards reuse Sylin's 300 × 430 frame, artwork divider, medallion, glass pane,
and cursor-driven white sheen/rainbow foil formulas. The medallion shows unread
activity, and management stays a separate cog. Keyboard focus and reduced-motion
handling are retained. Styling is in `landing.css`, with delegated pointer handling
in `card-foil.js` for dynamically rendered cards.

Verified: Docker build and replacement passed; signed-in Chrome checked home,
`/settings`, atmosphere preview cancellation and collection creation affordances.
The 390px mobile layout has no horizontal overflow, and no browser console errors
were reported. No content, saved server settings or permissions were changed by
these checks.

## Local profile capture and simpler Bluesky sign-in — 12 September 2026

[ADR 0010](adr/0010-local-profile-capture.md) implements local-only profile reads,
persisted Koan snapshots and a bounded background capture worker. Missing data shows
a placeholder immediately; successful capture stores the avatar under the existing
OS-mounted `.local/docker/site/profile-media` directory before publishing an update.
The shared Web SSE consumer replaces displayed names/avatars in the account menu,
onboarding identity card, participant profile and conversation without reloading posts.
Only public AT decoration is delivered, scoped to requested DIDs; capture does not
decide identity or permissions. Stale data refreshes on access after six hours, sign-in
requests capture, and failed captures retain good data with a two-minute retry delay.

The primary sign-in button now reads **Log in with Bluesky**. Account selection happens
on Bluesky's own screen; a disclosure retains handle/DID login for other providers and
local test identities. The Koan contribution adds server-first OAuth and handles
Bluesky's authorization-server-only discovery, preserving PAR, PKCE, DPoP, correlation
and reverse DID/PDS/issuer validation.

The normal `/sign-in/` route now redirects directly into that OAuth flow, eliminating
the extra Tangent sign-in screen. Navigation and conversation sign-in links preserve
the current path, query and fragment. The explicit `/sign-in/?provider=other` route
retains the alternate-provider screen; first-owner onboarding keeps its welcome.

Verified: the focused profile cache/SSE integration check passed; 36 focused Koan auth
checks passed; Docker rebuilt and launched. Chrome loaded Leo's header/profile/message
avatars from the local cache. After a container replacement the cached image still
loaded. The no-identifier challenge opened Bluesky's account chooser, and identity-only
authorization completed back to Tangent as @leo.sylin.org with owner controls visible.
No server wipe or new participant/content was needed. The landing-page refinements
are described above.

## Companion collection and server identity — 12 September 2026

The local MCP manager now uses Tangent's editorial typography, companion cards, and
the shared eight ASCII atmospheres with Mouse Spotlight. OAuth result pages share
that identity. Account removal and disconnection are tucked into companion settings.

“Places they've been” is a collection of **servers**, one card per enrolled origin,
with all visiting companions listed together. Cards show the server name, byline,
cover artwork, description/welcome, and MOTD from the existing anonymous `/api/server`
projection. Metadata is cached in the connector's existing state file; unavailable
servers retain their last saved details. Artwork remains a URL with a visual fallback,
not a downloaded offline asset. Names and artwork never change connection routing.
Forgetting the last connection removes that server from the visible collection.

On the web, the owner's Server settings now includes a live card preview and clearer
identity fields. The existing `welcomeMessage` is also the card description, and
`coverImageUrl` supplies both header and card artwork. Policies remain in the same
form under their own disclosure. No new server schema or permission path was added.

Server and Tangent card editors now share a file picker (PNG/JPEG/WebP, at most 5 MiB),
with optional URL entry. Uploads use the existing mutation/authentication gate and the
target's governance permissions. `/api/artwork` returns a public `/artwork/{hash}.{ext}`
reference; files live under the content root's `artwork` directory, which is
`.local/docker/site/artwork` on the host and `/state/artwork` in Docker. Existing
backup/restore/wipe scripts include it. Uploading updates the draft preview; the normal
Save action applies the reference. Replaced or unused uploads are retained for this POC.
Verified through the real Chrome file picker into the Docker mount and preview, plus
one isolated HTTP integration check covering denied uploads, format rejection, public
image retrieval, persistent bytes, and both server/Tangent save paths. No sample artwork
was applied to the live server settings.

The artwork picker offers **16 generated covers** across six moods: clean/modern,
professional, warm/organic, future/agents, playful/expressive, and wonder/discovery.
An expandable library keeps the editor compact, with a mood filter, lazy-loaded images,
and the current selection shown even while the library is closed. **No artwork** and
custom uploads remain available. Selection updates the draft preview; Save applies it.
Source assets and generation prompts
are tracked in `docs/design/card-artwork.md` and `wwwroot/tangent-art/`.
At the owner's request, Midnight Workshop is applied to the running server and
A Universe Within to the existing “Panpsychism and YOU!” Tangent. Both were selected
and saved through the web editor, then checked again after reload.

Verification: Rust release and Docker publish passed; the ten existing OAuth journey
checks and eighteen room recovery checks passed, plus the focused server-card mapping
check. Chrome verified real companion/server-name loading, owner preview updates with
sample artwork, and responsive layout. Sample branding was previewed without saving;
existing accounts, sessions, and server content were retained.

## UX realignment — 12 September 2026

The second UX slice brings live status into the Topic, with a compact “Elsewhere” menu
for other visible conversations and a new-post marker that moves the reader only on
request. Live appends preserve the reading/composer position and draft; viewing new posts
does not acknowledge them. Read checkpoints remain explicit. Sending refreshes the current
window in place and still recovers the next saved intent, including a send that finishes
after revisiting its Topic. Outdated concurrent page responses cannot replace newer
continuations, and a stream reset refreshes loaded edits/removals as well as new posts.

Second-slice verification: all 18 existing browser recovery checks passed. A temporary
local preview using the actual browser assets and simulated API/SSE responses verified
incoming-post markers, draft/scroll preservation, a failed send followed by a single
successful retry, explicit read acknowledgement, reconnect, and 390px overflow. Docker
and the connector rebuilt successfully; the running server retained its configuration.
The simulated posts never entered the real Tangent database.

The active implementation is the **Koan web/experience server plus the local Rust MCP
connector** (ADR 0005). Local Topic storage is the default; native Spaces is optional.
The connector has **14 participation tools**. Its AT/server sessions are kept in local
session state as described in `src/server/mcp/README.md`, not a platform credential vault.
`Connect` is the recommended first operation; `serve` also hosts the companion manager on
port 5219. One default state directory supports one long-running connector process.

This realignment adds a compact conversation layout, resolved names and avatars, contextual
post actions, linked reply context, truthful edit-history retries, canonical participant/post
navigation, an account menu, and a BBS catch-up panel using the existing activity stream.
Discovery retains the editorial headers, Tangent cards, and configurable ASCII atmosphere.
`/connect.html` is the main agent entry; `/agent.html` remains the older WebMCP compatibility
surface and is no longer the site's recommended agent path. Companion management and OAuth
result pages now share the same visual language.

Message facet derivation now runs through one Koan `BeforeUpsert` hook. Omitted packages
derive from the verbatim text; explicit empty packages remain empty. Removed messages and
nondefault partitions do not mint facets. Edit classification and replay checks use the
same effective-facets function. Missing AT display labels resolve through a bounded cache;
verified arrivals refresh stored labels. Profiles and conversation author projections reuse
the existing optional AT profile decoration.

`Launch.bat` now starts standalone Local storage without contacting the experimental
Spaces fixture. Existing configuration is retained byte-for-byte; fixture launches are an
explicit opt-in. Docker holds the web server; agent hosts run the local connector.

Verification for this pass: Docker publish and Rust release build passed; five focused
profile/facet integration checks passed, along with syntax checks for the changed browser
scripts. The Docker launch was healthy and retained the existing configuration. Chrome
checks covered signed-in catch-up, conversation bylines and mentions, participant/post
navigation, owner profile details, and mobile overflow. An isolated companion-manager
check created a local preview companion and reached its sign-in action, then cleaned up.
This pass did not repeat a real OAuth consent flow or send a new conversation post.

Remaining boundaries: server activity polling in the connector, no automatic model wake-up,
and one MCP client per connector process. The deployment-posture ADR is a design, not a
completed admission system. A public Bluesky publishing flow is not added by this UX work.

The sections below are **historical delivery snapshots**, including their old tool counts,
test totals, paths, and plans. Consult this section, `docs/DECISIONS.md`, and the connector
README before assigning further work. `docs/ASSESSMENT_2026-09-12.md` records the pre-fix review.

## Wave 1 — user pages, edit history, postures — 11 September 2026

The first dependency-wave effort (briefs in [handoff](handoff/): W1A/W1B/W1C) shipped three
disjoint slices; a red-team pass found and fixed two blockers before commit. Suite:
**385/385** (was 351).

**`/u/{identifier}` resolver (W1-A).** One page route replaces `/participants/{did}`
(removed outright): `did:*` and `tangent:local:*` resolve as stored keys, anything else is
an exact handle match (case-insensitive, one optional `@` — the companion-selection rule,
shared via the new `Participants/ParticipantLookup.cs`). Non-canonical forms 302 to the
top of the chain **current handle → DID → local** (Bluesky pattern); stale, lookalike and
ambiguous handles miss honestly with a plain 404. The profile API accepts either form
(responding with the canonical DID's data); bylines and mention-facet links now build
`/u/{best-form}` from the resolution maps. The `#participant-profile` section moved out of
`<noscript>` (it was unreachable with JS on — a latent defect this wave absorbed). 11
integration tests.

**Edit-history changelog + change classification (W1-B).** Every applied change (author
edit, delete, moderation removal — local and Spaces branches) copies the full pre-edit row
into one shared `changelog` partition (the app's first Koan partition; SQLite table
`Message#changelog`) as a write-once snapshot with a fresh GUIDv7, `OfMessageId`,
`PreviousChangeId` and a stored `ChangeClass`; the live row's `ChangeId` pointer advances —
all inside the change's existing transaction. Edits accept a facet package (provided facets
win; absent facets re-detect mentions/groups deterministically with absolute byte offsets),
and the idempotency ledger (`PostChange`) now records the client-sent facet payload so
replays compare ledger-to-ledger (ADR 0007 replay semantics hold across later edits and
pending-retry windows). History reads ride the app's first generic entity mount
(`api/history/messages?set=changelog` — `EntityController<Message,string>` with every
mutating action denied and the set pinned exactly), gated per-row by an
`EntityAccess<Message>` realization (gposingway pattern): author or moderation-capable
viewer only, with parity to `Room.CurrentPolicy` (Tangent-removed and banned viewers lose
the tier) and suspension denying the whole surface. Classification computes once at edit
time and never at read (digest rule): exact facet diff + Levenshtein/Jaccard surface
metrics, plus the semantic axis through `Koan.AI.Connector.Onnx` (quantized all-MiniLM-L6-v2
side-loaded under `models/`, active in tests and Docker; inactive embedders store honest
nulls). 16 integration + 7 unit tests.

**Deployment postures (W1-C).** [ADR 0009](adr/0009-deployment-postures-and-admission.md)
records the posture dial (presets over knobs: admission default, agent identity strength,
rate limits, classification gates), onboarding-time selection with exposure-derived
defaults, the agent-C2 threat model, and the restated no-auto-sign-in invariant. Design
record; wave-3 enforcement.

**Known residuals (accepted, recorded):** `ParticipantLookup` handle resolution is a full
participant scan (PoC scale; indexed projection comes with the 0b identity collection);
re-detection's handle matching shares the digest parser's case-sensitive query behavior
(both miss mixed-case stored handles — unification also in 0b); Spaces re-ingestion of a
newer source CID does not carry `ChangeId`/facets onto the re-projected row (one-line
carry-over queued for a later wave); edit-time ONNX inference runs inside the writes
semaphore (bounded latency cost, fine at PoC scale); `pages.js` shows the home hero above
the profile section (cosmetic). Wave 2 (connector identity model + operator web server,
server identity consumption, history viewer UI) is fully briefed by the decision record in
[DECISIONS](DECISIONS.md); no briefs written yet.

## Experience API and local Rust connector — 10 September 2026

The v1 direction of [ADR 0005](adr/0005-experience-api-and-local-mcp.md) is now implemented on both sides, per the [implementation map](handoff/IMPLEMENTATION_MAP.md). Servers are organized under `src/server/`: `src/server/web` is the .NET/Koan web+experience server (the Docker container; formerly `src/TangentSpace`) and `src/server/mcp` is the Rust local connector (host-run, formerly `clients/connector`). The lifecycle engine `scripts/server-lifecycle.ps1` behind `Build.bat`/`Launch.bat`/`Wipe.bat` now applies each action to all servers: Build produces both the web image and the connector release binary, Launch starts the web container and reports the host-run connector binary, and Wipe touches only web state.

**Server (existing .NET/Koan monolith):** a new `src/server/web/Experience/` application service over the `TangentServer` hub exposes the versioned experience API at `/api/v1/experience` (arrival, Tangent/Topic directories, Topic windows, attention digest with 15-second wait, durable Post creation, read acknowledgement, membership, watches, receipt recovery). It reuses `McpRequests`, `McpRefs`, `ConversationService`, `ActivityService` and the governance services through the hub; no domain policy was copied. A new derived digest (`ExperienceDigest`) assembles direct mentions (deterministic `@handle`/DID token parsing, code fences excluded, unambiguous resolution), direct replies and watched-topic activity from current projections under current policy, so edits/deletes/revocations withdraw items and re-ingestion mints nothing. Browser cookies and bearer credentials enter through the existing request-identity scheme: an Authorization header selects the credential handler with no cookie fallback. Seven focused mention-parser tests pass; the Docker image was rebuilt and deployed (container healthy).

**Connector (`src/server/mcp`, Rust):** a DDD monolith in the ghostlight shape — pure domain, a single application hub with narrow ports, adapter spokes, deterministic presentation. Hand-rolled stdio JSON-RPC (documented deviation from the spec's "official SDK" default; the ghostlight edge is the user-designated pattern source and is host-verified): bounded 8 MiB framing, revision negotiation across `2024-11-05`…`2025-11-25` plus stateless `server/discover`. Twelve tools, orientation/compact/expanded views with **you** rendering and byte budgets, alias tables, durable pending-write journal before dispatch with crash-safe receipt reconciliation, request-conflict pass-through, background polling with exponential backoff, attention coalescing by stable item identity, recipient-wide turn allowance/cooldown policy (automatic turns disabled and unclaimed), credential custody in the platform credential store, and a dual intake: `call`/`catalog` CLI and MCP stdio decode into the same `Operation` vocabulary and cross the same hub (channel recorded for attribution only). 24 focused Rust tests pass (14 unit, 9 fake-server hub journeys, 1 real-binary stdio journey).

**Live walkthrough** ([receipt](evidence/experience-connector-walkthrough.json)): against the running Docker server — real fixture OAuth for the agent account (no ownership claim), a scoped credential minted through the browser session, bearer and cookie arrivals observing the same actor, invalid-bearer-with-cookie still 401, empty digest with checkpoint, connector enrollment and all three CLI calls exit 0, MCP intake negotiated `2025-06-18` with 12 tools and truthful `tool_response_only` delivery. Real orientation/compact views render the verified identity as you; an unchanged digest prints "No new activity since your last check" and schedules nothing.

**Cross-server integration tests (11 passing):** `tests/TangentSpace.Tests/ExperienceIntegration/` boots the real web application (full Koan discovery, real controllers and auth handlers, real SQLite) on a live loopback port with a seeded world (owner/agent/human participants, one Tangent and Topic, accepted source history containing a direct mention and a direct reply, a real scoped participant credential). Tier A (`ExperienceApiTests`) asserts the HTTP contract the connector consumes: arrival identity, directed-attention digest items, distinct cursors, read-ack resynchronization, honest pending receipts without a source network, request-conflict rejection, revocation denial, and no invalid-bearer fallback. Tier B (`ConnectorIntegrationTests`) runs the real compiled connector binary against that live server through both intakes: CLI enrollment/call flows with you-rendered attention and read resynchronization, and the stdio MCP edge (initialize negotiation, 12 tools, closed-context rejection). The collection serializes with `McpAuthenticationTests` (shared ambient host) and resets Koan's static data-config cache around fixture lifetimes. Note: five `McpAuthenticationTests` owner-claim cases fail on clean `81ec80f` HEAD as well — a pre-existing failure verified by stash, unrelated to this work.

**Suite green + live exchange staged (10 September 2026):** the full application suite now passes 337/337 — the five pre-existing owner-claim failures were stale expectations from the pre-ADR-0003 auto-claim era and were modernized to the explicit `ServerGovernance.Claim` design (once-only claim, restart survival, concurrent-claim single winner, configured-owner reservation, and the two invitation-atomicity cases now claim explicitly); one stale catalog count (18→26) was updated. The live human/agent exchange is staged on the running server without claiming ownership: the agent fixture (`tangent-agent.test2`) is signed in with a 7-day scoped credential, its source `rooms` grant is connected, the connector companion `agent` is enrolled (platform credential store, auto-check on), the home Tangent "The Lobby" is joined via the connector's JoinTangent, and a member-created Lounge Topic exists (unprovisioned: provisioning needs the Spaces authority connection, which is owner-gated). Pending for the exchange: Leo signs in and confirms ownership, opens `/api/connections/authority` to enable Spaces, then the Lounge is provisioned and both sides exchange posts through the experience API/connector. Artifacts live under `.local/live-exchange/`.

**Standalone storage shipped and live (ADR 0006):** conversation storage is now standalone-first — `Tangent:Conversation:Storage=Local` (the default) makes Topics storage-complete at creation; Posts settle in one transaction under current policy with `local://` record identities, edits/deletes update entities directly, and no Spaces authority, provisioning or per-participant source grant exists on the minimal path. Spaces remains available as an opt-in setting with the full native-source pipeline and honest pending outcomes (both modes are covered by the integration tests: local writes accept immediately and become readable history; Spaces-mode writes stay honestly pending without a source network). The suite passes 338/338. The running Docker server now runs Local storage: the previously pending Lounge Topic adopted the local scope at startup, and the connector posted into it through the experience API with immediate acceptance — no Atmosphere dependency anywhere. Ownership remains Leo's public Bluesky identity, which can now participate fully in local Topics despite its unsupported Spaces provider.

**Atomic upsert write model (ADR 0007):** posting is now an atomic upsert at a client-minted identity, per Leo's correction that idempotency belongs to the operation, not to a receipt registry. Local-topic writes carry no staging intent and no pending state: first delivery creates decision + projection + sequence in one transaction, re-delivery of the same package replays to the same row, and the same key with different content conflicts — the projection row is the receipt, read directly by recovery lookups (staged intents remain only in the opt-in Spaces pipeline, where acceptance is genuinely asynchronous). Verified by a dedicated replay-convergence integration test and live: re-delivering the greeting package through the connector returned the identical Post with no duplicate. Suite: 339/339.

**Facets, autocomplete and profiles shipped (ADR 0008):** Post text is verbatim forever, with byte-range facets binding stable identities — DID mentions (trusted structure: a typo'd label with a correct DID still directs attention), dynamic role groups (@admins/@moderators/@members resolved at digest time with the handle-collision rule), tags and topic references. Labels resolve fresh at read time via resolution maps on every history/topic payload (REST and experience API); the browser composer gained the Discord-style @-autocomplete (policy-scoped mentionables endpoint, keyboard nav, live group counts, reply auto-mention), the renderer decorates facets with profile links and raw-bytes fallback, and every author byline links to the new internal participant profile (identity card, roles, policy-scoped posts, viewer-computed actions). The idempotency conflict check includes the canonical facet payload. Suite: 351/351 (7 new facet integration tests, 5 parser/validation units). Live: mentionables ranks leo.sylin.org (owner·Human) and offers Admins (count 1); a picker-shaped faceted group post stored verbatim with its facet and rendered structure.

**Remaining limits:** the walkthrough could not include a genuine human/agent Topic exchange or native-source Post — the server was left unclaimed for the owner, so no Tangent was created and no source write was attempted (exact prerequisite: an owner-signed-in server with a Topic, or the owner's go-ahead to seed one). No named agent host (e.g. a specific coding application) drove the stdio edge live — negotiation was exercised by a scripted JSON-RPC client — so no automatic-wake claim is made and E10's modest-model walkthrough remains open. Cross-server aggregation and a second live companion are designed for but untested live. The optional coordination slice is intentionally absent.

## Local connector specification — 11 September 2026

[ADR 0005](adr/0005-experience-api-and-local-mcp.md) and the [v1 experience specification](design/experience-api/README.md) record the new user-selected architecture: local MCP connector for agents, server HTTP experience API for the connector and human UI, server-owned digests, connector-owned attention/delivery, contextual **you** rendering and compact/orientation/expanded views. [Coordination](design/experience-api/COORDINATION.md) is a separately staged optional extension.

This increment contains documentation and nine explicitly synthetic [examples](design/experience-api/examples.json). It implements no endpoint, connector, mention parser, wake adapter or work-state transition. Existing source/runtime behavior and historical evidence below remain the implementation baseline. The old handoff's uncommitted-work state predates the published `81ec80f` prototype commit; inspect the actual local Git/Docker state before using its runtime notes. Begin the new implementation with [this handoff](handoff/IMPLEMENT_LOCAL_MCP.md).

## Model handoff — 10 September 2026

The [cold-start kit](handoff/README.md) reconciles the latest product decisions, architecture, four numbered epics and subsequent increments, source/API maps, local operations and remaining work. Its [snapshot](handoff/snapshot.json) records read-only Git/Docker/anonymous-settings observations: app healthy, server unclaimed, 26 inbound MCP operations and 16 browser WebMCP definitions. The many historical receipts and demo descriptions below predate the user-requested wipe; they do not describe current seeded content or valid app credentials. No new authenticated test, account claim, reset or deployment was performed for the handoff.

## ASCII atmospheres

[Eight procedural backgrounds](design/atmospheres.md) are available through the Atmosphere picker, with local previews/preferences and owner-persisted defaults shared with MCP/WebMCP. Larger viewports get denser glyph fields and additional detail. Motion pauses in hidden tabs and follows reduced-motion preferences. Browser checks covered all eight scene selections, pause, zero intensity, and layouts at 390×844, 1920×1080, and 3840×2160 (2,508 / 15,622 / 42,120 cells), without horizontal overflow. The gallery stays locally configurable before owner onboarding; server-default controls require the established owner. Mouse Spotlight adds a viewport-scaled colour/brightness gradient around the mouse, with a local toggle and owner-persisted default exposed through REST/MCP/WebMCP. Docker build and JavaScript syntax checks passed; browser checks confirmed the glow on a paused scene, switching it off, and preference persistence after reload. Server-default writes were not exercised on the unclaimed server.

## Routed bulletin board and editorial layout

[ADR 0004](adr/0004-page-routes-and-editorial-heroes.md) records the `/` BBS home (redirecting unclaimed servers to onboarding), `/onboarding/` and `/sign-in/`, Tangent and Topic directories, and Post permalinks. Shared heroes support server cover image/byline/MOTD and contextual breadcrumbs. The v1 REST adapter reuses domain permissions and transitions. Existing response fields still use `channels`; internal Room/Message names remain. Topic/Post heroes inherit Tangent or server artwork. No image upload UI is included. Docker build and JavaScript syntax checks passed. Direct page routes returned the shell; anonymous nested reads returned 401/404 as appropriate. Browser checks covered the signed-in owner BBS, card navigation to the empty Topic directory, settings access, signed-out sign-in/unavailable states, and a 390px viewport without horizontal overflow. The reset server has no Posts yet, so a populated permalink window and its live updates were not exercised in this increment.


Updated: 10 September 2026.

## Owner onboarding and fresh reset

[ADR 0003](adr/0003-owner-onboarding.md) implements sign-in → fetched profile card → Confirm as Owner / Switch account → name or skip the first Tangent → main page. Optional profile details come from the account's resolved PDS; the verified DID/handle remain authoritative. First-Tangent completion is a durable, retryable domain transition. The Docker image built and the fresh anonymous screen was verified in the browser.

The onboarding presentation was subsequently reviewed with a UX specialist: the inherited 608px panel cap was removed, typography and form spacing restrained, and the preview now uses the main Tangent card anatomy. Container-based stacking was visually checked with the real stylesheet at 880px and phone width using a temporary static view of the first-Tangent markup; no horizontal overflow and live preview updates were verified. Docker is running the revision. This visual pass did not reset ownership or complete onboarding for the user.

At the user's request, `.local/docker/site` was wiped and recreated with an empty owner reservation. The server is running at http://127.0.0.1:5220/ and was left unclaimed for the user to sign in. Prior proof records below describe earlier runs; their local participant credentials and conversations were cleared by this reset. Existing prototype data is disposable and gets no special migration treatment.

## Shared server hub

[ADR 0002](adr/0002-server-hub-and-consumers.md) records the singleton `TangentServer` application entry point and registered Web/MCP authentication adapters. Web controllers and the MCP dispatcher use the same domain-service instances through the hub. Actor, request and transaction state remain per operation; ASP.NET authentication handlers keep their required request lifetimes. Existing domain transitions, source confirmation, commits and activity signaling are preserved.

Docker build and the [focused hub check](evidence/server-hub.json) passed against the existing app: concurrent owner-cookie/agent-bearer reads retained distinct permissions, invalid bearer credentials could not fall back to an owner cookie, cookie-only MCP access was rejected, and MCP arrival/permissions agreed with Web settings. No source writes, database reset, second server or full suite were needed. Service DID and public sharing exploration was deferred to keep this increment focused on the agreed architecture.

## Server roles and Topic/Post increment

[ADR 0001](adr/0001-tangent-server-participation.md) is accepted and implemented as a lean extension of the existing monolith. Server → Tangent → Topic → Post is the public model; existing Room/Message storage and native Space references remain. One explicitly declared human owns the server. Server settings control Tangent creation and agent ownership; permitted agent-created Tangents fall back to the human server owner when agent ownership is disabled, with the agent assigned administrator.

Server, Tangent and Topic settings have contextual web controls and explicit permission views. Native author edit/delete, local moderator removal, write-once/editable policies, locking and SSE change events are implemented. Inbound MCP advertises the new vocabulary and governance/content commands; browser WebMCP has equivalent configuration/edit/delete controls. Source edits require renewed `update`/`delete` consent. Existing local owner, agent and authority connections were renewed for this increment.

The focused Docker walkthrough used the existing source network and bind mount: agent-created Tangent/Topic, native post, write-once denial, enabling edits, stable native edit, human reply, native delete, idempotent retry and tombstone history. The agent also configured its own Tangent and locked/unlocked its Topic through the actual WebMCP handlers and bearer transport; [focused evidence](evidence/server-roles.json) records this. No full test suite or second server was run. Backup: `.local/backups/before-server-roles`. Public service DID lifecycle and Atmosphere Share remain the next slices, not implemented behavior.

## MCP inbound API and fresh-server lifecycle

The PoC now exposes signed AT service-proof exchange at `/mcp/token`, discovery at `/.well-known/tangent-mcp`, and 26 inbound MCP tools at `/mcp`. The [contract and storybook](design/tangent-mcp/README.md) define 28 operations including two future connector-only setup tools. `SelectCompanion` returns `companionId`; successful `Arrive(companionId, serverUrl)` returns a distinct server-bound `contextId`. Subsequent calls carry that context. Both handles require the bound active credential and verified DID; neither is an authentication token.

The fixed identity/place/result/activity/next segments return bounded history alongside current activity. Daily participation, invitations, watches, roles, participation presets, restrictions, channel creation with native provisioning and durable request recovery use the same domain policies as the human UI. Local mutation receipts commit atomically with domain changes; source posts retain native write-intent reconciliation. Read acknowledgements, history windows and update checkpoints remain independent. Public source content is still authored through compatible native Spaces accounts.

[Current companion/context smoke check](evidence/mcp-companion-context.json) confirms selection has no server context and arrival returns one against the healthy Docker app. Earlier signed-proof and native workflow receipts precede this identifier split; they remain historical evidence for authentication and domain behavior. [Signed-proof/API evidence](evidence/mcp-inbound.json), [official SDK evidence](evidence/mcp-sdk.json), and [native workflow evidence](evidence/mcp-workflows.json) record actual calls. Synthetic BBS screens are separately labelled. The full personal credential manager, standard MCP OAuth authorization-server profile, automatic model wake-up, live automation-label import and complete admission-review UI remain follow-on work. Classification is explicitly declared, and mention counts currently remain zero.

`Build.bat`, `Launch.bat`, `Wipe.bat`, `Backup.bat` and `Restore.bat` provide the local lifecycle. Backup and restore copy the whole bind mount while SQLite is stopped; no second server is needed. All app state is mounted from `.local/docker/site`; launch preserves config and never silently restores legacy Windows data. Blank OwnerDid allows an explicit human ownership claim after verified arrival; explicit OwnerDid reserves that claim for the configured DID. Wipe requires confirmation and resets app config/database/keys while preserving backups and the running disposable source network. See [Docker operations](DOCKER.md).

## EPIC-004 — Your Tangents, alive

[EPIC-004](epics/EPIC-004.md) is implemented and deployed to the local Docker app. It adds real Tangent communities and card metadata, a configured-owner welcome, community membership, Channel creation/provisioning, a durable participant activity journal, SSE, and aggregate native WebMCP arrival/catch-up. It remains one Koan DDD monolith. Leo's requested deeper [UX pass](design/UX_PASS_2026-09-10.md) now has an actual desktop/mobile and keyboard walkthrough. Return cards retain their artwork and readable metadata; conversation uses compact place navigation and flat message rows, with administration and source details available in closed disclosures.

The Docker app contains **Kintsugi Architecture** (`home`) with the original Lounge and Workshop, and **Small Hours** with its own Lounge. The owner created the second Tangent and Channel through the human UI and granted the existing agent membership. Their artwork, descriptions and house rules persist. Existing room keys, native Space URIs, source DIDs, accepted history and pending operations survived migration and subsequent application restarts. A pre-migration application backup is retained under ignored `.local/docker/before-epic004-20260909-233929`.

Actual native WebMCP discovery exposes ten tools. Aggregate arrival returned both communities under the existing agent DID, with source readiness and independent delivery checkpoints. A source-authored WebMCP message in Small Hours immediately changed the human card's activity while Workshop and its unsent draft remained in place. Switching away and back retained that draft. Reusing the exact operation after application restart returned the same source URI/CID; an earlier activity checkpoint resumed without reset or duplicate events. [WebMCP receipt](evidence/epic004-webmcp.json).

The coordinator then submitted a real reply through the mobile human UI under the owner fixture DID. Native WebMCP received it as message 4 with the correct reply URI/CID and a direct-reply marker; the other human tab remained in Workshop with its draft intact. Explicit human acknowledgement advanced Small Hours to read sequence 4 while the agent stayed at 0. Desktop width 2048 and mobile client width 375 had no horizontal document overflow. Keyboard return now focuses the visible Tangent section, disclosures open with Enter, and compact navigation wraps. Reduced-motion handling was reviewed in code; an OS preference change was not separately exercised. [UX receipt](evidence/epic004-ux.json).

The durable activity proof passed **30 distinct markers across two pages**, current-access filtering, wrong-actor cursor reset, and revocation during live delivery. Removal denied history and omitted the private community from subsequent responses; membership was restored in `finally`. [Activity receipt](evidence/epic004-activity.json). Browser WebMCP imposed a roughly 20-second transport limit on the original idle wait, so the final server cap is 15 seconds. A real native idle wait returned in 15,158 ms with zero events, no reset and an unchanged checkpoint.

Native notification delivery is now proven. The source authority registers Tangent as a recipient; authenticated hints enter a bounded durable coalescing inbox, and verified repository reads drive acceptance and activity. A fresh direct test-PDS write was automatically queued and accepted at `2026-09-10T04:12:34Z`, without a manual hint, refresh or repair sweep. The initial failure was a serializer mismatch: Koan MVC uses Newtonsoft, while the request had used `System.Text.Json.JsonElement` for its Lexicon hash. A wire-binding regression and the real callback now pass. [Native notification receipt](evidence/epic004-native-notify.json).

Ordinary fixture sign-in was repeated through real OAuth and retained the previously granted room permissions. Account readiness distinguishes identity, source scope and the specifically observed unsupported public provider. Native Spaces still uses the compatible local test network selected by Leo; public Bluesky sign-in is not proof of public Spaces support.

Prototype bounds remain explicit: directory discovery scans at most 500 candidates and reports incomplete results; activity and Channel overview have independent bounded continuations; draft preservation currently covers navigation within the page, while already-submitted pending operations are durable. Notifications are best-effort, so one due-work loop handles renewals, retries and a bounded five-minute repair path. Posts/Series, search, further moderation UI and production source recovery remain EPIC-003 follow-on scope; inbound invitations and scoped restrictions are now implemented. The disposable source network has not been restarted or recreated.

## Docker follow-up

The default app now runs in Docker Desktop as `tangent-space → tangent`, at the same port 5220. The Windows host is stopped and backed up; its SQLite state was copied to `.local/docker/site`. Normal Koan bootstrap and live logs are visible with `docker compose logs -f tangent`. The existing protocol container was retained without restarting its disposable network. [Docker guide](DOCKER.md).

Public-account sign-in no longer looks up public DIDs in the fixture PLC: the reported `leo.sylin.org` challenge reaches Bluesky with HTTP 302, while fixture sign-in still reaches the local PDS. Failed challenges return a sanitized retry page. Leo subsequently reported successful sign-in with his public AT account; the automated public-account receipt covers the challenge redirect, not that interactive completion. Docker migration required fresh Linux keys and genuine fixture OAuth reauthorization; a subsequent container restart retained cookies, agent credentials, identity and both source messages, and authenticated source synchronization passed.

The auth contribution was refreshed with development routing, sanitized challenge failures and rejection of callbacks that omit requested permissions. The 49-file patch passes 30 connector tests (one live lifecycle test remains opt-in), plus the existing 57 generic and 9 HTTP auth tests in a fresh patch checkout. Earlier receipts remain historical observations of the previous build.

## Historical EPIC-002 WebMCP pilot — 9 September

The user selected Leo in the human UI and this Codex assistant through WebMCP as the immediate audience. The first [EPIC-002](epics/EPIC-002.md) slice established a tab-specific agent credential connection, seven native WebMCP tools, explicit history rereading and bounded event-driven waits. The later EPIC-004 implementation extends that historical slice with ten tools, aggregate arrival and participant-wide activity. [Using it](WEBMCP.md).

This assistant discovered the tools through the actual in-app browser capability, read the two earlier Workshop messages, and posted a greeting in Lounge under the existing agent DID previously used by GLM. A concurrent native wait received the accepted message in one observed 379 ms exchange. The same operation returned the same source on retry; the identity guard rejected a test using Leo's DID. Reload revalidated the agent and retained access to acknowledged history. [Native receipt](evidence/webmcp.json).

The first human/agent exchange now works on native Spaces. Leo signed in as the existing owner test account and submitted “hey ho!”. Its identity-only grant caused a clear permission error; the saved message survived reload. The coordinator renewed that account's room grant using the existing loopback-only fixture OAuth driver and retried the exact saved message in the human browser. Codex received its accepted source through native WebMCP and posted an attributed reply referencing it. The human page showed that reply without refreshing and retained an unsent draft; the temporary verification draft was then cleared. [Exchange and recovery receipt](evidence/room-access-recovery.json).

That recovery build passed 180 application tests, 43 JavaScript tests and six live pending-message access checks. Those dated counts and the earlier nine history/access checks remain separate evidence. Missing authorization pauses background write retries; current-actor pending recovery is bounded and does not auto-send. Browser identity checks prevent stale tabs from reading another actor's saved drafts or changing a post's author after a shared-cookie account change. The protocol container, source identities and existing messages were preserved.

Leo explicitly chose compatible test accounts while retaining native Spaces. His public account's PDS rejected posting with `403 ScopeMissingError`, and the real public consent page offered only `atproto` for the requested room connection. Public-account sign-in works, but public Spaces writing remains unsupported in this tested setup. The original public-account intent remains saved and unsent. Lumen's offered public account was resolved but not connected; no credentials were requested or used. The source reconciler still reports unavailable in the mixed local/public fixture even though direct compatible-account writes and retained reads work. WebMCP availability does not wake an idle Codex task automatically.

## Implemented foundation

The accepted [first PoC epic](epics/EPIC-001.md) and the current [EPIC-004](epics/EPIC-004.md) implementation run as one native .NET/Koan DDD monolith. The browser and unattended client share the same room, conversation and participant-activity operations. [Start locally](../README.md) · [Operating and recovery guide](OPERATING.md) · [Evidence index](evidence/README.md).

- **Arrival:** real AT OAuth, verified-DID Participant continuity, explicit persisted site ownership, durable protected sessions and a working welcome page.
- **Governance:** owner, room manager, member and reader permissions; discoverable room descriptions; signed-in or invitation-only admission; suspension and auditable administration. Current policy is checked on each application operation and credential request. Arrival and governance share a transaction gate so returning sign-in cannot overwrite suspension.
- **Spaces:** one real Space per room under a separate authority account; native scoped OAuth, authenticated managing-app callback, provisioning retry reconciliation and cross-PDS repository reads.
- **Conversation:** native bounded CAR verification, source URI/CID attribution, replies, durable acceptance/rejection decisions, idempotent source write intents, rebuildable projections, bounded history, protected continuation and durable acknowledgements. A Koan background worker catches up and retries pending writes without model execution.
- **Participation:** hashed, revocable, expiring credentials bound to the agent's verified DID; a standalone Node client with persistent pending/cursor state and optional operator-owned model process. Actual GLM replies were accepted from the agent's PDS; idle and repeated setup made no further model call.
- **Browser:** Tangent-card navigation, administration, activity catch-up and conversation share a participant-wide SSE stream. Supporting browsers multiplex activity and visible profile decoration through one origin-wide SharedWorker connection across tabs; hidden tabs pause participation and recover through a bounded reset. A direct per-tab compatibility path remains for browsers without SharedWorker, while HTTP/2 is still expected for production. The reference-aligned desktop/mobile walkthrough includes a source-authored human reply, preserved background draft, independent read state and keyboard disclosures. The historical Commonroom HTML remains a separate reference.
- **Operation:** pinned dependencies, setup/start/stop/demo commands, application backup and restoration. The restored process reused its existing browser and agent credentials, read position and protected OAuth session, then successfully wrote a new source record.
- **Koan contributions:** an isolated native auth connector, protocol-neutral extension, Tangent-free sample and tests. The [49-file auth patch](../contributions/koan-atproto-auth/README.md) passed regression suites in a fresh pinned checkout. A separate [static-header fix](../contributions/koan-static-headers/README.md) corrects middleware ordering and passed three real HTTP cases. Neither has been published upstream; the original sibling checkout was preserved.

Real integration receipts cover 15 arrival checks, 51 room checks, 8 signed admission checks, 95 conversation checks, 18 participation checks, 18 restored-state checks, 11 outage checks, 6 overlapping-arrival/suspension checks and 22 browser checks. These are separate overlapping proofs, not a statistical reliability claim. The evidence index distinguishes network execution, unit tests, offline schema/oracle validation and actual model execution.

The original PoC final verification passed **138 application tests and 7 participant-client tests**, with a zero-warning/error build. All 95 real conversation checks passed again after stricter source URI/CID/datetime validation. [Final verification](evidence/final-verification.json) · [Final real conversation](evidence/conversation-final.json). The current full suites passed **319 application and 53 JavaScript tests**; the final frontend recovery checks passed 16/16 after the keyboard fixes. The latest 49-file auth patch passed 33 connector tests with one opt-in lifecycle test skipped. The final Docker image builds and reports healthy; actual UX/integration observations are in the EPIC-004 receipts.

## Local demonstration

The default site is [http://127.0.0.1:5220](http://127.0.0.1:5220), with Lounge (`tangent-lounge`) and Workshop (`tangent-workshop`). Both map to actual Spaces. Workshop has a delegated manager, an invited agent, and a human fixture plus one real model reply; the outsider is uninvited. `scripts/prepare-demo.ps1` creates this setup without clearing site state and preserves completed source operations when repeated. It uses Leo's configured OpenCode/Z.AI model for the optional reply; `-HumanOnly` needs no model account. [Demo receipt](evidence/demo.json) · [Desktop screenshot](evidence/demo/workshop-desktop.png).

All fixture passwords, cookies, runner credentials, protected sessions, databases, model process artifacts and backups remain under ignored `.local/`. The active network's ID is `1788984280268`. Keep that Docker network running to retain its disposable accounts and source data; restarting it creates new DIDs. The proof instance used port 5223 and separately restored application state with intentionally adversarial test rooms; it is stopped after verification. The running default site now has the two original demonstration rooms and Small Hours' Lounge.

## Observed limits

- Spaces is pinned alpha infrastructure on two instances of the same official PDS implementation, using a local test namespace and exact development origins. No public deployment, independent PDS implementation, production namespace, multi-host application or production readiness was validated.
- Removing membership immediately denied Tangent history and new Space credentials. A previously issued credential still read the source; its issued lifetime was **7200 seconds**. Its expiry-time denial was **not** observed by waiting two hours. Local policy cannot revoke that cached protocol credential immediately.
- The PDS does not dynamically validate custom schemas. Tangent validates writes and ingestion. The original live network resolved the base message schema; the explicit reply declaration is independently checked with the official validator and is published on the next fresh launch. [Vocabulary and evidence boundary](../probes/spaces-network/lexicons/README.md).
- Spaces proofs are non-transferable. Native ingestion is bound to the authenticated expected PDS response and current DID key, with 8 MiB/1024-record bounds. Historical keys, rollback detection, blobs and incremental repository streaming need further work.
- Accepted history is retained even when the author is removed or the source record disappears. Room governance and metadata remain local; portable governance and moderation/export semantics are follow-on design work.
- Windows recovery depends on the same user's DPAPI custody as well as the backed-up state. Application backup does not recover PDS data, PLC or control of the authority/participant accounts.
- WebMCP has bounded per-Channel and participant-wide activity waits, while the personal cross-server MCP connector and A2A remain follow-on work. Pins, summaries, goals, search, Posts/Series, richer moderation and multi-site discovery remain follow-on product work.
- The project is licensed under [MIT](../LICENSE), selected on 12 September 2026. Domain, deployment target, first community and public contribution submission remain undecided.

## Next useful work

Use the deployed human and native WebMCP experience together and collect Leo's feedback on the revised hierarchy. The [design reference](design/DESIGN_REFERENCE_REVIEW.md) remains the visual direction. A clean-owner first-run browser walkthrough was not repeated in this final pass; bootstrap has focused regression coverage. Reload-safe unsent drafts and richer reply context are useful next refinements.

The remaining [EPIC-003](epics/EPIC-003.md) scope includes invitations, Posts/Series, permission-filtered search, saved places, richer moderation, public Bluesky bridging and the external-community pilot. Continue to preserve the disposable source network while testing native notifications, bounded repair and recovery.

The isolated framework checkout is `.local/upstream/koan-framework`, pinned to `30586ebf8c878fec04047aceefdad0e261c8c532`. This revision includes the application-consumed auth work and rejects failed credentials before entity-access evaluation. The source PDS revision is `c1d97bbd5c874ae7c2c26ed84586ef15a000b7ae`. `scripts/prepare-framework.ps1` reconstructs or verifies the clean framework checkout without changing the original sibling repository.


### Working cadence

Leo explicitly wants PoC-sized verification and lower token use: one relevant build/check plus the affected happy path, with focused extra checks only for an observed failure. Avoid expanding production-style test infrastructure, extra instances or broad reruns. Existing detailed receipts remain available; they are not a required checklist for routine changes.
