// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { ref } from 'vue'
import { useRouter } from '../src/stores/router'
import { useSite } from '../src/stores/site'
import { createSettings } from '../src/composables/settings'
import RolesPanel from '../src/components/RolesPanel.vue'
import AccessPanel from '../src/components/AccessPanel.vue'

const descriptor = {
  capabilities: [{ key: 'post.reply', label: 'Reply to posts', description: 'Take part in conversations', scopeTypes: ['host'] }],
}

const rolePage = {
  items: [
    { id: 'role:moderator', name: 'Moderator', metadata: { purpose: 'Helps look after the place', color: '#ff0055' }, permissions: ['post.reply'] },
    { id: 'role:owner', name: 'Owner', metadata: { purpose: 'Owns the server', color: '#a98df0', system: 'true' }, permissions: ['post.reply'] },
  ],
  totalCount: 2,
}

function stubFetch() {
  return vi.fn(async (input: RequestInfo | URL) => {
    const url = String(input)
    const json = (body: unknown) => new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
    if (url.startsWith('/api/roles/ui/descriptor')) return json(descriptor)
    if (url.startsWith('/api/identity/roles?pageSize')) return json(rolePage)
    if (url.startsWith('/api/roles/ui/search')) return json({ items: [], page: 1, nextPage: null, totalMembers: 0 })
    if (url.startsWith('/api/server/access')) return json({ selected: { see: ['everyone'], createTangents: ['role:moderator'], manage: [] }, effective: { see: ['everyone'], createTangents: ['role:moderator'], manage: [] } })
    if (url.startsWith('/api/identity/roles?page=1')) return json({ items: rolePage.items, totalCount: 2 })
    throw new Error('unexpected fetch ' + url)
  })
}

describe('the settings page coordination', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    window.history.replaceState(null, '', '/settings')
  })

  it('names the right opening tab for each context', () => {
    const settings = createSettings(useSite(), useRouter())
    expect(settings.requestedTab('server')).toBe('server')
    expect(settings.requestedTab('tangent')).toBe('context')
    window.history.replaceState(null, '', '/settings?tab=people')
    expect(settings.requestedTab('server')).toBe('people')
    expect(settings.requestedTab('tangent')).toBe('people')
    expect(settings.requestedTab('topic')).toBe('context')
    window.history.replaceState(null, '', '/settings?tab=access')
    expect(settings.requestedTab('tangent')).toBe('access')
  })

  it('builds the context and its decisions from the route', async () => {
    const router = useRouter()
    const settings = createSettings(useSite(), router)
    router.apply('/settings')
    expect(settings.contextFor(router.route)?.kind).toBe('server')
    expect(settings.contextFor(router.route)?.decisions.map(d => d.key)).toEqual(['see', 'createTangents', 'manage'])
    router.apply('/t/small-hours/settings')
    expect(settings.contextFor(router.route)?.kind).toBe('tangent')
    expect(settings.contextFor(router.route)?.accessPath).toBe('/api/v1/tangents/small-hours/access')
  })

  it('opens anonymous settings as a sign-in prompt, not a shell', async () => {
    const site = useSite()
    const router = useRouter()
    router.apply('/settings')
    const settings = createSettings(site, router)
    await settings.open()
    expect(settings.shellVisible.value).toBe(false)
    expect(settings.context.value).toBeNull()
  })
})

describe('the roles tab', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.stubGlobal('fetch', stubFetch())
    window.history.replaceState(null, '', '/settings')
  })

  it('lists the roles and opens the editor for a chosen one', async () => {
    const settings = { activeTab: ref<'roles' | 'people' | 'server' | 'access' | 'context'>('roles'), actorKey: ref('owner') }
    const wrapper = mount(RolesPanel, { props: { settings: settings as never }, global: { plugins: [createPinia()] } })
    await flushPromises()
    expect(wrapper.findAll('.role-directory-row')).toHaveLength(2)
    expect(wrapper.text()).toContain('Moderator')
    expect(wrapper.text()).toContain('Owner')
    const row = wrapper.findAll('.role-directory-row').find(node => node.text().includes('Moderator'))!
    await row.trigger('click')
    await flushPromises()
    const name = wrapper.find('#role-name').element as HTMLInputElement
    expect(name.value).toBe('Moderator')
    expect(wrapper.find('#role-managed').attributes('hidden')).toBeDefined()
  })

  it('marks a built-in role as managed and disabled', async () => {
    const settings = { activeTab: ref('roles'), actorKey: ref('owner') }
    const wrapper = mount(RolesPanel, { props: { settings: settings as never }, global: { plugins: [createPinia()] } })
    await flushPromises()
    const row = wrapper.findAll('.role-directory-row').find(node => node.text().includes('Owner'))!
    await row.trigger('click')
    await flushPromises()
    expect(wrapper.find('#role-managed').attributes('hidden')).toBeUndefined()
    expect((wrapper.find('#role-name').element as HTMLInputElement).disabled).toBe(true)
  })

  it('uses server member totals and pages rather than counting the visible batch', async () => {
    const base = stubFetch()
    const fetch = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input)
      if (url.startsWith('/api/roles/ui/search')) {
        const page = Number(new URL(url, 'http://localhost').searchParams.get('page'))
        return new Response(JSON.stringify({
          items: [{ id: 'participant-' + page, displayName: 'Member ' + page, inRole: true }],
          page, nextPage: page === 1 ? 2 : null, totalMembers: 120,
        }))
      }
      return base(input)
    })
    vi.stubGlobal('fetch', fetch)
    const settings = { activeTab: ref('roles'), actorKey: ref('owner') }
    const wrapper = mount(RolesPanel, { props: { settings: settings as never }, global: { plugins: [createPinia()] } })
    await flushPromises()
    await wrapper.findAll('.role-directory-row').find(node => node.text().includes('Moderator'))!.trigger('click')
    await wrapper.find('[data-role-editor-tab="members"]').trigger('click')
    await flushPromises()
    expect(wrapper.find('#role-members-heading-count').text()).toBe('120')
    expect(wrapper.find('#role-members').text()).toContain('Member 1')
    await wrapper.find('.role-member-pages button:last-child').trigger('click')
    await flushPromises()
    expect(wrapper.find('#role-members').text()).toContain('Member 2')
    expect(wrapper.find('#role-members-heading-count').text()).toBe('120')
  })
})

describe('the access tab', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.stubGlobal('fetch', stubFetch())
  })

  it('renders one decision row per context decision', async () => {
    const context = ref({
      kind: 'server' as const,
      title: 'Server settings',
      eyebrow: 'MAKE YOURSELF AT HOME',
      intro: 'intro',
      accessPath: '/api/server/access',
      decisions: [
        { key: 'see', label: 'Who can enter', description: 'Choose who can discover and open this server.', bypass: 'The Owner can always enter.' },
        { key: 'manage', label: 'Who can manage', description: 'Choose who may change settings.', bypass: 'The Owner can always manage.' },
      ],
    })
    const settings = { activeTab: ref('access'), context }
    const wrapper = mount(AccessPanel, { props: { settings: settings as never }, global: { plugins: [createPinia()] } })
    await flushPromises()
    const rows = wrapper.findAll('.access-action')
    expect(rows.length).toBe(2)
    expect(rows[0]!.text()).toContain('Who can enter')
    expect(wrapper.text()).toContain('@everyone')
  })
})
