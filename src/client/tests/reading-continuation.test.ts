// @vitest-environment jsdom
import { afterEach, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import ContinueReading from '../src/components/ContinueReading.vue'
import { useSite } from '../src/stores/site'
import type { Topic } from '../src/composables/conversation'

const topic: Topic = { key: 'story', tangentKey: 'garden', title: 'Current title', canRead: true, canWrite: false, isLocked: false }
const wrappers: ReturnType<typeof mount>[] = []
function identity(actor: string | null) {
  const site = useSite()
  site.site = { name: 'Space', established: true, signIn: '/sign-in', signOut: null, participant: actor ? { participantRef: actor, did: null, handle: null, isOwner: false, joinedAt: '' } : null }
  site.state = actor ? 'signedIn' : 'anonymous'
}
function setup(actor: string | null = 'alice') {
  setActivePinia(createPinia()); localStorage.clear(); identity(actor)
}
function recall(actor = 'alice') {
  localStorage.setItem('tangent:reading:' + actor, JSON.stringify({ tangentKey: 'garden', topicKey: 'story', title: 'Private old title', tangentName: 'Private parent name' }))
}
function view(active = true) {
  const wrapper = mount(ContinueReading, { props: { topic: null, active } }); wrappers.push(wrapper); return wrapper
}
afterEach(() => { wrappers.splice(0).forEach(wrapper => wrapper.unmount()); vi.restoreAllMocks(); vi.unstubAllGlobals(); localStorage.clear() })

it('offers the current readable Topic using its canonical link without cached parent metadata', async () => {
  setup(); recall()
  const fetch = vi.fn(async () => new Response(JSON.stringify(topic))); vi.stubGlobal('fetch', fetch)
  const wrapper = view(); await flushPromises()
  expect(wrapper.get('a').attributes('href')).toBe('/t/garden/topics/story')
  expect(wrapper.text()).toContain('Continue reading'); expect(wrapper.text()).toContain('Current title')
  expect(wrapper.text()).not.toContain('Private')
  expect(fetch).toHaveBeenCalledWith('/api/v1/tangents/garden/topics/story', { credentials: 'same-origin', cache: 'no-store' })
})

it.each([new Response('{}', { status: 403 }), new Response(JSON.stringify({ ...topic, canRead: false })), new Response(JSON.stringify({ ...topic, key: 'different' }))])('does not expose a denied or mismatched recall', async response => {
  setup(); recall(); vi.stubGlobal('fetch', vi.fn(async () => response))
  const wrapper = view(); await flushPromises(); expect(wrapper.find('a').exists()).toBe(false); expect(wrapper.text()).not.toContain('Private')
})

it('records a newly opened readable Topic and checks it again when returning home', async () => {
  setup(); const fetch = vi.fn(async () => new Response(JSON.stringify(topic))); vi.stubGlobal('fetch', fetch)
  const wrapper = view(false); await wrapper.setProps({ topic })
  expect(JSON.parse(localStorage.getItem('tangent:reading:alice')!)).toEqual({ tangentKey: 'garden', topicKey: 'story' })
  expect(fetch).not.toHaveBeenCalled(); await wrapper.setProps({ active: true }); await flushPromises()
  expect(wrapper.get('a').text()).toContain('Current title')
  await wrapper.setProps({ active: false }); fetch.mockResolvedValue(new Response('{}', { status: 404 }))
  await wrapper.setProps({ active: true }); await flushPromises(); expect(wrapper.find('a').exists()).toBe(false)
})

it('never recalls anonymously or carries another account through a late response or account round trip', async () => {
  setup(); recall(); recall('bob')
  const complete: Array<(response: Response) => void> = []
  vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(resolve => complete.push(resolve))))
  const wrapper = view(); identity('bob'); await flushPromises(); identity('alice'); await flushPromises()
  complete[0]!(new Response(JSON.stringify({ ...topic, title: 'Stale Alice' }))); await flushPromises()
  complete[1]!(new Response(JSON.stringify({ ...topic, title: 'Bob' }))); await flushPromises()
  expect(wrapper.find('a').exists()).toBe(false)
  complete[2]!(new Response(JSON.stringify({ ...topic, title: 'Fresh Alice' }))); await flushPromises()
  expect(wrapper.text()).toContain('Fresh Alice'); identity(null); await flushPromises()
  expect(wrapper.find('a').exists()).toBe(false); expect(complete).toHaveLength(3)
})

it('keeps home usable when recall storage is malformed or unavailable', async () => {
  setup(); localStorage.setItem('tangent:reading:alice', '{broken'); const fetch = vi.fn(); vi.stubGlobal('fetch', fetch)
  const wrapper = view(); await flushPromises(); expect(wrapper.find('a').exists()).toBe(false)
  vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('Storage denied') })
  await wrapper.setProps({ active: false }); await wrapper.setProps({ active: true }); await flushPromises()
  expect(wrapper.find('a').exists()).toBe(false); expect(fetch).not.toHaveBeenCalled()
})

it.each([null, { ...topic, canRead: false }])('removes an already visible private recall when live access becomes unavailable: %s', async inaccessible => {
  setup(); recall(); const fetch = vi.fn(async () => new Response(JSON.stringify(topic))); vi.stubGlobal('fetch', fetch)
  const wrapper = view(false); await wrapper.setProps({ topic, active: true }); await flushPromises()
  expect(wrapper.text()).toContain('Current title')
  fetch.mockResolvedValue(new Response('{}', { status: 403 }))
  await wrapper.setProps({ topic: inaccessible }); expect(wrapper.find('a').exists()).toBe(false)
  await flushPromises(); expect(wrapper.find('a').exists()).toBe(false)
})

it('does not restore a revoked title from a response pending before the denial refresh', async () => {
  setup(); recall(); const complete: Array<(response: Response) => void> = []
  vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(resolve => complete.push(resolve))))
  const wrapper = view(false); await wrapper.setProps({ topic, active: true }); await wrapper.setProps({ topic: null })
  complete.slice(0, -1).forEach(resolve => resolve(new Response(JSON.stringify({ ...topic, title: 'Stale private title' }))))
  await flushPromises(); expect(wrapper.find('a').exists()).toBe(false)
  complete.at(-1)!(new Response('{}', { status: 403 })); await flushPromises()
  expect(wrapper.find('a').exists()).toBe(false); expect(wrapper.text()).not.toContain('Stale private title')
})
