// @vitest-environment jsdom
import { describe, expect, it, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { useSite } from '../src/stores/site'
import FollowButton from '../src/components/FollowButton.vue'

const request = vi.hoisted(() => vi.fn())
vi.mock('../src/composables/roles-api', () => ({ rolesRequest: request }))

describe('following identity', () => {
  it('reloads on account changes and ignores the previous account’s pending write', async () => {
    const pinia = createPinia(); setActivePinia(pinia)
    const site = useSite()
    site.site = { name: 'Space', established: true, signIn: '/auth', signOut: '/logout', participant: { participantRef: 'first', did: null, handle: null, isOwner: false, joinedAt: '' } }
    let finishWrite!: (value: unknown) => void
    request.mockReset()
    request.mockImplementation((_path, options) => options?.method === 'PUT'
      ? new Promise(resolve => { finishWrite = resolve })
      : Promise.resolve({ following: false, scopeRef: 'https://canonical.example::place' }))
    const wrapper = mount(FollowButton, { props: { tangent: 'place', signedIn: true }, global: { plugins: [pinia] } })
    await flushPromises()
    await wrapper.find('button').trigger('click')
    expect(request.mock.calls[1]?.[1].body.scopeRef).toBe('https://canonical.example::place')
    site.site!.participant!.participantRef = 'second'
    await flushPromises()
    expect(request.mock.calls.filter(([, options]) => !options).length).toBe(2)
    finishWrite({}); await flushPromises()
    expect(wrapper.find('button').attributes('aria-pressed')).toBe('false')
    expect(wrapper.find('button').attributes('disabled')).toBeUndefined()
  })
})
