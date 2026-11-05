// The screens the visual loop captures, and where the design reference draws each one.
// A screen is a path under the base URL and the states it is captured in. A state is reached by a
// path of its own or a query on the path, by settings the Tangent Space starts with, by how far first run has
// gone, by the rows a seed writes, by signing the capture persona in, or by filling and clicking in the page; never
// by a dialog.

import type { Theme } from '../appearance.ts'

export type Width = 1440 | 1024 | 390
export type CaptureTheme = Exclude<Theme, 'system'>

export const widths: readonly Width[] = [1440, 1024, 390]
export const captureThemes: readonly CaptureTheme[] = ['light', 'dark']

export interface State {
  /** Part of the file name: lowercase words joined by hyphens. */
  name: string
  /** The path under the base URL this state opens, when it is not the screen's. */
  path?: string
  /** Query parameters added to the screen's path. */
  query?: Readonly<Record<string, string>>
  /** Settings the Tangent Space starts with, such as `{ 'Koan:Web:ApplicationUrl': '…' }`. */
  settings?: Readonly<Record<string, string>>
  /** How the place stands: established through first run by the capture operator before the page opens (the
   *  default), not established yet, or established by the capture persona, who is then its operator. */
  place?: 'established' | 'unestablished' | 'persona'
  /** Fields filled once the page has settled, by selector, before any click. */
  fill?: Readonly<Record<string, string>>
  /** Hidden fields given a value before any fill, by selector, such as a form's TID, so a state that writes repeats
   *  one Post in every width and theme rather than adding another. */
  values?: Readonly<Record<string, string>>
  /** Reloaded once the fields are filled, before any click, so the state is what the page keeps of them. */
  reload?: boolean
  /** A selector a picture is dropped on, at its end, once the fields are filled and before any click, as a person drops a file from
   *  their desktop: a PNG the capture paints, so the state shows an image brought into the words. */
  drop?: string
  /** A selector clicked once the page has settled; the state is what the click shows in the page. */
  click?: string
  /** A selector the page shows once the click is answered, for a click the page answers in place or by a navigation
   *  it starts itself, or, without a click, once the fill is answered; awaited before the network settles. */
  waitFor?: string
  /** The rows the Tangent Space holds, by the name of a seed the capture host writes before any page opens
   *  (src/server/Capture). States with the same seed and settings share one Tangent Space. */
  seed?: string
  /** A seed the capture host writes again before every width and theme, for a state whose click changes what the
   *  next width and theme would open. */
  reseed?: string
  /** Signed in as the capture persona through Koan's test connector before the page opens. */
  signedIn?: boolean
  /** A path under the base URL whose requests are cancelled: a click that would leave the page shows the moment
   *  before the answer, such as a form on its way, and a post an island sends by itself changes nothing. */
  cancel?: string
}

// Whom a signed-in state is captured as; the sign-in makes them a participant. The handle reaches the sign-in as
// the atproto connector's verified handle claim.
export const capturePersona = { name: 'ada', did: 'did:plc:capture4persona', handle: 'ada.bsky.social' } as const

// Who establishes the place through first run before a state opens, unless the state says otherwise; the place
// takes its name from their handle.
export const captureOperator = { name: 'leo', did: 'did:plc:capture4operator', handle: 'leo.bsky.social' } as const

export interface Screen {
  /** The path under the base URL, starting with a slash. */
  path: string
  states: readonly State[]
}

// A Tangent Space with no URL configured, in Production, behind the capture's loopback proxy, which it believes as it
// believes any proxy on its own machine: it answers at the base URL the proxy names, and offers its operator the
// public origin it is also reached at.
const offerSettings = { 'Koan:Web:ApplicationUrl': '', ASPNETCORE_ENVIRONMENT: 'Production' } as const

// A Tangent Space whose configured base URL is a loopback address, which only its own machine reaches; in Development,
// where atproto sign-in has a loopback http client.
const localSettings = { 'Koan:Web:ApplicationUrl': 'http://localhost', ASPNETCORE_ENVIRONMENT: 'Development' } as const

