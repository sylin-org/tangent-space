// @vitest-environment jsdom
import { afterEach, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { useSite } from '../src/stores/site'
import { useRouter } from '../src/stores/router'
import { createSettings } from '../src/composables/settings'
import ForumSidebar from '../src/components/ForumSidebar.vue'
import { createConversation } from '../src/composables/conversation'
import HostSettingsPanel from '../src/components/HostSettingsPanel.vue'
import ProfilePanel from '../src/components/ProfilePanel.vue'
import { rolesRequest } from '../src/composables/roles-api'
vi.mock('../src/composables/roles-api', () => ({ rolesRequest: vi.fn(), rolesSession: vi.fn(async () => ({})), clearRolesSession: vi.fn() }))
const wrappers: ReturnType<typeof mount>[] = []
afterEach(() => { wrappers.splice(0).forEach(wrapper => wrapper.unmount()); vi.restoreAllMocks(); vi.unstubAllGlobals(); vi.mocked(rolesRequest).mockReset(); sessionStorage.clear(); window.history.replaceState({}, '', '/') })
function identity(owner = false) {
  setActivePinia(createPinia()); const site = useSite(); site.state = 'signedIn'
  site.site = { name: 'Space', established: true, signIn: '/auth/atproto/challenge', signOut: '/auth/logout', participant: { participantRef: 'alice', did: null, handle: 'alice.test', isOwner: owner, joinedAt: '' } }
  return site
}

it.each([false, true])('keeps a privacy-safe doorstep usable when protected settings refuse access; signedIn=%s', async signedIn => {
  const site = identity(); const welcome = { ...site.site!, participant: signedIn ? site.site!.participant : null, name: 'Tangent Space', workspaceAvailable: false }
  vi.stubGlobal('fetch', vi.fn(async path => String(path) === '/api/site' ? new Response(JSON.stringify(welcome)) : new Response('{}', { status: 403 })))
  await site.load()
  expect(site.state).toBe(signedIn ? 'signedIn' : 'anonymous'); expect(site.site?.workspaceAvailable).toBe(false); expect(site.server).toBeNull()
  expect(site.announcement).not.toContain('could not be loaded')
})

it('opens delegated management in the existing People surface and never mounts unrelated Host controls', async () => {
  const site = identity(); site.server = { name: 'Space', canManage: false, canManageParticipants: true }
  vi.spyOn(site, 'loadServer').mockResolvedValue(undefined)
  window.history.replaceState({}, '', '/settings?tab=access'); const router = useRouter(); router.apply('/settings')
  const settings = createSettings(site, router); await settings.open()
  expect(settings.shellVisible.value).toBe(true); expect(settings.peopleOnly.value).toBe(true); expect(settings.activeTab.value).toBe('people')
  vi.mocked(rolesRequest).mockImplementation(async path => path.endsWith('/requests') || path.includes('/requests?page=') ? { items: [], nextPage: null } : { mode: 'Approval', admitted: true, status: 'accepted' })
  const wrapper = mount(HostSettingsPanel, { props: { settings, atmosphere: { canManage: false, close: vi.fn(), open: vi.fn() } as never, expectedDid: '' }, global: { stubs: { PeoplePanel: true, SpaceHero: true } } }); wrappers.push(wrapper); await flushPromises()
  expect(wrapper.findAll('[role=tab]').map(tab => tab.text())).toEqual(['People'])
  const sidebar = mount(ForumSidebar, { props: { directories: { tangents: [], topics: [] } as never, router, settings, tangent: null, conversation: createConversation(router), expanded: false, canCreate: false, signedIn: true, topicView: 'all', articleFilter: { month: '', author: '' }, writing: false } }); wrappers.push(sidebar)
  expect(sidebar.findAll('[aria-label="Settings sections"] button').map(button => button.text())).toEqual(['People'])
  for (const tab of ['server','appearance','roles','access','sources','storage']) expect(wrapper.find('#settings-' + tab + '-panel').exists()).toBe(false)
  expect(vi.mocked(rolesRequest).mock.calls.some(([path]) => path.startsWith('/api/space/admission/requests'))).toBe(true)
  settings.settingsTab('storage'); expect(settings.activeTab.value).toBe('people')
  site.server = { ...site.server, canManageParticipants: false }; await flushPromises()
  expect(settings.shellVisible.value).toBe(false); expect(wrapper.text()).toContain('Your current access does not allow Space settings')
  expect(wrapper.find('[role=tablist]').exists()).toBe(false)
  expect(sidebar.find('[aria-label="Settings sections"]').exists()).toBe(false)
})

it.each([false, true])('retains ordinary classification choices and explains the fixed owner badge; owner=%s', async owner => {
  const site = identity(owner); const router = useRouter(); router.apply('/u/alice.test')
  const profile = { participantRef: 'alice', displayName: 'Alice', handle: 'alice.test', classification: 'Human', self: true, roles: [], posts: [] }
  vi.mocked(rolesRequest).mockResolvedValue([])
  vi.stubGlobal('fetch', vi.fn(async path => new Response(JSON.stringify(String(path).startsWith('/api/roles/ui/') ? [] : profile))))
  const wrapper = mount(ProfilePanel, { props: { site, router } }); wrappers.push(wrapper); await flushPromises()
  expect(wrapper.find('.profile-classification-menu').exists()).toBe(!owner)
  expect(wrapper.text().includes('The Space owner remains a person')).toBe(owner)
})

it('clears a current denied standalone settings refresh and closes the previously authorized People shell', async () => {
  const site = identity(); site.server = { canManage: false, canManageParticipants: true }
  const router = useRouter(); router.apply('/settings')
  const settings = createSettings(site, router)
  const fetch = vi.fn().mockResolvedValueOnce(new Response(JSON.stringify(site.server))).mockResolvedValueOnce(new Response('{}', { status: 403 }))
  vi.stubGlobal('fetch', fetch); await settings.open(); expect(settings.shellVisible.value).toBe(true)
  await expect(site.loadServer()).rejects.toThrow('Server settings could not be read')
  await flushPromises(); expect(site.server).toBeNull(); expect(settings.shellVisible.value).toBe(false)
})

it('does not let an older denied settings response revoke a newer authorized refresh', async () => {
  const site = identity(); const complete: Array<(response: Response) => void> = []
  vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>(resolve => complete.push(resolve))))
  const earlier = site.loadServer().catch(() => {}), later = site.loadServer()
  complete[1]!(new Response(JSON.stringify({ canManage: false, canManageParticipants: true }))); await later
  complete[0]!(new Response('{}', { status: 403 })); await earlier
  expect(site.server?.canManageParticipants).toBe(true)
})

