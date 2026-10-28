// @vitest-environment jsdom
import { expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import DatabasePanel from '../src/components/DatabasePanel.vue'

it('requires a new successful test after edits and activates only the tested configuration', async () => {
  const activations: unknown[] = []
  let tests = 0
  const json = (data: unknown) => new Response(JSON.stringify(data), { headers: { 'Content-Type': 'application/json' } })
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const path = String(input)
    if (path === '/api/site') return json({ participant: { did: 'did:plc:owner' } })
    if (path.endsWith('/session')) return json({ headerName: 'X-CSRF', requestToken: 'token' })
    if (path.endsWith('/test')) {
      expect(init?.headers).toMatchObject({ 'X-CSRF': 'token' })
      tests++
      return json({ testId: 'tested-' + tests, provider: 'sqlite', message: 'Connection tested.' })
    }
    if (path.endsWith('/activate')) {
      activations.push(JSON.parse(String(init?.body)))
      return json({ provider: 'sqlite', restartRequired: true, message: 'Configuration saved.' })
    }
    return json({ activeProvider: 'sqlite', changeRequiresRestart: true, firstRun: true, pendingRestart: false, providers: [{
      id: 'sqlite', name: 'SQLite', fields: [{ name: 'path', label: 'Database path', type: 'text', required: true, secret: false }],
    }] })
  }))
  const wrapper = mount(DatabasePanel, { props: { expectedDid: 'did:plc:owner' } })
  await flushPromises()
  const input = wrapper.get('input[name="path"]')
  await input.setValue('first.db')
  await wrapper.get('form').trigger('submit')
  await flushPromises()
  expect(wrapper.text()).toContain('Activate tested configuration')
  await input.setValue('second.db')
  expect(wrapper.text()).not.toContain('Activate tested configuration')
  await wrapper.get('form').trigger('submit')
  await flushPromises()
  await wrapper.get('button.btn-primary').trigger('click')
  await flushPromises()
  expect(activations).toEqual([{ testId: 'tested-2', expectedDid: 'did:plc:owner' }])
  expect(wrapper.text()).toContain('Restart the server')
})

it('stops a database mutation when another tab switches accounts', async () => {
  let did = 'did:plc:owner'
  const mutations: string[] = []
  const json = (data: unknown) => new Response(JSON.stringify(data), { headers: { 'Content-Type': 'application/json' } })
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const path = String(input)
    if (init?.method === 'POST') mutations.push(path)
    if (path === '/api/site') return json({ participant: { did } })
    return json({ activeProvider: 'sqlite', activeDurable: true, changeRequiresRestart: true, firstRun: true, pendingRestart: false, providers: [{
      id: 'sqlite', name: 'SQLite', fields: [{ name: 'path', label: 'Database path', type: 'text', required: true, secret: false }],
    }] })
  }))
  const wrapper = mount(DatabasePanel, { props: { expectedDid: did } })
  await flushPromises()
  await wrapper.get('input[name="path"]').setValue('example.db')
  did = 'did:plc:other'
  await wrapper.get('form').trigger('submit')
  await flushPromises()
  expect(mutations).toEqual([])
  expect(wrapper.emitted('identityChanged')).toHaveLength(1)
  expect(wrapper.text()).toContain('signed-in account changed')
  expect(wrapper.get('fieldset').attributes('disabled')).toBeDefined()
})
