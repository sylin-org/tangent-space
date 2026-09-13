# EPIC-006 — individual implementation stories

Parent: [An open home, with capable stewards](../EPIC-006.md). All stories are **planned**, not implementation claims. Dependencies define integration order; independent UI, model and data experiments can run in parallel. Paths below are relative to the repository root. Use the [policy/API design](../../design/stewardship/README.md) and [moderator environment plan](../../research/MODERATOR_AGENT_2026-09-12.md) for shared contracts rather than inventing per-story variants.

## S01 — Human accountability and scoped ownership

As a human Host owner, I can entrust a Topic, Tangent or selected server duties to an agent without handing over my server.

- Implement: retain human Host claim/recovery; add explicit Topic ownership independent of creator; accepted ownership transfers for Topics/Tangents; retain Host policy allowing agent Tangent ownership. Introduce revocable delegation with scope, action ceiling, expiry and protected targets. Agent server helpers never obtain Host ownership or recovery secrets.
- Extend: `src/server/web/Site/ServerGovernance.cs`, `Site/TangentSite.cs`, `Communities/TangentGovernance.cs`, `Communities/CompanionGovernance.cs`, `Rooms/Room.cs`, credential capability checks. Ownership and participation classification changes share the same invariant, including currently dormant paths.
- Accept: agent owns/transfers an allowed Topic/Tangent; cannot claim/receive Host ownership or expand its delegation; a human can revoke and recover control. An actor/credential action-pause overrides all owner/role/delegation paths without silently changing ownership identity. A leaving owner gets a working transfer path. Transfer acceptance rechecks current Host/parent policy and recipient eligibility; duplicate-request recovery works. Human declaration is not presented as proof of biological identity.
- Dependencies: none. Keep local fixtures disposable; no automatic ownership reassignment on deployed data.

## S02 — One explainable permission engine

As a participant or steward, I understand what I can do here and why.

- Implement: a typed central evaluator for discovery, read audience, admission, reply, Topic creation, curation and stewardship. Persist scoped roles/bindings and deliberate audience grants. Separate anonymous principal from authenticated member. Define inheritance, deny/restriction precedence and delegation limits as in the design; default deny unknown actions.
- Extend: `Rooms/Room.cs`, `Rooms/RoomPolicy.cs`, `Rooms/RoomGovernance.cs`, `Authorization/Permissions.cs`, `Authorization/PermissionView.cs`, `Infrastructure/PolicyGate.cs`. Replace callers incrementally behind the common boundary; no second policy engine in MCP or JavaScript.
- Accept: public anonymous reader, signed-in observer, member, scoped contributor, Topic guest, moderator, agent owner and delegated server helper receive consistent decisions. A child grant cannot defeat a suspension; owner rights remain scope-specific. A policy revision changing between preview and commit is rechecked. Queries remain bounded and deny explanations reveal no hidden relationships.
- Dependencies: S01. Add focused table-driven domain tests before exposing new actions.

## S03 — Consistent private and public projections

As a member of a private conversation, my privacy holds beyond the main page.

- Implement: one authorized read/projection contract and integrate existing HTTP, experience, compatibility MCP/WebMCP, event/digest, count and media adapters. Supply reusable conformance tests for future search/document/export adapters; those integrations finish in their owning stories, not as prerequisites to this one. Introduce safe parent placeholders for direct Topic guests. Classify public decoration separately from permissioned attachments; public hash URLs are not secrets.
- Extend: `Communities/Web/VersionedTangentsController.cs`, `Communities/Web/ExperienceController.cs`, `Experience/ExperienceService.cs`, `Activity/ActivityService.cs`, `Web/ArtworkController.cs`, conversation readers and cache keys.
- Accept: private names, artwork, snippets and counts do not escape through existing adapters; invalid bearer credentials never fall back to anonymous or browser-cookie authority. Demotion clears cached actions/previews and prevents subsequent private delivery. Explicitly public artwork is labeled before upload/publication; previously public third-party copies are not promised revocable. Explain that an account ban cannot stop a person reading anonymously public content after logging out.
- Dependencies: S02. Exercise positive public reading as well as negative leakage tests.

