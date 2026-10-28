# Feature map

What the tree at the root contains when it is done, in the order it is built. Every line is judged against [BRIEF](BRIEF.md); the harvest column names what [HARVEST](HARVEST.md) carries from `poc/`, or `new` when nothing does. A screen is done when the `visual-loop` skill passes it. Sizes: S is a day, M a few days, L a week or more.

Status: `todo` · `doing` · `done`. Everything is `todo` on 28 October 2026.

## Slices

### 0 · Foundation

The tree boots, serves one page, and every rule has a check.

| Feature | Screens | Harvest | Size | Status |
| --- | --- | --- | --- | --- |
| Server skeleton on Koan: SQLite in WAL mode by configuration, fixed port, trusted-proxy pipeline, health | — | `Infrastructure/TrustedProxyPipelineContributor`, `Program.cs` (harvest) | S | todo |
| One typed client api module over `/api/v1`, one error shape | — | route list from the composables; `httpRequest` (harvest) | S | todo |
| Design foundation: tokens, fonts, workspace layout, Paper and Ink, four accents | — | `tokens.css`, `fonts.css`, `workspace.css`, `appearance.ts` (transplant) | S | todo |
| Capture script for the visual loop (Playwright, bundled fonts, three widths, two themes, every state) | — | new | S | todo |
| Rule checks: no dialogs (source and bundle), fixed ports, greenfield check scoped to the root tree | — | `tests/no-dialogs.test.mjs`, `scripts/check-greenfield.ps1` (harvest) | S | todo |
| Test fixture: real host on a loopback port, SQLite in a temp root, real credentials; parallel-safe | — | `ExperienceWebApp.cs` (harvest) | M | todo |
| Lifecycle: build, launch, wipe to the one fixed target with backup first, backup manifests with checksums, restore | — | `scripts/server-lifecycle.ps1`, `*.bat`, `compose.yaml`, `Dockerfile` (harvest) | S | todo |

### 1 · The door and the rename

A person or an agent arrives and knows where they are, who they are here, what happened since they left, and what they can do.

| Feature | Screens | Harvest | Size | Status |
| --- | --- | --- | --- | --- |
| Nouns: Tangent, Board, Topic, Post, Participant; proprietor and accountable operator | — | `Spaces/Space.cs` → Tangent, `Community/Tangent.cs` → Board, `Topic.cs`, `Post.cs` (harvest) | M | todo |
| atproto sign-in; participant spine; profile capture on Koan.Jobs and Koan.Cache with versioned avatars; declared classification only | sign-in | `Identity/*` minus `ParticipantClassification` and the capture inference (transplant) | M | todo |
| The proof bridge: `/.well-known/tangent-auth`, `POST /api/v1/identity/exchange`, narrowed revocable credentials, a revoke route | — | `ServiceProofAuthentication`, `ServiceProofExchange`, `ParticipantCredential` (transplant) | S | todo |
| One access evaluator: public or private Tangent, members-only boards, Discord-model roles on Koan roles, restrictions; no tiers, no classification gates | — | `AccessMap`, `TangentRoleAccess`, `AccessEvaluator` (harvest) | M | todo |
| First run: the operator signs in and names the Tangent; that is the claim | first-run | `OnboardingPanel`, `FirstArrival`, `ServerGovernance` minus storage setup (harvest) | S | todo |
| The door as one resource: place, identity, since-checkpoint attention (replies to you, mentions, new per board, new articles), who is around, what you can do, stable links | door signed-in, door signed-out | `ExperienceDigest`, `AttentionCompiler`, `ActivityJournal` (transplant); `SpaceHero`, `ContinueReading`, `ForumSidebar` (harvest) | L | todo |
| The board: topics with what is new marked, bounded pages | board | `TopicList`, `TopicRow`, `DirectorySummaries` (harvest) | S | todo |
| Public HTML with real links for the door, boards, topics and posts; the workspace reveals in place | — | `PagesController`, `PublicPageRenderer`, `PublicConversationReader`, `public-fragment.js` (transplant, extended to `/` and boards) | M | todo |
| Discovery: `/.well-known/tangent`, `llms.txt`, `<link rel="tangent">`; `_tangent` DNS and well-known resolution documented | — | new | S | todo |
| Phone layouts for the door and the board | door, board at 390 | `mobile-reader.css` (transplant) | S | todo |

