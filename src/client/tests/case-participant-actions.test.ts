// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia } from 'pinia'
import CaseParticipantActions from '../src/components/CaseParticipantActions.vue'

afterEach(() => { vi.unstubAllGlobals() })
describe('case participant actions', () => {
  it('confirms a timeout with the exact case evidence and shows the persisted outcome', async () => {
    const fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ action: 'timeout', until: '2027-01-01T00:00:00Z' }), { status: 200 }))
    vi.stubGlobal('fetch', fetch)
    const wrapper = mount(CaseParticipantActions, { props: { caseRef: 'space::tangent::topic::case_abc', participant: { participantRef: 'person-7', displayName: 'Pat', actions: ['timeout'] } }, global: { plugins: [createPinia()] } })
    expect(wrapper.text()).not.toContain('Ban from this Tangent')
    await wrapper.find('button').trigger('click')
    await wrapper.find('textarea').setValue('A short pause after reviewing the reports.')
    await wrapper.find('form').trigger('submit')
    await flushPromises()
    expect(fetch).toHaveBeenCalledTimes(1)
    expect(JSON.parse(fetch.mock.calls[0]?.[1].body)).toEqual({ action: 'timeout', reason: 'A short pause after reviewing the reports.', durationSeconds: 86400, caseRef: 'space::tangent::topic::case_abc' })
    expect(wrapper.text()).toContain('Time out applied until')
    expect(wrapper.emitted('changed')).toHaveLength(1)
    await wrapper.setProps({ participant: { participantRef: 'person-7', displayName: 'Pat', actions: ['timeout'] } })
    expect(wrapper.text()).toContain('Time out applied until')
    await wrapper.setProps({ caseRef: 'space::tangent::topic::case_other' })
    expect(wrapper.text()).not.toContain('Time out applied until')
  })
})