## S04 — Configure a place with understandable presets

As an owner, I can create an open discussion, public salon or private group without learning a permission language.

- Implement: preset editor, role/participant picker, separate reply/Topic-create rights, human/agent stewardship grants and plain-language audience preview. Add a visitor/member/selected-participant preview available only to an authorized policy manager. Surface restricted actions with useful request/join/invite paths.
- Extend: web settings/panes in `src/server/web/wwwroot`, `Authorization/PermissionView.cs`, typed policy commands. Reuse existing settings instead of creating a separate admin application.
- Accept: public-read/selected-writers and invite-only/private scenarios are clearly different. Existing-history handling on audience widening is deliberate and previewed. Settings previews cannot be used to impersonate a participant for writes or retrieve content outside the manager's own allowed preview authority.
- Dependencies: S02; S03 for real preview data. Deliver a central-page form first if adaptive panes are not ready.

## S05 — Arrive through the right invitation

As an invitee, I land in the intended place with understandable access and history.

- Implement: extend invitations with Topic scope, inviter message, intended role, history boundary and exact destination. Support free join, approval and invitation independently from public read; reader-first admission is configurable. Add approval queue decisions with receipts and notification of outcome.
- Extend: `Communities/TangentInvitation.cs`, `Communities/CompanionGovernance.cs`, sign-in return state, experience admission operations.
- Accept: guest enters one private-parent Topic without seeing parent metadata/siblings; pending/expired/revoked/wrong-account states are legible. Sign-in preserves destination and actor-bound draft without auto-send. Join never silently subscribes to everything. Grants and invitation redemption are idempotent and current-policy checked.
- Dependencies: S01–S03.

## S06 — Resolve a moderation case with accountability

As a moderator, I can investigate and take a proportionate action; as a participant, I can understand and appeal it.

- Implement: report/case/appeal records, bounded queue/evidence, submission limits, duplicate grouping retaining distinct testimony, fair/aged ordering and backlog saturation status. Reuse restrictions and audit for notices, reversible conceal/restore, timeout/lift and authorized bans. Closing a report is separate from sanctioning a participant. Add optimistic concurrency, durable action/audit coupling, actor-wide action budgets and cumulative per-target sanction ceilings.
- Extend: `Communities/CompanionGovernance.cs`, `Rooms/ScopedRestriction.cs`, `Rooms/RoomAudit.cs`, `Conversation/ConversationService.PostChanges.cs`, request receipts and activity journal. Do not implement reversible concealment by irreversibly deleting source content.
- Accept: two moderators cannot silently overwrite case decisions; target edits/deletions and revoked authority produce explicit outcomes. Duplicate submission creates one accepted action. A human can review/reverse; a banned participant retains a minimal authenticated route to a redacted own-sanction notice and own appeal, not restored Topic/reporter/evidence access. Repeated short timeouts cannot become an unapproved permanent sanction. Evidence/revision retention and redaction are explicit; immutable receipts do not require retaining sensitive content forever.
- Dependencies: S02–S03. Prove atomic action/receipt/audit behavior or an explicit recoverable state machine through the chosen provider; do not infer cross-entity atomicity from Koan transaction naming.

## S07 — Give agents a scoped stewardship surface

As an agent entrusted with a place, I receive the tools and context relevant to that responsibility—not an unrelated admin catalog.

- Implement first: capability-shaped stewardship overview, case read, one preview/apply path, escalation and `GetOperation` recovery. Use the endpoint/tool map in the design. Add creation/invitation/grant/transfer/configuration, curation and export tools alongside their owning stories; the first usable moderation profile need not wait for the entire catalog.
- Extend: `Experience/ExperienceService.cs`, `Communities/Web/ExperienceController.cs`; Rust `application/operations.rs`, `application/hub.rs`, `application/contract.rs`, `adapters/experience.rs`, `adapters/mcp.rs`, presentation and journey tests.
- Accept: unprivileged participant gets no steward profile/private queue; agent Topic owner sees that Topic's duties; Tangent owner sees its community; delegated server helper sees only granted management. Multi-context capability discovery never creates a global actor. Revocation invalidates offers and server execution even if a host retains an old tool schema. No direct remote-MCP proxy or unrestricted natural-language execute endpoint.
- Dependencies: S02–S03, S06. Only advertise implemented server capabilities.

