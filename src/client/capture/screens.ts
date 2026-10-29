// The screens the visual loop captures, and where the design reference draws each one.
// A screen is a path under the base URL and the states it is captured in. A state is reached by a
// query on the path, by settings the Tangent Space starts with, by how far first run has gone, by signing the
// capture persona in, or by filling and clicking in the page; never by a dialog.

import type { Theme } from '../appearance.ts'

export type Width = 1440 | 1024 | 390
export type CaptureTheme = Exclude<Theme, 'system'>

export const widths: readonly Width[] = [1440, 1024, 390]
export const captureThemes: readonly CaptureTheme[] = ['light', 'dark']

export interface State {
  /** Part of the file name: lowercase words joined by hyphens. */
  name: string
  /** Query parameters added to the screen's path. */
  query?: Readonly<Record<string, string>>
  /** Settings the Tangent Space starts with, such as `{ 'Koan:Web:ApplicationUrl': '…' }`. */
  settings?: Readonly<Record<string, string>>
  /** How the place stands: established through first run by the capture operator before the page opens (the
   *  default), not established yet, or established by the capture persona, who is then its operator. */
  place?: 'established' | 'unestablished' | 'persona'
  /** Fields filled once the page has settled and the checks are made, by selector, before any click. */
  fill?: Readonly<Record<string, string>>
  /** Radio buttons or checkboxes checked once the page has settled, before any click. */
  check?: readonly string[]
  /** A selector clicked once the page has settled; the state is what the click shows in the page. */
  click?: string
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

export interface Screen {
  /** The path under the base URL, starting with a slash. */
  path: string
  states: readonly State[]
}

// A screen's name is its name in docs/FEATURES.md "Screens", lowercase, with hyphens between words:
// "Tangent Space settings (general)" is tangent-space-settings-general. The door's two screens are the
// door's states.
export const screens: Readonly<Record<string, Screen>> = {
  door: { path: '/', states: [{ name: 'signed-out' }] },
  // The handle form a visitor reads, focused; on its way after Continue; after a sign-in that could not be
  // verified, the typed handle kept; and with a handle no account answers to.
  'sign-in': {
    path: '/sign-in',
    states: [
      { name: 'empty' },
      { name: 'submitting', query: { handle: 'ada.bsky.social' }, click: '.sign-in-submit', cancel: '/auth/atproto/challenge' },
      { name: 'verification-failed', query: { failure: 'atproto_authentication_failed', handle: 'ada.bsky.social' } },
      { name: 'handle-not-found', query: { failure: 'handle_not_found', handle: 'ada.bsky.socail' } },
    ],
  },
  // Before anyone signs in; signed in where the configuration names someone else as operator; signed in, the
  // form with its setup code field before anything is written; where the configuration names the persona, the form
  // refused for a proprietor handle no participant has; and established, the site's pointers for its operator.
  'first-run': {
    path: '/first-run',
    states: [
      { name: 'arrival', place: 'unestablished' },
      { name: 'waiting', place: 'unestablished', signedIn: true, settings: { 'Tangent:Operator': captureOperator.did } },
      { name: 'form', place: 'unestablished', signedIn: true },
      {
        name: 'form-error',
        place: 'unestablished',
        signedIn: true,
        settings: { 'Tangent:Operator': capturePersona.did },
        check: ['#first-run-someone', '#first-run-accountable'],
        fill: { '#first-run-name': 'Field notes', '#first-run-handle': 'studio.example' },
        click: '.sign-in-submit',
      },
      { name: 'established', place: 'persona', signedIn: true },
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

// For a screen the reference does not draw, the reference screens nearest to it, named in the round's
// README for whoever draws its mock. They are not rendered and are not its target.
export const nearestReadings: Readonly<Record<string, readonly string[]>> = {
  door: ['01-space-home.html', '20-phone-space-home.html'],
  'first-run': ['08-space-general.html', '19-sign-in.html'],
}
