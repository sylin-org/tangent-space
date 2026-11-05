// @vitest-environment jsdom
import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { mountIslands, type MountedIsland } from '../islands'
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
let mounted: MountedIsland[] = []

beforeEach(() => {
  fetched = vi.fn(async () => new Response(profile, { status: 200, headers: { 'Content-Type': 'text/html' } }))
  vi.stubGlobal('fetch', fetched)
  window.history.replaceState(null, '', '/t/workshop/lanterns')
  document.body.innerHTML =
    '<main><article class="post"><span class="mark chip participant" data-person="/p/lumen.example">LU</span>' +
    '<a class="post-author" href="/p/lumen.example">Lumen</a><div class="post-body"><p>Thanks ' +
    '<a href="/p/lumen.example" title="@lumen.example">@Lumen</a>, and <a href="/t/workshop/other">another Topic</a>.</p></div></article>' +
    '<p id="outside">Elsewhere</p></main><div hidden data-island="person" data-props=\'{"door":"/"}\'></div>'
  mounted = mountIslands(document)
})

afterEach(() => {
  // Each test's island stops listening to the document with it.
  for (const { island } of mounted) island.$.appContext.app.unmount()
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

test('a profile’s own paging links page, and open no card', () => {
  for (const href of ['/p/lumen.example?before=1', '/p/lumen.example#posts']) {
    document.querySelector('main')!.insertAdjacentHTML('beforeend', `<a class="paging" href="${href}">Older posts →</a>`)
    const click = new MouseEvent('click', { bubbles: true, cancelable: true, button: 0 })
    document.querySelector<HTMLAnchorElement>(`a.paging[href="${href}"]`)!.dispatchEvent(click)
    expect(click.defaultPrevented).toBe(false)
    expect(card()).toBeNull()
  }
})

// A touch press as a browser sends it: pointer events whose pointerType is touch.
const touch = (type: string) => Object.assign(new MouseEvent(type, { bubbles: true, cancelable: true, clientX: 10, clientY: 10 }), { pointerType: 'touch' })

test('a long press opens the card once, whatever context menu comes with it, and the next tap in it works', async () => {
  const mention = document.querySelector<HTMLAnchorElement>('.post-body a')!
  mention.dispatchEvent(touch('pointerdown'))
  await vi.waitFor(() => expect(card()).not.toBeNull(), { timeout: 2000 })
  await opened()

  // Android sends a context menu about now, and a Windows touch screen on the lift; neither closes the card.
  mention.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true }))
  mention.dispatchEvent(touch('pointerup'))
  mention.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true }))
  expect(card()).not.toBeNull()

  // The lift's own click is the press's; the card stays.
  const lift = new MouseEvent('click', { bubbles: true, cancelable: true, button: 0 })
  mention.dispatchEvent(lift)
  expect(lift.defaultPrevented).toBe(true)
  expect(card()).not.toBeNull()

  // The next tap, on the card's Profile, is the reader's.
  const profileLink = card()!.querySelector<HTMLAnchorElement>('a.person-card-action')!
  profileLink.dispatchEvent(touch('pointerdown'))
  profileLink.dispatchEvent(touch('pointerup'))
  const tap = new MouseEvent('click', { bubbles: true, cancelable: true, button: 0 })
  profileLink.dispatchEvent(tap)
  expect(tap.defaultPrevented).toBe(false)
})
