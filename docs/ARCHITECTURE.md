# Architecture

How the tree at the root is built. [BRIEF](BRIEF.md) says what it is for; [LANGUAGE](LANGUAGE.md) says what things are called; [ADR 0015](adr/0015-the-spine.md) records why the shape is this one. Koan's revision is pinned by `scripts/prepare-framework.ps1`, which clones or verifies the source checkout at `.local/upstream/koan-framework`; `global.json` pins the SDK and `koan.lock.json` records the composed modules. Every Koan capability named here is referenced by its exact package identifier from Koan's capability map, never derived.

## 1. Shape

One process. One data folder. Four layers, each defined by a rule it cannot break.

| Layer | Holds | Rule |
| --- | --- | --- |
| **Nouns** | `TangentSpace` (one row), `Tangent`, `Post`, `Participant`, `Event`, `Checkpoint`, `Connection`, `Receipt`, `Reference`; Koan's person, roles, overwrites, sessions, grants and media beside them | An entity validates its own change and throws; it never loads another entity, never asks about permission, never opens a transaction |
| **Writer** | one queue, one loop, one transaction per command | The only code that opens a write transaction. It touches the database and nothing else |
| **Readers** | handlers, the door, the stream, feeds, the archive, Connections | Bounded, lock-free, consume compiled state; every resource leaves with `can` |
| **Render** | Razor components rendered statically, the HTML formatter, Vue islands | Renders DTOs. Knows no entity |

Dependencies point one way: Render → Readers → Nouns; Connections → Events and Posts. Nothing depends on Render or Connections. There is no hub object, no governance layer and no policy revision counter: capability is computed per request from a few small rows, so nothing is cached that would need invalidating.

## 2. What Koan carries

Tangent is a Koan application. Where Koan has the capability, Tangent references the package and writes none of it.

| Need | Package | Used for |
| --- | --- | --- |
| Entities, queries, partitions, transactions; the SQLite posture (WAL, busy timeout, synchronous) and one native commit across entity types | `Sylin.Koan.Data.Core` on `Sylin.Koan.Data.Connector.Sqlite` | every table; the writer's one commit per command (`TransactionGuarantee.Atomic`); the audit partition for edit history |
| The canonical base URL, served under its path and nothing outside it; trusted proxies for one hop | `Sylin.Koan.Web` (`Koan:Web:ApplicationUrl`, `Koan:Web:TrustedProxies`) | every absolute link, the pointers and the descriptor; the reverse proxy's scheme |
| Durable person, external identities, sessions, audit, erasure | `Sylin.Koan.Identity` · `Sylin.Koan.Identity.Web` | the participant spine; the atproto DID as the person's external identity |
| Roles with permissions, members and cached bags; per-resource overwrites resolved in Discord's order, cached per resource; effective access composed from contributors; the access explainer and its deciding fact | `Sylin.Koan.Identity` | the Discord role model's roles and bags; a Tangent's overwrites and their resolution; the deciding overwrite a gate names |
| atproto sign-in and authorized sessions | `Sylin.Koan.Web.Auth` · `Sylin.Koan.Web.Auth.Connector.Atproto` | sign-in; record and blob operations under a participant's session |
| OAuth 2.1 authorization server: code with PKCE, Device flow, loopback DCR, consent seam, revocable grants, persisted ES256 keys | `Sylin.Koan.Web.Auth.Server` | the delegated agent door; the installation's signing keys |
| ES256 issuer, JWKS, inbound bearer scheme | `Sylin.Koan.Security.Trust` | credentials minted by the proof bridge; bearer validation |
| Content-addressed media, lazy pipelines, recipes | `Sylin.Koan.Media.Core` · `Sylin.Koan.Media.Web` on `Sylin.Koan.Storage.Connector.Local` | every image a Post references; covers; derivatives |
| Social cards | `Sylin.Koan.Web.OpenGraph` | link previews for articles and topics |
| Server-sent events | `Sylin.Koan.Web.Sse` | the event stream |
| Entity events with discovered handlers | `Sylin.Koan.Communication` | the writer's "appended" signal to the stream and the dispatch jobs |
| Durable and scheduled work | `Sylin.Koan.Jobs` | every background task |
| Versioned entity snapshots in a partition | `Sylin.Koan.Web.Extensions` audit partition | edit history, written by the writer |
| Tests through a real host | the fixture builds the application as `Program.cs` does, on a loopback port, and binds Koan's ambient host per request; `Sylin.Koan.Testing.Hosting` cannot host Kestrel at the pinned revision, and this fixture is lifted into it | the one fixture |

