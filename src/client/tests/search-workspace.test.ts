// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { mount, flushPromises, enableAutoUnmount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { defineComponent } from 'vue'
import SearchPage from '../src/components/SearchPage.vue'
import { useRouter } from '../src/stores/router'
import { useSite } from '../src/stores/site'
import { httpRequest } from '../src/api'

vi.mock('../src/api', () => ({ httpRequest: vi.fn() }))
const result = (title: string, more = false) => new Response(JSON.stringify({ items: [{ id: title, title, url: '/t/lab/post', excerpt: 'A matching reply', author: { displayName: 'Writer' } }], more }))
enableAutoUnmount(afterEach)
beforeEach(() => {
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] })
  setActivePinia(createPinia())
  vi.mocked(httpRequest).mockReset()
  window.history.replaceState({}, '', '/search?q=first')
  useSite().site = { name: 'Space', established: true, signIn: '', signOut: null, participant: null }
  useSite().state = 'anonymous'
})
afterEach(() => { vi.useRealTimers(); vi.restoreAllMocks() })

function workspace() {
  const router = useRouter()
  const Host = defineComponent({ components: { SearchPage }, setup: () => ({ router }), template: '<SearchPage :route="router.route" />' })
  return mount(Host)
}

it('searches the latest typed phrase after 200ms and clears the prior page immediately', async () => {
  vi.mocked(httpRequest).mockImplementation(async url => result(String(url), true))
  const view = workspace()
  await flushPromises()
  await view.get('.directory-pagination button').trigger('click')
  await flushPromises()
  expect(location.search).toBe('?q=first&page=2')
  expect(view.text()).toContain('page=2')
  await view.get('input').setValue('sec')
  expect(view.find('.search-result').exists()).toBe(false)
  expect(view.find('.directory-pagination').exists()).toBe(false)
  expect(view.get('[role=status]').text()).toBe('Searching…')
  await vi.advanceTimersByTimeAsync(150)
  await view.get('input').setValue('second')
  await vi.advanceTimersByTimeAsync(199)
  expect(httpRequest).toHaveBeenCalledTimes(2)
  await vi.advanceTimersByTimeAsync(1)
  await flushPromises()
  expect(httpRequest).toHaveBeenCalledTimes(3)
  expect(vi.mocked(httpRequest).mock.calls[2]![0]).toBe('/api/search?q=second&page=1')
  expect(location.search).toBe('?q=second')
  expect(view.text()).toContain('q=second&page=1')
})

it('submits another phrase through the URL, reloads results and clears them when the query is removed', async () => {
  const router = useRouter()
  vi.mocked(httpRequest).mockImplementation(async url => result(String(url).includes('second') ? 'Second title' : 'First title'))
  const view = workspace()
  await flushPromises()
  expect(view.text()).toContain('First title')
  await view.get('input').setValue('second phrase')
  await view.get('form').trigger('submit')
  await flushPromises()
  expect(location.search).toBe('?q=second%20phrase')
  expect(view.text()).toContain('Second title')
  expect(view.text()).not.toContain('First title')
  expect(view.get('.search-open').attributes('href')).toBe('/t/lab/post')
  await vi.advanceTimersByTimeAsync(200)
  expect(httpRequest).toHaveBeenCalledTimes(2)
  router.navigate('/search')
  await flushPromises()
  expect(view.find('.search-result').exists()).toBe(false)
  expect(view.find('.directory-pagination').exists()).toBe(false)
  view.unmount()
})

it('keeps one live-edit history entry and restores a completed search through Back', async () => {
  const router = useRouter()
  const pushed = vi.spyOn(window.history, 'pushState'), replaced = vi.spyOn(window.history, 'replaceState')
  vi.mocked(httpRequest).mockImplementation(async url => result(new URL(String(url), location.origin).searchParams.get('q')!))
  const view = workspace()
  await flushPromises()
  await view.get('input').setValue('second')
  await vi.advanceTimersByTimeAsync(200)
  await flushPromises()
  await view.get('input').setValue('second phrase')
  await vi.advanceTimersByTimeAsync(200)
  await flushPromises()
  expect(pushed).toHaveBeenCalledTimes(1)
  expect(replaced).toHaveBeenCalledTimes(1)
  await view.get('form').trigger('submit')
  await flushPromises()
  await view.get('input').setValue('third')
  await vi.advanceTimersByTimeAsync(200)
  await flushPromises()
  expect(pushed).toHaveBeenCalledTimes(2)
  const pop = () => router.apply(location.pathname)
  window.addEventListener('popstate', pop)
  try {
    vi.useRealTimers()
    const moved = new Promise<void>(resolve => window.addEventListener('popstate', () => resolve(), { once: true }))
    window.history.back()
    await moved
    await flushPromises()
    expect(location.search).toBe('?q=second%20phrase')
    expect((view.get('input').element as HTMLInputElement).value).toBe('second phrase')
    expect(view.get('.search-result').text()).toContain('second phrase')
    expect(view.text()).not.toContain('third')
  } finally { window.removeEventListener('popstate', pop) }
})

