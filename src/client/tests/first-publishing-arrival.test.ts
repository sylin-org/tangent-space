// @vitest-environment jsdom
import { afterEach, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import FirstArrival from '../src/components/FirstArrival.vue'
import { useSite } from '../src/stores/site'
import { useRouter } from '../src/stores/router'
import { saveSetupArrival, readSetupArrival } from '../src/composables/onboarding-state'
afterEach(() => sessionStorage.clear())
it.each(['articles', 'discussions'])('makes the first contribution match the selected %s Tangent', async kind => {
  setActivePinia(createPinia()); sessionStorage.clear()
  const site = useSite(), router = useRouter()
  site.site = { name: 'Space', established: true, signIn: '', signOut: null, participant: { participantRef: 'owner', did: 'did:plc:owner', handle: 'owner.test', isOwner: true, joinedAt: '' } }
  saveSetupArrival('did:plc:owner', { key: 'journal', name: 'My journal' }); router.apply('/t/journal/topics')
  const wrapper = mount(FirstArrival, { props: { site, router, kind } })
  expect(wrapper.get('.btn-primary').text()).toBe(kind === 'articles' ? 'Write an article' : 'Start a conversation')
  expect(wrapper.text()).not.toContain('Connect an agent')
  await wrapper.get('.btn-primary').trigger('click'); expect(wrapper.emitted('start')).toHaveLength(1)
  expect(readSetupArrival('did:plc:owner')).toBeNull(); expect(wrapper.find('.first-arrival').exists()).toBe(false)
  wrapper.unmount()
})
