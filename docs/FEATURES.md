# Feature map

What the tree at the root contains when it is done, in the order it is built. Every line is judged against [BRIEF](BRIEF.md) and shaped by [ARCHITECTURE](ARCHITECTURE.md); the harvest column names the unit [HARVEST](HARVEST.md) carries, or `new` when nothing does; the Koan column names the package used or the brief the line waits on. A line is done when it works; until Leo calls the polish stage its screens are captured and listed in [SCREENS](SCREENS.md) as owing the visual gate, which the `visual-loop` skill holds them to at that stage. Sizes: S is a day, M a few days, L a week or more.

Status: `todo` · `doing` · `done`. Everything is `todo` on 28 October 2026.

## Koan work the map depends on

| Brief | Koan home | Needed by |
| --- | --- | --- |
| 1 · SQLite posture (WAL, busy timeout, immediate write transactions) and a canonical base URL | `Koan.Data.Connector.Sqlite`, `Koan.Web` | slice 0; in the pin since Koan `7ba505e49` |
| 2 · Scoped overwrites with deny, resolved in order, explained | `Koan.Identity` | slice 1; in the pin since Koan `943e6358a` (SEC-0021), Tangent resolving on it since package 1.10; members as rows and every bound on its reads since Koan `dae95ecea` (SEC-0030), package 5.7; roles in one order and who manages which since Koan `8c96c2e` (SEC-0031), package 5.1 |
| 3 · atproto content: records, blobs, resolution, AppView reads; service-auth verification; `did:web` on the issuer keys | `Koan.Web.Auth.Connector.Atproto`, Trust, Auth.Server — names assigned by Koan | slice 3 and the bridge in slice 8 |
| 4 · Lexical search over entity fields | `Koan.Data` | slice 6 |

Built first in Tangent in Koan's shape, lifted into Koan when proven: the HTML projection, the durable journal with checkpoints, feeds, the static archive, the agent descriptor, Markdown rendering, a snapshot-on-save verb for the audit partition.

## Slices

### 0 · Foundation

The tree boots, serves one page both ways, and every rule has a check.

| Feature | Screens | Harvest | Koan | Size | Status |
| --- | --- | --- | --- | --- | --- |
| Server skeleton: `Sylin.Koan.App`, SQLite in WAL with immediate write transactions, fixed port, the base URL under a path prefix and declared proxies from `Koan:Web`, health | — | — | `Koan.Web`, `Koan.Data.Connector.Sqlite` | S | done |
| The writer: one queue, one loop, one transaction per command, sequences from high-water marks; the Koan transaction proven inside a loop | — | `ConversationService.Writes` ordering (harvest) | `EntityContext.Transaction` with `TransactionGuarantee.Atomic`; `KoanBackgroundServiceBase` | M | done |
| The HTML projection: one DTO rendered through a Razor component or as JSON by `Accept`; fragments by header | — | `PublicPageRenderer` (harvest) | lift later | M | done |
| Island bootstrap: `data-island` mounting into server HTML, one bundle, one event stream per page, fragment patch | — | `public-fragment.js` (harvest) | — | S | done |
| Design foundation: tokens, fonts, layout, Paper and Ink, four accents | — | `tokens.css`, `fonts.css`, `workspace.css`, `appearance.ts` (transplant) | — | S | done |
| Capture script for the visual loop: Playwright, bundled fonts, three widths, two themes, every state | — | new | — | S | done |
| Rule checks: no dialogs, language, greenfield scoped to the root tree, fixed ports | — | `tests/no-dialogs.test.mjs`, `scripts/check-greenfield.ps1` (harvest) | — | S | done |
| Test fixture: the real host on a loopback port over a temp folder, once under a path prefix; parallel-safe | — | `ExperienceWebApp` edge cases (harvest) | `Sylin.Koan.Testing.Hosting` | S | done |
| Lifecycle: one published binary and one data folder; build, launch, backup by `VACUUM INTO` and copy, restore; compose for Docker | — | `scripts/server-lifecycle.ps1`, `*.bat`, `compose.yaml`, `Dockerfile` (harvest) | — | S | done |
| Lexical search proven through the SQLite connector | — | — | `Sylin.Koan.Data.Core` (`[Lexical]`) on the SQLite connector | S | done |

### 1 · The door and the names

A person or an agent arrives and knows where they are, who they are here, what happened since they left, and what they can do.

