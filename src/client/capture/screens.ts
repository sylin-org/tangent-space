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
  /** Fields filled once the page has settled and the checks are made, by selector, before any click; the value
   *  setupCodeFill types the setup code this run's server wrote to its log. */
  fill?: Readonly<Record<string, string>>
  /** Radio buttons or checkboxes checked once the page has settled, before any click. */
  check?: readonly string[]
  /** A selector clicked once the page has settled; the state is what the click shows in the page. */
  click?: string
  /** The rows the Tangent Space holds, by the name of a seed the capture host writes before any page opens
   *  (src/server/Capture). States with the same seed and settings share one Tangent Space. */
  seed?: string
  /** Signed in as the capture persona through Koan's test connector before the page opens. */
  signedIn?: boolean
  /** A path under the base URL whose requests are cancelled, so a click that would leave the page shows the
   *  moment before the answer, such as a form on its way. */
  cancel?: string
}

// Whom a signed-in state is captured as; the sign-in makes them a participant. The handle reaches the sign-in as
// the atproto connector's verified handle claim.
export const capturePersona = { name: 'ada', did: 'did:plc:capture4persona', handle: 'ada.bsky.social' } as const

// Who establishes the place through first run before a state opens, unless the state says otherwise, and the
// name they give it.
export const captureOperator = { name: 'leo', did: 'did:plc:capture4operator', handle: 'leo.bsky.social' } as const
export const capturePlaceName = 'Tangent Space'

// The fill value that stands for the setup code the capture host logged at its start.
export const setupCodeFill = '{setup code}'

export interface Screen {
  /** The path under the base URL, starting with a slash. */
  path: string
  /** The name first run gives the place before the screen's states open; capturePlaceName unless the screen names
   *  its own, as one that shows the place's face does. */
  placeName?: string
  states: readonly State[]
}

// The consent screen's Tangent Space: Koan's server lets its code look-ups through as often as a capture asks.
const consentSettings = { 'Koan:Web:Auth:Server:UserCodeVerificationRateLimitPerMinute': '1000' } as const