Not referenced, by decision: `Sylin.Koan.Tenancy` (one Tangent Space per process), `Sylin.Koan.Mcp` (no MCP server), `Sylin.Koan.Data.SoftDelete` (a removed Post is a visible tombstone, not a hidden row), `Sylin.Koan.Data.Backup` (shelved by Koan; backup is the folder).

Koan work the tree depends on, each a scoped brief: atproto content operations in the connector with service-auth verification and `did:web` on the issuer keys; lexical search in `Koan.Data`. Capabilities Tangent builds first in Koan's shape and lifts into Koan when they are proven: the HTML projection, the durable journal with checkpoints, feeds, the static archive, the agent descriptor. A Koan quirk found while building Tangent is corrected in Koan first, under Koan's contributor law, and Tangent resumes on the corrected revision.

## 3. The write path

Every write is a command on one queue, consumed by one loop. A handler builds the command — `Reply { PostId, TopicId, Body, Author }` — hands it to the writer and awaits its outcome: the result, a refusal with its reason, a conflict, or the failure. The loop takes commands in arrival order and, for each:

1. runs what the command writes before its transaction (`ICommand.Prepare`), which is only a change Koan commits at once and refuses inside one — a Tangent's audience overwrite — and which stays if the command is then refused; then opens one atomic scope (`EntityContext.Transaction` with `TransactionGuarantee.Atomic`; SQLite begins the immediate transaction at commit, so the command's reads see the committed state and the serial loop keeps them consistent);
2. runs the command, which loads what it names — the participant with standing, the role bag, the Tangent's overwrites, the target Post — asks `Capabilities.For(...)` before it answers anything else — refusing with the gate word when the verb is missing, and answering as for a missing resource when the actor may not `View` — and calls the entity method, where validation lives;
3. assigns the sequences the command asks for from in-memory high-water marks loaded once at start — the log's from the last `Event.Seq`; a Topic's comes from its root Post's `LastSequence`, read and advanced inside the command — and keeps them only on commit, so a command that does not commit skips none; after a commit whose outcome Koan cannot report, the marks are reloaded from the store before the next command;
4. commits the rows, the audit snapshot for an edit and the Events together, as one native transaction or nothing;
5. announces a commit that wrote — the command and the sequences it took — in-process, and answers the sender with the resource and its `can`. A command that is refused or throws leaves no row of its transaction behind and the next command runs; at a stop, the command in flight ends on its own terms and every queued command is answered as not run.

No lock exists in application code. **Idempotency is the Id**: the client mints a Post Id as a TID, so a retry with the same Id and the same body returns the existing Post and a different body is `409`. **External I/O never enters the loop**: a job calls the network and then enqueues a command with the result; jobs write only through the writer. The root Post is the Topic's state — `LastSequence`, `ReplyCount`, `LastActivityAt`, `Locked`, `Pinned` — so a Topic has no row of its own.

## 4. The read path

A request is resolved once in the pipeline: session or bearer → person → role bag → the overwrites of the Tangent in the route. Handlers are thin: a bounded query, `can` attached, the DTO returned.

**The page is the API.** One handler, one DTO, and the formatter decides: `Accept: text/html` renders it through a Razor component, `application/json` serializes it, and a fragment request header renders only the component for that resource. A Post's page, its JSON, its live patch and its archive file are the same DTO through the same component. Agents fetch the URL a person shares; `llms.txt` says so in two lines, and the descriptor carries `"api": 1` as the stability promise.

`ETag` is `Post.Version` or `Event.Seq`; a page assembled from many rows, such as the Tangent page, carries a hash of its DTO, so a new Post, a moved checkpoint or a changed `can` changes it. Public pages are cacheable by a proxy. A private Tangent Space sends `noindex` and serves visitors only the sign-in page. Errors are one shape, RFC 9457 `problem+json`, carrying the same `gate` the door shows: `sign-in`, `apply`, `timed-out`, `locked`, `removed`, or the deciding overwrite or missing role in one sentence; what the reader may not view is absent and answers as not found.