| Feature | Screens | Harvest | Koan | Size | Status |
| --- | --- | --- | --- | --- | --- |
| Nouns and tables: `TangentSpace`, `Tangent`, `Post` with the root as Topic, `Participant` facet, `Overwrite`, `Event`, `Checkpoint` | — | `Space`, `Tangent`, `Topic`, `Post`, `ActivityJournal`, `ReadPosition` (harvest) | `Sylin.Koan.Data.Core` | M | done |
| atproto sign-in; the person reconciled from the DID; the participant facet; profile capture as a job; declared kind only | sign-in | `Identity/*` minus classification and the capture inference (harvest) | `Sylin.Koan.Web.Auth.Connector.Atproto`, `Sylin.Koan.Identity` | M | done |
| Access: Koan roles seeded (`everyone`, member, moderator), overwrites, `Capabilities.For`, `can` on every resource, `gate` from the explainer | — | `TangentPermissions` catalog, `AccessEvaluator` reason table (harvest) | `Sylin.Koan.Identity`; brief 2 | M | done |
| First run: the first person to sign in, or the one `Tangent:Operator` names, sets the place up by signing in, named from their handle, as its proprietor and operator, and lands on the site's pointer snippets | first-run | `OnboardingPanel`, `FirstArrival`, `ServerGovernance` (harvest) | — | S | done |
| The address learned and offered: first run takes the origin its sign-in completes on as the place's address, kept on its row for Koan; a public origin the place is reached at while that address is local becomes an Offer to the operator, `offer.made` in the log and one line on their door, used or declined once; a configured URL wins and lapses what is open | door, first-run | new | `Sylin.Koan.Web` (WEB-0081: `IApplicationUrlStore`, `KoanWebOrigins`) | S | done |
| The door as one resource: for a visitor, whose place this is, what is featured, what is alive; signed in, since my checkpoints — replies to me, mentions, new per Tangent, new articles — who is around, what I can do | door signed-out, door signed-in | `ExperienceDigest`, `AttentionCompiler` (harvest); `SpaceHero`, `ContinueReading`, `ForumSidebar` (harvest) | — | L | done |
| The Tangent page: topics with what is new marked, bounded | tangent | `TopicList`, `TopicRow`, `DirectorySummaries` (harvest) | — | S | done |
| Discovery: `tangent.json`, `llms.txt`, `<link rel="tangent">` on the site, `/.well-known/tangent` at the site origin, `_tangent` TXT; the reverse-proxy rule for a path install | — | new | — | S | done |
| The delegated agent door: the consent page for `Koan.Web.Auth.Server`, the Device flow, a pre-registered public client; `llms.txt` says how | consent | new | `Sylin.Koan.Web.Auth.Server`, `Sylin.Koan.Security.Trust` | S | done |
| Phone layouts for the door and the Tangent | door, tangent at 390 | `mobile-reader.css` (transplant) | — | S | todo |

### 2 · Conversation

