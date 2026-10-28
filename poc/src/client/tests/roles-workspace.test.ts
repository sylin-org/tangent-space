// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { ref } from 'vue'
import RolesPanel from '../src/components/RolesPanel.vue'
import type { Settings } from '../src/composables/settings'

const request = vi.hoisted(() => vi.fn())
vi.mock('../src/composables/roles-api', async importOriginal => ({
  ...await importOriginal<typeof import('../src/composables/roles-api')>(), rolesRequest: request,
}))
const administrator = { id: 'role:administrator', name: 'Administrators', permissions: ['host.enter'], metadata: { purpose: 'Manage this Space.', color: '#ee6c4d' }, memberCount: 2 }
const owner = { id: 'role:owner', name: 'Owner', permissions: ['host.enter'], metadata: { system: 'true' } }
let wrapper: VueWrapper | undefined

beforeEach(() => {
  sessionStorage.clear()
  history.replaceState({}, '', '/settings?tab=roles')
  request.mockReset()
  request.mockImplementation(async (path: string, options?: { method?: string }) => {
    if (options?.method === 'DELETE') return {}
    if (path === '/api/roles/ui/descriptor') return { capabilities: [
      { key: 'host.enter', label: 'Enter server', description: 'See this server.', scopeTypes: ['host'] },
      { key: 'host.manage', label: 'Manage server', description: 'Change server settings.', scopeTypes: ['host'] },
    ] }
    if (path.startsWith('/api/identity/roles?')) return { items: [administrator, owner], totalCount: 2 }
    if (path.startsWith('/api/roles/ui/search')) return { items: [], page: 1, totalMembers: 2 }
    throw new Error('Unexpected request: ' + path)
  })
})
afterEach(() => wrapper?.unmount())

async function openRoles() {
  wrapper = mount(RolesPanel, { props: { settings: { activeTab: ref('roles'), actorKey: ref('owner') } as unknown as Settings }, global: { plugins: [createPinia()], stubs: { Teleport: true } } })
  await flushPromises()
  await wrapper.find('.role-directory-row').trigger('click')
  return wrapper
}

describe('Roles workspace', () => {
  it('keeps navigation visible while inspecting a role and groups editable permissions with accurate state', async () => {
    const view = await openRoles()
    expect(view.find('#role-overview').attributes('hidden')).toBeUndefined()
    expect(view.find('.role-directory-row').attributes('aria-current')).toBe('true')
    expect(view.find('.role-directory-members').text()).toBe('2 members')
    await view.find('#role-tab-permissions').trigger('click')
    expect(view.find('.role-capability-group h4').text()).toContain('1 of 2 on')
    expect(view.find('input[role="switch"]').attributes('aria-label')).toBe('Enter the Space')
    const manage = view.findAll('input[role="switch"]')[1]!
    await manage.setValue(true)
    expect(view.findAll('.is-changed')).toHaveLength(1)
    expect(view.find('#role-save').attributes('disabled')).toBeUndefined()
    await manage.setValue(false)
    expect(view.findAll('.is-changed')).toHaveLength(0)
    expect(view.find('#role-save').attributes('disabled')).toBeDefined()
    await view.find('#role-tab-permissions').trigger('keydown', { key: 'ArrowRight' })
    expect(view.find('#role-tab-members').attributes('aria-selected')).toBe('true')
  })

  it('requires inline confirmation before deleting a role', async () => {
    const view = await openRoles()
    await view.find('#role-retire').trigger('click')
    expect(view.find('.role-delete-confirmation').text()).toContain('Delete Administrators?')
    expect(request.mock.calls.some(([, options]) => options?.method === 'DELETE')).toBe(false)
    await view.find('.role-delete-confirmation .role-danger').trigger('click')
    await flushPromises()
    expect(request).toHaveBeenCalledWith('/api/identity/roles/role%3Aadministrator', { method: 'DELETE' })
  })

  it('keeps built-in grants read-only and exposes automatic membership clearly', async () => {
    const view = await openRoles()
    await view.findAll('.role-directory-row')[1]!.trigger('click')
    await view.find('#role-tab-permissions').trigger('click')
    expect(view.findAll('input[role="switch"]').every(input => input.attributes('disabled') !== undefined)).toBe(true)
    expect(view.find('#role-retire').exists()).toBe(false)
    await view.find('#role-tab-members').trigger('click')
    expect(view.find('#role-member-protected').text()).toContain('Membership is automatic')
    expect(view.find('#role-member-search').exists()).toBe(false)
  })
})
