// @vitest-environment jsdom
import { beforeEach, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import { createConversation, type Message } from '../src/composables/conversation'
import { useRouter } from '../src/stores/router'
import { useSite } from '../src/stores/site'
import { clearAllFacets, recordFacet } from '../src/composables/facets'

const post = (id: string): Message => ({ id, authorParticipantId: 'author', sequence: 1, removed: false, editedAt: null, changeId: null, content: { text: id, createdAt: '2026-09-26', replyTo: null } })
const json = (data: unknown) => new Response(JSON.stringify(data), { headers: { 'Content-Type': 'application/json' } })

beforeEach(() => {
  setActivePinia(createPinia())
  sessionStorage.clear()
  clearAllFacets()
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

it('returns to a deep Post window in a Topic while preserving the unsent reply', async () => {
  const topic = { key: 'topic', tangentKey: 'tangent', title: 'Topic', canRead: true, canWrite: true, isLocked: false }
  const fetch = vi.fn(async (path: RequestInfo | URL) => String(path).endsWith('/posts/deep-post')
    ? json({ topic, window: { messages: [post('deep-post')], nextCursor: 'later', resumeCursor: 'resume', boundary: 800 } })
    : json(topic))
  vi.stubGlobal('fetch', fetch)
  const router = useRouter()
  router.apply('/t/tangent/topics/topic')
  sessionStorage.setItem('draft:alice:topic', JSON.stringify({ text: 'Reply in progress', replyTo: 'deep-post' }))
  const conversation = createConversation(router)
  await conversation.choose('topic', 'deep-post')
  expect(conversation.messages.value.map(item => item.id)).toEqual(['deep-post'])
  expect(conversation.draft.value).toBe('Reply in progress')
  expect(conversation.replyTo.value?.id).toBe('deep-post')
  expect(conversation.nextCursor).toBe('later')
  expect(fetch.mock.calls.map(call => String(call[0]))).not.toContain('/api/topics/topic/posts?from=start')
})

it('keeps the canonical Article opener separate from a deep reply window', async () => {
  const topic = { key: 'topic', tangentKey: 'tangent', title: 'Article', presentation: 'article', canRead: true, canWrite: true, isLocked: false }
  const opener = post('opener')
  opener.content.text = 'The original article'
  const reply = post('deep-reply')
  vi.stubGlobal('fetch', vi.fn(async () => json({ topic, window: { messages: [reply], openingPost: opener, nextCursor: 'later', resumeCursor: 'resume', boundary: 400 } })))
  const router = useRouter()
  router.apply('/t/tangent/deep-reply')
  const conversation = createConversation(router)
  await conversation.openPost('tangent', 'deep-reply')
  expect(conversation.selectedTopic.value?.presentation).toBe('article')
  expect(conversation.openingPost.value?.id).toBe('opener')
  expect(conversation.messages.value.map(message => message.id)).toEqual(['deep-reply'])
})

it('does not render a saved anchor from a different Topic under the current Topic URL', async () => {
  const topic = { key: 'topic', tangentKey: 'tangent', title: 'Topic', canRead: true, canWrite: true, isLocked: false }
  vi.stubGlobal('fetch', vi.fn(async (path: RequestInfo | URL) => String(path).includes('/posts/')
    ? json({ topic: { ...topic, key: 'other' }, window: { messages: [post('other-post')] } })
    : json(topic)))
  const router = useRouter()
  router.apply('/t/tangent/topics/topic')
  const conversation = createConversation(router)
  await conversation.choose('topic', 'other-post')
  expect(conversation.messages.value).toEqual([])
  expect(conversation.error.value).toContain('does not belong')
})

it('isolates saved drafts by participant and retains the selected reply on publication', async () => {
  const reads: string[] = []
  const writePaths: string[] = []
  const writes: Record<string, unknown>[] = []
  vi.stubGlobal('fetch', vi.fn(async (path: RequestInfo | URL, init?: RequestInit) => {
    if (init?.method === 'POST') {
      writePaths.push(String(path))
      writes.push(JSON.parse(String(init.body)))
      return json({ postRef: 'http://localhost:3000::tangent::topic::new-post' })
    }
    reads.push(String(path))
    if (String(path).includes('/posts')) return json({ messages: [], nextCursor: null, resumeCursor: 'resume', boundary: 0 })
    return json({ key: 'topic', tangentKey: 'tangent', title: 'Topic', canRead: true, canWrite: true, isLocked: false })
  }))
  const router = useRouter()
  router.apply('/t/tangent/topics/topic')
  const conversation = createConversation(router)
  await conversation.choose('topic')
  expect(reads).toContain('/api/v1/tangents/tangent/topics/topic')
  conversation.draft.value = 'Alice draft'
  await nextTick()
  conversation.reset()
  useSite().site!.participant!.participantRef = 'bob'
  await conversation.choose('topic')
  expect(conversation.draft.value).toBe('')
  conversation.draft.value = 'Bob reply'
  conversation.beginReply(post('parent'))
  await conversation.send()
  expect(writes[0]).toMatchObject({ text: 'Bob reply', replyTo: 'http://localhost:3000::tangent::topic::parent' })
  expect(writes[0]).toHaveProperty('requestId')
  expect(writePaths).toEqual(['/api/v1/experience/topics/topic/posts'])
  expect(sessionStorage.getItem('draft:alice:topic')).toContain('Alice draft')
})

it('sends edits and removals without an unused replay identity', async () => {
  const writes: Array<{ path: string; method: string; body?: string }> = []
  vi.stubGlobal('fetch', vi.fn(async (path: RequestInfo | URL, init?: RequestInit) => {
    if (init?.method === 'PATCH' || init?.method === 'DELETE') {
      writes.push({ path: String(path), method: init.method, body: init.body?.toString() })
      return json({ postRef: 'http://localhost:3000::tangent::topic::one' })
    }
    return json({ messages: [], nextCursor: null, resumeCursor: 'resume', boundary: 0 })
  }))
  const conversation = createConversation(useRouter())
  conversation.selectedTopic.value = { key: 'topic', tangentKey: 'tangent', title: 'Topic', canRead: true, canWrite: true, isLocked: false }
  const message = post('one')
  await conversation.saveEdit(message, 'New words')
  await conversation.removeMessage(message)
  expect(writes).toEqual([
    { path: '/api/v1/experience/posts/one', method: 'PATCH', body: '{"text":"New words","format":"plain"}' },
    { path: '/api/v1/experience/posts/one', method: 'DELETE', body: undefined },
  ])
})

it('retains a long Markdown draft and sends its format with the full text', async () => {
  const writes: Record<string, unknown>[] = []
  vi.stubGlobal('fetch', vi.fn(async (_path: RequestInfo | URL, init?: RequestInit) => {
    if (init?.method === 'POST') {
      writes.push(JSON.parse(String(init.body)))
      return json({ postRef: 'http://localhost:3000::tangent::topic::new-post' })
    }
    return json({ messages: [], nextCursor: null, resumeCursor: null, boundary: 0 })
  }))
  const conversation = createConversation(useRouter())
  conversation.selectedTopic.value = { key: 'topic', tangentKey: 'tangent', title: 'Topic', canRead: true, canWrite: true, isLocked: false }
  conversation.draft.value = '# An article\n\n' + 'Body paragraph. '.repeat(500)
  conversation.draftFormat.value = 'markdown'
  await nextTick()
  expect(JSON.parse(sessionStorage.getItem('draft:alice:topic')!).text).toBe(conversation.draft.value)
  expect(JSON.parse(sessionStorage.getItem('draft:alice:topic')!).format).toBe('markdown')
  await conversation.send()
  expect(writes[0]).toMatchObject({ text: '# An article\n\n' + 'Body paragraph. '.repeat(500), format: 'markdown' })
  expect(writes[0]).not.toHaveProperty('facets')
})

it('preserves Markdown format while editing a long post', async () => {
  const writes: Record<string, unknown>[] = []
  vi.stubGlobal('fetch', vi.fn(async (_path: RequestInfo | URL, init?: RequestInit) => {
    if (init?.method === 'PATCH') {
      writes.push(JSON.parse(String(init.body)))
      return json({ postRef: 'http://localhost:3000::tangent::topic::one' })
    }
    return json({ messages: [], nextCursor: null, resumeCursor: null, boundary: 0 })
  }))
  const conversation = createConversation(useRouter())
  conversation.selectedTopic.value = { key: 'topic', tangentKey: 'tangent', title: 'Topic', canRead: true, canWrite: true, isLocked: false }
  const message = post('one')
  message.content.format = 'markdown'
  const text = '## Revised\n\n' + 'Long passage. '.repeat(500)
  expect(await conversation.saveEdit(message, text)).toBe(true)
  expect(writes).toEqual([{ text, format: 'markdown' }])
})

it('keeps the new conversation draft when an old send confirms after navigation', async () => {
  let finishPost!: (response: Response) => void
  vi.stubGlobal('fetch', vi.fn(async (path: RequestInfo | URL, init?: RequestInit) => {
    if (init?.method === 'POST') return new Promise<Response>(resolve => { finishPost = resolve })
    if (String(path).endsWith('/posts')) return json({ messages: [], nextCursor: null, resumeCursor: null, boundary: 0 })
    const key = String(path).split('/').at(-1)
    return json({ key, tangentKey: 'tangent', title: key, canRead: true, canWrite: true, isLocked: false })
  }))
  const router = useRouter()
  router.apply('/t/tangent/topics/first')
  const conversation = createConversation(router)
  await conversation.choose('first')
  conversation.draft.value = 'First draft'
  const sending = conversation.send()
  router.apply('/t/tangent/topics/second')
  await conversation.choose('second')
  conversation.draft.value = 'Second draft'
  finishPost(json({ postRef: 'http://localhost:3000::tangent::first::posted' }))
  await sending
  expect(conversation.selectedTopic.value?.key).toBe('second')
  expect(conversation.draft.value).toBe('Second draft')
  expect(sessionStorage.getItem('draft:alice:second')).toContain('Second draft')
})

it('lets an author correct a definitively rejected post', async () => {
  const writes: string[] = []
  vi.stubGlobal('fetch', vi.fn(async (_path: RequestInfo | URL, init?: RequestInit) => {
    if (init?.method === 'POST') {
      writes.push(String(init.body))
      return writes.length === 1
        ? new Response(JSON.stringify({ reason: 'Please revise the post.' }), { status: 400 })
        : json({ postRef: 'http://localhost:3000::tangent::topic::posted' })
    }
    return json({ messages: [], nextCursor: null, resumeCursor: null, boundary: 0 })
  }))
  const conversation = createConversation(useRouter())
  conversation.selectedTopic.value = { key: 'topic', tangentKey: 'tangent', title: 'Topic', canRead: true, canWrite: true, isLocked: false }
  conversation.draft.value = 'First wording'
  await conversation.send()
  conversation.draft.value = 'Revised wording'
  await conversation.send()
  expect(writes).toHaveLength(2)
  expect(JSON.parse(writes[1]!).text).toBe('Revised wording')
})


it('reads anonymous public windows with bounded before/after navigation and cached author identity', async () => {
  useSite().site!.participant = null
  window.history.replaceState({}, '', '/t/tangent/topics/topic?before=older-post#post-visible')
  const windowData = { topic: { key: 'topic', tangentKey: 'tangent', title: 'Public topic', presentation: 'conversation' },
    posts: [{ id: 'visible', author: { ref: 'did:example:human', label: 'A person', classification: 'Human', avatar: '/api/profile-cache/avatar?id=one', profileUrl: '/u/person.test' }, text: 'Visible page', createdAt: '2026-09-26', removed: false, sourceUri: 'at://did:example:human/app.bsky.feed.post/visible' }],
    olderBefore: 'visible', newerAfter: 'visible' }
  const fetch = vi.fn(async (_path: RequestInfo | URL) => json(windowData))
  vi.stubGlobal('fetch', fetch)
  const router = useRouter()
  const conversation = createConversation(router)
  await conversation.choose('topic')
  expect(fetch).toHaveBeenCalledTimes(1)
  expect(String(fetch.mock.calls[0]![0])).toBe('/api/v1/public/tangents/tangent/topics/topic/posts?before=older-post')
  expect(conversation.messages.value.map(item => item.id)).toEqual(['visible'])
  expect(conversation.selectedTopic.value?.canRead).toBe(true)
  expect(conversation.canWrite).toBe(false)
  expect(conversation.publicOlderBefore.value).toBe('visible')
  expect(conversation.publicNewerAfter.value).toBe('visible')
  expect(conversation.resolved.value['did:example:human']?.profileUrl).toBe('/u/person.test')
  expect(conversation.messages.value[0]?.sourceUri).toBe('at://did:example:human/app.bsky.feed.post/visible')
  await conversation.refreshOpenHistory(true)
  expect(String(fetch.mock.calls[1]![0])).toContain('?before=older-post')
})

it('resolves an anonymous Post permalink without using signed-in endpoints', async () => {
  useSite().site!.participant = null
  const fetch = vi.fn(async (_path: RequestInfo | URL) => json({ anchorId: 'deep', window: {
    topic: { key: 'topic', tangentKey: 'tangent', title: 'Article', presentation: 'article' },
    posts: [{ id: 'deep', author: { ref: 'author', label: 'Author' }, text: 'A deep reply', createdAt: '2026-09-26', removed: false }],
    openingPost: { id: 'opener', author: { ref: 'author', label: 'Author', profileUrl: '/u/author.test' }, text: 'The actual article', createdAt: '2026-09-25', removed: false, sourceUri: 'at://did:example:author/app.bsky.feed.post/opener' },
    olderBefore: 'deep', newerAfter: null,
  } }))
  vi.stubGlobal('fetch', fetch)
  const conversation = createConversation(useRouter())
  await conversation.openPost('tangent', 'deep')
  expect(String(fetch.mock.calls[0]![0])).toBe('/api/v1/public/tangents/tangent/posts/deep')
  expect(conversation.openingPost.value?.content.text).toBe('The actual article')
  expect(conversation.openingPost.value?.sourceUri).toBe('at://did:example:author/app.bsky.feed.post/opener')
  expect(conversation.resolved.value.author?.profileUrl).toBe('/u/author.test')
  expect(conversation.messages.value.map(item => item.id)).toEqual(['deep'])
  expect(conversation.canWrite).toBe(false)
})

it('replays the exact accepted intent after a lost response and reload, including facets and reply target', async () => {
  const bodies: Record<string, unknown>[] = [], accepted = new Map<string, string>()
  const topic = { key: 'topic', tangentKey: 'tangent', title: 'Topic', canRead: true, canWrite: true, isLocked: false }
  vi.stubGlobal('fetch', vi.fn(async (path: RequestInfo | URL, init?: RequestInit) => {
    if (init?.method === 'POST') {
      const body = JSON.parse(String(init.body)); bodies.push(body)
      expect(sessionStorage.getItem('post-intent:' + JSON.stringify(['alice', 'tangent', 'topic']))).toContain(body.requestId)
      accepted.set(body.requestId, 'posted')
      if (bodies.length === 1) throw new TypeError('Synthetic response lost after acceptance')
      return json({ postRef: 'http://localhost:3000::tangent::topic::posted' })
    }
    return String(path).includes('/posts') ? json({ messages: [], nextCursor: null, resumeCursor: null, boundary: 0 }) : json(topic)
  }))
  let router = useRouter(); router.apply('/t/tangent/topics/topic')
  let conversation = createConversation(router); await conversation.choose('topic')
  conversation.draft.value = '@someone Hello'; conversation.beginReply(post('parent'))
  recordFacet('topic', conversation.draft.value, 0, { kind: 'mention', insert: 'someone', label: 'Someone', did: 'did:plc:someone' }, '@someone')
  await conversation.send()
  expect(conversation.pendingPost.value).not.toBeNull()
  conversation.reset(); clearAllFacets(); setActivePinia(createPinia())
  useSite().site = { name: 'Space', established: true, signIn: '', signOut: null, participant: { participantRef: 'alice', did: null, handle: null, isOwner: false, joinedAt: '' } }
  router = useRouter(); router.apply('/t/tangent/topics/topic')
  conversation = createConversation(router); await conversation.choose('topic')
  expect(bodies).toHaveLength(1)
  expect(conversation.pendingPost.value?.body.replyTo).toBe('http://localhost:3000::tangent::topic::parent')
  await conversation.send()
  expect(bodies[1]).toEqual(bodies[0]); expect(accepted.size).toBe(1)
  expect(conversation.pendingPost.value).toBeNull()
})

it('keeps uncertainty after a later denial and confirms the original without overwriting edited text or format', async () => {
  const bodies: Record<string, unknown>[] = []
  vi.stubGlobal('fetch', vi.fn(async (_path: RequestInfo | URL, init?: RequestInit) => {
    if (init?.method !== 'POST') return json({ messages: [], nextCursor: null, resumeCursor: null, boundary: 0 })
    const body = JSON.parse(String(init.body)); bodies.push(body)
    if (bodies.length === 1) throw new TypeError('Response lost')
    if (bodies.length === 2) return new Response('{}', { status: 403 })
    return json({ postRef: 'http://localhost:3000::tangent::topic::posted' })
  }))
  const conversation = createConversation(useRouter())
  conversation.selectedTopic.value = { key: 'topic', tangentKey: 'tangent', title: 'Topic', canRead: true, canWrite: true, isLocked: false }
  conversation.draft.value = 'Original'; await conversation.send(); await conversation.send()
  expect(conversation.pendingPost.value).not.toBeNull()
  conversation.draft.value = 'Edited draft'; conversation.draftFormat.value = 'markdown'; await nextTick()
  await conversation.send(); expect(bodies).toHaveLength(2)
  await conversation.retryPendingPost()
  expect(bodies).toHaveLength(3); expect(bodies.every(body => body.requestId === bodies[0]!.requestId)).toBe(true)
  expect(bodies[2]).toEqual(bodies[0]); expect(conversation.draft.value).toBe('Edited draft')
  expect(conversation.draftFormat.value).toBe('markdown')
  expect(JSON.parse(sessionStorage.getItem('draft:alice:topic')!).text).toBe('Edited draft')
  expect(conversation.pendingPost.value).toBeNull()
})

it('isolates saved uncertainty by author and destination and retires a late confirmed draft for its original author', async () => {
  let complete!: (response: Response) => void
  const writes: Record<string, unknown>[] = []
  vi.stubGlobal('fetch', vi.fn(async (path: RequestInfo | URL, init?: RequestInit) => {
    if (init?.method === 'POST') { writes.push(JSON.parse(String(init.body))); return new Promise<Response>(resolve => { complete = resolve }) }
    return String(path).includes('/posts') ? json({ messages: [], nextCursor: null, resumeCursor: null, boundary: 0 })
      : json({ key: String(path).split('/').at(-1), tangentKey: 'tangent', title: 'Topic', canRead: true, canWrite: true, isLocked: false })
  }))
  const router = useRouter(); router.apply('/t/tangent/topics/topic')
  const conversation = createConversation(router); await conversation.choose('topic')
  conversation.draft.value = 'Alice submitted'; const operation = conversation.send()
  conversation.reset(); useSite().site!.participant!.participantRef = 'bob'
  await conversation.choose('topic'); expect(conversation.pendingPost.value).toBeNull()
  conversation.draft.value = 'Bob draft'; await nextTick()
  complete(json({ postRef: 'http://localhost:3000::tangent::topic::posted' })); await operation
  expect(conversation.draft.value).toBe('Bob draft')
  expect(sessionStorage.getItem('draft:alice:topic')).toBeNull()
  expect(sessionStorage.getItem('draft:bob:topic')).toContain('Bob draft')
  expect(sessionStorage.getItem('post-intent:' + JSON.stringify(['alice', 'tangent', 'topic']))).toBeNull()
})

it.each([false, true])('reconciles a late acceptance after leaving and returning to the same Topic, preserving edits=%s', async edited => {
  let finish!: (response: Response) => void
  const writes: Record<string, unknown>[] = []
  vi.stubGlobal('fetch', vi.fn(async (path: RequestInfo | URL, init?: RequestInit) => {
    if (init?.method === 'POST') { writes.push(JSON.parse(String(init.body))); return new Promise<Response>(resolve => { finish = resolve }) }
    return String(path).includes('/posts') ? json({ messages: [], nextCursor: null, resumeCursor: null, boundary: 0 })
      : json({ key: String(path).split('/').at(-1), tangentKey: 'tangent', title: 'Topic', canRead: true, canWrite: true, isLocked: false })
  }))
  const router = useRouter(); router.apply('/t/tangent/topics/first')
  const conversation = createConversation(router); await conversation.choose('first')
  conversation.draft.value = 'Submitted in first'; const sending = conversation.send()
  router.apply('/t/tangent/topics/second'); await conversation.choose('second')
  router.apply('/t/tangent/topics/first'); await conversation.choose('first')
  expect(conversation.draft.value).toBe('Submitted in first')
  expect(conversation.pendingPost.value).not.toBeNull()
  if (edited) { conversation.draft.value = 'Newer edit'; await nextTick() }
  const path = router.route
  finish(json({ postRef: 'http://localhost:3000::tangent::first::posted' })); await sending
  expect(router.route).toEqual(path)
  expect(conversation.pendingPost.value).toBeNull()
  expect(conversation.draft.value).toBe(edited ? 'Newer edit' : '')
  if (!edited) { await conversation.send(); expect(writes).toHaveLength(1) }
  else expect(sessionStorage.getItem('draft:alice:first')).toContain('Newer edit')
})