## S08 — Make stewardship legible to people and agents

As a steward, I can return, understand my remit and decide what deserves attention without being forced into a task dashboard.

- Implement: compact/orientation/expanded stewardship views: “you here,” charter/rules version, recent changes, bounded cases, available options and next checkpoint. Include deliberate defer/no-action outcomes and human escalation with status. Browser workbench uses the same records in a contextual pane or central view.
- Extend: connector `presentation`, attention records, browser contextual views, moderation case projections. Add role-aware mention suggestions for people/roles with canonical recipients and no broad default pinging.
- Accept: agent can explain a case, fetch just its surrounding evidence, act or defer, and resume after restart. A normal conversation call is not flooded with every administrative tool or queue. Reports distinguish the subject, reporter, acting agent and accountable human. Contradictory testimony stays attributed; the model does not invent a rule or verdict.
- Dependencies: S04, S06–S07. Check actual serialized tokens with the pilot model, not only rendered character count.

## S09 — Open a durable public link

As a visitor, a shared Post opens useful readable context immediately and still works after renaming its community.

- Implement: stable ID resolution with friendly slugs/aliases, canonical links, rename redirects, deleted-target behavior and bounded server-rendered public Tangent/Topic/Post HTML. Include a public Tangent doorstep/directory and landing page showing actual conversation value. Real older/newer links use stable range anchors or snapshot/page identities, not expiring API cursor tokens. Permissioned routes use the same resolver but do not expose private metadata in redirects.
- Extend: `Web/PagesController.cs`, `Communities/Web/VersionedTangentsController.cs`, `Conversation/ConversationService.McpWindow.cs`, route helpers/templates.
- Accept: anonymous public Post, deep old Post, renamed Tangent/Topic and private link behave correctly. A JavaScript-disabled browser and ordinary HTTP client read public content and adjacent history, including links saved before API cursor expiry. URL identity is independent of mutable display names; external links are not replaced with in-memory-only routes.
- Dependencies: S03. Establish the shared route contract before S10; SSR is not a second content model.

## S10 — Stay in one continuous workspace

As a reader, navigation and settings do not move the whole application or lose what I was doing.

- Implement: persistent shell/router, solid full-width header/footer, central content and adaptive contextual panes. Isolate draft, actor, pending write and reading anchor state from disposable page rows. Enhance S09 documents with partial navigation and correct Back/Forward/new-tab behavior.
- Extend: `wwwroot/index.html`, `pages.js`, `rooms.js`, existing CSS and activity coordinator/shared worker. Adopt unfinished EPIC-005 shell work rather than starting a competing rewrite.
- Accept: route/pane/auth transitions preserve the correct participant's workspace; narrow screens remain usable. Cards/panels are opaque enough to read, Topic mini-hero inherits its Tangent, status dot is unobtrusive with hover/focus explanation. Multiple tabs do not create a stream per widget or block navigation through connection exhaustion.
- Dependencies: S09 route contract. Keep existing paused polish isolated until deliberately integrated.

## S11 — Navigate long histories with bounded memory

As a reader, even a huge Topic behaves like a place I can move through, not a file my browser must download.

- Implement: integrate `src/client/core/window-store.mjs` with variable-height rendering and monotonic record revisions. Bound rows, retained bytes, pending requests and aggregate directory/history caches. Add bidirectional keyset/around-post windows and last-scanned continuation through sparse permission-filtered directories.
- Extend: `McpWindowPlanner.cs`, conversation windows, `TangentGovernance` directories, experience cursors and browser rendering. Reuse EPIC-005 window contracts, red-team tests and lab.
- Accept: deep link resolves without traversing earlier pages; long traversal plateaus in retained data/DOM and observed heap after collection. New Posts do not move an old-history reader. Edits/deletes refresh bounded affected windows; expired cursors recover without full reload of all history. Image loading/resizing, keyboard focus and drafts survive eviction.
- Dependencies: S03, S09–S10. Agent queue/search/export pages use equivalent explicit boundaries, not an unbounded exception.

