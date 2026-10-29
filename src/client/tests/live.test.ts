// @vitest-environment jsdom
import { afterEach, expect, test, vi } from 'vitest'
import { mountIslands, type MountedIsland } from '../islands'
import { fragmentHeader } from '../islands/fragment'
import { createLiveTransport, type LiveEvent, type LiveState, type LiveTransport } from '../live'

// The live updater against a mocked Tangent Space: the stream's frames patch a Topic's page — a new reply appended at
// the newest window, counted in the notice elsewhere, an edited Post fetched again in place — and the door; when the
// stream fails the transport polls, and when the poll fails it reads the door; and nothing it sends writes.

const events = 'https://tangent.example/events'
const door = 'https://tangent.example/'
const topic = 'https://tangent.example/t/workshop/lanterns'

type Answer = (url: string, init: RequestInit) => Response | Promise<Response>

let mounted: MountedIsland[] = []
const calls: { url: string; init: RequestInit }[] = []

afterEach(() => {
  for (const island of mounted) (island.island as unknown as { transport: LiveTransport }).transport?.stop()
  mounted = []
  calls.length = 0
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
  document.body.innerHTML = ''
})

function serve(answer: Answer): void {
  vi.stubGlobal('fetch', vi.fn(async (url: string, init: RequestInit = {}) => {
    calls.push({ url, init })
    return answer(url, init)
  }))
}

const streamed = (init: RequestInit) => new Headers(init.headers).get('Accept') === 'text/event-stream'
const fragmentOf = (init: RequestInit) => new Headers(init.headers).has(fragmentHeader)

function row(seq: number, kind: string, post: string, extra: Partial<LiveEvent> = {}): LiveEvent {
  return { seq, kind, tangent: 'workshop', topic: 'root1', post, actor: 'did:plc:bea', target: null, version: 1, at: '2026-10-29T10:00:00Z', ...extra }
}

// A stream that sends the frames and stays open until it is cancelled.
function stream(rows: LiveEvent[]): Response {
  const encoder = new TextEncoder()
  const body = new ReadableStream<Uint8Array>({
    start(controller) {
      controller.enqueue(encoder.encode(': open\nretry: 3000\n\n'))
      for (const each of rows) controller.enqueue(encoder.encode(`event: ${each.kind}\nid: ${each.seq}\ndata: ${JSON.stringify(each)}\n\n`))
    },
  })
  return new Response(body, { headers: { 'Content-Type': 'text/event-stream' } })
}

const html = (body: string) => new Response(body, { headers: { 'Content-Type': 'text/html' } })
const json = (value: unknown) => new Response(JSON.stringify(value), { headers: { 'Content-Type': 'application/json' } })

function props(values: Record<string, unknown>): string {
  return JSON.stringify({ events, door, since: 10, ...values }).replaceAll('"', '&quot;')
}

function topicPage(newest: boolean): void {
  document.body.innerHTML =
    '<main class="topic-main"><article id="root1" class="post post-root">Who lights theirs first?</article>' +
    '<section class="topic-replies"><article id="r1" class="post">I do, at six.</article>' +
    '<form class="topic-seen" method="post" action="/t/workshop/lanterns/seen" data-island="seen"><button>Mark seen</button></form>' +
    `<div class="live" data-island="live" data-props="${props({ page: 'topic', tangent: 'workshop', topic: 'root1', address: topic, newest })}">` +
    `<nav class="topic-pages" aria-live="polite" hidden><a class="topic-pages-next" href="${topic}">New replies →</a></nav></div>` +
    '</section></main>'
}

// Every request the island made was a read: nothing posted, nothing to a seen or a checkpoint's address.
function onlyReads(): void {
  expect(calls.length).toBeGreaterThan(0)
  for (const call of calls) {
    expect(call.init.method ?? 'GET').toBe('GET')
    expect(call.url).not.toMatch(/\/seen$|\/watch$/)
  }
}

test('at the newest window a new reply is fetched as its fragment and appended, and an edited post is fetched again in place', async () => {
  topicPage(true)
  serve((url, init) => {
    if (streamed(init)) return stream([row(11, 'post.created', 'r2'), row(12, 'post.edited', 'r1', { version: 2 })])
    if (url === `${topic}/r2` && fragmentOf(init))
      return html('<div class="layout"><main><article id="root1" class="post post-root">Root</article><section class="topic-replies">' +
        '<article id="r1" class="post">I do, at six.</article><article id="r2" class="post is-anchor is-new" aria-current="true">Then the harbour.</article></section></main></div>')
    if (url === `${topic}/r1` && fragmentOf(init))
      return html('<div class="layout"><main><section class="topic-replies"><article id="r1" class="post is-anchor" aria-current="true">I do, at seven.</article></section></main></div>')
    return new Response('', { status: 404 })
  })

  mounted = mountIslands(document).filter((island) => island.name === 'live')

  await vi.waitFor(() => expect(document.getElementById('r1')!.textContent).toBe('I do, at seven.'), { timeout: 5000 })
  const replies = [...document.querySelectorAll('.topic-replies > *')].map((element) => element.id || element.tagName.toLowerCase())
  expect(replies).toEqual(['r1', 'r2', 'form', 'div'])
  const added = document.getElementById('r2')!
  expect(added.textContent).toBe('Then the harbour.')
  expect(added.classList.contains('is-anchor')).toBe(false)
  expect(added.hasAttribute('aria-current')).toBe(false)
  expect(added.classList.contains('is-new')).toBe(true)
  expect(document.getElementById('r1')!.classList.contains('is-anchor')).toBe(false)
  expect(calls[0].url).toBe(`${events}?since=10`)
  expect(document.querySelector('nav')!.hidden).toBe(true)
  onlyReads()
})