it('hides cached home Tangent metadata when the current Host welcome withdraws workspace access', async () => {
  const site = identity(); const router = useRouter(); router.apply('/')
  const settings = createSettings(site, router)
  const wrapper = mount(ForumSidebar, { props: { directories: { tangents: [{ key: 'private', name: 'Private community name', artwork: '/private-art.png', kind: 'discussions' }], topics: [] } as never, router, settings, tangent: null, conversation: createConversation(router), expanded: false, canCreate: false, signedIn: true, topicView: 'all', articleFilter: { month: '', author: '' }, writing: false, workspaceAvailable: true } }); wrappers.push(wrapper)
  expect(wrapper.text()).toContain('Private community name')
  await wrapper.setProps({ workspaceAvailable: false })
  expect(wrapper.text()).not.toContain('Private community name'); expect(wrapper.find('[src="/private-art.png"]').exists()).toBe(false)
})

it('immediately removes private server branding and ignores an older pending response after a same-account denied welcome', async () => {
  const site = identity(); site.server = { name: 'Private Space name', canManage: true }
  let complete!: (response: Response) => void
  const welcome = { ...site.site!, name: 'Tangent Space', workspaceAvailable: false }
  vi.stubGlobal('fetch', vi.fn(path => String(path) === '/api/site' ? Promise.resolve(new Response(JSON.stringify(welcome))) : new Promise<Response>(resolve => { complete = resolve })))
  const earlier = site.loadServer(); await site.load()
  expect(site.site?.participant?.participantRef).toBe('alice'); expect(site.server).toBeNull()
  complete(new Response(JSON.stringify({ name: 'Stale private Space name', canManage: true }))); await earlier
  expect(site.server).toBeNull(); expect(site.state).toBe('signedIn')
})
