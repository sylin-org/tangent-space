// @vitest-environment jsdom
import { beforeEach, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import BlueskyBridgePanel from '../src/components/BlueskyBridgePanel.vue'
import MediaStoragePanel from '../src/components/MediaStoragePanel.vue'
import SettingsToggle from '../src/components/SettingsToggle.vue'
import { rolesRequest } from '../src/composables/roles-api'
import type { SettingsChange } from '../src/composables/settings-changes'

const registrations = vi.hoisted(() => [] as SettingsChange[])
vi.mock('../src/composables/roles-api', () => ({ rolesRequest: vi.fn() }))
vi.mock('../src/composables/settings-changes', () => ({ registerSettingsChange: (change: SettingsChange) => { registrations.push(change); return true } }))
const request = vi.mocked(rolesRequest)
const sourceState = { bringReplies: false, showCounts: false, flows: [], status: 'Ready' }
const storageState = { location: 'local', maximumUploadMb: 5, resizeImages: true, removeLocationData: true, endpoint: '', bucket: '', region: 'us-east-1', credentialsConfigured: false }
beforeEach(() => { request.mockReset(); registrations.length = 0 })

it('Sources requires a loaded baseline and lets a failed load be retried', async () => {
  request.mockRejectedValueOnce(new Error('Connection lost')).mockResolvedValueOnce({ tangents: [] })
  const panel = mount(BlueskyBridgePanel, { props: { actorKey: 'owner', active: true } })
  await flushPromises()
  expect(panel.find('input[type=checkbox]').exists()).toBe(false)
  expect(panel.text()).toContain('Connection lost')
  request.mockImplementation(path => Promise.resolve(path === '/api/v1/tangents' ? { tangents: [] } : sourceState) as never)
  await panel.find('button').trigger('click')
  await flushPromises()
  expect(panel.find('input[type=checkbox]').exists()).toBe(true)
  expect(panel.text()).not.toContain('Connection lost')
})

it('Sources saves the submitted snapshot and retains any later edits as dirty', async () => {
  let finish!: (result: unknown) => void
  request.mockImplementation((path, options) => options?.method === 'PATCH'
    ? new Promise(resolve => { finish = resolve }) as never
    : Promise.resolve(path === '/api/v1/tangents' ? { tangents: [] } : sourceState) as never)
  const panel = mount(BlueskyBridgePanel, { props: { actorKey: 'owner', active: true } })
  await flushPromises()
  const input = panel.find('input[type=checkbox]')
  await input.setValue(true)
  const saving = registrations[0]!.save()
  await flushPromises()
  expect(panel.find('fieldset').attributes('disabled')).toBeDefined()
  expect((input.element as HTMLInputElement).disabled).toBe(true)
  panel.findComponent(SettingsToggle).vm.$emit('update:modelValue', false)
  await flushPromises()
  finish({})
  await saving
  expect(registrations[0]!.count()).toBe(1)
  expect(request).toHaveBeenCalledWith('/api/v1/content/bluesky/bridge', expect.objectContaining({ body: { bringReplies: true, showCounts: false, flows: [] } }))
})

it('Storage sends configuration fields only and uses the submitted save baseline', async () => {
  let finish!: (result: unknown) => void
  request.mockImplementation((_path, options) => options?.method === 'PATCH'
    ? new Promise(resolve => { finish = resolve }) as never : Promise.resolve(storageState) as never)
  const panel = mount(MediaStoragePanel, { props: { actorKey: 'owner', active: true } })
  await flushPromises()
  const select = panel.find('select')
  await select.setValue('10')
  const saving = registrations[0]!.save()
  await flushPromises()
  expect(panel.find('.storage-fields').attributes('disabled')).toBeDefined()
  await select.setValue('20')
  finish({})
  await saving
  expect(registrations[0]!.count()).toBe(1)
  const submitted = request.mock.calls.find(([, options]) => options?.method === 'PATCH')![1]!.body as Record<string, unknown>
  expect(submitted.maximumUploadMb).toBe(10)
  expect(submitted).not.toHaveProperty('credentialsConfigured')
})
