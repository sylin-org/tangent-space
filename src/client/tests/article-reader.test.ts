// @vitest-environment jsdom
import { expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { ref } from 'vue'
import ConversationPanel from '../src/components/ConversationPanel.vue'
import { createConversation, type Message } from '../src/composables/conversation'
import { useRouter } from '../src/stores/router'
import { useSite } from '../src/stores/site'

function post(id: string, text: string): Message {
  return { id, authorParticipantId: 'author', sequence: id === 'opener' ? 1 : 2, removed: false, editedAt: null, changeId: null,
    content: { text, createdAt: '2026-09-28T12:00:00Z', replyTo: null } }
}

it('mounts contextual actions only after their workspace host exists and removes them on navigation', async () => {
  setActivePinia(createPinia())
  useSite().site = { name: 'Space', established: true, signIn: '', signOut: null, participant: null }
  const conversation = createConversation(useRouter())
  conversation.selectedTopic.value = { key: 'essay', tangentKey: 'lab', title: 'A long essay', presentation: 'article', canRead: true, canWrite: false, isLocked: false }
  conversation.openingPost.value = post('opener', 'A real opening')
  conversation.messages.value = [conversation.openingPost.value]
  const moderation = { panelOpen: ref(false), stewardshipAvailable: ref(false), allowed: () => false }
  const wrapper = mount(ConversationPanel, { attachTo: document.body, props: { conversation, moderation: moderation as never } })
  await flushPromises()
  const host = document.createElement('div')
  host.id = 'topic-path-actions'
  document.body.append(host)
  await wrapper.setProps({ anchorPost: 'new-context' })
  await flushPromises()
  expect(host.querySelector('.topic-context-actions')).not.toBeNull()
  expect(wrapper.find('.article-context-bar').exists()).toBe(false)
  wrapper.unmount()
  expect(host.textContent).toBe('')
  host.remove()
})

it('renders the canonical opener once and keeps the bounded reply in discussion', async () => {
  setActivePinia(createPinia())
  sessionStorage.clear()
  useSite().site = { name: 'Space', established: true, signIn: '', signOut: null, participant: null }
  const conversation = createConversation(useRouter())
  conversation.selectedTopic.value = { key: 'essay', tangentKey: 'lab', title: 'A long essay', topic: 'A short introduction', presentation: 'article', canRead: true, canWrite: false, isLocked: false }
  const opener = post('opener', '## Beginning\n\nThe body')
  opener.content.format = 'markdown'
  opener.content.html = '<h2>Beginning</h2><p>The body</p>'
  const reply = post('reply', 'A response')
  conversation.openingPost.value = opener
  conversation.messages.value = [opener, reply]
  conversation.resolved.value = { author: { displayName: 'Writer' } }
  const moderation = { panelOpen: ref(false), stewardshipAvailable: ref(false), allowed: () => false }
  const wrapper = mount(ConversationPanel, { props: { conversation, moderation: moderation as never, tangentName: 'The Lab' } })
  await flushPromises()
  expect(wrapper.find('.article-reading').attributes('id')).toBe('opener')
  expect(wrapper.findAll('.article-reading .post-content')).toHaveLength(1)
  expect(wrapper.findAll('.messages > li').map(node => node.attributes('id'))).toEqual(['reply'])
  expect(wrapper.find('#article-heading-1').text()).toBe('Beginning')
  expect(wrapper.find('.article-outline a[href="#article-heading-1"]').exists()).toBe(true)
  expect(wrapper.find('.article-context-bar').exists()).toBe(true)
  expect(wrapper.find('.article-head .eyebrow').exists()).toBe(false)
  expect(wrapper.find('.article-audience-badge').text()).toBe('Admitted')
  expect(wrapper.findAll('.article-discussion-jump')).toHaveLength(1)
  expect(wrapper.find('.article-discussion-head').text()).toContain('Replies')
  wrapper.unmount()
})

it('numbers every heading like the public reader and restores an explicit heading fragment after the opener arrives', async () => {
  window.history.replaceState({}, '', '/t/lab/opener#article-heading-3')
  const scroll = vi.fn()
  const originalScrollIntoView = HTMLElement.prototype.scrollIntoView
  const originalAnimationFrame = globalThis.requestAnimationFrame
  HTMLElement.prototype.scrollIntoView = scroll
  globalThis.requestAnimationFrame = callback => { callback(0); return 1 }
  try {
    setActivePinia(createPinia())
    sessionStorage.clear()
    useSite().site = { name: 'Space', established: true, signIn: '', signOut: null, participant: null }
    const conversation = createConversation(useRouter())
    conversation.selectedTopic.value = { key: 'essay', tangentKey: 'lab', title: 'A long essay', presentation: 'article', canRead: true, canWrite: false, isLocked: false }
    const moderation = { panelOpen: ref(false), stewardshipAvailable: ref(false), allowed: () => false }
    const wrapper = mount(ConversationPanel, { attachTo: document.body, props: { conversation, moderation: moderation as never, anchorPost: 'opener' } })
    const opener = post('opener', '# Title\n\n## First\n\n#### Second\n\n##### Detail')
    opener.content.format = 'markdown'
    opener.content.html = '<h1>Title</h1><h2>First</h2><h4>Second</h4><h5>Detail</h5>'
    conversation.openingPost.value = opener
    conversation.messages.value = [opener]
    await flushPromises()
    expect(wrapper.findAll('.article-prose h1, .article-prose h2, .article-prose h4, .article-prose h5').map(node => node.attributes('id')))
      .toEqual(['article-heading-1', 'article-heading-2', 'article-heading-3', 'article-heading-4'])
    expect(wrapper.findAll('.article-outline a').map(node => node.attributes('href')))
      .toEqual(['#article-heading-2', '#article-heading-3'])
    expect(scroll).toHaveBeenCalledOnce()
    expect(scroll.mock.instances[0]).toBe(wrapper.find('#article-heading-3').element)
    wrapper.unmount()
  } finally {
    HTMLElement.prototype.scrollIntoView = originalScrollIntoView
    globalThis.requestAnimationFrame = originalAnimationFrame
    window.history.replaceState({}, '', '/')
  }
})

it('never promotes a later reply when the opener is unavailable', async () => {
  setActivePinia(createPinia())
  sessionStorage.clear()
  useSite().site = { name: 'Space', established: true, signIn: '', signOut: null, participant: null }
  const conversation = createConversation(useRouter())
  conversation.selectedTopic.value = { key: 'essay', tangentKey: 'lab', title: 'A long essay', presentation: 'article', canRead: true, canWrite: false, isLocked: false }
  conversation.messages.value = [post('deep-reply', 'A response')]
  const moderation = { panelOpen: ref(false), stewardshipAvailable: ref(false), allowed: () => false }
  const wrapper = mount(ConversationPanel, { props: { conversation, moderation: moderation as never } })
  await flushPromises()
  expect(wrapper.find('.article-reading').exists()).toBe(false)
  expect(wrapper.find('.article-unavailable').text()).toContain('opening Post is unavailable')
  expect(wrapper.findAll('.messages > li').map(node => node.attributes('id'))).toEqual(['deep-reply'])
  wrapper.unmount()
})

it('shows author editing beside the article title and keeps an open edit intact', async () => {
  setActivePinia(createPinia())
  sessionStorage.clear()
  useSite().site = { name: 'Space', established: true, signIn: '', signOut: null, participant: { participantRef: 'author', did: null, handle: null, isOwner: false, joinedAt: '' } }
  const conversation = createConversation(useRouter())
  conversation.selectedTopic.value = { key: 'essay', tangentKey: 'lab', title: 'A long essay', presentation: 'article', canRead: true, canWrite: true, isLocked: false, allowPostEditing: true }
  const opener = post('opener', 'Original article text')
  opener.permissions = { allowedActions: ['editOwnPost'] }
  conversation.openingPost.value = opener
  conversation.messages.value = [opener]
  const moderation = { panelOpen: ref(false), stewardshipAvailable: ref(false), allowed: () => false }
  const wrapper = mount(ConversationPanel, { props: { conversation, moderation: moderation as never } })
  const edit = wrapper.find('.article-edit-trigger')
  expect(edit.exists()).toBe(true)
  expect(edit.attributes('disabled')).toBeUndefined()
  expect(wrapper.find('.article-post-actions').text()).not.toContain('Reply to this Post')
  expect(wrapper.find('.article-post-actions').text()).not.toContain('Edit post')
  await edit.trigger('click')
  await wrapper.find('#article-edit').setValue('An unfinished revision')
  await edit.trigger('click')
  expect((wrapper.find('#article-edit').element as HTMLTextAreaElement).value).toBe('An unfinished revision')
  conversation.selectedTopic.value = { ...conversation.selectedTopic.value!, allowPostEditing: false }
  await flushPromises()
  expect(edit.attributes('disabled')).toBeDefined()
  expect(wrapper.text()).toContain('Editing is turned off for this article.')
  expect(wrapper.find('.message-edit-form button[type="submit"]').attributes('disabled')).toBeDefined()
  wrapper.unmount()
})

it('offers direct editing on the author’s ordinary Post and no Edit to another viewer', async () => {
  setActivePinia(createPinia())
  sessionStorage.clear()
  useSite().site = { name: 'Space', established: true, signIn: '', signOut: null, participant: { participantRef: 'author', did: null, handle: null, isOwner: false, joinedAt: '' } }
  const conversation = createConversation(useRouter())
  conversation.selectedTopic.value = { key: 'chat', tangentKey: 'lab', title: 'Chat', presentation: 'conversation', canRead: true, canWrite: true, isLocked: false, allowPostEditing: true }
  const message = post('one', 'Hello')
  message.permissions = { allowedActions: ['editOwnPost'] }
  conversation.messages.value = [message]
  const moderation = { panelOpen: ref(false), stewardshipAvailable: ref(false), allowed: () => false }
  const wrapper = mount(ConversationPanel, { props: { conversation, moderation: moderation as never } })
  expect(wrapper.find('.message-edit-trigger').exists()).toBe(true)
  await wrapper.find('.message-edit-trigger').trigger('click')
  expect(wrapper.find('.message-edit-form textarea').exists()).toBe(true)
  useSite().site!.participant!.participantRef = 'another'
  await flushPromises()
  expect(wrapper.find('.message-edit-trigger').exists()).toBe(false)
  wrapper.unmount()
})

it('keeps an unsent opener edit available when discussion history moves beyond the opener', async () => {
  setActivePinia(createPinia())
  sessionStorage.clear()
  useSite().site = { name: 'Space', established: true, signIn: '', signOut: null,
    participant: { participantRef: 'author', did: 'did:example:author', displayName: 'Writer', handle: null, isOwner: false, joinedAt: '' } }
  const conversation = createConversation(useRouter())
  conversation.selectedTopic.value = { key: 'essay', tangentKey: 'lab', title: 'Essay', presentation: 'article', canRead: true, canWrite: true, isLocked: false, allowPostEditing: true }
  const opener = post('opener', 'Original body')
  opener.permissions = { allowedActions: ['editOwnPost'] }
  conversation.openingPost.value = opener
  conversation.messages.value = [opener]
  const moderation = { panelOpen: ref(false), stewardshipAvailable: ref(false), allowed: () => false }
  const wrapper = mount(ConversationPanel, { props: { conversation, moderation: moderation as never } })
  await wrapper.find('.article-edit-trigger').trigger('click')
  await wrapper.find('.article-reading textarea').setValue('Unsent revision')
  conversation.selectedTopic.value = { ...conversation.selectedTopic.value!, presentation: 'conversation' }
  conversation.openingPost.value = null
  conversation.messages.value = [post('later', 'Later reply')]
  await flushPromises()
  expect((wrapper.find('.article-detached-edit textarea').element as HTMLTextAreaElement).value).toBe('Unsent revision')
  expect(wrapper.find('.article-reading').exists()).toBe(false)
  wrapper.unmount()
})