test('away from the newest window a new reply shows the notice that links to it, and fetches nothing', async () => {
  topicPage(false)
  serve((_url, init) => (streamed(init) ? stream([row(11, 'post.created', 'r9'), row(12, 'post.created', 'r10')]) : new Response('', { status: 500 })))

  mounted = mountIslands(document).filter((island) => island.name === 'live')

  const nav = document.querySelector('nav')!
  await vi.waitFor(() => expect(nav.textContent).toBe('2 new replies →'), { timeout: 5000 })
  expect(nav.hidden).toBe(false)
  expect(nav.querySelector('a')!.href).toBe(`${topic}/r9`)
  expect(document.getElementById('r9')).toBeNull()
  expect(calls.every((call) => streamed(call.init))).toBe(true)
  onlyReads()
})

test('on the door a row fetches the door again from its own fragment', async () => {
  document.body.innerHTML =
    `<main data-island="fragment" data-props="${JSON.stringify({ url: door }).replaceAll('"', '&quot;')}"><p>Nothing new since you were here.</p></main>` +
    `<div class="live" data-island="live" data-props="${props({ page: 'door' })}"></div>`
  serve((url, init) => {
    if (streamed(init)) return stream([row(11, 'post.created', 'r2')])
    if (url === door && fragmentOf(init)) return html('<main data-island="fragment"><p>Since you were here: one reply.</p></main>')
    return new Response('', { status: 404 })
  })

  mounted = mountIslands(document).filter((island) => island.name === 'live')

  await vi.waitFor(() => expect(document.querySelector('main')!.textContent).toBe('Since you were here: one reply.'), { timeout: 5000 })
  onlyReads()
})

test('the transport falls back from the stream to the poll, delivering its rows, and from a failing poll to the door', async () => {
  const states: LiveState[] = []
  const delivered: number[][] = []
  let moved = 0
  let polls = 0
  const fetched: string[] = []
  const transport = createLiveTransport({
    events,
    door,
    random: () => 0,
    timings: { retryMinMs: 1, retryMaxMs: 4, pollMs: 1, doorMs: 60000, reprobeMs: 60000 },
    fetch: (async (url: string, init: RequestInit) => {
      fetched.push(`${new Headers(init.headers).get('Accept')} ${url}`)
      if (streamed(init)) return new Response('', { status: 503 })
      if (url === door) return json({ name: 'Field notes', poll: { through: 20, next: `${events}?since=20` } })
      polls++
      if (polls === 1) return json({ since: 5, through: 12, more: false, events: [row(7, 'post.created', 'r7'), row(12, 'post.created', 'r12')], next: `${events}?since=12` })
      return new Response('', { status: 500 })
    }) as typeof fetch,
    onEvents: (rows) => {
      delivered.push(rows.map((each) => each.seq))
      return true
    },
    onMoved: () => {
      moved++
      return true
    },
    onState: (state) => states.push(state),
  })

  transport.start({ since: 5 })
  await vi.waitFor(() => expect(moved).toBe(1), { timeout: 5000 })
  transport.stop()

  expect(fetched).toEqual([
    `text/event-stream ${events}?since=5`,
    `application/json ${events}?since=5`,
    `application/json ${events}?since=12`,
    `application/json ${door}`,
  ])
  expect(delivered).toEqual([[7, 12]])
  expect(transport.snapshot().since).toBe(20)
  const modes = states.map((state) => state.mode).filter((mode, index, all) => mode !== all[index - 1])
  expect(modes).toEqual(['stopped', 'stream', 'poll', 'door', 'stopped'])
})

test('on a Tangent the island falls back to the door and fetches the list of Topics again when the door has moved', async () => {
  vi.spyOn(Math, 'random').mockReturnValue(0)
  document.body.innerHTML =
    '<section class="tangent-head"><h1>The workshop</h1></section><div class="layout"><main class="tangent-main"><p>No topics yet.</p></main></div>' +
    `<div class="live" data-island="live" data-props="${props({ page: 'tangent', tangent: 'workshop' })}"></div>`
  serve((url, init) => {
    if (url === door && !fragmentOf(init)) return json({ poll: { through: 14, next: `${events}?since=14` } })
    if (url === location.href && fragmentOf(init))
      return html('<section class="tangent-head"></section><div class="layout"><main class="tangent-main"><ul><li class="is-new">Lanterns at dusk</li></ul></main></div>')
    return new Response('', { status: 503 })
  })

  mounted = mountIslands(document).filter((island) => island.name === 'live')

  await vi.waitFor(() => expect(document.querySelector('main')!.innerHTML).toBe('<ul><li class="is-new">Lanterns at dusk</li></ul>'), { timeout: 8000 })
  expect(calls.slice(0, 3).map((call) => call.url)).toEqual([`${events}?since=10`, `${events}?since=10`, door])
  onlyReads()
})
