// @vitest-environment jsdom
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import SearchBox from '../src/components/SearchBox.vue'
import { useRouter } from '../src/stores/router'
import { httpRequest } from '../src/api'

vi.mock('../src/api', () => ({ httpRequest: vi.fn() }))
const results = (label: string, count = 1) => new Response(JSON.stringify({
  items: Array.from({ length: count }, (_, index) => ({
    id: String(index), title: label + index, url: '/t/lab/post-' + index,
    excerpt: 'A real matching reply', author: { displayName: 'Writer' },
  })), more: false,
}))
const wrappers: ReturnType<typeof mount>[] = []
function render() {
  const view = mount(SearchBox, { attachTo: document.body, props: { router: useRouter(), identityKey: 'alice' } })
  wrappers.push(view)
  return view
}
beforeEach(() => {
  vi.useFakeTimers()
  setActivePinia(createPinia())
  window.history.replaceState({}, '', '/')
  vi.mocked(httpRequest).mockReset()
})
afterEach(() => { wrappers.splice(0).forEach(view => view.unmount()); vi.useRealTimers(); document.body.innerHTML = '' })

it('debounces typing without submitting and bounds suggestions to five real matches', async () => {
  vi.mocked(httpRequest).mockResolvedValue(results('Found ', 8))
  const view = render()
  await view.get('input').trigger('focus')
  await view.get('input').setValue('m')
  await vi.advanceTimersByTimeAsync(250)
  expect(httpRequest).not.toHaveBeenCalled()
  await view.get('input').setValue('mc')
  await vi.advanceTimersByTimeAsync(100)
  await view.get('input').setValue('mcp')
  await vi.advanceTimersByTimeAsync(199)
  expect(httpRequest).not.toHaveBeenCalled()
  await vi.advanceTimersByTimeAsync(1)
  await flushPromises()
  expect(httpRequest).toHaveBeenCalledTimes(1)
  expect(String(vi.mocked(httpRequest).mock.calls[0]?.[0])).toContain('q=mcp')
  expect(view.findAll('[role=option]')).toHaveLength(5)
  expect(location.pathname).toBe('/')
  expect(view.get('input').attributes('aria-expanded')).toBe('true')
})

it('uses arrows and Enter to open the chosen result, while ordinary submit opens all results', async () => {
  vi.mocked(httpRequest).mockImplementation(async () => results('Match ', 2))
  const view = render()
  await view.get('input').trigger('focus')
  await view.get('input').setValue('match')
  await vi.advanceTimersByTimeAsync(200); await flushPromises()
  await view.get('input').trigger('keydown', { key: 'ArrowUp' })
  expect(view.get('input').attributes('aria-activedescendant')).toBe('space-search-option-1')
  await view.get('form').trigger('submit')
  expect(location.pathname).toBe('/t/lab/post-1')
  expect(view.find('[role=listbox]').exists()).toBe(false)
  await view.get('input').trigger('focus')
  await view.get('input').setValue('all words')
  await view.get('form').trigger('submit')
  expect(location.pathname + location.search).toBe('/search?q=all%20words')
})

it('cancels obsolete queries, Escape and identity changes even if an old response resolves', async () => {
  let resolveOld!: (response: Response) => void
  vi.mocked(httpRequest).mockReturnValueOnce(new Promise(resolve => { resolveOld = resolve }))
  const view = render()
  await view.get('input').trigger('focus')
  await view.get('input').setValue('private old')
  await vi.advanceTimersByTimeAsync(200)
  const oldSignal = vi.mocked(httpRequest).mock.calls[0]?.[1]?.signal
  vi.mocked(httpRequest).mockResolvedValueOnce(results('Current '))
  await view.get('input').setValue('current')
  expect(oldSignal?.aborted).toBe(true)
  await vi.advanceTimersByTimeAsync(200); await flushPromises()
  resolveOld(results('Old private ')); await flushPromises()
  expect(view.text()).toContain('Current')
  expect(view.text()).not.toContain('Old private')
  await view.get('input').trigger('keydown', { key: 'Escape' })
  expect(view.find('[role=listbox]').exists()).toBe(false)
  vi.mocked(httpRequest).mockReturnValueOnce(new Promise(resolve => { resolveOld = resolve }))
  await view.get('input').trigger('focus')
  await vi.advanceTimersByTimeAsync(200)
  await view.setProps({ identityKey: 'bob' })
  resolveOld(results('Alice only ')); await flushPromises()
  expect(view.find('[role=listbox]').exists()).toBe(false)
  expect((view.get('input').element as HTMLInputElement).value).toBe('')
  expect(view.text()).not.toContain('Alice only')
})

it('clears on deletion and outside click, navigates pointer results, and shows honest errors', async () => {
  vi.mocked(httpRequest).mockImplementation(async () => results('Match '))
  const view = render()
  await view.get('input').trigger('focus')
  await view.get('input').setValue('match')
  await vi.advanceTimersByTimeAsync(200); await flushPromises()
  await view.get('[role=option]').trigger('click', { button: 0 })
  expect(location.pathname).toBe('/t/lab/post-0')
  await view.get('input').trigger('focus')
  await vi.advanceTimersByTimeAsync(200); await flushPromises()
  await view.get('input').setValue('')
  expect(view.find('[role=listbox]').exists()).toBe(false)
  vi.mocked(httpRequest).mockResolvedValueOnce(new Response('{}', { status: 503 }))
  await view.get('input').setValue('failure')
  await vi.advanceTimersByTimeAsync(200); await flushPromises()
  expect(view.get('[role=alert]').text()).toContain('Search is unavailable')
  document.body.dispatchEvent(new Event('pointerdown', { bubbles: true }))
  await flushPromises()
  expect(view.find('[role=alert]').exists()).toBe(false)
})
