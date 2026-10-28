// @vitest-environment jsdom
import { afterEach, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { ref } from 'vue'
import ConversationPanel from '../src/components/ConversationPanel.vue'
import { createConversation } from '../src/composables/conversation'
import { useRouter } from '../src/stores/router'
import { useSite } from '../src/stores/site'

const wrappers: ReturnType<typeof mount>[] = []
afterEach(() => { wrappers.splice(0).forEach(wrapper => wrapper.unmount()); vi.unstubAllGlobals(); sessionStorage.clear(); window.history.replaceState({}, '', '/') })
function view(signedIn: boolean, isLocked = false, canWrite = false, presentation: 'conversation' | 'article' = 'conversation') {
  setActivePinia(createPinia()); sessionStorage.clear()
  window.history.replaceState({}, '', '/t/garden/topics/story?after=older#post-deep')
  vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({ items: [] }))))
  const site = useSite(); site.state = signedIn ? 'signedIn' : 'anonymous'
  site.site = { name: 'Space', established: true, signIn: '/sign-in', signOut: null, participant: signedIn ? { participantRef: 'alice', did: null, handle: 'alice.test', isOwner: false, joinedAt: '' } : null }
  const conversation = createConversation(useRouter())
  conversation.selectedTopic.value = { key: 'story', tangentKey: 'garden', title: 'Story', canRead: true, canWrite, isLocked, presentation }
  const moderation = { panelOpen: ref(false), stewardshipAvailable: ref(false), allowed: () => false }
  const wrapper = mount(ConversationPanel, { props: { conversation, moderation: moderation as never } }); wrappers.push(wrapper); return { wrapper, conversation }
}

it.each(['conversation', 'article'] as const)('explains signed-out %s replies and preserves the complete destination at sign-in', async presentation => {
  const { wrapper } = view(false, false, false, presentation); await flushPromises()
  const notice = wrapper.get('.reply-unavailable')
  expect(notice.text()).toContain('Sign in to see whether your account can reply')
  const link = new URL(notice.get('a').attributes('href')!, window.location.origin)
  expect(link.searchParams.get('return')).toBe('/t/garden/topics/story?after=older#post-deep')
  expect(wrapper.find('form.compose').exists()).toBe(false)
})

it.each([false, true])('explains locked replies without promising sign-in will unlock them; signedIn=%s', async signedIn => {
  const { wrapper } = view(signedIn, true); await flushPromises()
  expect(wrapper.get('.reply-unavailable').text()).toContain('Replies are paused')
  expect(wrapper.find('.reply-unavailable a').exists()).toBe(false); expect(wrapper.find('form.compose').exists()).toBe(false)
})

it('explains restricted replies and removes the notice when current permission enables composition', async () => {
  const { wrapper, conversation } = view(true); await flushPromises()
  expect(wrapper.get('.reply-unavailable').text()).toContain('Your current access does not allow replies here')
  expect(wrapper.text()).toContain('A Tangent steward')
  conversation.selectedTopic.value = { ...conversation.selectedTopic.value!, canWrite: true }; await flushPromises()
  expect(wrapper.find('.reply-unavailable').exists()).toBe(false); expect(wrapper.find('form.compose').exists()).toBe(true)
})

it('offers no reading or sign-in promise when the Topic itself is unreadable', async () => {
  const { wrapper, conversation } = view(false)
  conversation.selectedTopic.value = { ...conversation.selectedTopic.value!, canRead: false }; await flushPromises()
  expect(wrapper.find('.reply-unavailable').exists()).toBe(false)
})