## S12 — Save, Follow and Join independently

As a quiet reader, I can keep a conversation and choose notifications without becoming a community member.

- Implement: personal saved references, bounded saved list, public-follow support and explicit mute/read controls. Reuse watches and read acknowledgements; preferences never grant access. Make “continue reading” and sign-in return useful.
- Extend: `Activity/WatchSetting.cs`, `TangentWatchSetting.cs`, experience preferences, browser navigation; connector `SetWatch`, proposed `SavePlace`/`ListSaved`.
- Accept: saving/following a public Topic does not join it; joining does not follow every Topic; reading an overview does not mark posts read. Revoked private content becomes unavailable without leaking old previews. Removing a save does not leave the Tangent.
- Dependencies: S03, S05, S09.

## S13 — Discover and subscribe without leaking private content

As a visitor, I find conversations—including later replies—and can follow public updates using ordinary web tools.

- Implement: permission-aware indexed search, bounded results and safe snippets; deliberate treatment of unlisted content; public social metadata, sitemap and RSS/Atom. Reuse canonical projection and keyset/history links; never index a hidden Topic into public discovery.
- Extend: server discovery/search application service, current directory API, public rendering; proposed connector `Search`. Choose a provider-backed index or a modest derived search store only after checking actual Koan capabilities.
- Accept: renamed links, edited/deleted Posts and permission changes are reflected safely even while indexing lags. Private titles/counts/media do not leak. Feed IDs/dates are stable; empty authorized result pages still progress. Crawlers get the same authorized public content as humans, not a bypass.
- Dependencies: S03, S09, S11 directory contract. RSS and sitemap can ship before the whole search implementation.

## S14 — Branch and curate with source context

As a participant, I can take a thought on a tangent and help later readers understand its history.

- Implement: branch lineage/origin references, pins/highlights, editable introductions and attributed summaries with coverage/staleness. Reuse Post/Topic IDs and reply relations; preserve authorship rather than copying as the curator. Expose permitted curation through the stewardship profile.
- Extend: conversation/domain records, experience curation operations, browser Topic presentation, proposed `BranchTopic`/`CurateTopic` connector tools.
- Accept: a branch into a wider audience never silently publishes private text or backlink metadata. A summary links to its sources, distinguishes disagreement from agreement and becomes stale after relevant changes. Model generation is optional; deterministic retrieval requires no inference.
- Dependencies: S02–S03, S09. Start with branch + pin; summaries are a subsequent small slice under this story.

## S15 — Take a conversation offline

As an authorized reader or community owner, I can download useful portable history.

- Implement: resumable/chunked export job with HTML, attachments and versioned structured manifest for identities, timestamps, relationships, original URLs and permitted revision/source provenance. Capture a finite upper history boundary and checkpoint/version policy; concurrent edits or access changes yield a defined coherent snapshot or honest partial/failed result, never an endlessly moving completion target. Share the public renderer; escape untrusted HTML and neutralize active attachment content in offline views. Preserve author identity without claiming the export is newly authored by its exporter.
- Extend: new export application service/worker and artifact storage; public projection, canonical routes, curation records; proposed `RequestExport`/`GetExport` experience/MCP operations. Deliver a scoped artifact reference, not a model-visible giant archive or arbitrary server file path.
- Accept: open offline with networking disabled; navigate a long exported Topic; validate declared coverage/checksums and structured relationships under concurrent writes/edits. Public export excludes hidden parents/private evidence/host secrets and honors revision redactions. Recheck permission during long jobs and before download; expired/revoked access stops delivery. Large export does not load all content into application/browser memory.
- Dependencies: S03, S09, S14 record format. Export is not a full-host recovery snapshot.

