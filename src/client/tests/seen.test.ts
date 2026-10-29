// @vitest-environment jsdom
import { afterEach, expect, test, vi } from 'vitest'
import { mountIslands } from '../islands'

// The server renders the seen control at the end of a Topic's window as a form; the island posts it once by itself
// when the form comes on screen, and the form leaves the page when the checkpoint has moved.

const action = 'https://tangent.example/t/workshop/lanterns/seen'
let observed: ((entries: { isIntersecting: boolean }[]) => void) | undefined

class Observer {
  constructor(callback: (entries: { isIntersecting: boolean }[]) => void) {
    observed = callback
  }
  observe() {}
  disconnect() {}
}

function render() {
  document.body.innerHTML =
    `<form class="topic-seen" method="post" action="${action}" data-island="seen">` +
    '<input type="hidden" name="__RequestVerificationToken" value="token-value">' +
    '<input type="hidden" name="through" value="7">' +
    '<button type="submit">Mark seen</button></form>'
  return document.querySelector('form')!
}

afterEach(() => {
  vi.unstubAllGlobals()
  observed = undefined
  document.body.innerHTML = ''
})

test('the seen island posts its form once when the end of the window comes on screen, and the form then leaves', async () => {
  vi.stubGlobal('IntersectionObserver', Observer)
  const fetched = vi.fn(async (_url: string, _init: RequestInit) => new Response('{"scope":"topic:x","seq":7,"watch":false}', { status: 200 }))
  vi.stubGlobal('fetch', fetched)
  const form = render()

  mountIslands(document)
  observed!([{ isIntersecting: false }])
  expect(fetched).not.toHaveBeenCalled()

  observed!([{ isIntersecting: true }])
  observed!([{ isIntersecting: true }])
  await vi.waitFor(() => expect(form.hidden).toBe(true))

  expect(fetched).toHaveBeenCalledOnce()
  const [url, init] = fetched.mock.calls[0]
  expect(url).toBe(action)
  expect(init.method).toBe('POST')
  expect(new Headers(init.headers).get('Accept')).toBe('application/json')
  expect(String(init.body)).toBe('__RequestVerificationToken=token-value&through=7')
})

test('a seen post that does not succeed leaves the form and its button as they were', async () => {
  vi.stubGlobal('IntersectionObserver', Observer)
  const fetched = vi.fn(async () => new Response('', { status: 400 }))
  vi.stubGlobal('fetch', fetched)
  const form = render()

  mountIslands(document)
  observed!([{ isIntersecting: true }])
  await vi.waitFor(() => expect(fetched).toHaveBeenCalledOnce())
  await Promise.resolve()

  expect(form.hidden).toBe(false)
  expect(form.querySelector('button')!.textContent).toBe('Mark seen')
})
