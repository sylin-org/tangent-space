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
  /** A selector clicked once the page has settled; the state is what the click shows in the page. */
  click?: string
  /** A selector the page shows once the click is answered, for a click the page answers in place or by a navigation
   *  it starts itself; awaited before the network settles. */
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

// The consent screen's Tangent Space: Koan's server lets its code look-ups through as often as a capture asks.
const consentSettings = { 'Koan:Web:Auth:Server:UserCodeVerificationRateLimitPerMinute': '1000' } as const

// A Tangent Space with no URL configured, in Production, behind the capture's loopback proxy, which it believes as it
// believes any proxy on its own machine: it answers at the base URL the proxy names, and offers its operator the
// public origin it is also reached at.
const offerSettings = { 'Koan:Web:ApplicationUrl': '', ASPNETCORE_ENVIRONMENT: 'Production' } as const

// A Tangent Space whose configured base URL is a loopback address, which only its own machine reaches; in Development,
// where atproto sign-in has a loopback http client.
const localSettings = { 'Koan:Web:ApplicationUrl': 'http://localhost', ASPNETCORE_ENVIRONMENT: 'Development' } as const

// A screen's name is its name in docs/FEATURES.md "Screens", lowercase, with hyphens between words:
// "Tangent Space settings (general)" is tangent-space-settings-general. The door's two screens are the
// door's states.
export const screens: Readonly<Record<string, Screen>> = {
  // A visitor's front page; the capture persona with news since her place checkpoint — replies to her Topic, a
  // mention of her, new articles and new Posts in each Tangent; and the same persona with her checkpoint at the
  // log's last sequence, quiet; and the persona as the one who set the place up, who administers it and is offered a
  // new Tangent, with her account open, where the place's settings are offered; and her first door after setup,
  // holding the first Tangent and the moderators'. Then the persona as the operator of a place reached at a public
  // origin while it has no address: the Offer on her door, with its evidence and its two answers; where Use it lands, first run's
  // pointers under the origin she used; and her door once she said Not this one. Last, the persona arriving at a place
  // whose admission says apply: her door says her application is waiting.
  door: {
    path: '/',
    states: [
      { name: 'visitor', seed: 'door' },
      { name: 'signed-in-with-news', seed: 'door', signedIn: true },
      { name: 'quiet', seed: 'door-quiet', signedIn: true },
      { name: 'administrator', place: 'persona', seed: 'door', signedIn: true },
      { name: 'administrator-account', place: 'persona', seed: 'door', signedIn: true, click: '.account-button' },
      { name: 'first', place: 'persona', signedIn: true },
      { name: 'offer', seed: 'offer', place: 'persona', signedIn: true, settings: offerSettings },
      { name: 'offer-used', seed: 'offer-used', place: 'persona', signedIn: true, settings: offerSettings, path: '/first-run' },
      { name: 'offer-declined', seed: 'offer-declined', place: 'persona', signedIn: true, settings: offerSettings },
      { name: 'applicant', seed: 'apply', signedIn: true },
    ],
  },
  // What a visitor reads: the action that signs in with Bluesky; on its way once pressed; the handle form for another
  // server, opened; and back from a failed sign-in with Koan's code: one through Bluesky that the provider did not
  // confirm, and by handle, the typed handle kept, one refused on the provider's page and a handle no account answers to.
  'sign-in': {
    path: '/sign-in',
    states: [
      { name: 'empty' },
      { name: 'submitting', click: '.sign-in-submit', cancel: '/auth/atproto/challenge' },
      { name: 'another-server', click: '.sign-in-other-toggle' },
      { name: 'sign-in-failed', query: { error: 'sign_in_failed' } },
      { name: 'access-denied', query: { error: 'access_denied', handle: 'ada.bsky.social' } },
      { name: 'handle-unresolved', query: { error: 'handle_unresolved', handle: 'ada.bsky.socail' } },
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
  // in the rail and the name and description; and by the persona in a place someone else set up, where she holds no
  // role, refused with the gate above read-only fields.
  'tangent-space-settings-general': {
    path: '/settings',
    states: [
      { name: 'proprietor', place: 'persona', signedIn: true },
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
  // The page a person decides on for an agent, signed in as the persona: asking for the code a device shows; the
  // request the shared client waits with under the seeded code; a code no request waits under, said under its field;
  // a code a self-registered client asked for, refused under its field; and the done page reached without a decision
  // of the persona's to show. Every look-up of a code counts against the rate limit, raised so every width and theme
  // reaches the page.
  consent: {
    path: '/consent',
    states: [
      { name: 'code', seed: 'consent', signedIn: true, settings: consentSettings },
      { name: 'request', seed: 'consent', signedIn: true, settings: consentSettings, query: { user_code: 'BCDF-GHJK' } },
      { name: 'code-error', seed: 'consent', signedIn: true, settings: consentSettings, query: { user_code: 'ZZZZ-ZZZZ' } },
      { name: 'code-refused', seed: 'consent', signedIn: true, settings: consentSettings, query: { user_code: 'CDFG-HJKM' } },
      { name: 'done', seed: 'consent', signedIn: true, settings: consentSettings, path: '/consent/done' },
    ],
  },
  // The form that starts a Topic in the discussions Tangent, the composer on it: empty for the persona, a member; her
  // words typed, then the page opened again, which brings them back; sent with a body of nothing but spaces, refused
  // above the fields; and as a visitor reads it, who is told to sign in.
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
    ],
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
  // The form that edits the persona's own Topic: as it opens; sent with a body of nothing but spaces, refused under
  // the body; with the removal opened, confirmed in the page; and Leo's reply in the same Topic, which is not hers.
  edit: {
    path: '/t/discussions/what-should-an-introduction-say/3mwgqaw5is227/edit',
    states: [
      { name: 'form', seed: 'write', signedIn: true },
      { name: 'refused', seed: 'write', signedIn: true, fill: { '#edit-body': '   ' }, click: '.post-form-submit' },
      { name: 'remove-confirmation', seed: 'write', signedIn: true, click: '.post-remove-toggle' },
      { name: 'not-yours', seed: 'write', signedIn: true, path: '/t/discussions/what-should-an-introduction-say/3mwnstt4b222d/edit' },
    ],
  },
  // A discussions Tangent and an articles Tangent, populated and empty, read by the persona, a member with some
  // Topics new to them; the discussions Tangent also as a visitor reads it, with the persona's account open, with
  // the Tangent switcher open, and with the switcher open for the persona as the one who set the place up, who is
  // offered its settings and a new Tangent.
  tangent: {
    path: '/t/discussions',
    states: [
      { name: 'discussions', seed: 'tangent', signedIn: true },
      { name: 'discussions-visitor', seed: 'tangent' },
      { name: 'discussions-account', seed: 'tangent', signedIn: true, click: '.account-button' },
      { name: 'discussions-switcher', seed: 'tangent', signedIn: true, click: '.switcher-button' },
      { name: 'discussions-administrator', place: 'persona', seed: 'tangent', signedIn: true, click: '.switcher-button' },
      { name: 'discussions-empty', seed: 'tangent', signedIn: true, path: '/t/workshop' },
      { name: 'articles', seed: 'tangent', signedIn: true, path: '/t/articles' },
      { name: 'articles-empty', seed: 'tangent', signedIn: true, path: '/t/field-notes' },
    ],
  },
  // A short Topic whose replies answer one another, read by the persona up to its first reply, with the composer empty
  // under it, and as a visitor reads it, told to sign in to reply; a long Topic of more replies than two windows,
  // opened at the persona's first unread with the older window a cursor away; the same Topic at the address of its
  // fortieth reply, marked in the middle of its window, the composer replying to it; a Topic with a reply a moderator
  // removed; and a Topic without replies. Then the composer: her reply typed and the page opened again, which brings
  // it back; a reply of nothing but spaces, refused above the field; a locked Topic, whose gate she reads instead; and
  // last, since it writes, a reply sent through the composer, which lands on the reply's address; it carries the same
  // TID in every width and theme, so the server's idempotency keeps it one Post. Then take this on a tangent: a Topic
  // whose reply was taken into the workshop, each Post offering the verb and the reply listing the Topic taken from
  // it; that Topic, linked to its origin; and a Topic taken from a Post in a Tangent the persona may not view, which
  // says only that. Then reporting: the root's Report opened in the page, its short form under the Post; a reply's
  // report page, the same form on a page of its own; and that page once the persona's report of the reply was
  // received. A signed-in state cancels the seen post the seen island sends once the window's end is on screen,
  // so its control stays on the page and every width and theme reads the same checkpoints.
  topic: {
    path: '/t/discussions/what-kind-of-discussions',
    states: [
      { name: 'populated', seed: 'topic', signedIn: true, cancel: '/t/discussions/what-kind-of-discussions/seen' },
      { name: 'populated-visitor', seed: 'topic' },
      {
        name: 'long',
        seed: 'topic',
        signedIn: true,
        path: '/t/discussions/notes-from-the-repair-workshop',
        cancel: '/t/discussions/notes-from-the-repair-workshop/seen',
      },
      {
        name: 'anchored',
        seed: 'topic',
        signedIn: true,
        path: '/t/discussions/notes-from-the-repair-workshop/3mwdfsj6d226g',
        cancel: '/t/discussions/notes-from-the-repair-workshop/seen',
      },
      {
        name: 'tombstone',
        seed: 'topic',
        signedIn: true,
        path: '/t/discussions/is-a-visible-repair-a-better-repair',
        cancel: '/t/discussions/is-a-visible-repair-a-better-repair/seen',
      },
      {
        name: 'empty-replies',
        seed: 'topic',
        signedIn: true,
        path: '/t/discussions/a-quiet-question',
        cancel: '/t/discussions/a-quiet-question/seen',
      },
      {
        name: 'composer-restored',
        seed: 'topic',
        signedIn: true,
        path: '/t/discussions/a-quiet-question',
        cancel: '/t/discussions/a-quiet-question/seen',
        fill: { '#reply-body': 'Mornings, before anyone else is in.' },
        reload: true,
      },
      {
        name: 'composer-refused',
        seed: 'topic',
        signedIn: true,
        path: '/t/discussions/a-quiet-question',
        cancel: '/t/discussions/a-quiet-question/seen',
        fill: { '#reply-body': '   ' },
        click: '.post-form-submit',
        waitFor: 'form[data-composer="refused"]',
      },
      {
        name: 'locked',
        seed: 'topic-locked',
        signedIn: true,
        path: '/t/discussions/the-kiln-schedule-is-settled',
        cancel: '/t/discussions/the-kiln-schedule-is-settled/seen',
      },
      {
        name: 'replied',
        seed: 'topic',
        signedIn: true,
        cancel: '/t/discussions/what-kind-of-discussions/seen',
        values: { '#reply-form-id': '3mwfixedrep22' },
        fill: { '#reply-body': 'Fridays it is. I’ll bring a link about glue.' },
        click: '.post-form-submit',
        waitFor: 'article.post.is-anchor',
      },
      {
        name: 'taken-origin',
        seed: 'tangent-taken',
        signedIn: true,
        path: '/t/discussions/where-should-the-glue-notes-live',
        cancel: '/t/discussions/where-should-the-glue-notes-live/seen',
      },
      {
        name: 'taken-from',
        seed: 'tangent-taken',
        signedIn: true,
        path: '/t/workshop/adhesives-we-trust',
        cancel: '/t/workshop/adhesives-we-trust/seen',
      },
      {
        name: 'taken-from-unseen',
        seed: 'tangent-taken',
        signedIn: true,
        path: '/t/workshop/the-kiln-budget',
        cancel: '/t/workshop/the-kiln-budget/seen',
      },
      {
        name: 'report-open',
        seed: 'topic',
        signedIn: true,
        cancel: '/t/discussions/what-kind-of-discussions/seen',
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
  // The form that takes Leo's reply on a tangent, read by the persona, a member: as it opens, the reply quoted and the
  // Tangents she may start a Topic in listed; sent with a title of nothing but spaces, refused under the title; and as a
  // visitor reads it, who is told to sign in.
  'take-this-on-a-tangent': {
    path: '/t/discussions/where-should-the-glue-notes-live/3mwfyixhb2224/tangent',
    states: [
      { name: 'form', seed: 'tangent-taken', signedIn: true },
      { name: 'refused', seed: 'tangent-taken', signedIn: true, fill: { '#take-title': '   ' }, click: '.post-form-submit' },
      { name: 'visitor', seed: 'tangent-taken' },
    ],
  },
}

// The reference's screens by width, as file names under its screens/ folder. Each is drawn at one size
// and switches theme by its ?theme= query. The door, take this on a tangent, bring your agent, consent
// and connections have no reference screen; their target is the mock Leo approves.
export const references: Readonly<Record<string, Partial<Record<Width, string>>>> = {
  'tangent-discussions': { 1440: '03-tangent-discussions.html', 390: '21-phone-discussions.html' },
  'tangent-articles': { 1440: '02-tangent-articles.html' },
  topic: { 1440: '04-discussion-topic.html', 390: '22-phone-discussion-topic.html' },
  'article-editor': { 1440: '06-article-editor.html' },
  'new-topic': { 1440: '05-new-topic.html' },
  notifications: { 1440: '18-notifications.html' },
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