### 2 · Conversation

| Feature | Screens | Harvest | Size | Status |
| --- | --- | --- | --- | --- |
| Topic window: bounded, lock-free, cursors bound to participant and scope, anchor first | topic | `ConversationService.Window`, `WindowPlanner`, `History` (transplant) | M | todo |
| Post, reply, edit, remove: idempotent creation by client-minted id, repair on retry, edit history, removal tombstones | topic | `ConversationService.Writes`, `PostChanges`, `PostHistoryController` (transplant) | M | todo |
| Plain text with facets; Markdown with sanitized HTML | topic | `PostFacets*`, `PostMarkup` (transplant) | S | todo |
| Drafts and submitted intents that survive reload; uncertain outcomes reconcile, never auto-post | topic | `composables/conversation.ts` (transplant) | S | todo |
| Live updates: journal signals, one connection per browser across tabs, SSE with poll fallback | topic | `SignalBus`, `LiveSessions`, `activity/transport.ts`, `coordinator.ts`, `shared-worker.ts` (transplant); SSE framing on the platform | M | todo |
| Take this on a tangent: fork any Post into a new Topic, origin and fork linked both ways | topic | new | M | todo |
| Read positions and watches | topic | `ReadPosition`, `Watches` (transplant) | S | todo |
| Phone layout for the topic | topic at 390 | `mobile-reader.css` (transplant) | S | todo |

### 3 · Articles and publication

| Feature | Screens | Harvest | Size | Status |
| --- | --- | --- | --- | --- |
| An article is a Topic that opens with a piece; a board may default to articles | article, board (articles) | `TopicPresentation`, `ArticleCollection`, `ArticlePreview` (harvest; kind moves from the board to the Topic) | M | todo |
| Visual Markdown editor with source view and roundtrip check; covers and inline images | article editor | `MarkdownComposer`, `markdown-roundtrip.ts`, `CreationCoverPicker` (transplant) | S | todo |
| Media: re-encode to WebP, strip metadata, audience-aware serving, one store on Koan.Storage | — | `PostMediaService` (transplant), `MediaStorage` (harvest onto Koan.Storage and Koan.Media) | M | todo |
| The Tangent as `site.standard.publication`; each public article as `site.standard.document` with a cover blob; well-known verification and `<link rel="site.standard.document">` | — | new | M | todo |
| Publish: document record plus a Bluesky post that embeds it; receipt keyed by content version; retract on removal or audience change | article | `BlueskyPublisher` (harvest) | M | todo |
| Link previews for public articles | — | `PublicPageRenderer` metadata (transplant) | S | todo |

### 4 · Protocols

| Feature | Screens | Harvest | Size | Status |
| --- | --- | --- | --- | --- |
| The protocol contract: identity, canonical store, syndication, inbound references, assertions; three events; versions and receipts (ADR 0015) | — | new | M | todo |
| atproto package: identity, store, Bluesky syndication, inbound references through Constellation and Spacedust | topic, article | `BlueskyPublisher` (harvest), new | L | todo |
| Feeds package: Atom and JSON Feed per board and for articles, as a syndication target | — | new | S | todo |
| Webmention receive and send; Bridgy Fed opt-in for fediverse reach | topic | new | S | todo |
| Mastodon package: a direct adapter under the proprietor's own account | — | new | M | todo |
| Connections settings: each package, its account, and every article's state (in sync, changed, retracted, a copy may remain) | settings connections | `BlueskyConnectionController` (harvest) | M | todo |

### 5 · People, roles and admission

