// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { useSite } from '../src/stores/site'
import { useRouter } from '../src/stores/router'
import ProfilePanel from '../src/components/ProfilePanel.vue'

const profileEnvelope = {
  result: {
    data: {
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
    },
  },
  actions: [],
}

function stubFetch(overrides: Record<string, unknown> = {}) {
  return vi.fn(async (input: RequestInfo | URL) => {
    const url = String(input)
    const json = (body: unknown, status = 200) =>
      new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
    if (url.startsWith('/api/participants/')) {
      return 'blocked' in overrides ? json({ status: 'blocked' }) : json(overrides.envelope ?? profileEnvelope)
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

  it('renders the identity card from the profile envelope', async () => {
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
    // The envelope's handle-based canonical path replaces the requested one.
    expect(router.route).toMatchObject({ kind: 'participant', identifier: 'fellow.bsky.social' })
  })

  it('explains a blocked profile and offers another try', async () => {
    vi.stubGlobal('fetch', stubFetch({ blocked: true }))
    const wrapper = await mountOnParticipantRoute()
    expect(wrapper.find('#profile-name').text()).toBe('Profile unavailable')
    expect(wrapper.text()).toContain('No participant is visible under that address.')
    expect(wrapper.find('#profile-actions button').text()).toBe('Try again')
  })
})
