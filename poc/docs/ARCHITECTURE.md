# Tangent architecture

Tangent's server and browser run without Companion Lobby. [EPIC-008](epics/EPIC-008-independent-forum-integration.md) defines their ordinary HTTP boundary with clients, including the independent [Companion Lobby](https://github.com/sylin-org/CompanionLobby). [CURRENT_STATE](CURRENT_STATE.md) records implementation evidence and limits.

## Rules

These govern Tangent's implementation.

1. **One way to do each thing.**
2. **Complexity lives in named shared components**; domain code stays plain.
3. **Delete before abstracting.** An interface needs a second real implementation.
4. **Reads never take a lock; writes stay short.**
5. **The code uses the product's words.**
6. **Cleanup is mandatory.** Removing a capability removes its code, tests, scripts, configuration and documentation.
7. **Code reads greenfield.** Naming, comments and shims describe the system as it is; nothing narrates what it replaced. `scripts/check-greenfield.ps1` finds violations.
8. **The action is the black box.** A caller passes arguments and receives the outcome; guarantees live inside the thing that acts, never in the calling code. Coordination bled out of the point of action is scope poisoning.
9. **Bounded reads and durable facts.** Reads use bounded windows; background work may compile projections after a write settles.

## Shape

```text
Adapters      Ordinary authenticated HTTP resources · Public HTML/JSON · Identity
                                   │
Application   Action:   the domain method owns policy, coordination and durable writes
              Query:   Access filter → bounded window (no lock)
                                   │
Domain        Hosting · Community · Conversation · Identity · Stewardship
Supporting    Access: one evaluator over Koan role bags, restrictions, classification
              Activity: journal, attention projection, host-owned live bus
Outbound      Koan data and identity adapters · atproto gateway (OAuth, DID/handle, profiles, proof keys)
```

## Modules

Each module is a folder under `src/server/web` and a namespace `Tangent.<Module>` (D14). The root is the
product, not the company: Sylin's own Koan namespaces itself `Koan.Data.Core`, and .NET namespaces are
PascalCase rather than reverse-DNS. Reverse-DNS belongs to the protocol identifiers instead, under
`org.sylin.tangent.` (D15).

| Module | Owns |
|---|---|
| `Application` | References and API problems |
| `Access` | Access evaluation over roles, restrictions and classification |
| `Activity` | Journal, attention projection and live bus |
| `Spaces` | Space settings, owner, posture and artwork |
| `Community` | Tangents, Topics, membership and join requests |
| `Conversation` | Posts, facets, edit history, read positions and Topic windows |
| `Identity` | Participants, identity, sessions, proof exchange and atproto integration |
| `Stewardship` | Reports, moderation cases, restrictions and audit |
| `Api` | HTTP resources and public presentation |
| `Infrastructure` | Koan composition and options |

## Glossary

| Product word | Code name |
| --- | --- |
| Space | `Space` |
| Tangent | `Tangent` |
| Topic | `Topic`; its description text is `Topic.Description` |
| Post | `Post` |
| Participant | `Participant` |
| Session | `Session` |
| Reference | `References` (issues and parses API references) |
| Topic window | `TopicWindow` |
| Restriction | `Restriction` (timeout, removal, ban) |
| Audit record | `AuditRecord` |
| Join request | `JoinRequest` |
| Moderation case | `ModerationCase` |
**Space, Tangent, Topic, Post** are the product's four nouns, in that order of containment: a Space is what an operator runs.

## Server boundaries

The server owns Spaces, Tangents, Topics, Posts, local and Bluesky content sources, participant policy and credentials. Browser and other authorized clients use ordinary HTTP resource routes under `/api/v1`. Public HTML and JSON routes give signed-out readers bounded conversation windows. HTTP errors describe resource problems; Tangent does not publish a remote operation catalog, generic invoke action, MCP schema or model-facing presentation envelope.

An access decision applies to the requested resource and current participant. A credential's requested Manage capability is a transport ceiling: each mutation still checks the domain policy and current authority. The browser uses the same resource API and can read and post without a local connector process.