| Feature | Screens | Harvest | Koan | Size | Status |
| --- | --- | --- | --- | --- | --- |
| Topic window: bounded, lock-free, cursors are sequence numbers, anchor first | topic | `ConversationService.Window`, `WindowPlanner`, `History` (harvest) | — | M | done |
| Post, reply, edit, remove through the writer: TID ids, pre-edit snapshots in the audit partition, visible tombstones | topic | `PostChanges` snapshot chain (harvest) | audit partition | M | done |
| A moderator's hand is visible: a Post someone other than its author edited says who and when wherever it or its first words are read, as a page and as JSON, until the author's own edit makes the words theirs again; the author is told of someone else's edit or removal in their notifications and on their door, with who did it; editing or removing your own Post tells no one | topic, tangent, door, profile, notifications, edit, report, take-this-on-a-tangent | new | — | S | done |
| Markdown body, sanitized HTML, mentions as identity-bearing links resolved at render | topic | `PostMarkup` (harvest) | — | S | done |
| The composer island: drafts in `localStorage` by scope; an uncertain submit reconciles by `GET` of the minted Id; never auto-posts | topic | `composables/conversation.ts` intents (harvest) | — | S | done |
| Live: the event stream, one per page, fragment patch; the door as the poll | topic | `activity/transport.ts` framing (harvest) | `Sylin.Koan.Web.Sse`, `Sylin.Koan.Communication` | M | done |
| Take this on a tangent: choose the Tangent, a root Post with an origin, linked both ways | topic | new | — | S | done |
| Checkpoints and the watch flag | topic | `ReadPosition`, `Watches` (harvest) | — | S | done |
| Reports: a Post taken on a tangent into the moderators' Tangent, with the origin | topic | `ReportPanel` (harvest) | — | S | done |
| Phone layout for the topic | topic at 390 | `mobile-reader.css` (transplant) | — | S | todo |
| Mentions a person can type: a typed `@handle` of a participant here becomes a mention when the Post is written, so it targets them as a DID link does | topic | — | — | S | done |

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
| The roles at `/settings/roles` over Koan's roles in their order (SEC-0031), for whoever holds `ManageRoles`: a role created with a name and its permissions in plain words, directly above the member role; moved one place higher or lower, renamed and given other permissions in the page; deleted with an inline confirmation, the seeded roles kept; only the roles below the reader's own highest (`RoleCollection.Manages`), and only the permissions they hold; every role but everyone and member given and taken on People under the same order; each Tangent's access by role, its overwrites, at `/t/{tangent}/settings/access`, which makes a members-only Tangent | Tangent Space settings (roles), Tangent settings (access) | `RolesPanel` (harvest), `RoleUiController` rules (harvest) | `Sylin.Koan.Identity` (SEC-0031) | M | done |
| Join or apply, set at `/settings/admission`; every admitted participant in the member role, joined after the commit that admits them and at every sign-in where it is missing; the applications at `/settings/applications` for whoever holds `ManageStanding`, admitted or declined inline, each a `standing.changed` that targets the applicant; the applicant's door says where they stand | Tangent Space settings (admission, applications), door | `SpaceAdmission`, `SpaceAdmissionController`, `SpaceJoining` (harvest) | — | S | done |
| The one knob "new accounts post after approval": a new account's Posts held until approved. Needs the account's age, which neither the profile the tree reads nor Koan's atproto connector exposes (a `did:plc`'s first operation in the PLC directory's audit log holds it; a `did:web` has none), and a held state on a Post with a place to approve it | settings people | — | atproto account age | M | todo |
| People directory with seek cursors; time out, ban and lift inline: the people at `/settings/people` for whoever holds `ManageStanding` or `ManageRoles`, a page at a time in handle order from a sealed cursor and by the start of a handle through the handle index; time out for an hour, a day or a week, ban confirmed in the page, and lift, and the place's roles given and taken, there and on a profile, each one command and one event that targets the participant, whose door tells them; nobody acts on someone whose highest role is not below theirs, on themselves or on the operator | settings people, profile, door | `TangentMemberDirectory`, `Restrictions`, `ParticipantActions` (harvest) | `Sylin.Koan.Identity` | S | done |
| Participant profile: the PDS profile, roles, posts here, the person's grants from Koan with an inline revoke; Connections join it with slice 3 | profile | `ProfilePanel`, `ParticipantAvatar` (harvest) | `Sylin.Koan.Identity`, `Sylin.Koan.Web.Auth.Server` | S | done |
| Creating a Tangent: New Tangent on the door and in the switcher for whoever holds `ManageSpace`; the name alone, the slug derived, everything else a quiet default | new tangent | `NewTangentPanel` (harvest) | — | S | done |
| Tangent settings: name, description, default kind, order, and the audience as the `everyone` overwrite; the moderators' Tangent's audience and order fixed | tangent settings | `TopicSettingsPanel`, `AccessPanel` (harvest) | — | S | done |
| The place's settings (general) at `/settings`, offered in the account menu to whoever holds `ManageSpace`: the place's name and description, `space.changed` on a change; the sections' frame the later settings join | Tangent Space settings (general) | new | — | S | done |
| A first Tangent at setup: first run creates *General*, public and of discussions, beside the moderators' Tangent, so the place can be talked in from its first minute | door | new | — | S | done |
| Any number of Tangents and members: a role's members are Koan's rows; access on a Tangent reads only the overwrites the reader's bag meets there; a move reads the Tangents a page at a time; the door, the switcher, `/t`, take this on a tangent and a profile read every Tangent the reader may view a page at a time; the moderators' Tangent is on the door for whoever may view it; a profile's approvals page | door, tangents, tangent, profile, take-this-on-a-tangent | new | `Sylin.Koan.Identity` (SEC-0030) | S | done |
| The place's own audience: whoever holds `ManageSpace` sets the Tangent Space public or private; a private place sends noindex and serves visitors only the sign-in page | Tangent Space settings (general), sign-in | — | — | S | done |

### 6 · Attention

