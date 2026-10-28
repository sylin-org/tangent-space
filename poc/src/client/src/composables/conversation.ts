import { httpRequest } from '../api'
import { ref, watch } from 'vue'
import { useSite } from '../stores/site'
import type { RouterStore } from '../stores/router'
import { wireFacets, clearFacets } from './facets'

// A bounded conversation page, with drafts scoped to the acting participant.

export type PostFormat = 'plain' | 'markdown'
export const MAX_POST_BYTES = 64 * 1024

export interface Message {
  id: string
  authorParticipantId: string
  authorHandles?: Record<string, string>
  sequence: number
  removed: boolean
  deleted?: boolean
  editedAt: string | null
  changeId: string | null
  content: { text: string; format?: PostFormat; html?: string | null; createdAt: string; replyTo: string | null }
  facets?: unknown
  sourceUri?: string
  sourceCid?: string
  permissions?: { allowedActions: string[] } | null
  _editing?: boolean
}

export interface MessagesPage {
  messages: Message[]
  openingPost?: Message | null
  nextCursor: string | null
  resumeCursor: string | null
  boundary: number
  resolved?: Record<string, { handle?: string; displayName?: string; value?: string; profileUrl?: string; avatar?: string | null }>
  authorHandles?: Record<string, string>
}

interface PublicPost {
  id: string; author: { ref: string; label: string; classification?: string; avatar?: string | null; profileUrl?: string }; text: string | null
  createdAt: string; editedAt: string | null; removed: boolean; format?: PostFormat; html?: string | null
  sourceUri?: string | null
}
interface PublicWindow {
  topic: Pick<Topic, 'key' | 'tangentKey' | 'title' | 'topic' | 'presentation'>
  posts: PublicPost[]; olderBefore: string | null; newerAfter: string | null; openingPost?: PublicPost | null
}

export interface Topic {
  key: string
  tangentKey: string
  title: string
  presentation?: 'conversation' | 'article'
  topic?: string
  canRead: boolean
  canWrite: boolean
  isLocked: boolean
  canManage?: boolean
  canAppointManagers?: boolean
  allowPostEditing?: boolean
  readAudience?: string
  publiclyReadable?: boolean
  accessState?: string
  permissions?: { role?: string; allowedActions: string[] } | null
}

interface MutationIntent {
  actor: string
  tangent: string
  topic: string
  uncertain: boolean
  method: 'POST'
  body: { requestId: string; text: string; format: PostFormat; replyTo?: string; facets?: unknown[] }
}

const encoder = new TextEncoder()

async function request(path: string, body?: unknown, method = 'POST', signal?: AbortSignal) {
  const response = await httpRequest(path, {
    method,
    credentials: 'same-origin',
    cache: 'no-store',
    headers: { Accept: 'application/json', ...(body === undefined ? {} : { 'Content-Type': 'application/json' }) },
    body: body === undefined ? undefined : JSON.stringify(body),
    signal,
  })
  const data = await response.json().catch(() => ({}))
  const typed = data as { state?: string; reason?: string; error?: string; title?: string; message?: string }
  if (!response.ok) {
    const error = new Error(
      typed.message || typed.reason || typed.error || typed.title
        || (response.status === 403 ? 'Your current access does not permit this action.' : 'This request could not be completed.'),
    ) as Error & { status: number }
    error.status = response.status
    throw error
  }
  return { data: data as { state?: string }, status: response.status }
}