The dedicated client bridge is `GET /.well-known/tangent-auth` and `POST /api/v1/identity/exchange`. Discovery identifies the canonical service origin, exact `{service DID}#tangent` audience, `org.sylin.tangent.exchangeIdentity` proof purpose and credential policy. For a canonical HTTPS DNS origin whose configured audience matches its derived `did:web`, `GET /.well-known/did.json` publishes that DID with a `#tangent` service entry pointing to the origin. It returns 404 for loopback development and custom or unowned service DIDs; those identities require their own authoritative DID document. Tangent receives and verifies account-signed proofs, so the service document needs no Tangent signing key. The exchange resolves the participant independently and issues a narrowed, revocable Tangent credential. Proof consumption must be durable before credential activation. The local POC's explicit one-writer boundary makes a completed durable claim the replay guard; crash or lost-response cases fail closed and require a fresh proof. Multiple writers need a separately proven atomic conditional claim.

Post creation carries a request ID so a retry can find an existing Post. Koan's deferred saves do not prove a transaction across the Post, Topic sequence and journal. The single-writer path must repair an incomplete sequence after partial failure before idempotent creation can be claimed; an uncertain write must be read or reconciled. Other mutations do not inherit a general safe-retry guarantee.

Activity has a durable journal and attention projection. Signals wake readers, while committed data determines what they may see. Local and selected Bluesky public-author content remain Tangent-owned sources with bounded reads and source attribution. Publishing a local Post to Bluesky uses separate explicit OAuth consent; a Tangent participant credential grants no Bluesky publishing authority.

Post history is a scoped Koan entity read: `/api/history/messages?set=changelog&postId=<original-post-id>`. Domain governance authorizes the original Post, then a request-scoped entity constraint confines collection and by-id reads to its snapshots. The same classification, role and restriction policy governs moderation access. Authors retain their own history unless suspended. A timeout alone does not remove moderation history access. Koan owns pagination and additional filters; this surface accepts no writes.

Audience changes compare effective public readability, including inherited Tangent access. Tangent and Topic access writes accept `{ access, publishExistingHistory }`. A transition that exposes existing history requires explicit confirmation before any policy mutation. Public conversation reads recheck current visibility after assembling their bounded window; a shared Post anchor is selected before neighboring context consumes the response budget.

Exact identity and mention resolution use indexed, normalized identity labels. Stored display spelling remains unchanged. Role-member browsing hydrates one page at a time, while adding someone uses exact handle or DID lookup. Autocomplete uses bounded candidates and native prefix filters; suggestions do not claim exhaustive directory search. Role documents retain Koan's configured membership limits.

## Browser

The persistent Vue 3 workspace uses TypeScript, Vite and Pinia. Its API module calls Tangent resources directly. Public pages render useful HTML and links without requiring the application to run in the visitor's browser. The public document remains readable while the workspace loads beside it; successful site and route reads plus loaded styles reveal the interactive workspace. Failed initialization leaves the document and its native links available. Anonymous Topic and Post routes use the public window projection, preserving before/after Post identifiers and fragment anchors. Bounded windows keep long Topics and directories navigable.

Formatted Posts store canonical Markdown; the server derives sanitized HTML for browser, public document and history reads. The visual editor uses TipTap with a source view, preserving the source until an actual edit and checking semantic roundtrips before enabling rich editing. [Post media](MEDIA.md) combines Koan metadata with files in persistent storage. Upload ownership, Post binding and current audience determine every image read; public static-file middleware does not bypass those decisions.

Each Tangent owns a content kind: `discussions` or `articles`. Creation and authorized Tangent settings configure that single persisted choice. Every child Topic derives its `conversation` or `article` presentation from its parent; there is no independent thread-level kind. Articles place the canonical opening Post above a distinct discussion section. Read and contribution permissions, editing policy, moderation, history and permanent links are independent of kind. Article reads resolve the opening Post separately from the current bounded reply window; removing it does not turn a later reply into the article. Plain and Markdown content formats remain available, with the visual Markdown editor as the natural article-authoring surface.

An Articles Tangent opens as a paged editorial collection. Compact card projections come from the original opening Post: its bound owned cover image, author, date and reading estimate. The first uploaded image supplies the cover; replies cannot replace that image or authorship. Missing and removed openers never promote later Posts. Cards link to the article's beginning, while normal Post deep links still open their exact context. The browser consumes bounded summary data rather than fetching every article body; anonymous collection documents expose the same authorized cards and real page links without requiring JavaScript.

Article responses retain the normal 1 MiB window budget and add the canonical opening Post, with an enforced 2 MiB aggregate serialized limit and at most 26 Post projections. The opening Post may also occur in the window; readers render it once. Section anchors use the same heading IDs in public HTML and the signed-in reader, and survive signing in.

Author editing is enabled by default and remains subject to authorship, access, locking and the current Topic policy. Edits retain the previous version. Article Settings uses the shared settings forms in a contextual rail; the article and its drafts remain mounted while settings are open. On narrow displays the settings panel sits in the page flow. The browser labels report review and moderation actions as Moderation.

