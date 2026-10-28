// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { useSite } from '../src/stores/site'
import { useRouter } from '../src/stores/router'
import ProfilePanel from '../src/components/ProfilePanel.vue'

const profile = {
      participantRef: 'abc123',
      did: 'did:plc:example',
      handle: 'fellow.bsky.social',
      displayName: 'Fellow Participant',
      description: 'Here for the quiet conversations.',
      classification: 'Human',
      self: false,
      joinedAt: '2026-09-01T00:00:00Z',
      roles: [{ scope: 'server', role: 'participant' }],
      posts: [{ createdAt: '2026-09-10T10:00:00Z', url: '/t/small-hours/topics/first/xx', text: 'A first thought.', removed: false }],
}

function stubFetch(overrides: Record<string, unknown> = {}) {
  return vi.fn(async (input: RequestInfo | URL) => {
    const url = String(input)
    const json = (body: unknown, status = 200) =>
      new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
    if (url.startsWith('/api/participants/')) {
      return 'blocked' in overrides ? json({ code: 'permission_denied' }, 403) : json(overrides.profile ?? profile)
    }
    if (url.startsWith('/api/roles/ui/participant/')) return json([])
    throw new Error('unexpected fetch ' + url)
  })
}

async function mountOnParticipantRoute() {
  const site = useSite()
  const router = useRouter()
  router.apply('/u/fellow.bsky.social')
  site.state = 'anonymous'
  const wrapper = mount(ProfilePanel, { props: { site, router }, global: { plugins: [createPinia()] } })
  await flushPromises()
  return wrapper
}

describe('the participant profile', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.stubGlobal('fetch', stubFetch())
  })

  it('renders the identity card from the profile resource', async () => {
    const wrapper = await mountOnParticipantRoute()
    expect(wrapper.find('#profile-name').text()).toBe('Fellow Participant')
    expect(wrapper.find('#profile-handle').text()).toContain('@fellow.bsky.social')
    expect(wrapper.find('#profile-did').text()).toBe('did:plc:example')
    expect(wrapper.text()).toContain('Joined')
    expect(wrapper.find('#profile-roles').text()).toContain('Participant')
  })

  it('renders the recent posts with links back into the Topic', async () => {
    const wrapper = await mountOnParticipantRoute()
    expect(wrapper.find('#profile-posts').text()).toContain('A first thought.')
    expect(wrapper.find('#profile-posts a').attributes('href')).toBe('/t/small-hours/topics/first/xx')
  })

  it('keeps a canonical URL when the profile answers under another address', async () => {
    const router = useRouter()
    await mountOnParticipantRoute()
    // The profile's handle-based canonical path replaces the requested one.
    expect(router.route).toMatchObject({ kind: 'participant', identifier: 'fellow.bsky.social' })
  })

  it('explains a blocked profile and offers another try', async () => {
    vi.stubGlobal('fetch', stubFetch({ blocked: true }))
    const wrapper = await mountOnParticipantRoute()
    expect(wrapper.find('#profile-name').text()).toBe('Profile unavailable')
    expect(wrapper.text()).toContain('No participant is visible under that address.')
    expect(wrapper.find('#profile-actions button').text()).toBe('Try again')
  })

  it('ignores late role reads and toggles from a different participant profile', async () => {
    const site = useSite()
    site.state = 'signedIn'
    site.site = { name: 'Space', established: true, signIn: '/auth/atproto/challenge', signOut: null,
      participant: { participantRef: 'viewer', did: null, handle: 'viewer', isOwner: true, joinedAt: '' } }
    const router = useRouter()
    window.history.replaceState(null, '', '/u/a')
    router.apply('/u/a')
    let finishOldRoles!: (response: Response) => void
    let finishToggle!: (response: Response) => void
    let delayOldRoles = true
    const json = (body: unknown) => new Response(JSON.stringify(body), { headers: { 'Content-Type': 'application/json' } })
    vi.stubGlobal('fetch', vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      if (url === '/api/participants/a/profile') return Promise.resolve(json({ ...profile, participantRef: 'a', handle: 'a', displayName: 'Person A', roles: [] }))
      if (url === '/api/participants/b/profile') return Promise.resolve(json({ ...profile, participantRef: 'b', handle: 'b', displayName: 'Person B', roles: [] }))
      if (url === '/api/roles/ui/participant/a/roles') {
        if (delayOldRoles) return new Promise<Response>(resolve => { finishOldRoles = resolve })
        return Promise.resolve(json([{ id: 'role:a', name: 'A Role', canToggle: true, isMember: false }]))
      }
      if (url === '/api/roles/ui/participant/b/roles') return Promise.resolve(json([{ id: 'role:b', name: 'B Role', canToggle: true, isMember: false }]))
      if (url === '/api/roles/ui/session') return Promise.resolve(json({ requestToken: 'token', headerName: 'X-Request-Token' }))
      if (url === '/api/roles/ui/participant/b/roles/role%3Ab' && init?.method === 'PUT')
        return new Promise<Response>(resolve => { finishToggle = resolve })
      throw new Error('unexpected fetch ' + url)
    }))
    const wrapper = mount(ProfilePanel, { props: { site, router }, global: { plugins: [createPinia()] } })
    await flushPromises()
    window.history.replaceState(null, '', '/u/b')
    router.apply('/u/b')
    await flushPromises()
    finishOldRoles(json([{ id: 'role:a', name: 'A Role', canToggle: true, isMember: false }]))
    await flushPromises()
    expect(wrapper.find('#profile-name').text()).toBe('Person B')
    expect(wrapper.find('#profile-role-management').text()).toContain('B Role')
    expect(wrapper.find('#profile-role-management').text()).not.toContain('A Role')
    await wrapper.find('.profile-role-toggle').trigger('click')
    await flushPromises()
    delayOldRoles = false
    window.history.replaceState(null, '', '/u/a')
    router.apply('/u/a')
    await flushPromises()
    finishToggle(json({ accepted: true }))
    await flushPromises()
    expect(wrapper.find('#profile-name').text()).toBe('Person A')
    expect(wrapper.find('#profile-role-management').text()).toContain('A Role')
    expect(wrapper.find('#profile-role-status').text()).not.toContain('B Role')
    expect(wrapper.find('#profile-roles').text()).not.toContain('B Role')
  })
})


