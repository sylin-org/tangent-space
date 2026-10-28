// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, shallowMount } from '@vue/test-utils'
import { ref, shallowRef } from 'vue'
import { rolesRequest } from '../src/composables/roles-api'
import TangentPeoplePanel from '../src/components/TangentPeoplePanel.vue'
import TangentSettingsPanel from '../src/components/TangentSettingsPanel.vue'

vi.mock('../src/composables/roles-api', () => ({ rolesRequest: vi.fn() }))
const request = vi.mocked(rolesRequest)

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>(done => { resolve = done })
  return { promise, resolve }
}

const member = (participantId: string) => ({
  participantId, handle: `${participantId}.example`, displayName: participantId,
  role: 'Member', isOwner: false, canChangeRole: true, allowedRoles: ['Member', 'Reader'],
})

beforeEach(() => {
  request.mockReset()
  sessionStorage.clear()
  window.history.replaceState(null, '', '/t/garden/settings')
})

describe('Tangent people', () => {
  it('replaces each bounded directory page and ignores the previous account response', async () => {
    const alice = deferred<{ items: ReturnType<typeof member>[]; nextCursor: string | null }>()
    const bob = deferred<{ items: ReturnType<typeof member>[]; nextCursor: string | null }>()
    let calls = 0
    request.mockImplementation((path) => {
      if (String(path).includes('cursor=next')) return Promise.resolve({ items: [member('second')], nextCursor: null }) as never
      if (String(path).includes('/members')) return (++calls === 1 ? alice.promise : bob.promise) as never
      throw new Error(`Unexpected ${path}`)
    })
    const wrapper = mount(TangentPeoplePanel, { props: { tangent: 'garden', actor: 'alice' } })
    await wrapper.setProps({ actor: 'bob' })
    bob.resolve({ items: [member('bob-only')], nextCursor: null })
    await flushPromises()
    alice.resolve({ items: [member('alice-only')], nextCursor: 'next' })
    await flushPromises()
    expect(wrapper.text()).not.toContain('alice-only')
    expect(wrapper.text()).toContain('bob-only')

    request.mockImplementation((path) => Promise.resolve(String(path).includes('cursor=next')
      ? { items: [member('second')], nextCursor: null }
      : { items: [member('first')], nextCursor: 'next' }) as never)
    await wrapper.setProps({ tangent: 'orchard' })
    await flushPromises()
    expect(wrapper.text()).toContain('first')
    await wrapper.find('.tangent-people-pages button').trigger('click')
    await flushPromises()
    expect(wrapper.text()).toContain('second')
    expect(wrapper.text()).not.toContain('first')
  })
})

describe('Tangent identity drafts', () => {
  function settings(description: string) {
    return {
      actorKey: ref('alice'), loadedActor: ref('alice'),
      context: shallowRef({ kind: 'tangent', tangent: 'garden' }),
      resource: shallowRef({ tangent: { key: 'garden', name: 'Garden', description, motto: '', accent: '#fbbf24', artwork: '', admission: 'Open', canManageAdmission: true } }),
      activeTab: ref('context'), shellVisible: ref(true), loading: ref(false), denied: ref(''), settingsTab: vi.fn(),
    }
  }

  it('restores only edited fields and saves only changed identity fields', async () => {
    request.mockResolvedValue({} as never)
    const first = shallowMount(TangentSettingsPanel, { props: { settings: settings('Old description') as never, directories: { loadTangents: vi.fn().mockResolvedValue(undefined) } as never } })
    await first.find('input[maxlength="120"]').setValue('New Garden')
    const stored = JSON.parse(sessionStorage.getItem('tangent:identity-draft:alice:garden') || '{}')
    expect(stored).toEqual({ name: 'New Garden' })
    first.unmount()

    const second = shallowMount(TangentSettingsPanel, { props: { settings: settings('Fresh description') as never, directories: { loadTangents: vi.fn().mockResolvedValue(undefined) } as never } })
    expect((second.find('textarea[maxlength="500"]').element as HTMLTextAreaElement).value).toBe('Fresh description')
    expect((second.find('input[maxlength="120"]').element as HTMLInputElement).value).toBe('New Garden')
    await second.find('form.tangent-identity-form').trigger('submit')
    await flushPromises()
    expect(request).toHaveBeenCalledWith('/api/v1/tangents/garden', expect.objectContaining({ method: 'PATCH', body: { name: 'New Garden' } }))
    expect(sessionStorage.getItem('tangent:identity-draft:alice:garden')).toBeNull()
  })

  it('keeps an unsaved joining choice through an identity save and a remount', async () => {
    request.mockResolvedValue({} as never)
    const state = settings('Garden conversations')
    const directories = { loadTangents: vi.fn().mockResolvedValue(undefined) }
    const wrapper = shallowMount(TangentSettingsPanel, { props: { settings: state as never, directories: directories as never } })
    await wrapper.find('input[type="radio"][value="Approval"]').setValue()
    await wrapper.find('input[maxlength="120"]').setValue('New Garden')
    await wrapper.find('form.tangent-identity-form').trigger('submit')
    await flushPromises()
    expect(sessionStorage.getItem('tangent:admission-draft:alice:garden')).toBe('Approval')
    expect((wrapper.find('input[type="radio"][value="Approval"]').element as HTMLInputElement).checked).toBe(true)
    wrapper.unmount()

    const reopened = shallowMount(TangentSettingsPanel, { props: { settings: settings('Garden conversations') as never, directories: directories as never } })
    expect((reopened.find('input[type="radio"][value="Approval"]').element as HTMLInputElement).checked).toBe(true)
    expect(sessionStorage.getItem('tangent:identity-draft:alice:garden')).toBeNull()
  })
})