// A screen's name is its key here, lowercase words joined by hyphens, and the visual gate row of
// docs/FEATURES.md names each screen of this registry by that key. The door's two screens are the
// door's states.
export const screens: Readonly<Record<string, Screen>> = {
  // A visitor's front page; the capture persona with news since her place checkpoint — replies to her Topic, a
  // mention of her, new articles and new Posts in each Tangent; and the same persona with her checkpoint at the
  // log's last sequence, quiet; a visitor's door whose Tangents wear their looks and hold older Topics, each Tangent's shelf
  // holding its latest past the front page; and the persona as the one who set the place up, who administers it and is offered a
  // new Tangent, with her account open, where the place's settings are offered; and her first door after setup,
  // holding the first Tangent and the moderators', with nothing new. Then the persona as the operator of a place reached at a public
  // origin while it has no address: the Offer on her door, with its evidence and its two answers; where Use it lands, first run's
  // pointers under the origin she used; and her door once she said Not this one. Last, the persona arriving at a place
  // whose admission says apply: her door says her application is waiting. And the persona as a moderator of a place of
  // sixty Tangents after its first: the first page of them, the moderators' Tangent after it, and the way to all of them.
  // Last, the persona timed out by the operator, whose door says until when; and lifted and made a moderator, whose door
  // says she is back and what she was made.
  door: {
    path: '/',
    states: [
      { name: 'visitor', seed: 'door' },
      { name: 'dressed', seed: 'dressed' },
      { name: 'signed-in-with-news', seed: 'door', signedIn: true },
      { name: 'quiet', seed: 'door-quiet', signedIn: true },
      { name: 'shelves', seed: 'door-shelves' },
      { name: 'administrator', place: 'persona', seed: 'door', signedIn: true },
      { name: 'administrator-account', place: 'persona', seed: 'door', signedIn: true, click: '.account-button' },
      { name: 'first', place: 'persona', signedIn: true },
      { name: 'offer', seed: 'offer', place: 'persona', signedIn: true, settings: offerSettings },
      { name: 'offer-used', seed: 'offer-used', place: 'persona', signedIn: true, settings: offerSettings, path: '/first-run' },
      { name: 'offer-declined', seed: 'offer-declined', place: 'persona', signedIn: true, settings: offerSettings },
      { name: 'applicant', seed: 'apply', signedIn: true },
      { name: 'many-tangents', seed: 'many-tangents', signedIn: true },
      { name: 'timed-out', seed: 'timed-out', signedIn: true },
      { name: 'lifted', seed: 'lifted', signedIn: true },
    ],
  },
  // Every Tangent the persona may view in a place of sixty after its first: the first page, the page after it, and the
  // first page as a visitor reads it.
  tangents: {
    path: '/t',
    states: [
      { name: 'first-page', seed: 'many-tangents', signedIn: true },
      { name: 'next-page', seed: 'many-tangents', signedIn: true, click: '.topic-pages-next', waitFor: '.topic-pages a:not(.topic-pages-next)' },
      { name: 'visitor', seed: 'many-tangents' },
    ],
  },
  // Members-only Tangents as someone outside them finds them: one anyone joins, one to ask to join, one whose ask waits.
  'tangents-members-only': {
    path: '/t',
    states: [{ name: 'outside', seed: 'members-only', signedIn: true }],
  },
  // A members-only Tangent's people, for whoever manages it: two asking to join, who is in, and a search.
  'tangent-settings-people': {
    path: '/t/workshop/settings/people',
    states: [
      { name: 'asking', seed: 'members-only-people', signedIn: true },
      { name: 'found', seed: 'members-only-people', signedIn: true, query: { find: 'l' } },
      { name: 'door', seed: 'members-only-people', signedIn: true, path: '/' },
    ],
  },
  // What a visitor reads: the action that signs in with Bluesky; on its way once pressed; the handle form for another
  // server, opened; and back from a failed sign-in with Koan's code: one through Bluesky that the provider did not
  // confirm, and by handle, the typed handle kept, one refused on the provider's page and a handle no account answers to;
  // and what a private place answers a visitor at the address of one of its Tangents.
  'sign-in': {
    path: '/sign-in',
    states: [
      { name: 'empty' },
      { name: 'submitting', click: '.sign-in-submit', cancel: '/auth/atproto/challenge' },
      { name: 'another-server', click: '.sign-in-other-toggle' },
      { name: 'sign-in-failed', query: { error: 'sign_in_failed' } },
      { name: 'access-denied', query: { error: 'access_denied', handle: 'ada.bsky.social' } },
      { name: 'handle-unresolved', query: { error: 'handle_unresolved', handle: 'ada.bsky.socail' } },
      { name: 'private-place', seed: 'private-place', path: '/t/discussions' },
    ],
  },
  // A visitor, before anyone signs in, whose sign-in sets the place up; signed in where the configuration names
  // someone else's handle as operator, waiting; and set up by the persona's sign-in, ready, with the site's pointers
  // for its operator, under the name her handle gives the place; and ready at a loopback address, which no site can
  // point at, saying when the pointers appear instead.
  'first-run': {
    path: '/first-run',
    states: [
      { name: 'visitor', place: 'unestablished' },
      { name: 'waiting', place: 'unestablished', signedIn: true, settings: { 'Tangent:Operator': captureOperator.handle } },
      { name: 'ready', place: 'persona', signedIn: true },
      { name: 'ready-local', place: 'persona', signedIn: true, settings: localSettings },
    ],
  },
  // The place's settings, general, read by the persona, who set the place up and so holds ManageSpace: the sections
  // in the rail, the name and description, and who reads it, public; the same once the place is private; and by the
  // persona in a place someone else set up, where she holds no role, refused with the gate above read-only fields.
  'tangent-space-settings-general': {
    path: '/settings',
    states: [
      { name: 'proprietor', place: 'persona', signedIn: true },
      { name: 'private', place: 'persona', seed: 'private-place', signedIn: true },
      { name: 'refused', signedIn: true },
    ],
  },
  // The place's roles, read by the persona, who set the place up and so shapes every role: administrator, moderator,
  // the night shift and patrons, member and everyone, each with what she may do to it; the new role's form opened; the
  // night shift's change opened in place; its deletion opened, confirmed in the page; and refused, read by the persona
  // as a member of a place someone else set up.
  'tangent-space-settings-roles': {
    path: '/settings/roles',
    states: [
      { name: 'with-roles', place: 'persona', seed: 'roles', signedIn: true },
      { name: 'new-role-open', place: 'persona', seed: 'roles', signedIn: true, click: '.roles-new > summary', waitFor: '.roles-new[open]' },
      { name: 'change-open', place: 'persona', seed: 'roles', signedIn: true, click: '.roles-change > summary', waitFor: '.roles-change[open]' },
      {
        name: 'delete-confirmation',
        place: 'persona',
        seed: 'roles',
        signedIn: true,
        click: '.roles-delete > summary',
        waitFor: '.roles-delete[open]',
      },
      { name: 'refused', signedIn: true },
    ],
  },
  // The place's admission, read by the persona, who set the place up and so holds ManageSpace: join at once, and apply
  // and wait once the place says so; and by the persona in a place someone else set up, refused above disabled choices.
  'tangent-space-settings-admission': {
    path: '/settings/admission',
    states: [
      { name: 'proprietor', place: 'persona', signedIn: true },
      { name: 'apply', place: 'persona', seed: 'apply', signedIn: true },
      { name: 'refused', signedIn: true },
    ],
  },
  // The place's appearance, read by the persona, who set the place up and so holds ManageSpace: the base's own colours, as a place
  // starts, jade once the place says so, and dressed, with a cover, jade and every word written, the frame carrying the lines; and
  // by the persona in a place someone else set up, refused above disabled choices.
  'tangent-space-settings-appearance': {
    path: '/settings/appearance',
    states: [
      { name: 'proprietor', place: 'persona', signedIn: true },
      { name: 'jade', place: 'persona', seed: 'jade', signedIn: true },
      { name: 'dressed', place: 'persona', seed: 'dressed', signedIn: true },
      { name: 'refused', signedIn: true },
    ],
  },
  // Export, read by the persona, who set the place up and so holds ManageSpace: the whole place and each Tangent with
  // its download; and by the persona in a place someone else set up, refused with no download.
  'tangent-space-settings-export': {
    path: '/settings/export',
    states: [
      { name: 'proprietor', place: 'persona', signedIn: true },
      { name: 'refused', signedIn: true },
    ],
  },
  // The place's applications, read by the persona, who set the place up and so decides them: two waiting and one
  // declined; the first one's decline opened, confirmed in the page; and none.
  'tangent-space-settings-applications': {
    path: '/settings/applications',
    states: [
      { name: 'with-applicants', place: 'persona', seed: 'applications', signedIn: true },
      { name: 'decline-confirmation', place: 'persona', seed: 'applications', signedIn: true, click: '.application-decline summary' },
      { name: 'empty', place: 'persona', signedIn: true },
    ],
  },
  // The place's people, read by the persona, who set the place up and so is its operator and acts on everyone else:
  // an administrator, a moderator, members, one an agent, one timed out and one banned, each with what she may change
  // of them; the first time out opened, with its durations; the first ban opened, confirmed in the page; the start of a
  // handle found; as a moderator of a place someone else set up, whose rail lists only what she may use; and refused,
  // read by the persona as a member of a place someone else set up.
  'tangent-space-settings-people': {
    path: '/settings/people',
    states: [
      { name: 'with-people', place: 'persona', seed: 'people', signedIn: true },
      {
        name: 'time-out-open',
        place: 'persona',
        seed: 'people',
        signedIn: true,
        click: '.people-time-out > summary',
        waitFor: '.people-time-out[open]',
      },
      { name: 'ban-confirmation', place: 'persona', seed: 'people', signedIn: true, click: '.people-ban > summary', waitFor: '.people-ban[open]' },
      { name: 'found', place: 'persona', seed: 'people', signedIn: true, query: { handle: 'ma' } },
      { name: 'moderator', seed: 'many-tangents', signedIn: true },
      { name: 'refused', signedIn: true },
    ],
  },
  // The page as a visitor reads it and as the persona does: the link that opens Companion Lobby with the address, and
  // the one that gets it.
  'bring-your-agent': {
    path: '/bring-your-agent',
    states: [
      { name: 'visitor' },
      { name: 'signed-in', signedIn: true },
    ],
  },
  // The form that starts a Topic in the discussions Tangent, the composer on it: empty for the persona, a member; her
  // words typed, then the page opened again, which brings them back; sent with a body of nothing but spaces, refused
  // above the fields; opened by a visitor, refused with the sign-in; and opened in a place where newcomers apply by the
  // persona, whose application waits, refused with the wait.
  'new-topic': {
    path: '/t/discussions/new',
    states: [
      { name: 'empty', seed: 'write', signedIn: true },
      {
        name: 'restored',
        seed: 'write',
        signedIn: true,
        fill: { '#new-topic-title': 'A weekly link and a question', '#new-topic-body': 'One link each, one question each, on Fridays.' },
        reload: true,
      },
      {
        name: 'refused',
        seed: 'write',
        signedIn: true,
        fill: { '#new-topic-title': 'A weekly link and a question', '#new-topic-body': '   ' },
        click: '.post-form-submit',
        waitFor: 'form[data-composer="refused"]',
      },
      { name: 'visitor', seed: 'write' },
      { name: 'applicant', seed: 'apply', signedIn: true, path: '/t/general/new' },
    ],
  },
  // The article editor, the persona writing: a new article as it opens; her article, its cover, heading, list, quote and
  // image in their places, as it will read; the same in its source view; and a new article whose title and words she
  // typed, the page opened again, which brings them back.
  'article-editor': {
    path: '/t/articles/new',
    states: [
      { name: 'empty', seed: 'article', signedIn: true },
      { name: 'writing', seed: 'article', signedIn: true, path: '/t/articles/mending-with-gold/3mwgqaw5is227/edit' },
      { name: 'source', seed: 'article', signedIn: true, path: '/t/articles/mending-with-gold/3mwgqaw5is227/edit', click: '.article-views button[title="Markdown source"]' },
    ],
  },
  // The article as it reads: the same article the editor holds, to the persona and to a visitor.
  article: {
    path: '/t/articles/mending-with-gold',
    states: [
      { name: 'read', seed: 'article', signedIn: true },
      { name: 'visitor', seed: 'article' },
    ],
  },
  // The persona's Connections: her Bluesky account publishing her articles, one updated, one published, and one it
  // couldn't reach Bluesky for, trying again; and her article as it reads once it is out.
  connections: {
    path: '/settings/connections',
    states: [
      { name: 'publishing', seed: 'connections', signedIn: true },
      { name: 'article-published', seed: 'connections', signedIn: true, path: '/t/articles/mending-with-gold' },
    ],
  },
  // A published article as a visitor reads it, with what Bluesky said about it underneath: its counts there and the
  // newest replies and quotes, each attributed and linked to the original.
  elsewhere: {
    path: '/t/articles/mending-with-gold',
    states: [{ name: 'visitor', seed: 'connections' }],
  },
  // The form that creates a Tangent, read by the persona, who set the place up and so administers it, in a place that
  // holds only the moderators' Tangent: as it opens, the name alone with the rest at its defaults behind one line; and
  // sent with a name of nothing but spaces, refused under the name.
  'new-tangent': {
    path: '/t/new',
    states: [
      { name: 'empty', place: 'persona', signedIn: true },
      { name: 'refused', place: 'persona', signedIn: true, fill: { '#new-tangent-name': '   ' }, click: '.post-form-submit' },
    ],
  },
  // The discussions Tangent's settings, read by the persona, who set the place up and so administers it: its name,
  // description, kind and audience, and its place between its neighbours with the two moves; by the persona as a member
  // who holds ManageTangent there alone, who reads nothing of where it stands; the moderators' Tangent's, whose
  // audience and place stay as they are; and, last, the same member making it private, which closes it to her and
  // lands on the page that says it is saved, the Tangent opened again before every width and theme.
  'tangent-settings-details': {
    path: '/t/discussions/settings',
    states: [
      { name: 'administrator', place: 'persona', seed: 'tangent', signedIn: true },
      { name: 'dressed', place: 'persona', seed: 'tangent-dressed', signedIn: true },
      { name: 'manager', seed: 'tangent-manager', signedIn: true },
      {
        name: 'closed',
        seed: 'tangent-manager',
        signedIn: true,
        reseed: 'discussions-reopened',
        values: { 'input[name="audience"][value="public"]': 'private' },
        click: '.post-form-submit',
        waitFor: '#closed-heading',
      },
      { name: 'moderators', place: 'persona', seed: 'tangent', signedIn: true, path: '/t/moderators/settings' },
    ],
  },
  // A Tangent's access, read by the persona, who set the place up and so manages every role: the studio, private, whose
  // member role reads and posts there, members-only, where patrons read alone; the public discussions, where each role
  // holds what it holds in the place; the moderators' Tangent, whose access stays as it is; and refused, read by the
  // persona as a member of a place someone else set up, who reads the gate and no role.
  'tangent-settings-access': {
    path: '/t/studio/settings/access',
    states: [
      { name: 'members-only', place: 'persona', seed: 'roles', signedIn: true },
      { name: 'public', place: 'persona', seed: 'roles', signedIn: true, path: '/t/discussions/settings/access' },
      { name: 'moderators', place: 'persona', seed: 'roles', signedIn: true, path: '/t/moderators/settings/access' },
      { name: 'refused', seed: 'roles', signedIn: true, path: '/t/discussions/settings/access' },
    ],
  },
  // The form that edits the persona's own Topic: as it opens; sent with a body of nothing but spaces, refused under
  // the body; sent with a value that cannot bind, refused in the place's frame; with the removal opened, confirmed in the page; and Leo's reply in the same Topic, which is not hers.
  edit: {
    path: '/t/discussions/what-should-an-introduction-say/3mwgqaw5is227/edit',
    states: [
      { name: 'form', seed: 'write', signedIn: true },
      { name: 'refused', seed: 'write', signedIn: true, fill: { '#edit-body': '   ' }, click: '.post-form-submit' },
      { name: 'not-accepted', seed: 'write', signedIn: true, values: { 'input[name="version"]': 'not-a-number' }, click: '.post-form-submit' },
      { name: 'remove-confirmation', seed: 'write', signedIn: true, click: '.post-remove-toggle' },
      { name: 'not-yours', seed: 'write', signedIn: true, path: '/t/discussions/what-should-an-introduction-say/3mwnstt4b222d/edit' },
    ],
  },
  // A discussions Tangent and an articles Tangent, populated and empty, read by the persona, a member with some
  // Topics new to them; the discussions Tangent also as a visitor reads it, with the persona's account open, with
  // the Tangent switcher open, and with the switcher open for the persona as the one who set the place up, who is
  // offered its settings and a new Tangent; and with the switcher open in a place of sixty Tangents after its first,
  // which lists the first page of them and sends the reader to all of them.
  tangent: {
    path: '/t/discussions',
    states: [
      { name: 'discussions', seed: 'tangent', signedIn: true },
      { name: 'discussions-dressed', place: 'persona', seed: 'tangent-dressed', signedIn: true, click: '.switcher-button' },
      { name: 'discussions-visitor', seed: 'tangent' },
      { name: 'discussions-account', seed: 'tangent', signedIn: true, click: '.account-button' },
      { name: 'discussions-switcher', seed: 'tangent', signedIn: true, click: '.switcher-button' },
      { name: 'discussions-administrator', place: 'persona', seed: 'tangent', signedIn: true, click: '.switcher-button' },
      { name: 'discussions-empty', seed: 'tangent', signedIn: true, path: '/t/workshop' },
      { name: 'articles', seed: 'tangent', signedIn: true, path: '/t/articles' },
      { name: 'articles-empty', seed: 'tangent', signedIn: true, path: '/t/field-notes' },
      { name: 'articles-covered', seed: 'article', signedIn: true, path: '/t/articles' },
      { name: 'many-switcher', seed: 'many-tangents', signedIn: true, path: '/t/tangent-01', click: '.switcher-button' },
    ],
  },
  // A short Topic whose replies answer one another, read by the persona up to its first reply, with the composer empty
  // under it, and as a visitor reads it, told to sign in to reply; a long Topic of more replies than two windows,
  // opened at the persona's first unread with the older window a cursor away; the same Topic at the address of its
  // fortieth reply, marked in the middle of its window, the composer replying to it; a Topic with a reply a moderator
  // removed; and a Topic without replies. Then the composer: her reply typed and the page opened again, which brings
  // it back; a reply of nothing but spaces, refused above the field; and
  // last, since it writes, a reply sent through the composer, which lands on the reply's address; it carries the same
  // TID in every width and theme, so the server's idempotency keeps it one Post; and a reply to the quiet question
  // that types its author's handle, which lands reading it as her mention. Then take this on a tangent: a Topic
  // whose reply was taken into the workshop, each Post offering the verb and the reply listing the Topic taken from
  // it; that Topic, linked to its origin; and a Topic taken from a Post in a Tangent the persona may not view, which
  // says only that. Then reporting: the root's Report opened in the page, its short form under the Post; a reply's
  // report page, the same form on a page of its own; and that page once the persona's report of the reply was
  // received. A signed-in state cancels the seen post the seen island sends once the window's end is on screen,
  // so its control stays on the page and every width and theme reads the same checkpoints.
  // The reply composer offering what the persona can name as she types: after @, the people of the Topic and the
  // Tangent, then the roles with how many hold each; after #, the Tangents she may view; and the article editor's visual
  // view offering the same after @.
  suggestions: {
    path: '/t/discussions/what-kind-of-discussions',
    states: [
      { name: 'people', seed: 'topic', signedIn: true, cancel: '/seen', fill: { '#reply-body': 'Good question. @' }, waitFor: '.suggestions:not([hidden])' },
      { name: 'tangents', seed: 'topic', signedIn: true, cancel: '/seen', fill: { '#reply-body': 'This belongs in #' }, waitFor: '.suggestions:not([hidden])' },
      { name: 'article', seed: 'article', signedIn: true, path: '/t/articles/new', fill: { '.article-surface': 'Mended with @' }, waitFor: '.suggestions:not([hidden])' },
    ],
  },
  // What a reader and a writer meet beyond a Topic's Posts, read by the persona: the reply a moderator rewrote, its
  // earlier version open under it; the reply box with the article editor's tools; and an article with its outline.
  reading: {
    path: '/t/discussions/is-a-visible-repair-a-better-repair',
    states: [
      { name: 'history-open', seed: 'topic', signedIn: true, cancel: '/seen', click: '.post-history > summary', waitFor: '.post-history-versions' },
      { name: 'reply-toolbar', seed: 'topic', signedIn: true, cancel: '/seen', waitFor: '.post-body-field.is-visual' },
      {
        name: 'reply-picture-dropped',
        seed: 'topic',
        signedIn: true,
        cancel: '/seen',
        fill: { '.post-surface': 'Here is the bowl after the second firing:' },
        drop: '.post-surface',
        waitFor: '.post-surface img[src*="/media/"]',
      },
      {
        name: 'new-topic-picture-dropped',
        seed: 'topic',
        signedIn: true,
        path: '/t/discussions/new',
        fill: { '#new-topic-title': 'What a seam remembers', '.post-surface': 'A bowl we mended last week, seam and all:' },
        drop: '.post-surface',
        waitFor: '.post-surface img[src*="/media/"]',
      },
      { name: 'article-outline', seed: 'article', signedIn: true, cancel: '/seen', path: '/t/articles/mending-with-gold' },
    ],
  },
  topic: {
    path: '/t/discussions/what-kind-of-discussions',
    states: [
      { name: 'populated', seed: 'topic', signedIn: true, cancel: '/seen' },
      { name: 'populated-visitor', seed: 'topic' },
      {
        name: 'long',
        seed: 'topic',
        signedIn: true,
        path: '/t/discussions/notes-from-the-repair-workshop',
        cancel: '/seen',
      },
      {
        name: 'anchored',
        seed: 'topic',
        signedIn: true,
        path: '/t/discussions/notes-from-the-repair-workshop/3mwdfsj6d226g',
        cancel: '/seen',
      },
      {
        name: 'tombstone',
        seed: 'topic',
        signedIn: true,
        path: '/t/discussions/is-a-visible-repair-a-better-repair',
        cancel: '/seen',
      },
      {
        name: 'empty-replies',
        seed: 'topic',
        signedIn: true,
        path: '/t/discussions/a-quiet-question',
        cancel: '/seen',
      },
      {
        name: 'composer-restored',
        seed: 'topic',
        signedIn: true,
        path: '/t/discussions/a-quiet-question',
        cancel: '/seen',
        fill: { '#reply-body': 'Mornings, before anyone else is in.' },
        reload: true,
      },
      {
        name: 'composer-refused',
        seed: 'topic',
        signedIn: true,
        path: '/t/discussions/a-quiet-question',
        cancel: '/seen',
        fill: { '#reply-body': '   ' },
        click: '.post-form-submit',
        waitFor: 'form[data-composer="refused"]',
      },
      {
        name: 'replied',
        seed: 'topic',
        signedIn: true,
        cancel: '/seen',
        values: { '#reply-form-id': '3mwfixedrep22' },
        fill: { '#reply-body': 'Fridays it is. I’ll bring a link about glue.' },
        click: '.post-form-submit',
        waitFor: 'article.post.is-current',
      },
      {
        name: 'replied-mention',
        seed: 'topic',
        signedIn: true,
        path: '/t/discussions/a-quiet-question',
        cancel: '/seen',
        values: { '#reply-form-id': '3mwfixedmen22' },
        fill: { '#reply-body': 'Index cards, @mara.bsky.social. A notebook replaced them in March.' },
        click: '.post-form-submit',
        waitFor: 'article.post.is-current',
      },
      {
        name: 'taken-origin',
        seed: 'tangent-taken',
        signedIn: true,
        path: '/t/discussions/where-should-the-glue-notes-live',
        cancel: '/seen',
      },
      {
        name: 'taken-from',
        seed: 'tangent-taken',
        signedIn: true,
        path: '/t/workshop/adhesives-we-trust',
        cancel: '/seen',
      },
      {
        name: 'taken-from-unseen',
        seed: 'tangent-taken',
        signedIn: true,
        path: '/t/workshop/the-kiln-budget',
        cancel: '/seen',
      },
      {
        name: 'report-open',
        seed: 'topic',
        signedIn: true,
        cancel: '/seen',
        click: '.post-root .post-report > summary',
        waitFor: '.post-root .post-report[open]',
      },
      {
        name: 'report-page',
        seed: 'topic-reported',
        signedIn: true,
        path: '/t/discussions/glazes-we-keep-coming-back-to/3mw6hf56bs224/report',
      },
      {
        name: 'report-received',
        seed: 'topic-reported',
        signedIn: true,
        path: '/t/discussions/glazes-we-keep-coming-back-to/3mw6hf56bs224/report',
        query: { received: '3mw6iivles225' },
      },
    ],
  },
  // A participant's profile: Leo's, an administrator who wrote in the discussions, the workshop and the studio, read
  // by the persona, a member, without the studio's Topic; the same as a visitor reads it; the persona's own; and a handle
  // no participant holds.
  profile: {
    path: '/p/leo.sylin.org',
    states: [
      { name: 'populated', seed: 'profile', signedIn: true },
      { name: 'visitor', seed: 'profile' },
      { name: 'own', seed: 'profile', signedIn: true, path: '/p/ada.bsky.social' },
      { name: 'absent', seed: 'profile', path: '/p/nobody.example' },
      // Mara's profile read by the persona as the operator of the place, who may change her standing and roles: where
      // she stands, with what the persona may change inline, and the ban opened, confirmed in the page.
      { name: 'keeper', place: 'persona', seed: 'profile', signedIn: true, path: '/p/mara.bsky.social' },
      {
        name: 'keeper-ban-open',
        place: 'persona',
        seed: 'profile',
        signedIn: true,
        path: '/p/mara.bsky.social',
        click: '.profile-standing .people-ban > summary',
        waitFor: '.profile-standing .people-ban[open]',
      },
    ],
  },
  // A search of the Topic seed's words: found, read by the persona and by a visitor; words nothing here holds; the page
  // before anyone has searched, which points at the box; and the box's suggestions as someone types, on the door.
  search: {
    path: '/search',
    states: [
      { name: 'found', seed: 'topic', signedIn: true, query: { q: 'half' } },
      { name: 'visitor', seed: 'topic', query: { q: 'half' } },
      { name: 'nothing', seed: 'topic', signedIn: true, query: { q: 'porcelain' } },
      { name: 'start', seed: 'topic', signedIn: true },
      {
        name: 'suggestions',
        seed: 'topic',
        signedIn: true,
        path: '/',
        fill: { '.masthead-search-field': 'hal' },
        click: '.masthead-search-field',
        waitFor: '.masthead-search-hit',
      },
    ],
  },
  // The new-topic form taking Leo's reply on a tangent, read by the persona, a member: as it opens, the reply quoted,
  // the Tangents she may start a Topic in as links, and a title offered from its first words; sent with a title of
  // nothing but spaces, refused under the title; and as a visitor reads it, who is told to sign in.
  'take-this-on-a-tangent': {
    path: '/t/discussions/new',
    states: [
      { name: 'form', seed: 'tangent-taken', signedIn: true, query: { origin: '3mwfyixhb2224' } },
      { name: 'refused', seed: 'tangent-taken', signedIn: true, query: { origin: '3mwfyixhb2224' }, fill: { '#new-topic-title': '   ' }, click: '.post-form-submit' },
      { name: 'visitor', seed: 'tangent-taken', query: { origin: '3mwfyixhb2224' } },
    ],
  },
}

