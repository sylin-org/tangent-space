// The screens the visual loop captures, and where the design reference draws each one.
// A screen is a path under the base URL and the states it is captured in. A state is reached by a
// query on the path, by settings the Tangent Space starts with, or by a click in the page; never by a dialog.

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
  /** `Tangent:*` settings the Tangent Space starts with, such as `{ 'Tangent:BaseUrl': '…' }`. */
  settings?: Readonly<Record<string, string>>
  /** A selector clicked once the page has settled; the state is what the click shows in the page. */
  click?: string
}

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
}