export function createConversation(router: RouterStore) {
  const site = useSite()
  const viewerRef = () => site.site?.participant?.participantRef ?? ''
  const selectedTopic = ref<Topic | null>(null)
  const messages = ref<Message[]>([])
  const openingPost = ref<Message | null>(null)
  const replyTo = ref<Message | null>(null)
  const loadingTopic = ref(false)
  const loadingHistory = ref(false)
  const moreMessages = ref(false)
  const newMessages = ref(false)
  const publicOlderBefore = ref<string | null>(null)
  const publicNewerAfter = ref<string | null>(null)
  const error = ref('')
  const draft = ref('')
  const draftFormat = ref<PostFormat>('plain')
  const status = ref('')
  const resolved = ref<Record<string, { handle?: string; displayName?: string; value?: string; profileUrl?: string; avatar?: string | null; classification?: string }>>({})

  let epoch = 0
  let nextCursor: string | null = null
  let resumeCursor: string | null = null
  let currentCursor: string | undefined
  let anchor: { tangent: string; post: string } | null = null
  let publicWindowQuery: string | null = null
  let replyParentId: string | null = null
  let refreshingHistory = false
  let queuedRefresh: { force: boolean } | undefined
  const pendingPost = ref<MutationIntent | null>(null)
  const intentKey = (actor: string, tangent: string, topic: string) =>
    'post-intent:' + JSON.stringify([actor, tangent, topic])
  function savedIntent(actor: string, tangent: string, topic: string): MutationIntent | null {
    const key = intentKey(actor, tangent, topic)
    const saved = sessionStorage.getItem(key)
    if (!saved) return null
    const intent = JSON.parse(saved) as MutationIntent
    if (intent.actor !== actor || intent.tangent !== tangent || intent.topic !== topic || intent.method !== 'POST'
      || typeof intent.body?.requestId !== 'string' || typeof intent.body.text !== 'string'
      || !['plain', 'markdown'].includes(intent.body.format)
      || (intent.body.replyTo !== undefined && typeof intent.body.replyTo !== 'string')
      || (intent.body.facets !== undefined && !Array.isArray(intent.body.facets)))
      throw new Error('The saved Post cannot be safely retried. Check the conversation before posting again.')
    // A saved intent may have reached the server even when this tab never recorded its response.
    intent.uncertain = true
    return intent
  }
  function retireIntent(intent: MutationIntent) {
    const key = intentKey(intent.actor, intent.tangent, intent.topic)
    const current = savedIntent(intent.actor, intent.tangent, intent.topic)
    if (current?.body.requestId === intent.body.requestId) sessionStorage.removeItem(key)
    if (pendingPost.value?.body.requestId === intent.body.requestId) pendingPost.value = null
  }

  const topicPath = (key: string) => `/api/topics/${encodeURIComponent(key)}`
  const resourcePath = (key: string) => `/api/v1/experience/topics/${encodeURIComponent(key)}`
  const postRef = (tangent: string, topic: string, post: string) =>
    `${window.location.origin}::${tangent}::${topic}::${post}`
  const draftKey = (key: string) => 'draft:' + viewerRef() + ':' + key
  function rememberDraft() {
    if (selectedTopic.value) sessionStorage.setItem(draftKey(selectedTopic.value.key), JSON.stringify({ text: draft.value, format: draftFormat.value, replyTo: replyParentId }))
  }
  function restoreDraft() {
    if (!selectedTopic.value) return
    draft.value = ''
    draftFormat.value = 'plain'
    replyParentId = null
    try { pendingPost.value = savedIntent(viewerRef(), selectedTopic.value.tangentKey, selectedTopic.value.key) }
    catch (cause) { error.value = (cause as Error).message }
    const saved = sessionStorage.getItem(draftKey(selectedTopic.value.key))
    if (!saved) return
    try {
      const parsed = JSON.parse(saved) as { text?: string; format?: PostFormat; replyTo?: string }
      draft.value = typeof parsed.text === 'string' ? parsed.text : ''
      draftFormat.value = parsed.format === 'markdown' ? 'markdown' : 'plain'
      replyParentId = typeof parsed.replyTo === 'string' ? parsed.replyTo : null
    } catch { sessionStorage.removeItem(draftKey(selectedTopic.value.key)) }
  }
  watch(draft, rememberDraft)
  watch(draftFormat, rememberDraft)

  async function choose(key: string, aroundPost?: string) {
    if (router.route.kind === 'post') return openPost(router.route.tangent, key)
    rememberDraft()
    const version = ++epoch
    selectedTopic.value = null
    pendingPost.value = null
    replyTo.value = null
    replyParentId = null
    messages.value = []
    openingPost.value = null
    nextCursor = resumeCursor = null
    publicOlderBefore.value = publicNewerAfter.value = null
    publicWindowQuery = null
    currentCursor = undefined
    anchor = null
    newMessages.value = false
    error.value = ''
    loadingTopic.value = true
    try {
      const routeTangent = 'tangent' in router.route ? router.route.tangent : ''
      const publicQuery = new URLSearchParams(window.location.search)
      const edgeQuery = new URLSearchParams()
      for (const direction of ['before', 'after']) { const edge = publicQuery.get(direction); if (edge !== null) edgeQuery.set(direction, edge) }
      if (!viewerRef()) {
        if (aroundPost) { edgeQuery.delete('before'); edgeQuery.delete('after'); edgeQuery.set('around', aroundPost) }
        await loadPublicWindow(routeTangent, key, edgeQuery.toString(), version)
        return
      }
      const data = (await request(`/api/v1/tangents/${encodeURIComponent(routeTangent)}/topics/${encodeURIComponent(key)}`, undefined, 'GET')).data as unknown as Topic
      if (version !== epoch) return
      selectedTopic.value = data
      restoreDraft()
      if (data.canRead && aroundPost) {
        const result = (await request(`/api/v1/tangents/${encodeURIComponent(routeTangent)}/posts/${encodeURIComponent(aroundPost)}`, undefined, 'GET')).data as unknown as { topic: Topic; window: MessagesPage }
        if (version !== epoch) return
        if (result.topic.key !== key || result.topic.tangentKey !== routeTangent)
          throw new Error('The saved reading position does not belong to this conversation.')
        anchor = { tangent: routeTangent, post: aroundPost }
        nextCursor = result.window.nextCursor
        resumeCursor = result.window.resumeCursor
        renderPage(result.window)
      } else if (data.canRead && edgeQuery.size && data.publiclyReadable) await loadPublicWindow(routeTangent, key, edgeQuery.toString(), version)
      else if (data.canRead) await historyPage(undefined, version, key, true)
    } catch (err) {
      if (version === epoch) error.value = (err as Error).message
    } finally {
      if (version === epoch) loadingTopic.value = false
    }
  }

  function renderPublicWindow(window: PublicWindow) {
    const previous = selectedTopic.value
    selectedTopic.value = previous?.key === window.topic.key && previous.tangentKey === window.topic.tangentKey
      ? { ...previous, ...window.topic } : { ...window.topic, canRead: true, canWrite: false, isLocked: false, publiclyReadable: true }
    const authors: typeof resolved.value = {}
    const project = (post: PublicPost, index: number): Message => {
      authors[post.author.ref] = { displayName: post.author.label, classification: post.author.classification, avatar: post.author.avatar, profileUrl: post.author.profileUrl }
      return { id: post.id, authorParticipantId: post.author.ref, sequence: !window.olderBefore && index === 0 ? 1 : index + 2,
        removed: post.removed, editedAt: post.editedAt, changeId: null, sourceUri: post.sourceUri ?? undefined,
        content: { text: post.text ?? '', format: post.format, html: post.html, createdAt: post.createdAt, replyTo: null } }
    }
    messages.value = window.posts.map(project)
    openingPost.value = window.openingPost ? project(window.openingPost, 0) : null
    resolved.value = authors
    publicOlderBefore.value = window.olderBefore
    publicNewerAfter.value = window.newerAfter
    nextCursor = resumeCursor = null
    moreMessages.value = newMessages.value = false
    restoreDraft()
  }

  async function loadPublicWindow(tangent: string, topic: string, query: string, version = epoch, activity?: ActivityWorkContext) {
    const result = (await request(`/api/v1/public/tangents/${encodeURIComponent(tangent)}/topics/${encodeURIComponent(topic)}/posts${query ? '?' + query : ''}`,
      undefined, 'GET', activity?.signal)).data as unknown as PublicWindow
    if (version !== epoch || activity?.isCurrent && !activity.isCurrent()) return
    publicWindowQuery = query
    renderPublicWindow(result)
  }

  async function refreshTopic() {
    const current = selectedTopic.value
    if (!current) return
    const version = epoch
    try {
      const data = (await request(`/api/v1/tangents/${encodeURIComponent(current.tangentKey)}/topics/${encodeURIComponent(current.key)}`, undefined, 'GET')).data as unknown as Topic
      if (version !== epoch || selectedTopic.value?.key !== current.key || selectedTopic.value?.tangentKey !== current.tangentKey) return
      selectedTopic.value = data
      if (data.canRead) await refreshOpenHistory(true)
      else { messages.value = []; openingPost.value = null; resolved.value = {} }
    } catch (cause) {
      if (version !== epoch) return
      if ([401, 403, 404].includes((cause as { status?: number }).status ?? 0)) {
        selectedTopic.value = null
        messages.value = []
        openingPost.value = null
        resolved.value = {}
      } else error.value = (cause as Error).message
    }
  }

  async function historyPage(cursor: string | undefined, version = epoch, key = selectedTopic.value?.key, fromStart = false, activity?: ActivityWorkContext) {
    if (!key || version !== epoch) return false
    if (activity && activity.isCurrent && !activity.isCurrent()) return false
    try {
      const liveAppend = !!cursor && cursor === resumeCursor && !nextCursor
      const page = (await request(
        topicPath(key) + '/posts' + (cursor ? '?cursor=' + encodeURIComponent(cursor) : fromStart ? '?from=start' : ''),
        undefined, 'GET', activity?.signal)).data as unknown as MessagesPage
      if (version !== epoch || selectedTopic.value?.key !== key) return false
      if (activity && activity.isCurrent && !activity.isCurrent()) return false
      if (activity && liveAppend) {
        newMessages.value = page.messages.length > 0
        return true
      }
      currentCursor = cursor
      nextCursor = page.nextCursor
      resumeCursor = page.resumeCursor
      renderPage(page)
      newMessages.value = false
      return true
    } catch (err) {
      if (version !== epoch) return false
      const status = (err as { status?: number }).status
      if (status === 403 || status === 401 || status === 404) selectedTopic.value = null
      throw err
    }
  }

  function renderPage(page: MessagesPage) {
    messages.value = page.messages
    openingPost.value = page.openingPost ?? null
    resolved.value = page.resolved ?? {}
    moreMessages.value = !!nextCursor
    replyTo.value = replyParentId
      ? page.messages.find(message => message.id === replyParentId) ?? (openingPost.value?.id === replyParentId ? openingPost.value : null)
      : null
  }

  async function readMore() {
    const cursor = nextCursor ?? (newMessages.value ? resumeCursor : null)
    if (!cursor || loadingHistory.value) return
    loadingHistory.value = true
    error.value = ''
    try { await historyPage(cursor) }
    catch (err) { error.value = (err as Error).message }
    finally { loadingHistory.value = false }
  }

  async function refreshOpenHistory(force = false, activity?: ActivityWorkContext) {
    if (refreshingHistory) {
      queuedRefresh = { force: force || queuedRefresh?.force || false }
      return
    }
    if (!selectedTopic.value?.canRead) return
    if (!force && document.querySelector('.message-edit-form')) return
    if (activity && activity.isCurrent && !activity.isCurrent()) return
    const key = selectedTopic.value.key
    const version = epoch
    refreshingHistory = true
    try {
      if (publicWindowQuery !== null) {
        await loadPublicWindow(selectedTopic.value!.tangentKey, key, publicWindowQuery, version, activity)
      } else if (anchor) {
        const result = (await request(`/api/v1/tangents/${encodeURIComponent(anchor.tangent)}/posts/${encodeURIComponent(anchor.post)}`, undefined, 'GET', activity?.signal)).data as unknown as { window: MessagesPage }
        if (version === epoch && (!activity?.isCurrent || activity.isCurrent())) renderPage(result.window)
      } else await historyPage(currentCursor, version, key, !currentCursor, activity)
    } finally {
      refreshingHistory = false
      const queued = queuedRefresh
      queuedRefresh = undefined
      if (queued) void refreshOpenHistory(queued.force)
    }
  }

  async function openPost(tangentKey: string, postId: string) {
    rememberDraft()
    const version = ++epoch
    selectedTopic.value = null
    messages.value = []
    openingPost.value = null
    nextCursor = resumeCursor = null
    publicOlderBefore.value = publicNewerAfter.value = null
    publicWindowQuery = null
    newMessages.value = false
    error.value = ''
    anchor = { tangent: tangentKey, post: postId }
    loadingTopic.value = true
    try {
      if (!viewerRef()) {
        const document = (await request(`/api/v1/public/tangents/${encodeURIComponent(tangentKey)}/posts/${encodeURIComponent(postId)}`, undefined, 'GET')).data as unknown as { window: PublicWindow }
        if (version !== epoch) return
        publicWindowQuery = 'around=' + encodeURIComponent(postId)
        renderPublicWindow(document.window)
        return
      }
      const result = (await request(`/api/v1/tangents/${encodeURIComponent(tangentKey)}/posts/${encodeURIComponent(postId)}`, undefined, 'GET')).data as unknown as { topic: Topic; window: MessagesPage }
      if (version !== epoch) return
      selectedTopic.value = result.topic
      nextCursor = result.window.nextCursor
      resumeCursor = result.window.resumeCursor
      currentCursor = undefined
      restoreDraft()
      renderPage(result.window)
    } catch (err) {
      if (version === epoch) error.value = (err as Error).message
    } finally {
      if (version === epoch) loadingTopic.value = false
    }
  }

  function beginReply(message: Message) {
    rememberDraft()
    replyParentId = message.id
    replyTo.value = message
    rememberDraft()
  }
  function cancelReply() {
    replyParentId = null
    replyTo.value = null
    rememberDraft()
  }

  function invalidatePending() {
    epoch++
  }

  async function send(retrySubmitted = false) {
    if (!selectedTopic.value || !viewerRef() || (!retrySubmitted && !draft.value.trim())) return
    const key = selectedTopic.value.key
    const tangent = selectedTopic.value.tangentKey
    const actor = viewerRef()
    const storedDraftKey = draftKey(key)
    let pending: MutationIntent | null
    try { pending = savedIntent(actor, tangent, key) }
    catch (cause) { error.value = (cause as Error).message; return }
    pendingPost.value = pending
    if (retrySubmitted && !pending) return
    const submittedText = retrySubmitted ? pending!.body.text : draft.value
    const submittedFormat = retrySubmitted ? pending!.body.format : draftFormat.value
    if (encoder.encode(submittedText).length > MAX_POST_BYTES) {
      error.value = 'A Post must fit within 64 KiB of UTF-8 text.'
      return
    }
    const submittedReply = retrySubmitted ? pending!.body.replyTo?.split('::').at(-1) ?? null : replyParentId
    const version = epoch
    error.value = ''
    const facets = submittedFormat === 'plain' ? wireFacets(key, submittedText) : []
    if (!retrySubmitted && pending && (pending.body.text !== submittedText || pending.body.format !== submittedFormat
      || pending.body.replyTo !== (submittedReply ? postRef(tangent, key, submittedReply) : undefined)
      || facets.length > 0 && JSON.stringify(facets) !== JSON.stringify(pending.body.facets))) {
      error.value = 'A submitted Post is awaiting confirmation. Confirm that Post below; your edited draft will be kept.'
      return
    }
    const intent: MutationIntent = pending ?? {
      actor, tangent, topic: key, uncertain: false, method: 'POST',
      body: {
        requestId: 'post-' + crypto.randomUUID(), text: submittedText, format: submittedFormat,
        ...(facets.length ? { facets } : {}),
        ...(submittedReply ? { replyTo: postRef(tangent, key, submittedReply) } : {}),
      },
    }
    try {
      rememberDraft()
      sessionStorage.setItem(intentKey(actor, tangent, key), JSON.stringify(intent))
      pendingPost.value = intent
    } catch {
      error.value = 'This tab cannot preserve the submitted Post. Enable browser storage before replying.'
      return
    }
    try {
      const response = await request(resourcePath(key) + '/posts', intent.body, intent.method)
      const postRef = (response.data as { postRef?: unknown }).postRef
      if (typeof postRef !== 'string') throw new Error('The post could not be confirmed.')
      retireIntent(intent)
      const savedDraft = sessionStorage.getItem(storedDraftKey)
      if (savedDraft) {
        const stored = JSON.parse(savedDraft) as { text?: string; format?: PostFormat; replyTo?: string | null }
        if (stored.text === submittedText && (stored.format ?? 'plain') === submittedFormat && (stored.replyTo ?? null) === submittedReply)
          sessionStorage.removeItem(storedDraftKey)
      }
      if (actor !== viewerRef() || selectedTopic.value?.key !== key || selectedTopic.value.tangentKey !== tangent) return
      if (draft.value !== submittedText || draftFormat.value !== submittedFormat || replyParentId !== submittedReply) return
      sessionStorage.removeItem(storedDraftKey)
      clearFacets(key)
      draft.value = ''
      draftFormat.value = 'plain'
      replyParentId = null
      replyTo.value = null
      if (version !== epoch) { await refreshOpenHistory(true); return }
      const postId = postRef.split('::').at(-1)
      if (postId && /^[a-zA-Z0-9-]+$/.test(postId))
        router.navigate('/t/' + encodeURIComponent(tangent) + '/' + encodeURIComponent(postId))
      else await refreshOpenHistory(true)
      return postId
    } catch (err) {
      const rejected = [400, 401, 403, 404, 409, 422].includes((err as { status?: number }).status ?? 0)
      if (rejected && !intent.uncertain) retireIntent(intent)
      else {
        intent.uncertain = true
        try {
          const current = savedIntent(actor, tangent, key)
          if (current?.body.requestId === intent.body.requestId)
            sessionStorage.setItem(intentKey(actor, tangent, key), JSON.stringify(intent))
        } catch { /* The intent was saved before the request. Keep its original identity. */ }
      }
      if (version === epoch && actor === viewerRef()) error.value = (err as Error).message
    }
  }

  async function beginEdit(message: Message) {
    message._editing = true
    ;(message as Message & { _savedText?: string })._savedText = message.content.text
  }

  async function saveEdit(message: Message, text: string) {
    if (!text.trim() || encoder.encode(text).length > MAX_POST_BYTES) {
      error.value = 'A Post must contain text and fit within 64 KiB of UTF-8 text.'
      return false
    }
    const body = { text, format: message.content.format === 'markdown' ? 'markdown' : 'plain' }
    try {
      const response = await request('/api/v1/experience/posts/' + encodeURIComponent(message.id), body, 'PATCH')
      if (typeof (response.data as { postRef?: unknown }).postRef !== 'string') throw new Error('The edit could not be confirmed.')
      message._editing = false
      await refreshOpenHistory(true)
      return true
    } catch (err) {
      error.value = (err as Error).message + ' Check the current Post before submitting another edit.'
      void refreshOpenHistory(true)
      return false
    }
  }

  function cancelEdit(message: Message) {
    message._editing = false
    void refreshOpenHistory(true)
  }

  async function removeMessage(message: Message) {
    try {
      const response = await request('/api/v1/experience/posts/' + encodeURIComponent(message.id), undefined, 'DELETE')
      if (typeof (response.data as { postRef?: unknown }).postRef !== 'string') throw new Error('The removal could not be confirmed.')
      await refreshOpenHistory(true)
    } catch (err) {
      error.value = (err as Error).message + ' Check the current Post before trying another removal.'
      void refreshOpenHistory(true)
    }
  }

  /** An identity change drops the whole conversation world; nothing is re-attributed. */
  function reset() {
    const staleKey = selectedTopic.value?.key
    epoch++
    selectedTopic.value = null
    messages.value = []
    openingPost.value = null
    replyTo.value = null
    replyParentId = null
    draft.value = ''
    draftFormat.value = 'plain'
    error.value = ''
    status.value = ''
    nextCursor = null
    resumeCursor = null
    currentCursor = undefined
    anchor = null
    publicWindowQuery = null
    publicOlderBefore.value = publicNewerAfter.value = null
    moreMessages.value = newMessages.value = false
    pendingPost.value = null
    resolved.value = {}
    if (staleKey) clearFacets(staleKey)
  }

  async function acknowledgeVisiblePage() {
    const topic=selectedTopic.value; const cursor=resumeCursor; const actor=viewerRef();
    if(!topic || !cursor || !actor)return;
    await request('/api/v1/experience/topics/'+encodeURIComponent(topic.key)+'/read-position',{readCursor:cursor});
  }
  return {
    publicOlderBefore, publicNewerAfter, acknowledgeVisiblePage, selectedTopic, messages, openingPost, replyTo, replyParentId, loadingTopic, loadingHistory, moreMessages, newMessages, error, draft, draftFormat, status, resolved,
    get canWrite() { return selectedTopic.value?.canWrite ?? false },
    get nextCursor() { return nextCursor },
    get resumeCursor() { return resumeCursor },
    pendingPost, retryPendingPost: () => send(true), choose, openPost, historyPage, readMore, refreshOpenHistory, refreshTopic,
    beginReply, cancelReply, send, beginEdit, saveEdit, cancelEdit, removeMessage,
    reset, invalidatePending,
  }
}

export interface ActivityWorkContext {
  signal?: AbortSignal
  isCurrent?: () => boolean
}

export type Conversation = ReturnType<typeof createConversation>