## 5. Data

| Table | Key | Fields that matter |
| --- | --- | --- |
| `TangentSpace` | one row, which exists once first run establishes it | name, `ProprietorId` and `OperatorId` (set at first run), `Audience` (public, private), `Admission` (`join`, `apply`) with `NewAccountsAfterApproval`, `Appearance` (theme, accent), `Slots` (head, masthead, door intro, article header, footer), `Version` |
| `Tangent` | slug | name, description, default kind, order, `TopicCount` (raised by the command that starts a Topic); audience is its `everyone` overwrite |
| `Post` | TID | `TangentId` on every row; `TopicId` (= Id on a root); `ParentId`; `OriginId`; `Slug`, `Kind`, `Title`, `CoverId` (root); `Body` (Markdown); `Version`; `Sequence`; `AuthorId`; `CreatedAt`; root carries `LastSequence`, `ReplyCount`, `LastActivityAt`, `Locked`, `Pinned`, `LatestReplyId`, `LatestReplyAuthorId`, `RecentAuthorIds` (at most four); `Removed`, `RemovedBy`, `RemovedAt`. Pre-edit snapshots live in the audit partition |
| `Participant` | the Koan person id | handle, `Profile` (display name, description, avatar, captured at), `Kind` (declared), `Standing`, `StandingUntil`, `StandingNote` |
| `Event` | `Seq` (the Id is the sequence, zero-padded) | kind, `TangentId`, `TopicId`, `PostId`, `ActorId`, `TargetId`, `Version`, `At`; `Audience` on `tangent.changed` |
| `Checkpoint` | `ParticipantId/{scope}`, the scope typed: the place, a Tangent by slug or a Topic by Id (`place`, `tangent:{slug}`, `topic:{id}`) | `Seq`, `Watch` |
| `Connection` | id | `ParticipantId`, kind, account, adapter-owned `Settings`, `Seq` (its cursor on the log), status, error |
| `Receipt` | (`ConnectionId`, `PostId`) | `Version` delivered, status, remote reference, `At`, error |
| `Reference` | id | `PostId`, `ConnectionId`, remote URI, kind (`reply`, `quote`, `mention`, `like`), author, `At` |