it('makes no request for blank or one-character input and provides a distinct no-match state', async () => {
  vi.mocked(httpRequest).mockResolvedValue(result('First title', true))
  const view = workspace()
  await flushPromises()
  await view.get('input').setValue('a')
  expect(view.get('[role=status]').text()).toContain('at least 2 characters')
  await vi.advanceTimersByTimeAsync(200)
  expect(httpRequest).toHaveBeenCalledTimes(1)
  expect(location.search).toBe('?q=a')
  await view.get('input').setValue('')
  expect(view.get('[role=status]').text()).toContain('Search by a title')
  await vi.advanceTimersByTimeAsync(200)
  expect(httpRequest).toHaveBeenCalledTimes(1)
  expect(location.search).toBe('')
  vi.mocked(httpRequest).mockResolvedValueOnce(new Response(JSON.stringify({ items: [], more: false })))
  await view.get('input').setValue('unmatched')
  await vi.advanceTimersByTimeAsync(200)
  await flushPromises()
  expect(view.get('[role=status]').text()).toBe('No results for “unmatched”')
  expect(view.find('.directory-pagination').exists()).toBe(false)
})

it('aborts an in-flight phrase and ignores its late response after new results arrive', async () => {
  let finish!: (response: Response) => void
  vi.mocked(httpRequest).mockReturnValueOnce(new Promise(resolve => { finish = resolve }))
  const view = workspace()
  const signal = vi.mocked(httpRequest).mock.calls[0]![1]!.signal!
  vi.mocked(httpRequest).mockResolvedValueOnce(result('Current title'))
  await view.get('input').setValue('current')
  expect(signal.aborted).toBe(true)
  await vi.advanceTimersByTimeAsync(200)
  await flushPromises()
  finish(result('Stale private title'))
  await flushPromises()
  expect(view.text()).toContain('Current title')
  expect(view.text()).not.toContain('Stale private title')
})

it('ignores an old account response and exposes a failed search instead of stale results', async () => {
  let finish!: (response: Response) => void
  vi.mocked(httpRequest).mockReturnValueOnce(new Promise(resolve => { finish = resolve }))
  const view = mount(SearchPage, { props: { route: { kind: 'search' } } })
  const signal = vi.mocked(httpRequest).mock.calls[0]![1]!.signal!
  vi.mocked(httpRequest).mockResolvedValueOnce(new Response('{}', { status: 503 }))
  useSite().site!.participant = { participantRef: 'other', did: null, handle: null, isOwner: false, joinedAt: '' }
  expect(signal.aborted).toBe(true)
  await flushPromises()
  finish(result('Old private title'))
  await flushPromises()
  expect(view.text()).not.toContain('Old private title')
  expect(view.get('[role=alert]').text()).toContain('Search could not be completed')
  vi.mocked(httpRequest).mockResolvedValueOnce(result('Recovered title'))
  await view.get('[role=alert] button').trigger('click')
  await flushPromises()
  expect(view.text()).toContain('Recovered title')
  expect(view.find('[role=alert]').exists()).toBe(false)
  view.unmount()
})

it('cancels a pending edit when identity changes and cancels work on unmount', async () => {
  vi.mocked(httpRequest).mockImplementation(async () => result('First title'))
  const view = workspace()
  await flushPromises()
  await view.get('input').setValue('pending owner query')
  useSite().state = 'loading'
  await vi.advanceTimersByTimeAsync(200)
  expect(httpRequest).toHaveBeenCalledTimes(1)
  expect(view.find('.search-result').exists()).toBe(false)
  useSite().state = 'anonymous'
  await flushPromises()
  expect((view.get('input').element as HTMLInputElement).value).toBe('first')
  expect(httpRequest).toHaveBeenCalledTimes(2)
  await view.get('input').setValue('pending unmount')
  view.unmount()
  await vi.advanceTimersByTimeAsync(200)
  expect(httpRequest).toHaveBeenCalledTimes(2)

  let finish!: (response: Response) => void
  vi.mocked(httpRequest).mockReturnValueOnce(new Promise(resolve => { finish = resolve }))
  const otherView = workspace()
  const signal = vi.mocked(httpRequest).mock.calls[2]![1]!.signal!
  otherView.unmount()
  expect(signal.aborted).toBe(true)
  finish(result('A detached response'))
  await flushPromises()
})
