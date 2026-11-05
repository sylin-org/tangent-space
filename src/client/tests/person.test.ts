// @vitest-environment jsdom
import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { mountIslands } from '../islands'
import { addComposer, type Named } from '../named'

// A participant's card opens beside a name, a mark or a mention of them, by a click, the keyboard or a right-click, from
// their profile's own fragment, and closes by Escape or a click outside it; never a dialog.

const profile =
  '<main class="frame-main"><section class="tangent-head profile-head participant" data-name="Lumen" ' +
  'data-address="https://tangent.example/p/lumen.example" data-mention="at://did:plc:lumen" ' +
  'data-elsewhere="https://bsky.app/profile/did:plc:lumen"><div class="tangent-head-inner">' +
  '<span class="mark chip participant profile-mark tone-2" aria-hidden="true">LU</span><div class="tangent-head-text">' +
  '<h1 id="profile-title" class="tangent-title">Lumen</h1><p class="tangent-meta"><span class="profile-handle">@lumen.example</span>' +
  '<span class="profile-roles">Moderators</span><span>Here since <time>3 November 2026</time></span></p>' +
  '<p class="tangent-description profile-description">Makes lanterns.</p></div></div></section>' +
  '<section class="profile-standing"><div class="door-offer bar participant"><p class="door-offer-line">Lumen is a member.</p>' +
  '<div class="people-answers"><form method="post" action="/settings/people/ban"><input type="hidden" name="participant" value="did:plc:lumen">' +
  '<input type="hidden" name="back" value="/p/lumen.example"><button type="submit">Ban</button></form></div></div></section></main>'

let fetched: ReturnType<typeof vi.fn>

beforeEach(() => {
  fetched = vi.fn(async () => new Response(profile, { status: 200, headers: { 'Content-Type': 'text/html' } }))
  vi.stubGlobal('fetch', fetched)
  window.history.replaceState(null, '', '/t/workshop/lanterns')
  document.body.innerHTML =
    '<main><article class="post"><span class="mark chip participant" data-person="/p/lumen.example">LU</span>' +
    '<a class="post-author" href="/p/lumen.example">Lumen</a><div class="post-body"><p>Thanks ' +
    '<a href="/p/lumen.example" title="@lumen.example">@Lumen</a>, and <a href="/t/workshop/other">another Topic</a>.</p></div></article>' +
    '<p id="outside">Elsewhere</p></main><div hidden data-island="person" data-props=\'{"door":"/"}\'></div>'
  mountIslands(document)
})

afterEach(() => {
  vi.unstubAllGlobals()
  document.body.replaceChildren()
})

const card = () => document.querySelector<HTMLElement>('.person-card')
const opened = () => vi.waitFor(() => expect(card()?.getAttribute('aria-busy')).toBeNull())

test('a click on a mention opens the card beside it, read from the profile, and a click outside closes it', async () => {
  const mention = document.querySelector<HTMLAnchorElement>('.post-body a')!
  const click = new MouseEvent('click', { bubbles: true, cancelable: true, button: 0 })
  mention.dispatchEvent(click)
  expect(click.defaultPrevented).toBe(true)
  await opened()

  expect(fetched.mock.calls[0]![0]).toBe(location.origin + '/p/lumen.example')
  expect(card()!.querySelector('.person-card-name')!.textContent).toBe('Lumen')
  expect(card()!.querySelector('.person-card-meta')!.textContent).toContain('@lumen.example')
  expect(card()!.querySelector('.person-card-meta')!.textContent).toContain('Moderators')
  expect(card()!.querySelector('.person-card-description')!.textContent).toBe('Makes lanterns.')
  const actions = [...card()!.querySelectorAll<HTMLElement>('.person-card-action')]
  expect(actions.map((action) => action.textContent)).toEqual(['Profile', 'Copy link', 'On Bluesky ↗'])
  expect(actions[2]!.getAttribute('href')).toBe('https://bsky.app/profile/did:plc:lumen')
  expect(card()!.querySelector<HTMLInputElement>('input[name="back"]')!.value).toBe('/t/workshop/lanterns')
  expect(card()!.closest('dialog')).toBeNull()
  expect(mention.getAttribute('aria-expanded')).toBe('true')

  document.getElementById('outside')!.dispatchEvent(new MouseEvent('pointerdown', { bubbles: true }))
  expect(card()).toBeNull()
})

test('the keyboard opens it on a name, Escape closes it and gives the name its focus back', async () => {
  const name = document.querySelector<HTMLAnchorElement>('.post-author')!
  name.focus()
  name.click()
  await opened()
  expect(document.activeElement).toBe(card())

  document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }))
  expect(card()).toBeNull()
  expect(document.activeElement).toBe(name)
})

test('a right-click on a mark opens it too, and a click with a modifier follows the link', async () => {
  const mark = document.querySelector<HTMLElement>('[data-person]')!
  const menu = new MouseEvent('contextmenu', { bubbles: true, cancelable: true })
  mark.dispatchEvent(menu)
  expect(menu.defaultPrevented).toBe(true)
  await opened()
  document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))

  const modified = new MouseEvent('click', { bubbles: true, cancelable: true, button: 0, ctrlKey: true })
  document.querySelector<HTMLAnchorElement>('.post-author')!.dispatchEvent(modified)
  expect(modified.defaultPrevented).toBe(false)
  const other = new MouseEvent('click', { bubbles: true, cancelable: true, button: 0 })
  document.querySelector<HTMLAnchorElement>('a[href="/t/workshop/other"]')!.dispatchEvent(other)
  expect(other.defaultPrevented).toBe(false)
  expect(card()).toBeNull()
})

test('Mention puts their name into the composer open in the page', async () => {
  const put: Named[] = []
  const composer = document.createElement('div')
  document.body.append(composer)
  const remove = addComposer({ element: composer, put: (named) => put.push(named) })

  document.querySelector<HTMLAnchorElement>('.post-author')!.click()
  await opened()
  const mention = [...card()!.querySelectorAll<HTMLButtonElement>('button.person-card-action')].find((button) => button.textContent === 'Mention')!
  mention.click()
  expect(put).toEqual([{ words: '@Lumen', target: 'at://did:plc:lumen' }])
  expect(card()).toBeNull()
  remove()
})
