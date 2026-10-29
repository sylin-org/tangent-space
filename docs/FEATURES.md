# Feature map

What the tree at the root contains when it is done, in the order it is built. Every line is judged against [BRIEF](BRIEF.md) and shaped by [ARCHITECTURE](ARCHITECTURE.md); the harvest column names the unit [HARVEST](HARVEST.md) carries, or `new` when nothing does; the Koan column names the package used or the brief the line waits on. A screen is done when the `visual-loop` skill passes it. Sizes: S is a day, M a few days, L a week or more.

Status: `todo` · `doing` · `done`. Everything is `todo` on 28 October 2026.

## Koan work the map depends on

| Brief | Koan home | Needed by |
| --- | --- | --- |
| 1 · SQLite posture (WAL, busy timeout, immediate write transactions) and a canonical base URL | `Koan.Data.Connector.Sqlite`, `Koan.Web` | slice 0 |
| 2 · Scoped overwrites with deny, resolved in order, explained | `Koan.Identity` | slice 1 (Tangent composes its own step beside Koan's facts until then) |
| 3 · atproto content: records, blobs, resolution, AppView reads; service-auth verification; `did:web` on the issuer keys | `Koan.Web.Auth.Connector.Atproto`, Trust, Auth.Server — names assigned by Koan | slice 3 and the bridge in slice 8 |
| 4 · Lexical search over entity fields | `Koan.Data` | slice 6 |

Built first in Tangent in Koan's shape, lifted into Koan when proven: the HTML projection, the durable journal with checkpoints, feeds, the static archive, the agent descriptor, Markdown rendering, a snapshot-on-save verb for the audit partition.

## Slices

### 0 · Foundation

The tree boots, serves one page both ways, and every rule has a check.

| Feature | Screens | Harvest | Koan | Size | Status |
| --- | --- | --- | --- | --- | --- |
| Server skeleton: `Sylin.Koan.App`, SQLite in WAL with immediate write transactions, fixed port, trusted-proxy pipeline, base URL under a path prefix, health | — | `TrustedProxyPipelineContributor` (harvest) | brief 1 | S | done |
| The writer: one queue, one loop, one transaction per command, sequences from high-water marks; the Koan transaction proven inside a loop | — | `ConversationService.Writes` ordering (harvest) | `EntityContext.Transaction` | M | todo |
| The HTML projection: one DTO rendered through a Razor component or as JSON by `Accept`; fragments by header | — | `PublicPageRenderer` (harvest) | lift later | M | done |
| Island bootstrap: `data-island` mounting into server HTML, one bundle, one event stream per page, fragment patch | — | `public-fragment.js` (harvest) | — | S | todo |
| Design foundation: tokens, fonts, layout, Paper and Ink, four accents | — | `tokens.css`, `fonts.css`, `workspace.css`, `appearance.ts` (transplant) | — | S | done |
| Capture script for the visual loop: Playwright, bundled fonts, three widths, two themes, every state | — | new | — | S | todo |
| Rule checks: no dialogs, language, greenfield scoped to the root tree, fixed ports | — | `tests/no-dialogs.test.mjs`, `scripts/check-greenfield.ps1` (harvest) | — | S | done |
| Test fixture: the real host on a loopback port over a temp folder, once under a path prefix; parallel-safe | — | `ExperienceWebApp` edge cases (harvest) | `Sylin.Koan.Testing.Hosting` | S | done |
| Lifecycle: one published binary and one data folder; build, launch, backup by `VACUUM INTO` and copy, restore; compose for Docker | — | `scripts/server-lifecycle.ps1`, `*.bat`, `compose.yaml`, `Dockerfile` (harvest) | — | S | todo |
| Lexical search proven through the SQLite connector | — | — | brief 4 | S | todo |

### 1 · The door and the names

A person or an agent arrives and knows where they are, who they are here, what happened since they left, and what they can do.

| Feature | Screens | Harvest | Koan | Size | Status |
| --- | --- | --- | --- | --- | --- |
| Nouns and tables: `TangentSpace`, `Tangent`, `Post` with the root as Topic, `Participant` facet, `Overwrite`, `Event`, `Checkpoint` | — | `Space`, `Tangent`, `Topic`, `Post`, `ActivityJournal`, `ReadPosition` (harvest) | `Sylin.Koan.Data.Core` | M | todo |
| atproto sign-in; the person reconciled from the DID; the participant facet; profile capture as a job; declared kind only | sign-in | `Identity/*` minus classification and the capture inference (harvest) | `Sylin.Koan.Web.Auth.Connector.Atproto`, `Sylin.Koan.Identity` | M | todo |
| Access: Koan roles seeded (`everyone`, member, moderator), overwrites, `Capabilities.For`, `can` on every resource, `gate` from the explainer | — | `TangentPermissions` catalog, `AccessEvaluator` reason table (harvest) | `Sylin.Koan.Identity`; brief 2 | M | todo |
| First run: the operator signs in, names the Tangent Space, sets the base URL, declares proprietor and operator; the page prints the site's pointer snippets | first-run | `OnboardingPanel`, `FirstArrival`, `ServerGovernance` (harvest) | — | S | todo |
| The door as one resource: for a visitor, whose place this is, what is featured, what is alive; signed in, since my checkpoints — replies to me, mentions, new per Tangent, new articles — who is around, what I can do | door signed-out, door signed-in | `ExperienceDigest`, `AttentionCompiler` (harvest); `SpaceHero`, `ContinueReading`, `ForumSidebar` (harvest) | — | L | todo |
| The Tangent page: topics with what is new marked, bounded | tangent | `TopicList`, `TopicRow`, `DirectorySummaries` (harvest) | — | S | todo |
| Discovery: `tangent.json`, `llms.txt`, `<link rel="tangent">` on the site, `/.well-known/tangent` at the site origin, `_tangent` TXT; the reverse-proxy rule for a path install | — | new | — | S | todo |
| The delegated agent door: the consent page for `Koan.Web.Auth.Server`, the Device flow, a pre-registered public client; `llms.txt` says how | consent | new | `Sylin.Koan.Web.Auth.Server`, `Sylin.Koan.Security.Trust` | S | todo |
| Phone layouts for the door and the Tangent | door, tangent at 390 | `mobile-reader.css` (transplant) | — | S | todo |

### 2 · Conversation

| Feature | Screens | Harvest | Koan | Size | Status |
| --- | --- | --- | --- | --- | --- |
| Topic window: bounded, lock-free, cursors are sequence numbers, anchor first | topic | `ConversationService.Window`, `WindowPlanner`, `History` (harvest) | — | M | todo |
| Post, reply, edit, remove through the writer: TID ids, pre-edit snapshots in the audit partition, visible tombstones | topic | `PostChanges` snapshot chain (harvest) | audit partition | M | todo |
| Markdown body, sanitized HTML, mentions as identity-bearing links resolved at render | topic | `PostMarkup` (harvest) | — | S | todo |
| The composer island: drafts in `localStorage` by scope; an uncertain submit reconciles by `GET` of the minted Id; never auto-posts | topic | `composables/conversation.ts` intents (harvest) | — | S | todo |
| Live: the event stream, one per page, fragment patch; the door as the poll | topic | `activity/transport.ts` framing (harvest) | `Sylin.Koan.Web.Sse`, `Sylin.Koan.Communication` | M | todo |
| Take this on a tangent: choose the Tangent, a root Post with an origin, linked both ways | topic | new | — | S | todo |
| Checkpoints and the watch flag | topic | `ReadPosition`, `Watches` (harvest) | — | S | todo |
| Reports: a Post taken on a tangent into the moderators' Tangent, with the origin | topic | `ReportPanel` (harvest) | — | S | todo |
| Phone layout for the topic | topic at 390 | `mobile-reader.css` (transplant) | — | S | todo |

### 3 · Articles and publication

| Feature | Screens | Harvest | Koan | Size | Status |
| --- | --- | --- | --- | --- | --- |
| An article is a root Post of kind article with a cover; a Tangent may default to articles | article, tangent (articles) | `TopicPresentation`, `ArticleCollection`, `ArticlePreview` (harvest) | — | M | todo |
| The article editor island: visual Markdown with source view and roundtrip check; cover and inline images | article editor | `MarkdownComposer`, `markdown-roundtrip.ts`, `CreationCoverPicker` (transplant) | — | S | todo |
| Media on Koan: content-addressed, re-encoded derivatives, metadata stripped, audience from the referencing Post | — | `PostMediaService` audience rules (harvest) | `Sylin.Koan.Media.Core`, `Sylin.Koan.Storage.Connector.Local` | S | todo |
| Social cards for articles and topics | — | — | `Sylin.Koan.Web.OpenGraph` | S | todo |
| Connections: the row per participant, the dispatch job as a log reader with a cursor, receipts by version, attention on repeated failure | settings connections | `BlueskyPublisher` receipt idea (harvest) | `Sylin.Koan.Jobs` | M | todo |
| The atproto package: the Tangent Space as `site.standard.publication` under the proprietor's Connection; each public article as `site.standard.document` with the cover blob and a Bluesky post embedding it; `<link rel="site.standard.document">`; retract on removal or audience change | article | `BlueskyPublisher` mapping (harvest) | brief 3 | M | todo |

### 4 · Protocols

| Feature | Screens | Harvest | Koan | Size | Status |
| --- | --- | --- | --- | --- | --- |
| The protocol contract recorded from code as ADR 0016: `IConnectionAdapter`, `IReferenceSource`, `IAsserter`; the three events as a filter over the log | — | new | — | S | todo |
| Feeds as a Render output: Atom and JSON Feed per Tangent and for articles | — | new | lift later | S | todo |
| References on a Post: Constellation and Spacedust as an optional source; "elsewhere" on the topic page | topic | new | — | M | todo |
| Webmention: send on `post.created`, receive at an endpoint; Bridgy Fed opt-in | topic | new | — | S | todo |
| The Mastodon package: a status under the participant's own account; replies as references | — | new | — | M | todo |
| Connections settings: each Connection, its account, every Post's state derived from Receipt against Version | settings connections | `BlueskyConnectionController` (harvest) | — | S | todo |

### 5 · People, roles and admission

| Feature | Screens | Harvest | Koan | Size | Status |
| --- | --- | --- | --- | --- | --- |
| The role editor island over Koan's roles: Discord's model, members, permissions; the overwrites editor per Tangent | settings roles, tangent access | `RolesPanel` (harvest), `RoleUiController` rules (harvest) | `Sylin.Koan.Identity.Web` | M | todo |
| Join or apply; standing; the one knob "new accounts post after approval", age from the DID | settings people, door | `SpaceAdmission`, `Admission` (harvest) | — | S | todo |
| People directory with seek cursors; time out, ban and lift inline | settings people | `TangentMemberDirectory`, `Restrictions`, `ParticipantActions` (harvest) | — | S | todo |
| Participant profile: the PDS profile, roles, posts here, Connections, the person's grants from Koan | profile | `ProfilePanel`, `ParticipantAvatar` (harvest) | `Sylin.Koan.Identity.Web` | S | todo |
| Tangent settings: details, default kind, order, access | tangent settings | `TopicSettingsPanel`, `AccessPanel` (harvest) | — | S | todo |

### 6 · Attention

| Feature | Screens | Harvest | Koan | Size | Status |
| --- | --- | --- | --- | --- | --- |
| Notifications: events where I am the target, read by advancing the checkpoint; a few preferences | notifications | `NotificationsPage`, `ParticipantPreferences` (harvest) | — | S | todo |
| Watching: the flag on a checkpoint; one line on the door | door | `FollowButton` (harvest) | — | S | todo |
| Search across titles and bodies, bounded, with header suggestions | search | `SearchBox`, `SearchPage` (transplant) | brief 4 | S | todo |

### 7 · Self-reliance

| Feature | Screens | Harvest | Koan | Size | Status |
| --- | --- | --- | --- | --- | --- |
| Export a Tangent or the Tangent Space as readable HTML with data and media, streamed, through the one renderer | settings export | new | lift later | M | todo |
| Backup and restore of the folder, verified by restoring | — | `scripts/server-lifecycle.ps1` (harvest) | — | S | todo |
| Static archive: every public page and feed rendered to a folder with the same URLs; retirement mode | — | new | lift later | M | todo |

### 8 · Agents

| Feature | Screens | Harvest | Koan | Size | Status |
| --- | --- | --- | --- | --- | --- |
| Bring your agent: one button that hands the address to a listening Companion Lobby, which registers as a loopback client and runs the code flow | bring your agent | `agent-entry.ts` (harvest) | `Sylin.Koan.Web.Auth.Server` | S | todo |
| The DID-native door: an atproto service-auth token verified, exchanged for a bearer from Trust's issuer; revoke through the person's grants | — | `ServiceProofExchange` flow (harvest; verification is Koan's) | brief 3, `Sylin.Koan.Security.Trust` | S | todo |
| An agent as moderator or proprietor; the operator declared a person | settings roles | `HumanHostAccountability` (harvest into the declaration) | — | S | todo |
| The installation's identity: `did:web` on the issuer keys at `<base>/did.json`; an A2A agent card when the proprietor is an agent | — | new | brief 3 | S | todo |
| Assertions: the Tangent Space as a labeler for member and role badges | — | new | — | M | todo |

### 9 · Theming

| Feature | Screens | Harvest | Koan | Size | Status |
| --- | --- | --- | --- | --- | --- |
| Appearance: light, dark, system; accent; the published `site.standard.theme.basic` as the first four tokens | settings appearance | `AppearanceSwitch`, `TangentAccentPalette` (transplant) | — | S | todo |
| Slots with sanitized HTML (head, masthead, door intro, article header, footer) and a bounded typed settings schema; identical in the site, the archive and exports | settings appearance | new | — | M | todo |

## Screens

The visual loop runs on each, at 1440, 1024 and 390, in light and dark: door signed-out, door signed-in, tangent (discussions), tangent (articles), topic, article, article editor, new topic, profile, notifications, search, sign-in, consent, first-run, bring your agent, Tangent Space settings (general, appearance, tangents, people, roles, export), Tangent settings (details, access), connections. Current scores live in [SCREENS](SCREENS.md) once the first screen passes.

## Not built

Several Tangent Spaces per installation. Stewardship tiers. Moderation cases with testimony and escalation. Classification presets and inferred classification. A database chooser and a storage panel. Feed import. Atmospheres and ASCII scenes. An operations catalog, MCP server or model-facing envelope. A client router or store library. A table of credentials or of media. A shared worker for the stream.
