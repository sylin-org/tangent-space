// @vitest-environment jsdom
import { afterEach, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import NotificationsPage from '../src/components/NotificationsPage.vue'
import { useSite } from '../src/stores/site'
import { createWorkspaceAttention } from '../src/composables/workspace-attention'
import { rolesRequest } from '../src/composables/roles-api'
vi.mock('../src/composables/roles-api', () => ({ rolesRequest: vi.fn() }))
const request = vi.mocked(rolesRequest)
const prefs = { notifyReplies: false, notifyMentions: true, notifyFollowing: false, notifyNewPeople: true }
function identity(actor: string) {
  const site = useSite()
  site.site = { name: 'Space', established: true, signIn: '/sign-in', signOut: '/sign-out', participant: { participantRef: actor, did: 'did:plc:' + actor, handle: actor + '.test', isOwner: true, joinedAt: '' } }
  site.state = 'signedIn'
  return site
}
afterEach(() => { request.mockReset(); document.body.innerHTML = '' })
it('does not let a late mark-read result alter another accountÃ¢â‚¬â„¢s notifications', async () => {
  let settle!: () => void
  request.mockImplementation(async path => path.endsWith('/read') ? new Promise<void>(resolve => { settle = resolve }) : { items: [], more: false })
  const attention = createWorkspaceAttention(), operation = attention.markRead()
  await flushPromises(); attention.reset()
  attention.notifications.value = [{ id: 'bob-message', kind: 'direct_reply', title: 'BobÃ¢â‚¬â„¢s topic', topicKey: 'b', tangentKey: 't', excerpt: '', createdAt: '', unread: true, url: '/t/t/b', author: { id: 'another' } }]
  settle(); await operation
  expect(attention.notifications.value[0]?.unread).toBe(true)
  expect(request).toHaveBeenCalledTimes(1)
})
it('clears preference state while a new account loads and ignores the earlier response', async () => {
  setActivePinia(createPinia()); const site = identity('alice')
  let oldResponse!: (value: unknown) => void, newResponse!: (value: unknown) => void
  request.mockImplementationOnce(() => new Promise(resolve => { oldResponse = resolve })).mockImplementationOnce(() => new Promise(resolve => { newResponse = resolve }))
  document.body.innerHTML = '<aside id="notification-index-slot"></aside><div id="notification-path-actions"></div>'
  const wrapper = mount(NotificationsPage, { props: { attention: createWorkspaceAttention(), signedIn: true }, global: { stubs: { Teleport: true } } })
  identity('bob'); await flushPromises(); oldResponse(prefs); await flushPromises()
  await wrapper.get('.notification-settings-link').trigger('click')
  expect(wrapper.find('form').exists()).toBe(false)
  expect(wrapper.text()).toContain('Loading notification settings')
  newResponse({ ...prefs, notifyReplies: true }); await flushPromises()
  expect((wrapper.get('input').element as HTMLInputElement).checked).toBe(true)
  site.site!.participant = null; await flushPromises()
  expect(wrapper.find('form').exists()).toBe(false)
  wrapper.unmount()
})
it('saves a fixed preference snapshot and disables unsaved defaults while loading', async () => {
  setActivePinia(createPinia()); identity('alice')
  const bodies: unknown[] = []; let saveResponse!: () => void
  request.mockImplementation(async (path, options) => {
    if (options?.method === 'PATCH') { bodies.push(options.body); return new Promise<void>(resolve => { saveResponse = resolve }) }
    return path.endsWith('/preferences') ? prefs : { items: [], more: false }
  })
  document.body.innerHTML = '<aside id="notification-index-slot"></aside><div id="notification-path-actions"></div>'
  const wrapper = mount(NotificationsPage, { props: { attention: createWorkspaceAttention(), signedIn: true }, global: { stubs: { Teleport: true } } })
  await flushPromises(); await wrapper.get('.notification-settings-link').trigger('click')
  expect(wrapper.get('button[type=submit]').attributes('disabled')).toBeDefined()
  await wrapper.get('input').setValue(true); await wrapper.get('form').trigger('submit'); await flushPromises()
  expect(wrapper.get('fieldset').attributes('disabled')).toBeDefined()
  expect(bodies).toEqual([{ ...prefs, notifyReplies: true }])
  saveResponse(); await flushPromises()
  expect(wrapper.text()).toContain('Notification settings saved.')
  expect(wrapper.get('button[type=submit]').attributes('disabled')).toBeDefined()
  wrapper.unmount()
})

it('moves notification filters to the rail when site loading establishes the workspace', async () => {
  setActivePinia(createPinia())
  const site = useSite()
  site.state = 'loading'
  request.mockResolvedValue(prefs)
  const wrapper = mount(NotificationsPage, { attachTo: document.body, props: { attention: createWorkspaceAttention(), signedIn: false } })
  const rail = document.createElement('aside'); rail.id = 'notification-index-slot'; document.body.append(rail)
  identity('alice'); await wrapper.setProps({ signedIn: true }); await flushPromises()
  expect(rail.querySelector('.notification-index')).not.toBeNull()
  expect(wrapper.find('.notification-index').exists()).toBe(false)
  wrapper.unmount()
})

it('replaces bounded notification pages, preserves the cursor on refresh, and clears continuation on identity reset', async () => {
  request.mockImplementation(async path => path.includes('?cursor=next')
    ? { items: [{ id: 'second' }], more: false, previousCursor: 'first', incomplete: true }
    : { items: [{ id: 'first' }], more: true, nextCursor: 'next', incomplete: false })
  const attention = createWorkspaceAttention(); await attention.load()
  expect(attention.nextCursor.value).toBe('next')
  await attention.load(attention.nextCursor.value)
  expect(attention.notifications.value.map(item => item.id)).toEqual(['second'])
  expect(attention.incomplete.value).toBe(true); expect(attention.previousCursor.value).toBe('first')
  await attention.load(); expect(request.mock.calls.at(-1)?.[0]).toBe('/api/workspace/notifications?cursor=next')
  attention.reset(); expect(attention.nextCursor.value).toBeNull(); expect(attention.previousCursor.value).toBeNull()
  await attention.load(); expect(request.mock.calls.at(-1)?.[0]).toBe('/api/workspace/notifications')
})

it('shows bounded empty and failure states without claiming complete catch-up, with usable continuation', async () => {
  setActivePinia(createPinia()); identity('alice'); request.mockResolvedValue(prefs)
  const attention = createWorkspaceAttention(); attention.incomplete.value = true; attention.more.value = true; attention.nextCursor.value = 'next'
  const wrapper = mount(NotificationsPage, { props: { attention, signedIn: true }, global: { stubs: { Teleport: true } } })
  await flushPromises()
  expect(wrapper.text()).toContain('No notifications on this page.')
  expect(wrapper.text()).toContain('limited view of recent activity')
  expect(wrapper.text()).not.toContain('all caught up.')
  expect(wrapper.text()).toContain('No unread notifications on this page.')
  await wrapper.get('.notification-mobile-filters button:nth-child(2)').trigger('click')
  expect(wrapper.text()).toContain('No replies on this page.')
  expect(wrapper.text()).toContain('Next page')
  attention.loading.value = true; await flushPromises(); expect(wrapper.text()).toContain('Loading notifications.')
  attention.loading.value = false; attention.error.value = 'Page changed'; await flushPromises()
  expect(wrapper.text()).toContain('Notifications could not be loaded.')
  expect(wrapper.text()).toContain('Return to first page'); wrapper.unmount()
})