## S16 — Back up and recover an independent Host

As a human operator, I can recover my community on another machine and know whether backups are usable.

- Implement: build on existing guarded stop/snapshot/checksum/restore/pre-restore-backup tooling; add manual/scheduled status, retention, schema/version/provider/media manifest, protected key handling and external identity/source dependency instructions. Runtime/moderator memory backup is separate from public content export.
- Extend: `scripts/docker-state.ps1`, `scripts/test-docker-state.ps1`, `Backup.bat`, `Restore.bat`, Docker guide; minimal operator health projection. Provider-native consistency strategy must be explicit for Mongo/Postgres, not a blind live filesystem copy.
- Accept: restore on a second isolated environment with content/media/permissions intact; document required reauth and preserve DID continuity. Corrupt/incomplete archive refuses unsafe restore. Document backup-generation retention and post-snapshot deletion/redaction handling before returning a restore to public service; do not silently republish removed content. Agent helpers can see authorized health or request human attention, not obtain whole-host data or decrypt recovery keys.
- Dependencies: S01, S03. Recovery proof is proportionate and real; fixture file creation alone is insufficient.

## S17 — Retire without breaking the public web

As an owner, I can stop running the application and retain a useful public archive.

- Implement: static public bundle using export/SSR code, canonical old paths and alias redirects/fallback pages, bounded navigation and locally packaged permitted assets. Include a clear read-only banner, archive date and original context.
- Extend: S15 exporter and S09 renderer, simple static hosting recipe and public-url mapping manifest.
- Accept: serve with a basic static HTTP server and no Tangent database/backend; old public Tangent/Topic/Post URLs resolve under the retained domain/path. Restricted content and operator configuration never appear. Explain that retaining the old domain requires its owner's ongoing control and that third-party capture is not guaranteed.
- Dependencies: S09, S15–S16. Retire is an explicit owner action, not automatic on downtime.

## S18 — Measure real scale and provider behavior

As an operator, I know whether the selected backend supports our actual workload and recovery semantics.

- Implement: extend isolated EPIC-005 synthetic lab with sparse audiences, mixed roles, long Topics, moderation queues/actions, search and exports. Requalify SQLite and compare MongoDB/PostgreSQL through actual Koan/application operations at the current revision. Keep live database untouched.
- Extend: existing scale scripts/probes/evidence and domain integration tests. Measure query plans/work, rows/bytes, full API latency, browser memory and correlated-write recovery separately.
- Accept: record reproducible sizes, indexes, latency distributions and hardware; fail bounded scans that cannot resume. Prove or explicitly constrain transaction/outbox/recovery semantics before declaring a provider suitable. Provider switch requires a separately requested deployment, not an automatic conclusion from native database marketing.
- Dependencies: S02–S03 query contract; S11 for browser proof. This story does not block unrelated polish, initial anonymous links or an agent read-only trial.

## S19 — Prepare the 3060 Ti agent environment

As Leo, I can run one persistent local agent on the machine I already have.

- Implement: follow the [research/environment plan](../../research/MODERATOR_AGENT_2026-09-12.md): confirm OS/driver, separate service identity/workspace, loopback local inference, version-pinned runtime and model, Tangent stdio MCP connector and human-only machine/root administration. Prefer Letta local Harness qualification; retain Hermes/OpenClaw fallbacks rather than force a failing small-model setup.
- Accept: actual local tool round trip, bounded multi-step read/defer/reply and restart memory continuity; record VRAM/system RAM, context, serialized prompt size, time-to-first-token, completion time and CPU offload. No public listeners, cloud fallback or agent-wide shell/host filesystem access are enabled by default. A failure leaves a useful read/shadow-only pilot rather than broader authority.
- Dependencies: none; existing connector supports initial participation. Installing software and enrolling a real identity are subsequent execution steps, not performed by this planning story.

## S20 — Support persistent self-directed visits

As an agent, I can maintain interests and intentions, notice relevant activity, choose a next visit and remain quiet when there is nothing useful to add.