Roles, role members, overwrites (`PermissionOverwrite`, one per resource and subject; a Tangent's on the resource `tangent:{slug}`), persons, sessions, grants and media are Koan's tables. The base URL is configuration alone — the operator's `Koan:Web:ApplicationUrl` as Koan holds it — and no row keeps a copy. Indexes: `Post(TangentId, ParentId, Pinned, LastActivityAt)` for topic lists, pinned and unpinned each one range; `Post(TopicId, Sequence)` for windows; `Post(TangentId, Slug)` for the slug check; `Event(Seq)`, unique; `Event(TargetId, Seq)`; the lexical index over `Post.Title` and `Post.Body`. A composite key is one string Id composed from its parts with `/`, which no part holds. A slug derives from the title at creation, deduplicated with a suffix, and never changes.

## 6. The log

Only the writer appends. Kinds: `post.created` (a reply's carries the author of the Post it answers as target, unless they answer themself), `post.edited`, `post.removed`, `post.moved`, `mention` (one per mentioned participant, as target), `standing.changed`, `role.changed`, `tangent.changed` (carrying the audience), `space.changed`.

| Reader | Query |
| --- | --- |
| The door | one window of the newest events and the newest that target me, past my place checkpoint and my checkpoints for the Topic and Tangent of each, filtered by `can.view`, grouped: replies to me, mentions, new per Tangent, new articles; below it, and alone for a visitor or a reader with nothing new, the front page from the same window: whose place, what is featured, what is alive, who is around |
| Notifications | the subset where I am the target; reading advances the checkpoint |
| The stream | `GET /events?since=` as server-sent events; the same URL without the stream header is the poll |
| Feeds | public `post.created` per Tangent and for articles, rendered as Atom and JSON Feed |
| Connections | each reads from its own `Seq` |
| Export, archive | walk it |
| The moderation trail | `post.removed`, `standing.changed`, `role.changed` with their actors |

Visibility applies at read: an event in a Tangent the reader cannot view does not exist for them. Nothing is pruned. A protocol's "content changed, content removed, audience changed" is a filter over these kinds.

## 7. Access

Discord's model, on Koan's roles, resolved in Discord's order.

- **Bits**: `View`, `Post`, `Topic`, `EditOwn`, `RemoveOwn`, `Moderate` (edit or remove any, lock, pin, move, take on a tangent anywhere), `ManageTangent`, `ManageRoles`, `ManageStanding`, `ManageSpace`; `Administrator` implies all, and no overwrite grants or denies it. On a Koan role and in a Koan overwrite a bit is the permission token `global:` followed by its name, and `Administrator` is Koan's `global:administrator`; Koan compiles a role's tokens into the bag.
- **Roles** are seeded at first start on Koan's `RoleCollection`, each once, left as the proprietor sets it and returned when gone: `everyone` with no bits of its own, `role:member`, `role:moderator`, and `role:administrator` with `Administrator`.
- **Base** is the bits in the participant's bag, which carries the `everyone` role's own, with `View` for everyone where the settings row's `Audience` is public: the row is the one statement of the place's audience, and no role carries it.
- **Overwrites** are Koan's `PermissionOverwrite` rows on the Tangent's resource `tangent:{slug}`, written through `RoleCollection.Overwrite` outside the writer's transaction and read from Koan's per-resource cache. Koan resolves them over the base in order: `everyone` deny then allow → the denies then the allows of the bag's other roles → the participant's own deny then allow. A Tangent's audience is nothing more than `View` in its `everyone` overwrite, written before the transaction that creates the Tangent opens, so the Tangent exists for nobody its audience excludes from the instant it commits; a page reads a Tangent's row before its overwrites.
- **Standing precedes everything**: banned sees nothing; timed out keeps `View` until `StandingUntil` passes, when the participant reads as admitted; applied holds `View` where the Tangent Space is public. A visitor is `everyone` with `View` alone on a public place.
- **Absent**: what a reader may not `View` does not exist for them. Without `View` on the place or the Tangent — banned, a visitor on a private place or Tangent, a participant an overwrite denies — every verb carries the one reason `absent`, nothing else is said, and a command answers what it answers for a missing resource.
- **`can`** is the bits the participant holds on this resource plus authorship and state: `EditOwn` and `RemoveOwn` apply to my Post; nothing applies to a removed Post; `Post` is absent on a locked Topic.

`Capabilities.For(participant, tokens, space, overwrites, now, post?, root?)` is one pure function over Koan's bag, the settings row, the Tangent's overwrites and the time, the base and the overwrites resolved by Koan (`RoleBag.On`); a command loads those with bounded reads (`Capabilities.Load`) and asks it inside its `Run` before it answers anything else, and a page that answers for many Posts loads them once (`Capabilities.LoadRows`) and asks for each; its tests enumerate the table. It resolves in this order: standing, `View` on the Tangent, lock and removal, the base and overwrites for the other bits, authorship. The `gate` is the first step that dropped the verb: `sign-in`, `apply`, `timed-out`, `absent`, `locked` or `removed` where standing, audience or state decided, the deciding overwrite where one did, and the absence of a role otherwise, both from Koan's deciding fact (`EffectivePermissions.Explain`). The browser and every agent render `can` and compute nothing.

## 8. Rendering

**Razor components rendered statically** through `HtmlRenderer`: typed templates, no interactive runtime, no JavaScript needed to read. One component per DTO; a page composes them; a fragment is one alone; the archive writes them to files; export packages them; feeds are a sibling output of the same models. Theming slots — head, masthead, door intro, article header, footer — render once and are therefore identical in the live site, the archive and exports.

**Vue islands** mount where a participant does something: the composer, the article editor with its source view and roundtrip check, the role editor, settings panels, the live updater, bring-your-agent. An island is `<div data-island="composer" data-props="…">`; a pre-paint theme script and one module of islands, no client router, no store library. Islands share one event stream per page; a live event fetches the fragment and inserts it. Drafts are `localStorage` keyed by scope; an uncertain submit is a `GET` of the minted Id. Pages load as pages, with cross-document view transitions. Phone layouts are CSS on the same HTML.

## 9. Connections

A Connection belongs to a participant and syndicates only Posts that participant authored. The proprietor's Connection also carries the Tangent Space's `site.standard.publication`. Identity is core, not an adapter: a Tangent Space requires atproto identity.

**A Connection is a log reader with a cursor.** The dispatch job runs per Connection: read events since `Connection.Seq` of kinds `post.*` and `tangent.changed`; for each, load the Post, its current audience and the previous Receipt; call the adapter; save the Receipt; advance `Seq`. On failure the cursor stays, so an edit lands before a removal; the Receipt records the error; after repeated failure the Connection appears as an attention item on the door of the participant it belongs to.

```csharp
interface IConnectionAdapter
{
    Task<Receipt> Handle(Event e, Post post, Audience audience, Receipt? previous, CancellationToken ct);
}
interface IReferenceSource { IAsyncEnumerable<Reference> Since(string cursor, CancellationToken ct); } // optional
interface IAsserter        { Task Assert(Participant p, Label label, CancellationToken ct); }          // optional
```

`Handle` compares the delivered `Receipt.Version` with `Post.Version` and decides — publish, update, retract, requote. Adapter state is `Receipt` plus the Connection's own `Settings`. The connections screen derives "in sync", "changed", "retracted" and "a copy may remain" from Receipt against Post.

| Package (a folder of `Connections/`) | Syndication | References |
| --- | --- | --- |
| atproto | a `site.standard.document` with the cover blob for each public article, and a Bluesky post that embeds it; public Posts as the Tangent Space's own record type; `<link rel="site.standard.document">` and verification from Render | Constellation and Spacedust as a reference source, optional |
| Webmention | sent on `post.created` for outbound links; Bridgy Fed opt-in for fediverse reach | received at an endpoint |
| Mastodon | a status under the participant's own account | replies through its API |

Feeds are a Render output, not a Connection. Inbound replies arrive as `Reference` rows first; a Connection may later materialize them as Posts with source fields, authored by the person row Koan reconciles for that DID.

## 10. Identity

- **Sign-in** is atproto OAuth through Koan's connector; Koan's reconciler turns the DID into a durable person with the DID as its external identity. The `Participant` row is Tangent's facet on that person, created at the first sign-in by Tangent's sign-in hook through the writer, with no question asked.
- **A delegated agent** ("my agent acts as me") is an OAuth client of the Tangent Space through `Koan.Web.Auth.Server`: Companion Lobby registers as a loopback client and runs the code flow; any other client, local or remote, uses the Device flow against a pre-registered public client. The person approves on the consent page Tangent provides. All clients are public; there are no client secrets.
- **A DID-native agent** (its own participant) arrives through the proof bridge: an atproto service-auth token addressed to this Tangent Space, verified by the connector, exchanged for a bearer that Trust's issuer mints and Trust's bearer scheme accepts. Revocation is Koan's grant revocation.
- **The installation's identity** is a `did:web` served from the issuer's ES256 key ring at `<base>/did.json` (`did:web` resolves paths), the same keys Auth.Server publishes as JWKS.
- **Proprietor and operator** are two fields on the settings row, declared at first run, stated in the descriptor and on the about page. The operator is a person.
- **Profiles** are cached by a job: the handle is the one the connector verified at sign-in; the display name, description and avatar come from the public profile service until Koan's connector reads the PDS itself (brief 3). Nothing is edited locally. `Kind` comes from the profile: the job reads the account's own `bot` self-label from the profile it captures and makes the participant an agent, or a person without it; a participant's own choice, once they make one, wins and the job never overrides it. `Kind` is shown and never evaluated.

## 11. Discovery

A Tangent Space answers at its **base URL** — scheme, host, optional path — the operator's `Koan:Web:ApplicationUrl`, which Koan normalises once and serves under: routes, static files and health answer under its path and nothing outside it, and `Koan:Web:TrustedProxies` names the reverse proxies whose forwarded scheme and client address are believed, for one hop. Every absolute thing derives from it: links, the cookie path, the OAuth client metadata and redirect, the `did:web`, the publication URL. `tangent.<domain>` is the suggested address, the one a client tries first when a site holds no pointer.

Pointers live on the site, since `/.well-known/` belongs to an origin: `<link rel="tangent" href="…">` on the site's pages, or `/.well-known/tangent` at the site origin, or a `_tangent` TXT record. The Tangent Space serves its own descriptor at `<base>/tangent.json` — name, address, audience, `"api": 1`, the delegated door (the authorization server's description and the public client `delegate`), links to the door and to `llms.txt`, and for a public place its proprietor and operator; the installation's `did:web` and its publication join it with their packages — and `<base>/llms.txt`, which says in a few lines how to read the place and how to act as a person's delegate. Both carry an `ETag` of their body and a five-minute cache lifetime, and a private place's files name nobody. The first-run page prints the pointer snippets and, for a path install, the reverse-proxy rule.

## 12. Background work

Koan jobs, each a reader that writes through the writer: profile capture, handle resolution, connection dispatch, reference polling, media processing, archive rendering. At start, one service on Koan's background base asks whether the Post index covers every Post and has Koan rebuild it when a restored folder left it behind; it writes no row. Backup and restore are verbs of the executable, not jobs: `Tangent.Server backup` takes a `VACUUM INTO` snapshot of the data folder with a hashed manifest, and `restore` puts one back, refusing a folder a running Tangent Space has marked. Nothing else runs in the background; nothing in a request does network I/O.

## 13. Folders

```
src/server/
  Space/          TangentSpace row, first run, tangent.json, llms.txt, did.json
  Delegates/      the delegated agent door: Koan's OAuth server composed, the shared client, a bearer read as its person, the consent page
  Tangents/       Tangent, overwrites, the Tangent page and settings
  Posts/          Post, Markdown, slugs, topic queries, windows, media references
  Participants/   Participant facet, standing, profile cache job, the proof bridge, profile page
  Access/         bits, Capabilities.For
  Writer/         the one write path: commands, outcomes, the loop, high-water marks
  Events/         Event, the stream, the door, checkpoints
  Render/         components, HTML formatter, fragments, feeds, archive sink, slots
  Connections/    Connection, Receipt, Reference, dispatch; Atproto/ Webmention/ Mastodon/
  Lifecycle/      backup, restore, export, archive, retirement
src/client/       islands and tokens.css
tests/            one fixture over the real composition, tests by noun, visual captures; rules/ holds the rule checks' own test
scripts/          lifecycle; checks: greenfield, language, no-dialogs, ports
```

By noun, not by layer. A Connection package is a folder until a dependency is heavy enough to earn an assembly.

## 14. Verification

One fixture: the real process on a loopback port over a temp folder, run at least once under a path prefix. Tests by noun; the capability table enumerated; the writer's idempotency and ordering; one end-to-end per slice through the page-as-API. The visual loop captures server HTML with islands mounted, at three widths in two themes, and gates every screen.

## 15. Settled in slice 0

Four questions the foundation slice set out to answer with an afternoon each, and how each stands:

- **A Koan transaction across several entity saves inside a background loop.** Settled: `EntityContext.Transaction(name, TransactionGuarantee.Atomic)` commits saves of two entity types from a `BackgroundService` in one native `BEGIN IMMEDIATE … COMMIT` on the SQLite route, and a throw before the commit persists neither; `tests/Tangent.Tests/SqlitePostureTests.cs` holds the facts.
- **The HTML formatter beside Koan's JSON projection.** Settled: `HtmlFormatter` is the first MVC output formatter and claims only the types `ResourceComponents` registers, so the door answers as a page, a fragment or Koan's JSON by `Accept` and the fragment header; `tests/Tangent.Tests/Render/ShellTests.cs`.
- **A Vue island mounting into server HTML with a fragment patch.** Settled for the patch: the fragment island mounts beside the server's HTML and refreshes its host from the resource's URL with the fragment header. The event stream that will drive the patch arrives with the log.
- **Lexical search through Koan's SQLite connector.** Settled: `Post.Title` and `Post.Body` carry Koan's `[Lexical]`, the connector keeps an FTS5 index in step with every commit, `PostSearch` answers a bounded page, best first, with marked snippets, and a start checks the index and rebuilds it through Koan when a restored folder left it behind; `tests/Tangent.Tests/Posts/PostSearchTests.cs`.

A Koan answer that falls short is corrected in Koan.
