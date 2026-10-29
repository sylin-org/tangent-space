// @vitest-environment jsdom
import { afterEach, expect, test, vi } from 'vitest'
import { mountIslands } from '../islands'
import { fragmentHeader } from '../islands/fragment'

// The server renders the door's <main> as the fragment island; mounting keeps that render, and refresh()
// swaps in the children of the fragment the server answers for the island's URL.

const url = 'https://tangent.example/'
const props = JSON.stringify({ url }).replaceAll('"', '&quot;')

afterEach(() => {
  vi.unstubAllGlobals()
  document.body.innerHTML = ''
})

test('the fragment island keeps the server render and refreshes from the fragment', async () => {
  document.body.innerHTML =
    `<main data-island="fragment" data-props="${props}"><h1>Tangent Space</h1><p>First render</p></main>` +
    `<div data-island="nothing-by-this-name"><p>Left alone</p></div>`
  const host = document.querySelector('main')!
  const fetched = vi.fn(async (_url: string, _init: RequestInit) =>
    new Response(`<main data-island="fragment" data-props="${props}"><h1>Leo's tangent space</h1></main>`, {
      headers: { 'Content-Type': 'text/html' },
    }))
  vi.stubGlobal('fetch', fetched)

  const mounted = mountIslands(document)

  expect(mounted.map((island) => island.name)).toEqual(['fragment'])
  expect(mounted[0].element).toBe(host)
  expect(host.innerHTML).toBe('<h1>Tangent Space</h1><p>First render</p>')
  expect(document.querySelector('div')!.innerHTML).toBe('<p>Left alone</p>')

  await (mounted[0].island as unknown as { refresh(): Promise<void> }).refresh()

  expect(fetched).toHaveBeenCalledOnce()
  const [requested, init] = fetched.mock.calls[0]
  expect(requested).toBe(url)
  const headers = new Headers(init.headers)
  expect(headers.get('Accept')).toBe('text/html')
  expect(headers.has(fragmentHeader)).toBe(true)
  expect(document.querySelector('main')).toBe(host)
  expect(host.innerHTML).toBe("<h1>Leo's tangent space</h1>")
  expect(host.dataset.island).toBe('fragment')
})

test('a fragment that does not answer leaves the content as it was', async () => {
  document.body.innerHTML = `<main data-island="fragment" data-props="${props}"><h1>Tangent Space</h1></main>`
  vi.stubGlobal('fetch', vi.fn(async () => new Response('', { status: 503 })))

  const [mounted] = mountIslands(document)

  await expect((mounted.island as unknown as { refresh(): Promise<void> }).refresh()).rejects.toThrow('503')
  expect(document.querySelector('main')!.innerHTML).toBe('<h1>Tangent Space</h1>')
})

test('the appearance island presses the saved theme and applies a choice', () => {
  localStorage.setItem('tangent:appearance', 'dark')
  document.body.innerHTML =
    '<div data-island="appearance"><button data-theme-choice="system" aria-pressed="true">System</button>' +
    '<button data-theme-choice="light" aria-pressed="false">Light</button>' +
    '<button data-theme-choice="dark" aria-pressed="false">Dark</button></div>'

  mountIslands(document)
  const pressed = () => [...document.querySelectorAll('[aria-pressed="true"]')].map((button) => button.textContent)

  expect(pressed()).toEqual(['Dark'])
  document.querySelector<HTMLButtonElement>('[data-theme-choice="light"]')!.click()
  expect(pressed()).toEqual(['Light'])
  expect(document.documentElement.dataset.theme).toBe('light')
  expect(localStorage.getItem('tangent:appearance')).toBe('light')
})

test('the submitting island marks the form busy once and takes no second submission', () => {
  document.body.innerHTML =
    '<form data-island="submitting"><input name="identifier" value="ada.bsky.social">' +
    '<button type="submit" data-busy-label="Continuing…">Continue</button></form>'
  const form = document.querySelector('form')!
  mountIslands(document)

  const first = new Event('submit', { cancelable: true })
  form.dispatchEvent(first)
  const second = new Event('submit', { cancelable: true })
  form.dispatchEvent(second)

  expect(first.defaultPrevented).toBe(false)
  expect(second.defaultPrevented).toBe(true)
  expect(form.getAttribute('aria-busy')).toBe('true')
  expect(form.querySelector('input')!.readOnly).toBe(true)
  expect(form.querySelector('button')!.textContent).toBe('Continuing…')
  expect(form.querySelector('button')!.getAttribute('aria-disabled')).toBe('true')
})
