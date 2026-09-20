import { ref } from 'vue'
import { useSite } from '../stores/site'
import type { RouterStore } from '../stores/router'
import { wireFacets, clearFacets } from './facets'

// The conversation loop, ported from rooms.js: epoch-guarded navigation,
// cursor page accumulation, pending-mutation retries, refresh queueing.

export interface Message {
  id: string
  authorParticipantId: string
  authorHandles?: Record<string, string>
  sequence: number
  removed: boolean
  deleted?: boolean
  editedAt: string | null
  changeId: string | null
  content: { text: string; createdAt: string; replyTo: string | null }
  facets?: unknown
  sourceUri?: string
  sourceCid?: string
  permissions?: { allowedActions: string[] } | null
  _editing?: boolean
}

export interface MessagesPage {
  messages: Message[]
  nextCursor: string | null
  resumeCursor: string | null
  boundary: number
  resolved?: Record<string, { handle?: string; displayName?: string; value?: string; profileUrl?: string }>
  authorHandles?: Record<string, string>
}

export interface Room {
  key: string
  tangentKey: string
  title: string
  topic?: string
  canRead: boolean
  canWrite: boolean
  isLocked: boolean
  canManage?: boolean
  canAppointManagers?: boolean
  allowPostEditing?: boolean
  readAudience?: string
  accessState?: string
  permissions?: { role?: string; allowedActions: string[] } | null
}

interface MutationIntent {
  method: 'POST' | 'PATCH' | 'DELETE'
  body: Record<string, unknown>
}

const encoder = new TextEncoder()

