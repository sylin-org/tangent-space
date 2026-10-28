// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { ref } from 'vue'
import { createPinia, setActivePinia } from 'pinia'
import HistoryDisclosure from '../src/components/HistoryDisclosure.vue'
import { useSite } from '../src/stores/site'

afterEach(() => { vi.unstubAllGlobals() })
describe('post edit history', () => {
  it('loads on native details toggle and follows the bounded newest-to-oldest chain', async () => {
    const request = vi.fn(async (path: string) => new Response(JSON.stringify(path.includes('/newest?')
      ? { id: 'newest', previousChangeId: 'original', content: { text: 'The second version.' } }
      : { id: 'original', content: { text: 'The original version.' } }), { status: 200 }))
    vi.stubGlobal('fetch', request)
    const wrapper = mount(HistoryDisclosure, { props: { conversation: { resolved: ref({}) } as never, message: { id: 'live-post', changeId: 'newest', editedAt: '2026-01-01' } as never }, global: { plugins: [createPinia()] } })
    expect(request).not.toHaveBeenCalled()
    wrapper.find('details').element.open = true
    await wrapper.find('details').trigger('toggle')
    await flushPromises()
    expect(request).toHaveBeenCalledTimes(2)
    expect(request.mock.calls[0]?.[0]).toBe('/api/history/messages/newest?set=changelog&size=1&postId=live-post')
    expect(wrapper.findAll('.history-era').map(row => row.text())).toEqual(['Posted earlierThe second version.', 'Posted earlierThe original version.'])
    wrapper.unmount()
  })
  it('discards an earlier account’s pending history when identity changes', async () => {
    const pinia = createPinia(); setActivePinia(pinia)
    const site = useSite()
    site.site = { name: 'Space', established: true, signIn: '/auth', signOut: '/logout', participant: { participantRef: 'first', did: null, handle: null, isOwner: false, joinedAt: '' } }
    let complete!: (value: Response) => void
    const request = vi.fn().mockImplementationOnce(() => new Promise(resolve => { complete = resolve }))
      .mockResolvedValue(new Response('{}', { status: 403 }))
    vi.stubGlobal('fetch', request)
    const wrapper = mount(HistoryDisclosure, { props: { conversation: { resolved: ref({}) } as never, message: { id: 'live-post', changeId: 'newest' } as never }, global: { plugins: [pinia] } })
    wrapper.find('details').element.open = true
    await wrapper.find('details').trigger('toggle')
    site.site!.participant!.participantRef = 'second'
    await flushPromises()
    complete(new Response(JSON.stringify({ id: 'newest', content: { text: 'Private earlier text.' } }), { status: 200 }))
    await flushPromises()
    expect(wrapper.text()).not.toContain('Private earlier text.')
    expect(wrapper.text()).toContain('Earlier versions aren’t available to this account.')
    wrapper.unmount()
  })
})