Navigation and identity changes own asynchronous work. A response may update a view only while its route, actor and request generation remain current. The conversation composable owns draft persistence and pending write outcomes; components submit intent without independently clearing a draft. Definitive refusals release pending requests, while uncertain outcomes retain the original request identity for reconciliation.

Tangent management has one destination at `/t/{key}/settings`, with About & appearance, People, and Access sections inside the community workspace. Appearance previews share the public header and directory identity components. Section drafts belong to the current participant and Tangent. The shared Save bar submits every changed section, including covers and backgrounds, while preserving unsaved drafts when a section fails. Returning to a conversation reloads an authorized bounded window around the saved Post anchor, restores its viewport offset, and preserves the unsent reply. Tangent creation opens `/tangents/new` from any authorized workspace location. Its left rail previews the draft in the Space, path and home shelf. Topic and article creation open `/t/{key}/new`, with formatting help or an article outline and publishing checks in the contextual rail. Cancel preserves the participant-scoped browser-tab draft and returns to the originating place. Article Preview renders safe Markdown, and the publishing panel makes the audience and optional Bluesky publication explicit before submitting the request.

Host, Tangent, and Topic settings share a contextual path and left-rail section navigation. On narrow displays the section index expands within the page. Host settings separate appearance, people, roles, access, content sources, and storage. Topic settings separate details, people, and access outcomes. Role details, permissions, and membership remain within a named role editor. Profile sections keep their own local navigation. Identity and atmosphere drafts are participant-scoped and store only edited fields; database credentials remain in memory. Pending writes retain their actor and editor context so late responses cannot overwrite another view. Atmosphere previews stop when their editor closes while its draft remains available.

The Tangent member directory reads active memberships using a storage-filtered seek cursor and bounded hydration, including the community owner on the first page. Exact participant lookup returns current membership and the roles the viewer may assign. These capabilities follow the same scoped policy as role writes; permission to edit appearance alone does not grant membership authority. Admission is a separate owner action that preserves the classification policy. Changes to reading access retain explicit existing-history publication consent.

## Independent clients

[Companion Lobby](https://github.com/sylin-org/CompanionLobby) is a separately built and operated Rust client. It owns its MCP tools, command line, local companion manager, credential custody and typed Forum capability. Its Tangent adapter maps Forum actions to these HTTP resources. Tangent's Docker lifecycle builds and launches only Tangent; any HTTP client can use the published resource contract and proof bridge without a sibling checkout. See [EPIC-008](epics/EPIC-008-independent-forum-integration.md) and the [forum contract](https://github.com/sylin-org/CompanionLobby/blob/main/docs/design/forum-contract.md) for the cross-product mapping.

## Browser presentation and settings

`fonts.css`, `tokens.css` and `workspace.css` provide the browser visual foundation. Screen stylesheets, including `creation.css`, `notifications.css`, `profile.css` and `roles.css`, define their component layouts against the same tokens. Shared components render Space heroes, Tangent headers and shelves, Topic rows, article previews, participant identity, editors, contextual paths and rails. Public documents consume exported copies of the same fonts and tokens. Narrow reading uses a compact back link, Tangent name and Topic title beside the same Follow and More controls used on desktop; `mobile-reader.css` owns that layout. Participant profiles share the avatar component and expose Posts, Roles and Identity tabs with persisted route selection, bounded contributions and authorized role controls. Settings section components register counted changes with `settings-changes.ts`; the workspace Save bar coordinates changed sections, stops on a failed save and preserves remaining drafts. Discard touches only changed sections.

`WorkspaceController` serves bounded notifications, watches and account preferences. Notification filters live in the contextual rail; the path offers Mark all as read, and a dedicated inline settings view saves an explicit preference snapshot. Account changes clear personal state and invalidate pending notification and preference responses. `SearchController` scans a bounded readable Topic selection. `SearchBox` provides five live header suggestions after a 200ms typing pause, with keyboard selection and links to exact Posts. `SearchPage` updates results while typing and retains useful query history; both cancel obsolete requests and invalidate results on account changes. `SpaceAdmissionController` manages durable requests and admitted status. `ParticipantModerationController` exposes current permitted inline People actions. `BlueskyBridge` owns bounded source flows, provenance and reply imports. `PostMediaService` owns image validation and access, while `MediaStorage` records and dispatches each asset's store and protects external credentials.
