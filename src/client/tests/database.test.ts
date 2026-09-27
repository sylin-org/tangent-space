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
  const wrapper = mount(DatabasePanel)
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
  expect(activations).toEqual([{ testId: 'tested-2' }])
  expect(wrapper.text()).toContain('Restart the server')
})