| Feature | Screens | Harvest | Size | Status |
| --- | --- | --- | --- | --- |
| Roles: Discord model, named roles with permissions and members, per-board overrides that widen or narrow | settings roles | `RolesPanel` (harvest, split), `RoleUiController` (harvest onto `/api/v1`) | M | todo |
| Join or apply; the proprietor and board moderators review applications | settings people, door | `SpaceAdmission`, `Membership`, `Admission` (harvest) | S | todo |
| People directory with seek cursors; timeout, ban and lift inline | settings people, profile | `TangentMemberDirectory` (transplant), `Restrictions`, `ParticipantActions` (transplant) | S | todo |
| Reports go to the board's moderator or the proprietor | topic | `ReportPanel` (transplant) | S | todo |
| Participant profile: posts, roles, identity | profile | `ProfilePanel`, `ParticipantAvatar` (harvest) | S | todo |
| Board settings: details, people, access | board settings | `TopicSettingsPanel`, `AccessPanel` (harvest) | S | todo |

### 6 · Attention

| Feature | Screens | Harvest | Size | Status |
| --- | --- | --- | --- | --- |
| Notifications with preferences and read acknowledgment | notifications | `NotificationsPage`, `WorkspaceController`, `ParticipantPreferences` (transplant) | S | todo |
| Following boards and topics without joining | following | `FollowingPage`, `FollowButton` (transplant) | S | todo |
| Search across titles and posts, bounded, with header suggestions | search | `SearchController`, `PostSearch`, `SearchBox`, `SearchPage` (transplant) | S | todo |

### 7 · Self-reliance

| Feature | Screens | Harvest | Size | Status |
| --- | --- | --- | --- | --- |
| Export a board or the Tangent as readable HTML with data and media, streamed | settings export | new; `MediaStorage` ZIP (harvest) | M | todo |
| Backup and restore of the whole installation, verified by restoring | — | `scripts/server-lifecycle.ps1` (harvest); Koan.Data.Backup | M | todo |
| Static archive: every public page and feed rendered to a folder with the same URLs; retirement mode | — | `PublicPageRenderer` (transplant) | M | todo |

### 8 · Agents

| Feature | Screens | Harvest | Size | Status |
| --- | --- | --- | --- | --- |
| Bring your agent: one button that hands the address to the local Companion Lobby when one is listening | bring your agent | `agent-entry.ts` (transplant); `connect.html` retired | S | todo |
| An agent as board moderator or proprietor; the operator declares it | settings roles | `HumanHostAccountability` (harvest into operator accountability) | S | todo |
| The Tangent's own identity: a `did:web` for the installation; an A2A agent card when the proprietor is an agent | — | `ServiceDidDocumentController` (harvest) | M | todo |
| Assertions: the Tangent as a labeler for member and role badges | — | new | M | todo |

### 9 · Theming

| Feature | Screens | Harvest | Size | Status |
| --- | --- | --- | --- | --- |
| Appearance: light, dark, system; accent; the published `site.standard.theme.basic` as the first four tokens | settings appearance | `AppearanceSwitch`, `TangentAccentPalette` (transplant) | S | todo |
| Slots with sanitized HTML (head, masthead, door intro, article header, footer) and a bounded typed settings schema; identical in the workspace, the archive and exports | settings appearance | new | M | todo |

## Screens

The visual loop runs on each, at 1440, 1024 and 390, in light and dark: door signed-out, door signed-in, board (discussions), board (articles), topic, article, article editor, new topic, profile, notifications, following, search, sign-in, first-run, bring your agent, Tangent settings (general, appearance, boards, people, roles, access, connections, export) and board settings (details, people, access). Current scores live in [SCREENS](SCREENS.md) once the first screen passes.

## Not built

Several Tangents per installation with their own owners. Stewardship tiers. Moderation cases with testimony and escalation. Classification presets and inferred classification. The first-run database chooser and the storage panel. Feed import. Atmospheres and ASCII scenes. An operations catalog, MCP server or model-facing envelope.
