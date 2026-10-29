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