it('keeps the selected identity section when resolving the canonical participant URL', async () => {
  setActivePinia(createPinia())
  window.history.replaceState({}, '', '/u/did:plc:example?tab=identity')
  vi.stubGlobal('fetch', stubFetch())
  const site = useSite(); site.state = 'anonymous'
  const router = useRouter()
  const wrapper = mount(ProfilePanel, { props: { site, router }, global: { plugins: [createPinia()] } })
  await flushPromises()
  expect(window.location.pathname).toBe('/u/fellow.bsky.social')
  expect(window.location.search).toBe('?tab=identity')
  expect(wrapper.find('#profile-identity-tab').attributes('aria-selected')).toBe('true')
  expect(wrapper.find('#profile-posts-tab').attributes('tabindex')).toBe('-1')
  wrapper.unmount()
})

it('reports a failed account badge change and leaves the stored classification visible', async () => {
  setActivePinia(createPinia())
  window.history.replaceState({}, '', '/u/fellow.bsky.social')
  const standard = stubFetch({ profile: { ...profile, self: true } })
  vi.stubGlobal('fetch', vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
    if (String(input) === '/api/participants/me' && init?.method === 'PATCH') return Promise.resolve(new Response('{}', { status: 500 }))
    return standard(input)
  }))
  const site = useSite(); site.state = 'anonymous'
  const router = useRouter()
  const wrapper = mount(ProfilePanel, { props: { site, router }, global: { plugins: [createPinia()] } })
  await flushPromises()
  await wrapper.find('.profile-classification-option').trigger('click')
  await flushPromises()
  expect(wrapper.find('.profile-classification-badge').text()).toContain('Person')
  expect(wrapper.find('.profile-feedback').text()).toContain('could not be saved')
  wrapper.unmount()
})
