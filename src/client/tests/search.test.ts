// @vitest-environment jsdom
import { afterEach, beforeEach, expect, test, vi } from 'vitest'
import { mountIslands } from '../islands'
import { searchDelay, suggestionLimit } from '../islands/search'

// The masthead's search box: the server renders a plain form, and the island adds suggestions from the same resource as
// JSON once the typing pauses, keeps only the newest answer, moves through them by key, and sets what it is sent as text.

const address = 'https://tangent.example/search'

function hit(id: number, words = 'a lantern'): object {
  return {
    id: `id${id}`,
    topic: `Topic ${id}`,
    tangent: 'Workshop',
    address: `https://tangent.example/t/workshop/topic-${id}`,
    snippet: [
      { text: 'before ', marked: false },
      { text: words, marked: true },
    ],
  }
}

function answering(body: object, status = 200): ReturnType<typeof vi.fn> {
  const stub = vi.fn(async () => new Response(JSON.stringify(body), { status }))
  vi.stubGlobal('fetch', stub)
  return stub
}

function box(): { form: HTMLFormElement; input: HTMLInputElement; panel: () => HTMLElement } {
  document.body.innerHTML = `<form class="masthead-search" method="get" action="${address}" data-island="search"><input type="search" name="q"></form>`
  mountIslands(document)
  const form = document.querySelector('form')!
  return { form, input: form.querySelector('input')!, panel: () => form.querySelector<HTMLElement>('.masthead-search-panel')! }
}

function type(input: HTMLInputElement, text: string): void {
  input.value = text
  input.dispatchEvent(new Event('input', { bubbles: true }))
}

async function settled(): Promise<void> {
  await vi.advanceTimersByTimeAsync(searchDelay)
}

function press(input: HTMLInputElement, key: string): void {
  input.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }))
}

beforeEach(() => {
  vi.useFakeTimers()
})

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
  document.body.innerHTML = ''
})

test('it asks once the typing pauses, and shows the first few as links', async () => {
  const fetching = answering({ hits: [1, 2, 3, 4, 5, 6, 7].map((id) => hit(id)) })
  const { input, panel } = box()

  type(input, 'la')
  type(input, 'lan')
  await vi.advanceTimersByTimeAsync(searchDelay - 1)
  expect(fetching).not.toHaveBeenCalled()
  await settled()

  expect(fetching).toHaveBeenCalledTimes(1)
  expect(fetching.mock.calls[0]![0]).toBe(`${address}?q=lan`)
  expect(fetching.mock.calls[0]![1].headers).toEqual({ Accept: 'application/json' })
  const links = panel().querySelectorAll<HTMLAnchorElement>('[role="option"]')
  expect(links).toHaveLength(suggestionLimit)
  expect(links[0]!.href).toBe('https://tangent.example/t/workshop/topic-1')
  expect(links[0]!.querySelector('mark')!.textContent).toBe('a lantern')
  expect(panel().hidden).toBe(false)
  expect(input.getAttribute('aria-expanded')).toBe('true')
  expect(panel().querySelector<HTMLAnchorElement>('a.masthead-search-all')!.href).toBe(`${address}?q=lan`)
})

test('it asks nothing for one letter or for punctuation', async () => {
  const fetching = answering({ hits: [] })
  const { input } = box()

  type(input, 'l')
  await settled()
  type(input, '--')
  await settled()

  expect(fetching).not.toHaveBeenCalled()
})

test('words from the server are set as text, never as markup', async () => {
  answering({ hits: [hit(1, '<img src=x onerror=boom(1)>')] })
  const { input, panel } = box()

  type(input, 'lan')
  await settled()

  expect(panel().querySelector('img')).toBeNull()
  expect(panel().querySelector('mark')!.textContent).toBe('<img src=x onerror=boom(1)>')
})

test('an older keystroke is dropped: its request is cancelled and its answer is not shown', async () => {
  const signals: AbortSignal[] = []
  vi.stubGlobal('fetch', vi.fn(async (_: string, init: RequestInit) => {
    signals.push(init.signal!)
    return new Response(JSON.stringify({ hits: [hit(signals.length)] }))
  }))
  const { input, panel } = box()

  type(input, 'lan')
  await vi.advanceTimersByTimeAsync(searchDelay)
  type(input, 'lant')
  expect(signals[0]!.aborted).toBe(true)
  await settled()

  expect(panel().querySelector('.masthead-search-topic')!.textContent).toBe('Topic 2')
})

test('arrow keys choose a suggestion, Enter opens it, and Escape closes the panel', async () => {
  answering({ hits: [hit(1), hit(2)] })
  const { input, panel } = box()
  type(input, 'lan')
  await settled()
  const links = panel().querySelectorAll<HTMLAnchorElement>('[role="option"]')

  press(input, 'ArrowDown')
  expect(links[0]!.getAttribute('aria-selected')).toBe('true')
  expect(input.getAttribute('aria-activedescendant')).toBe(links[0]!.id)
  press(input, 'ArrowUp')
  expect(links[1]!.getAttribute('aria-selected')).toBe('true')

  let opened = ''
  links[1]!.addEventListener('click', (event) => {
    event.preventDefault()
    opened = links[1]!.href
  })
  press(input, 'Enter')
  expect(opened).toBe('https://tangent.example/t/workshop/topic-2')

  press(input, 'Escape')
  expect(panel().hidden).toBe(true)
  expect(input.getAttribute('aria-expanded')).toBe('false')
})

test('Enter with nothing chosen is the form, which goes to the search page', async () => {
  answering({ hits: [hit(1)] })
  const { input } = box()
  type(input, 'lan')
  await settled()

  const enter = new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true })
  input.dispatchEvent(enter)

  expect(enter.defaultPrevented).toBe(false)
})

test('a page with nothing says so, and a refusal or a failure closes the panel', async () => {
  answering({ hits: [] })
  const { input, panel } = box()
  type(input, 'lan')
  await settled()
  expect(panel().querySelector('[role="status"]')!.textContent).toContain('lan')

  answering({ refused: 'No.' }, 400)
  type(input, 'lant')
  await settled()
  expect(panel().hidden).toBe(true)

  vi.stubGlobal('fetch', vi.fn(async () => { throw new TypeError('offline') }))
  type(input, 'lante')
  await settled()
  expect(panel().hidden).toBe(true)
})

test('nothing is asked while an input method is composing', async () => {
  const fetching = answering({ hits: [] })
  const { input } = box()

  input.dispatchEvent(new CompositionEvent('compositionstart', { bubbles: true }))
  type(input, 'かな')
  await settled()
  expect(fetching).not.toHaveBeenCalled()

  input.dispatchEvent(new CompositionEvent('compositionend', { bubbles: true }))
  await settled()
  expect(fetching).toHaveBeenCalledTimes(1)
})