| Feature | Screens | Harvest | Koan | Size | Status |
| --- | --- | --- | --- | --- | --- |
| Notifications: the events where I am the target, that someone else did and that I may view, at `/notifications` a page at a time from a sealed cursor, each one line one click from what it is about, an open Offer with its two answers; my own opening of them, never my delegate's, moves my notifications checkpoint without waiting on the writer, and every page's masthead counts what lies past it among what I may view, "at least" past 99, raised live by the page's stream; the door names them, with how many, while there are some. No preference scored five, so none is built | notifications, door, every page's masthead | `NotificationsPage`, `ParticipantPreferences` (harvest) | — | S | done |
| An application reaches whoever decides it: while it waits, everyone who holds `ManageStanding` when they read counts it among their notifications, on every page's masthead and live from their stream, finds who applied and when on `/notifications`, one click from the applications, and reads on their door how many wait; it is seen as every notification is, and a decision by any decider ends it for all; an arrival writes one row and one event however many may decide | notifications, door, every page's masthead | new | — | S | done |
| A report reaches whoever may act on it: everyone who may view the moderators' Tangent when they read, someone given that later among them, counts each report someone else made among their notifications, on every page's masthead and live from their stream, and finds who reported a post by whom on `/notifications`, with the reason's first words, one click from the report; the author is named only to a reader who may view the reported Post; it is seen as every notification is; the reporter and the reported author read nothing of it; a report writes one event however many may read it | notifications, every page's masthead | new | — | S | done |
| Watching: the flag on a checkpoint; one line on the door | door | `FollowButton` (harvest) | — | S | done |
| Search across titles and bodies, bounded, with header suggestions | search | `SearchBox`, `SearchPage` (transplant) | brief 4 | S | todo |

### 7 · Self-reliance

| Feature | Screens | Harvest | Koan | Size | Status |
| --- | --- | --- | --- | --- | --- |
| Export a Tangent or the Tangent Space as readable HTML with data and media, streamed, through the one renderer | settings export | new | lift later | M | todo |
| Backup and restore of the folder, verified by restoring | — | `scripts/server-lifecycle.ps1` (harvest) | — | S | done |
| Static archive: every public page and feed rendered to a folder with the same URLs; retirement mode | — | new | lift later | M | todo |

### 8 · Agents

| Feature | Screens | Harvest | Koan | Size | Status |
| --- | --- | --- | --- | --- | --- |
| Agents write without guessing: every write a page offers its reader is a form in its JSON, the one its HTML holds — where it posts, its method, its fields with what each holds, and the TID minted for a new Post — present only where the reader may make it; Tangent's own writes take the fields as a form or as one JSON object; `llms.txt` says how an agent writes and what signing in makes of a newcomer by the place's own admission | — | new | lift later, with the HTML projection | S | done |
| Bring your agent: one button that hands the address to a listening Companion Lobby, which registers as a loopback client and runs the code flow | bring your agent | `agent-entry.ts` (harvest) | `Sylin.Koan.Web.Auth.Server` | S | todo |
| The DID-native door: an atproto service-auth token verified, exchanged for a bearer from Trust's issuer; revoke through the person's grants | — | `ServiceProofExchange` flow (harvest; verification is Koan's) | brief 3, `Sylin.Koan.Security.Trust` | S | todo |
| An agent as moderator or proprietor | settings roles | — | — | S | todo |
| The installation's identity: `did:web` on the issuer keys at `<base>/did.json`; an A2A agent card when the proprietor is an agent | — | new | brief 3 | S | todo |
| Assertions: the Tangent Space as a labeler for member and role badges | — | new | — | M | todo |

### 9 · Theming

| Feature | Screens | Harvest | Koan | Size | Status |
| --- | --- | --- | --- | --- | --- |
| Appearance: light, dark, system; accent; the published `site.standard.theme.basic` as the first four tokens | settings appearance | `AppearanceSwitch`, `TangentAccentPalette` (transplant) | — | S | todo |
| Slots with sanitized HTML (head, masthead, door intro, article header, footer) and a bounded typed settings schema; identical in the site, the archive and exports | settings appearance | new | — | M | todo |

## Screens

The visual loop runs on each, at 1440, 1024 and 390, in light and dark: door signed-out, door signed-in, tangents, tangent (discussions), tangent (articles), topic, article, article editor, new topic, new Tangent, profile, notifications, search, sign-in, consent, first-run, bring your agent, Tangent Space settings (general, admission, applications, appearance, tangents, people, roles, export), Tangent settings (details, access), connections. Current scores live in [SCREENS](SCREENS.md) once the first screen passes.

## Not built

Several Tangent Spaces per installation. Stewardship tiers. Moderation cases with testimony and escalation. Classification presets and inferred classification. A database chooser and a storage panel. Feed import. Atmospheres and ASCII scenes. An operations catalog, MCP server or model-facing envelope. A client router or store library. A table of credentials or of media. A shared worker for the stream.
