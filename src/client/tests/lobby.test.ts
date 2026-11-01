// @vitest-environment jsdom
import { afterEach, describe, expect, test, vi } from 'vitest'
import { mountIslands } from '../islands'
import { findLobby, handOff } from '../lobby'

const stated = { product: 'companion-lobby', discoveryVersion: 1, managerOrigin: 'http://127.0.0.1:5219', protocolVersions: ['2025-11-25'] }
const answer = (value: unknown, headers: Record<string, string> = {}) =>
  new Response(typeof value === 'string' ? value : JSON.stringify(value), { headers: { 'Content-Type': 'application/json', ...headers } })

afterEach(() => {
  vi.unstubAllGlobals()
  document.body.innerHTML = ''
})

describe('finding the Lobby', () => {
  test('asks the one address, without credentials or redirects, and believes an answer that states itself', async () => {
    const send = vi.fn(async (_url: RequestInfo | URL, _init?: RequestInit) => answer(stated))

    expect(await findLobby({ send })).toBe(true)

    const [url, init] = send.mock.calls[0]
    expect(url).toBe('http://127.0.0.1:5219/api/discovery')
    expect(init).toMatchObject({ method: 'GET', mode: 'cors', credentials: 'omit', redirect: 'error', cache: 'no-store' })
  })

  test.each([
    ['another product', { ...stated, product: 'someone-else' }],
    ['another version of the document', { ...stated, discoveryVersion: 2 }],
    ['another origin', { ...stated, managerOrigin: 'https://elsewhere.example' }],
    ['nothing it needs', {}],
    ['null', 'null'],
    ['words that are not JSON', 'nope'],
  ])('refuses an answer with %s', async (_, value) => {
    expect(await findLobby({ send: async () => answer(value) })).toBe(false)
  })

  test('refuses a failure, another content type and a redirect', async () => {
    expect(await findLobby({ send: async () => new Response(JSON.stringify(stated), { status: 500, headers: { 'Content-Type': 'application/json' } }) })).toBe(false)
    expect(await findLobby({ send: async () => new Response(JSON.stringify(stated), { headers: { 'Content-Type': 'text/html' } }) })).toBe(false)
    const redirected = answer(stated)
    Object.defineProperty(redirected, 'redirected', { value: true })
    expect(await findLobby({ send: async () => redirected })).toBe(false)
  })

  test('refuses more than 4 KiB, declared or read', async () => {
    const read = vi.fn(async () => '')
    const declared = answer(stated, { 'Content-Length': '4097' })
    Object.defineProperty(declared, 'text', { value: read })
    expect(await findLobby({ send: async () => declared })).toBe(false)
    expect(read).not.toHaveBeenCalled()

    const large = JSON.stringify({ ...stated, padding: 'x'.repeat(4096) })
    expect(await findLobby({ send: async () => answer(large) })).toBe(false)
  })

  test('stops reading a body past 4 KiB that declares no length', async () => {
    let chunks = 0
    const flood = new ReadableStream<Uint8Array>({
      pull(controller) {
        chunks += 1
        controller.enqueue(new Uint8Array(1024))
      },
    })
    const send = async () => new Response(flood, { headers: { 'Content-Type': 'application/json' } })

    expect(await findLobby({ send })).toBe(false)
    expect(chunks).toBeLessThan(10)
  })

  test('refuses what the network refuses, and stops waiting at its time', async () => {
    expect(await findLobby({ send: async () => Promise.reject(new TypeError('Failed to fetch')) })).toBe(false)

    const hanging = (_url: RequestInfo | URL, init?: RequestInit) =>
      new Promise<Response>((_, reject) => init?.signal?.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError'))))
    expect(await findLobby({ send: hanging, timeoutMs: 1 })).toBe(false)
  })
})

describe('the hand-off', () => {
  test('is the page of the Lobby that names the place, its path and all', () => {
    const page = new URL(handOff('https://www.example.com/notes'))

    expect(page.origin + page.pathname).toBe('http://127.0.0.1:5219/')
    expect(page.searchParams.get('service')).toBe('tangent')
    expect(page.searchParams.get('address')).toBe('https://www.example.com/notes')
  })

  const page = () => {
    const props = JSON.stringify({ address: 'https://tangent.example/notes' }).replaceAll('"', '&quot;')
    document.body.innerHTML =
      `<div data-island="lobby" data-props="${props}">` +
      '<div data-lobby-absent><a href="https://get.example/">Get Companion Lobby</a></div>' +
      '<div data-lobby-offer hidden><a target="_blank" rel="noopener noreferrer">Bring your agent</a></div>' +
      '</div>'
    return {
      absent: document.querySelector<HTMLElement>('[data-lobby-absent]')!,
      offer: document.querySelector<HTMLElement>('[data-lobby-offer]')!,
      link: document.querySelector<HTMLAnchorElement>('[data-lobby-offer] a')!,
    }
  }

  test('is offered as the one button when the Lobby answers', async () => {
    const { absent, offer, link } = page()
    vi.stubGlobal('fetch', vi.fn(async () => answer(stated)))

    expect(mountIslands(document).map((island) => island.name)).toEqual(['lobby'])
    await vi.waitFor(() => expect(offer.hidden).toBe(false))

    expect(absent.hidden).toBe(true)
    expect(link.href).toBe(handOff('https://tangent.example/notes'))
    expect(link.target).toBe('_blank')
  })

  test('leaves how to get the Lobby when nothing answers', async () => {
    const { absent, offer, link } = page()
    vi.stubGlobal('fetch', vi.fn(async () => Promise.reject(new TypeError('Failed to fetch'))))

    mountIslands(document)
    await vi.waitFor(() => expect(fetch).toHaveBeenCalledOnce())
    await new Promise((resolve) => setTimeout(resolve))

    expect(absent.hidden).toBe(false)
    expect(offer.hidden).toBe(true)
    expect(link.hasAttribute('href')).toBe(false)
  })
})