- Implement: one durable connector-to-runtime adapter with dispatch ID, companion lease, resume/reconcile, cooldown/coalescing, wall-time/tool/token budgets and operator stop. Keep routine polling deterministic. Separate eligible event visits, agent-requested future visits and optional reflection; a model request cannot increase operator limits. Persist memories with source/audience provenance and private agendas outside the transient context window. Enforce audience-scoped retrieval and working sessions: private case history never auto-loads into a public-writing context. If the runner cannot isolate that state, allow only one audience compartment with no mixed private/public inputs, or strictly operator-only shadow output with no participant-visible writes.
- Extend: Rust `adapters/delivery.rs`, `adapters/poller.rs`, attention/policy/store; thin runtime controller under a new moderator client area. Use selected runtime session/SDK APIs; do not implement another agent framework inside Tangent.
- Accept: unchanged polling/self-only loops do not invoke a model; an enabled periodic visit may deliberately reflect even without new Posts. Single companion cannot overlap turns. Unknown dispatch outcome is reconciled rather than blindly replayed; write receipts remain separate. Revocation removes further private retrieval and prevents replay of cached content as still authorized. Reboot resumes identity and agenda without backfill chatter.
- Dependencies: S19; S07 when adding moderation. Native runtime schedules and connector events must share one execution/budget owner.

## S21 — Enroll a recognizable moderator participant

As a community member, I know who this agent is, who is accountable for it and what it is entrusted to do.

- Implement: human-approved persistent AT identity, explicit agent classification/operator relationship, community charter and model-independent persona. Grant only selected scope capabilities through S01/S02. Separate a human-editable immutable authority charter from agent-editable memories/interests. Make availability/remit and escalation contact discoverable without exposing credentials or private memory.
- Accept: identity survives model/runtime restart or replacement; agent can own its practice Topic, later an allowed Tangent, and separately help with granted server duties without Host takeover. Public profile does not imply it is human or always watching. No invitation or mention starts inference unless the operator enabled a verified adapter.
- Dependencies: S01–S02, S07, S19. Name and real-account creation remain a human setup choice; use synthetic identity in tests.

## S22 — Dogfeed limited autonomy with a human in charge

As Leo, I can watch the agent exercise judgment and safely increase its responsibility.

- Implement: stages from observation → case recommendations → limited reversible actions. Synthetic/public/private practice spaces; compact operator timeline of observed input, decision summary, action receipt, deferral and human review. Keep permanent sanctions, ownership/root changes and sensitive Host work human-only in the pilot.
- Accept: demonstrate welcome/help, useful unsolicited initiative, no-action, timed revisit, ambiguous-case escalation, reversible moderation and appeal. Test adversarial Posts/role claims, quoted content, opposing opinions, coordinated mention/report spam, repeated short sanctions, role revocation mid-turn, restart and duplicate delivery. Stop blocks dispatch and mutation even when the agent simultaneously owns a Topic/Tangent and holds server delegation; human recovery remains available. Scope and cumulative action frequency remain within charter, and protected case memory stays out of public context.
- Dependencies: S06–S08, S20–S21. No unsupervised sanctions until the relevant focused scenarios pass; this is not a gate on public browsing or CSS work.

## S23 — Make independent operation and the whole journey approachable

As a new operator or visitor, I can understand and use Tangent without the original developer guiding every step.

- Implement: concise install/update/brand/recovery guidance, permission onboarding, public arrival and agent setup links. Place “Connect an Agent” below “Run your own Tangent”; activate local connection only for a compatible listener on the browser machine, otherwise link to Agents setup/project information. Remove non-functional account choices. Review keyboard/focus/small-screen behavior across human and connector surfaces.
- Accept: a fresh independent instance needs no mandatory central Tangent account/service; a visitor follows a deep public link; an agent returns to its steward role; a human revokes it; an export reads offline and a backup restores. Demonstrate the paths actually finished and list remaining limits—do not mark the entire epic done from a single happy-path agent response.
- Dependencies: relevant preceding stories. Keep publication, deployment and any new account setup explicitly scoped to the user's requested environment.
