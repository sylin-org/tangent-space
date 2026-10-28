import { ref } from 'vue'
import { rolesRequest } from './roles-api'

export interface WorkspaceNotification {
  id: string; kind: string; title: string; topicKey: string; tangentKey: string; excerpt: string; createdAt: string; unread: boolean; url: string
  author: { id: string; displayName?: string; handle?: string; avatar?: string; classification?: string }
}
export function createWorkspaceAttention() {
  const notifications = ref<WorkspaceNotification[]>([])
  const loading = ref(false)
  const error = ref('')
  const more = ref(false)
  const incomplete = ref(false)
  const nextCursor = ref<string | null>(null)
  const previousCursor = ref<string | null>(null)
  let currentCursor: string | null = null
  const markingRead = ref(false)
  let identity = 0
  let generation = 0
  async function load(cursor: string | null = currentCursor) {
    const token = ++generation
    loading.value = true
    error.value = ''
    try {
      const page = await rolesRequest<{ items: WorkspaceNotification[]; more: boolean; incomplete?: boolean; nextCursor?: string | null; previousCursor?: string | null }>(
        '/api/workspace/notifications' + (cursor ? '?cursor=' + encodeURIComponent(cursor) : ''))
      if (token !== generation) return
      notifications.value = page.items
      more.value = page.more
      incomplete.value = page.incomplete ?? false
      nextCursor.value = page.nextCursor ?? null
      previousCursor.value = page.previousCursor ?? null
      currentCursor = cursor
    } catch (cause) { if (token === generation) error.value = (cause as Error).message }
    finally { if (token === generation) loading.value = false }
  }
  async function markRead() {
    if (markingRead.value) return
    const actor = identity
    markingRead.value = true
    error.value = ''
    try {
      await rolesRequest('/api/workspace/notifications/read', { method: 'POST', body: {} })
      if (actor !== identity) return
      notifications.value = notifications.value.map(item => ({ ...item, unread: false }))
      await load()
    } catch (cause) { if (actor === identity) error.value = (cause as Error).message }
    finally { if (actor === identity) markingRead.value = false }
  }

  function reset() { currentCursor = null; nextCursor.value = previousCursor.value = null; incomplete.value = false; identity++; generation++; markingRead.value = false; notifications.value = []; more.value = false; error.value = ''; loading.value = false }
  return { notifications, loading, error, more, incomplete, nextCursor, previousCursor, markingRead, load, markRead, reset }
}
export type WorkspaceAttention = ReturnType<typeof createWorkspaceAttention>
