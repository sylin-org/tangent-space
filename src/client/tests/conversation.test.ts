// @vitest-environment jsdom
import { beforeEach, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import { createConversation, type Message } from '../src/composables/conversation'
import { useRouter } from '../src/stores/router'
import { useSite } from '../src/stores/site'

const post = (id: string): Message => ({ id, authorParticipantId: 'author', sequence: 1, removed: false, editedAt: null, changeId: null, content: { text: id, createdAt: '2026-09-26', replyTo: null } })
const json = (data: unknown) => new Response(JSON.stringify(data), { headers: { 'Content-Type': 'application/json' } })

beforeEach(() => {
  setActivePinia(createPinia())
  sessionStorage.clear()
  useSite().site = { name: 'Space', established: true, signIn: '', signOut: null, participant: { participantRef: 'alice', did: null, handle: null, isOwner: false, joinedAt: '' } }
})

it('refreshes one bounded page and leaves new Posts for an explicit continuation', async () => {
  const fetch = vi.fn(async (path: RequestInfo | URL) => json({
    messages: [post(String(path).includes('resume') ? 'new' : 'first')],
    nextCursor: null, resumeCursor: 'resume', boundary: 1,
  }))
  vi.stubGlobal('fetch', fetch)
  const conversation = createConversation(useRouter())
  conversation.selectedTopic.value = { key: 'topic', tangentKey: 'tangent', title: 'Topic', canRead: true, canWrite: true, isLocked: false }
  await conversation.refreshOpenHistory()
  expect(fetch).toHaveBeenCalledTimes(1)
  await conversation.historyPage('resume', undefined, 'topic', false, { isCurrent: () => true })
  expect(conversation.messages.value.map(item => item.id)).toEqual(['first'])
  expect(conversation.newMessages.value).toBe(true)
  await conversation.readMore()
  expect(conversation.messages.value.map(item => item.id)).toEqual(['new'])
  expect(conversation.newMessages.value).toBe(false)
})

it('isolates saved drafts by participant and retains the selected reply on publication', async () => {
  const writes: Record<string, unknown>[] = []
  vi.stubGlobal('fetch', vi.fn(async (path: RequestInfo | URL, init?: RequestInit) => {
    if (init?.method === 'POST') {
      writes.push(JSON.parse(String(init.body)))
      return json({ state: 'accepted' })
    }
    if (String(path).includes('/posts')) return json({ messages: [], nextCursor: null, resumeCursor: 'resume', boundary: 0 })
    return json({ key: 'topic', tangentKey: 'tangent', title: 'Topic', canRead: true, canWrite: true, isLocked: false })
  }))
  const router = useRouter()
  router.apply('/t/tangent/topics/topic')
  const conversation = createConversation(router)
  await conversation.choose('topic')
  conversation.draft.value = 'Alice draft'
  await nextTick()
  conversation.reset()
  useSite().site!.participant!.participantRef = 'bob'
  await conversation.choose('topic')
  expect(conversation.draft.value).toBe('')
  conversation.draft.value = 'Bob reply'
  conversation.beginReply(post('parent'))
  await conversation.send()
  expect(writes[0]).toMatchObject({ text: 'Bob reply', replyTo: 'parent' })
  expect(sessionStorage.getItem('draft:alice:topic')).toContain('Alice draft')
})
