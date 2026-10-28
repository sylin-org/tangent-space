// @vitest-environment jsdom
import { afterEach, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import ParticipantActions from '../src/components/ParticipantActions.vue'
import { clearRolesSession } from '../src/composables/roles-api'

afterEach(() => { clearRolesSession(); vi.unstubAllGlobals() })

it('allows a steward to lift a restriction after the preceding action refreshes permissions', async () => {
  let restricted = false
  const actions: string[] = []
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const json = (body: unknown) => new Response(JSON.stringify(body), { status: 200 })
    if (String(input) === '/api/roles/ui/session') return json({ headerName: 'X-CSRF', requestToken: 'fixture-token' })
    if (init?.method === 'POST') {
      const action = JSON.parse(String(init.body)).action
      actions.push(action)
      restricted = action === 'ban'
      return json({ saved: true })
    }
    return json({ actions: ['ban', 'lift'], scopeActions: [], restriction: restricted ? { kind: 'Ban', active: true, reason: 'Test' } : null })
  }))
  const wrapper = mount(ParticipantActions, { props: { identifier: 'pat.example', label: 'Pat', actorKey: 'owner', tangent: 'garden' } })
  await wrapper.find('button').trigger('click')
  await flushPromises()
  await wrapper.findAll('button').find(button => button.text() === 'Ban from this Tangent')!.trigger('click')
  await wrapper.find('form').trigger('submit')
  await flushPromises()
  expect(wrapper.text()).toContain('Banned from this Tangent')
  await wrapper.findAll('button').find(button => button.text() === 'Lift restriction')!.trigger('click')
  expect(wrapper.find('button[type="submit"]').attributes('disabled')).toBeUndefined()
  await wrapper.find('form').trigger('submit')
  await flushPromises()
  expect(actions).toEqual(['ban', 'lift'])
  expect(wrapper.text()).not.toContain('Banned from this Tangent')
})