async function request(path: string, body?: unknown, method = 'POST', signal?: AbortSignal) {
  const response = await fetch(path, {
    method: body === undefined ? 'GET' : method,
    credentials: 'same-origin',
    cache: 'no-store',
    headers: { Accept: 'application/json', ...(body === undefined ? {} : { 'Content-Type': 'application/json' }) },
    body: body === undefined ? undefined : JSON.stringify(body),
    signal,
  })
  const data = await response.json().catch(() => ({}))
  const typed = data as { state?: string; reason?: string; error?: string; title?: string }
  if (!response.ok && !['rejected', 'conflict'].includes(typed.state ?? '')) {
    const error = new Error(
      typed.reason || typed.error || typed.title
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
  const room = ref<Room | null>(null)
  const messages = ref<Message[]>([])
  const replyTo = ref<string | null>(null)
  const loadingRoom = ref(false)
  const loadingHistory = ref(false)
  const moreMessages = ref(false)
  const error = ref('')
  const draft = ref('')
  const status = ref('')
  const resolved = ref<Record<string, { handle?: string; displayName?: string; value?: string; profileUrl?: string; classification?: string }>>({})

  let epoch = 0
  let nextCursor: string | null = null
  let resumeCursor: string | null = null
  let replyParentId: string | null = null
  let refreshingHistory = false
  let queuedRefresh: { force: boolean } | undefined
  const pendingMutations = new Map<string, MutationIntent>()

  const roomPath = (key: string) => `/api/rooms/${encodeURIComponent(key)}`
  const can = (subject: { permissions?: { allowedActions: string[] } | null } | null, action: string) =>
    subject?.permissions?.allowedActions?.includes(action) ?? false

  function rememberDraft() {
    if (room.value) sessionStorage.setItem('draft:' + room.value.key, JSON.stringify({ text: draft.value, replyTo: replyParentId }))
  }
  function restoreDraft() {
    if (!room.value) return
    const saved = sessionStorage.getItem('draft:' + room.value.key)
    if (saved) {
      const parsed = JSON.parse(saved) as { text: string; replyTo: string | null }
      draft.value = parsed.text
      replyParentId = parsed.replyTo
    }
  }

  async function choose(key: string) {
    if (router.route.kind === 'post') return openPost(router.route.tangent, key)
    rememberDraft()
    const version = ++epoch
    room.value = null
    replyTo.value = null
    replyParentId = null
    messages.value = []
    loadingRoom.value = true
    try {
      const routeTangent = 'tangent' in router.route ? router.route.tangent : ''
      const data = (await request(`/api/tangents/${encodeURIComponent(routeTangent)}/topics/${encodeURIComponent(key)}`, undefined, 'GET')).data as unknown as Room
      if (version !== epoch) return
      room.value = data
      restoreDraft()
      if (data.canRead) await historyPage(undefined, version, key, true)
    } catch (err) {
      if (version === epoch) error.value = (err as Error).message
    } finally {
      loadingRoom.value = false
    }
  }

  async function historyPage(cursor: string | undefined, version = epoch, key = room.value?.key, fromStart = false, activity?: ActivityWorkContext) {
    if (!key || version !== epoch) return false
    if (activity && activity.isCurrent && !activity.isCurrent()) return false
    try {
      const liveAppend = !!cursor && cursor === resumeCursor && !nextCursor
      const page = (await request(
        roomPath(key) + '/messages' + (cursor ? '?cursor=' + encodeURIComponent(cursor) : fromStart ? '?from=start' : ''),
        undefined, 'GET', activity?.signal)).data as unknown as MessagesPage
      if (version !== epoch || room.value?.key !== key) return false
      if (activity && activity.isCurrent && !activity.isCurrent()) return false
      renderPage(page, true, liveAppend)
      nextCursor = page.nextCursor
      resumeCursor = page.resumeCursor
      return true
    } catch (err) {
      if (version !== epoch) return false
      const status = (err as { status?: number }).status
      if (status === 403 || status === 401 || status === 404) room.value = null
      throw err
    }
  }

  function renderPage(page: MessagesPage, nativeHistory = true, liveAppend = false) {
    if (nativeHistory) messages.value = page.messages
    else messages.value.push(...page.messages.filter(m => !messages.value.some(existing => existing.id === m.id)))
    if (page.resolved) resolved.value = { ...resolved.value, ...page.resolved }
    moreMessages.value = !!nextCursor
    void liveAppend
  }

  async function refreshOpenHistory(force = false, activity?: ActivityWorkContext) {
    if (refreshingHistory) {
      queuedRefresh = { force: force || queuedRefresh?.force || false }
      return
    }
    if (!room.value?.canRead) return
    if (!force && document.querySelector('.message-edit-form')) return
    if (activity && activity.isCurrent && !activity.isCurrent()) return
    const key = room.value.key
    const version = epoch
    refreshingHistory = true
    try {
      const pages: MessagesPage[] = []
      const cursors = new Set<string>()
      let cursor: string | null = null
      let loaded = 0
      do {
        const page = (await request(roomPath(key) + '/messages' + (cursor ? '?cursor=' + encodeURIComponent(cursor) : '?from=start'), undefined, 'GET', activity?.signal)).data as unknown as MessagesPage
        if (version !== epoch || room.value?.key !== key) return
        if (activity && activity.isCurrent && !activity.isCurrent()) return
        pages.push(page)
        loaded += page.messages?.length || 0
        cursor = page.nextCursor
        if (!cursor || cursors.has(cursor) || !page.messages?.length) break
        cursors.add(cursor)
      } while (loaded < 4096)
      if (version !== epoch || room.value?.key !== key) return
      if (!force && document.querySelector('.message-edit-form')) return
      messages.value = pages.flatMap(page => page.messages)
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
    room.value = null
    messages.value = []
    loadingRoom.value = true
    try {
      const result = (await request(`/api/tangents/${encodeURIComponent(tangentKey)}/posts/${encodeURIComponent(postId)}`, undefined, 'GET')).data as unknown as { topic: Room; window: MessagesPage }
      if (version !== epoch) return
      room.value = result.topic
      renderPage(result.window, false)
    } catch (err) {
      if (version === epoch) error.value = (err as Error).message
    } finally {
      loadingRoom.value = false
    }
  }

  function beginReply(message: Message) {
    if (pendingMutations.size > 0) return
    rememberDraft()
    replyParentId = message.id
    restoreDraft()
  }
  function cancelReply() {
    replyParentId = null
    replyTo.value = null
  }

  async function send() {
    if (!room.value || !draft.value.trim()) return
    const key = room.value.key
    const mutationKey = (viewerRef()) + ':' + key + ':new'
    const facets = wireFacets(key, draft.value)
    const intent: MutationIntent = {
      method: 'POST',
      body: {
        operationId: 'post-' + crypto.randomUUID(),
        text: draft.value,
        ...(facets.length ? { facets } : {}),
        ...(replyParentId ? { replyTo: replyParentId } : {}),
      },
    }
    pendingMutations.set(mutationKey, intent)
    try {
      const response = await request(roomPath(key) + '/messages', intent.body, intent.method)
      if ((response.data as { state?: string }).state !== 'accepted') throw new Error('The post was not accepted.')
      pendingMutations.delete(mutationKey)
      sessionStorage.removeItem('draft:' + key)
      clearFacets(key)
      draft.value = ''
      replyParentId = null
      replyTo.value = null
      await refreshOpenHistory(true)
    } catch (err) {
      error.value = (err as Error).message
    }
  }

  async function beginEdit(message: Message) {
    const key = room.value?.key ?? ''
    const mutationKey = (viewerRef()) + ':' + key + ':' + message.id
    const saved = pendingMutations.get(mutationKey)
    if (saved?.method === 'DELETE') {
      error.value = 'A deletion is pending. Retry Delete to finish it.'
      return
    }
    message._editing = true
    ;(message as Message & { _savedText?: string })._savedText = saved?.body.text as string ?? message.content.text
    void saved
  }

  async function saveEdit(message: Message, text: string) {
    const key = room.value?.key ?? ''
    const mutationKey = (viewerRef()) + ':' + key + ':' + message.id
    if (!text.trim() || encoder.encode(text).length > 4096) throw new Error('Use a post of 1–4096 UTF-8 bytes.')
    const saved = pendingMutations.get(mutationKey)
    const body = { text, ...(saved?.body.operationId ? { operationId: saved.body.operationId } : { operationId: crypto.randomUUID() }) }
    pendingMutations.set(mutationKey, { method: 'PATCH', body })
    try {
      const response = await request(roomPath(key) + '/messages/' + encodeURIComponent(message.id), body, 'PATCH')
      if ((response.data as { state?: string }).state !== 'accepted') throw new Error('The edit was not accepted.')
      pendingMutations.delete(mutationKey)
      message.content.text = text
      message._editing = false
      await refreshOpenHistory(true)
    } catch (err) {
      error.value = (err as Error).message
    }
  }

  function cancelEdit(message: Message) {
    message._editing = false
    void refreshOpenHistory(true)
  }

  async function removeMessage(message: Message) {
    const key = room.value?.key ?? ''
    const mutationKey = (viewerRef()) + ':' + key + ':' + message.id
    if (pendingMutations.get(mutationKey)?.method === 'PATCH') {
      error.value = 'An edit is pending. Retry Edit to finish it.'
      return
    }
    const removing = can(room.value, 'removePost') && message.authorParticipantId !== viewerRef()
    const saved = pendingMutations.get(mutationKey)
    const intent: MutationIntent = saved ?? {
      method: 'DELETE',
      body: { operationId: crypto.randomUUID() },
    }
    pendingMutations.set(mutationKey, intent)
    try {
      const response = await request(roomPath(key) + '/messages/' + encodeURIComponent(message.id), intent.body, intent.method)
      if (!['deleted', 'moderated'].includes((response.data as { state?: string }).state ?? '')) {
        throw new Error('The post was not removed.')
      }
      pendingMutations.delete(mutationKey)
      await refreshOpenHistory(true)
    } catch (err) {
      error.value = (err as Error).message
    }
    void removing
  }

  /** An identity change drops the whole conversation world; nothing is re-attributed. */
  function reset() {
    const staleKey = room.value?.key
    epoch++
    room.value = null
    messages.value = []
    replyTo.value = null
    replyParentId = null
    draft.value = ''
    error.value = ''
    status.value = ''
    nextCursor = null
    resumeCursor = null
    pendingMutations.clear()
    resolved.value = {}
    if (staleKey) clearFacets(staleKey)
  }

  // The room's settings mutations (rooms.js mutateRoom): the action re-opens the
  // room afterwards; a 403 sends the viewer home.
  async function mutate(suffix: string, body: unknown, method: string) {
    const key = room.value?.key
    if (!key) throw new Error('Choose a room before changing its settings.')
    try {
      await request(roomPath(key) + suffix, body, method)
    } catch (error) {
      if ((error as Error & { status?: number }).status === 403) {
        window.location.assign('/')
        return
      }
      throw error
    }
    status.value = 'Saved.'
    if (room.value?.key === key) await choose(key)
  }

  const saveTopicDescription = (topic: string) => mutate('/topic', { topic }, 'PUT')
  const updateMember = (did: string, role: string) => mutate('/members/' + encodeURIComponent(did.trim()), { role }, 'PUT')
  const saveReadAudience = (audience: string, publishExistingHistory: boolean) => mutate('/read-audience', { audience, publishExistingHistory }, 'PUT')
  const saveParticipation = (allowPostEditing: boolean, isLocked: boolean) => mutate('/settings', { allowPostEditing, isLocked }, 'PATCH')

  return {
    room, messages, replyTo, replyParentId, loadingRoom: loadingRoom, loadingHistory, moreMessages, error, draft, status, resolved,
    get canWrite() { return room.value?.canWrite ?? false },
    get nextCursor() { return nextCursor },
    get resumeCursor() { return resumeCursor },
    choose, openPost, historyPage, refreshOpenHistory,
    beginReply, cancelReply, send, beginEdit, saveEdit, cancelEdit, removeMessage,
    saveTopicDescription, updateMember, saveReadAudience, saveParticipation,
    reset,
  }
}

export interface ActivityWorkContext {
  signal?: AbortSignal
  isCurrent?: () => boolean
}

export type Conversation = ReturnType<typeof createConversation>