// The reference's screens by width, as file names under its screens/ folder. Each is drawn at one size
// and switches theme by its ?theme= query. The door, take this on a tangent, bring your agent and
// connections have no reference screen; their target is the mock Leo approves.
export const references: Readonly<Record<string, Partial<Record<Width, string>>>> = {
  'tangent-discussions': { 1440: '03-tangent-discussions.html', 390: '21-phone-discussions.html' },
  'tangent-articles': { 1440: '02-tangent-articles.html' },
  topic: { 1440: '04-discussion-topic.html', 390: '22-phone-discussion-topic.html' },
  'article-editor': { 1440: '06-article-editor.html' },
  'new-topic': { 1440: '05-new-topic.html' },
  'sign-in': { 1440: '19-sign-in.html' },
  'tangent-space-settings-general': { 1440: '08-space-general.html' },
  'tangent-space-settings-appearance': { 1440: '09-space-appearance.html' },
  'tangent-space-settings-people': { 1440: '10-space-people.html' },
  'tangent-space-settings-roles': { 1440: '11-space-roles.html' },
  'tangent-settings-details': { 1440: '15-tangent-about.html' },
  'tangent-settings-access': { 1440: '17-tangent-access.html' },
}

// A screen the reference draws as several screens, by variant: each variant names an entry of `references`, and
// its reference captures are named <width>-<theme>-reference-<variant>.png.
export const referenceVariants: Readonly<Record<string, Readonly<Record<string, string>>>> = {
  tangent: { discussions: 'tangent-discussions', articles: 'tangent-articles' },
}

// For a screen the reference does not draw, the reference screens nearest to it, named in the round's
// README for whoever draws its mock. They are not rendered and are not its target.
export const nearestReadings: Readonly<Record<string, readonly string[]>> = {
  door: ['01-space-home.html', '20-phone-space-home.html'],
  'first-run': ['08-space-general.html', '19-sign-in.html'],
  edit: ['05-new-topic.html'],
}