// A screen's name is its name in docs/FEATURES.md "Screens", lowercase, with hyphens between words:
// "Tangent Space settings (general)" is tangent-space-settings-general. The door's two screens are the
// door's states.
export const screens: Readonly<Record<string, Screen>> = {
  // A visitor's front page; the capture persona with news since her place checkpoint — replies to her Topic, a
  // mention of her, new articles and new Posts in each Tangent; and the same persona with her checkpoint at the
  // log's last sequence, quiet.
  door: {
    path: '/',
    states: [
      { name: 'visitor', seed: 'door' },
      { name: 'signed-in-with-news', seed: 'door', signedIn: true },
      { name: 'quiet', seed: 'door-quiet', signedIn: true },
    ],
  },
  // The handle form a visitor reads, focused; on its way after Continue; and back from a failed sign-in with Koan's
  // code and the typed handle kept: one the provider did not confirm, one refused on the provider's page, and a
  // handle no account answers to.
  'sign-in': {
    path: '/sign-in',
    placeName: 'Lantern Works',
    states: [
      { name: 'empty' },
      { name: 'submitting', query: { handle: 'ada.bsky.social' }, click: '.sign-in-submit', cancel: '/auth/atproto/challenge' },
      { name: 'sign-in-failed', query: { error: 'sign_in_failed', handle: 'ada.bsky.social' } },
      { name: 'access-denied', query: { error: 'access_denied', handle: 'ada.bsky.social' } },
      { name: 'handle-unresolved', query: { error: 'handle_unresolved', handle: 'ada.bsky.socail' } },
    ],
  },
  // Before anyone signs in; signed in where the configuration names someone else as operator; signed in, the
  // form with its setup code field before anything is written, and refused for a wrong setup code; the form refused
  // for a proprietor handle no participant has, its setup code accepted; and established,
  // the site's pointers for its operator, under the name the persona gave the place.
  'first-run': {
    path: '/first-run',
    placeName: 'Ada’s Notebook',
    states: [
      { name: 'arrival', place: 'unestablished' },
      { name: 'waiting', place: 'unestablished', signedIn: true, settings: { 'Tangent:Operator': captureOperator.did } },
      { name: 'form', place: 'unestablished', signedIn: true },
      {
        name: 'form-code-error',
        place: 'unestablished',
        signedIn: true,
        check: ['#first-run-accountable'],
        fill: { '#first-run-code': 'AAAA-BBBB-CCCC-DDDD', '#first-run-name': 'Field notes' },
        click: '.sign-in-submit',
      },
      {
        name: 'form-error',
        place: 'unestablished',
        signedIn: true,
        check: ['#first-run-someone', '#first-run-accountable'],
        fill: { '#first-run-code': setupCodeFill, '#first-run-name': 'Field notes', '#first-run-handle': 'studio.example' },
        click: '.sign-in-submit',
      },
      { name: 'established', place: 'persona', signedIn: true },
    ],
  },
  // The page a person decides on for an agent, signed in as the persona: asking for the code a device shows; the
  // request the shared client waits with under the seeded code; a code no request waits under, said under its field;
  // and the done page reached without a decision of the persona's to show. Every look-up of a code counts against the
  // rate limit, raised so every width and theme reaches the page.
  consent: {
    path: '/consent',
    placeName: 'Lantern Works',
    states: [
      { name: 'code', seed: 'consent', signedIn: true, settings: consentSettings },
      { name: 'request', seed: 'consent', signedIn: true, settings: consentSettings, query: { user_code: 'BCDF-GHJK' } },
      { name: 'code-error', seed: 'consent', signedIn: true, settings: consentSettings, query: { user_code: 'ZZZZ-ZZZZ' } },
      { name: 'done', seed: 'consent', signedIn: true, settings: consentSettings, path: '/consent/done' },
    ],
  },
  // A discussions Tangent and an articles Tangent, populated and empty, read by the persona, a member with some
  // Topics new to them; the discussions Tangent also as a visitor reads it, with the persona's account open, and with
  // the Tangent switcher open.
  tangent: {
    path: '/t/discussions',
    states: [
      { name: 'discussions', seed: 'tangent', signedIn: true },
      { name: 'discussions-visitor', seed: 'tangent' },
      { name: 'discussions-account', seed: 'tangent', signedIn: true, click: '.account-button' },
      { name: 'discussions-switcher', seed: 'tangent', signedIn: true, click: '.switcher-button' },
      { name: 'discussions-empty', seed: 'tangent', signedIn: true, path: '/t/workshop' },
      { name: 'articles', seed: 'tangent', signedIn: true, path: '/t/articles' },
      { name: 'articles-empty', seed: 'tangent', signedIn: true, path: '/t/field-notes' },
    ],
  },
  // A short Topic whose replies answer one another, read by the persona up to its first reply, and as a visitor
  // reads it; a long Topic of more replies than two windows, opened at the persona's first unread with the older
  // window a cursor away; the same Topic at the address of its fortieth reply, marked in the middle of its window; a
  // Topic with a reply a moderator removed; and a Topic without replies.
  topic: {
    path: '/t/discussions/what-kind-of-discussions',
    states: [
      { name: 'populated', seed: 'topic', signedIn: true },
      { name: 'populated-visitor', seed: 'topic' },
      { name: 'long', seed: 'topic', signedIn: true, path: '/t/discussions/notes-from-the-repair-workshop' },
      { name: 'anchored', seed: 'topic', signedIn: true, path: '/t/discussions/notes-from-the-repair-workshop/3mwdfsj6d226g' },
      { name: 'tombstone', seed: 'topic', signedIn: true, path: '/t/discussions/is-a-visible-repair-a-better-repair' },
      { name: 'empty-replies', seed: 'topic', signedIn: true, path: '/t/discussions/a-quiet-question' },
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
}
