import { reactive, ref } from 'vue'

// The Tangent and Topic directories, ported from rooms.js's directory flows.
// Same endpoints as the vanilla app (the browser data plane).

interface TangentCard {
  key: string
  name: string
  description: string
  motto?: string
  accent?: string
  artwork?: string
  canManage?: boolean
  isOwner?: boolean
  canCreateTopic?: boolean
  permissions?: { role?: string; allowedActions?: string[] }
  topics?: Array<{ key: string }>
}

export interface TopicCard {
  key: string
  title: string
  description?: string
  canRead?: boolean
  canWrite?: boolean
  tangentKey?: string
}

export function createDirectories() {
  const tangents = ref<TangentCard[]>([])
  const tangentsNextPage = ref<number | null>(null)
  const tangentsCanCreate = ref(false)
  const topics = ref<TopicCard[]>([])
  const topicsNextPage = ref<number | null>(null)
  const loading = ref(false)

  async function loadTangents(page?: number) {
    loading.value = true
    try {
      const data = (await fetch('/api/v1/tangents' + (page ? `?page=${encodeURIComponent(page)}` : ''), {
        credentials: 'same-origin', cache: 'no-store',
      }).then(r => r.ok ? r.json() : null)) as { tangents?: TangentCard[]; nextPage?: number | null; canCreate?: boolean } | null
      if (!data) return
      tangents.value = page ? [...tangents.value, ...(data.tangents ?? [])] : (data.tangents ?? [])
      tangentsNextPage.value = data.nextPage ?? null
      tangentsCanCreate.value = data.canCreate === true
    } finally {
      loading.value = false
    }
  }

  async function createTangent(card: { name: string; key: string; description: string; motto: string; artwork: string }) {
    const response = await fetch('/api/v1/tangents', {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(card),
    })
    const body = await response.json().catch(() => ({}))
    if (!response.ok) throw new Error((body as { reason?: string; title?: string }).reason || (body as { title?: string }).title || 'The Tangent could not be created.')
    await loadTangents()
  }

  async function loadTopics(tangentKey: string, page?: number) {
    loading.value = true
    try {
      const data = (await fetch(`/api/tangents/${encodeURIComponent(tangentKey)}/topics` + (page ? `?page=${encodeURIComponent(page)}` : ''), {
        credentials: 'same-origin', cache: 'no-store',
      }).then(r => r.ok ? r.json() : null)) as { topics?: TopicCard[]; nextPage?: number | null } | null
      if (!data) return
      topics.value = page ? [...topics.value, ...(data.topics ?? [])] : (data.topics ?? [])
      topicsNextPage.value = data.nextPage ?? null
    } finally {
      loading.value = false
    }
  }

  async function createTopic(tangentKey: string, topic: { key: string; title: string }) {
    const response = await fetch(`/api/tangents/${encodeURIComponent(tangentKey)}/topics`, {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(topic),
    })
    const body = await response.json().catch(() => ({}))
    if (!response.ok) throw new Error((body as { reason?: string; title?: string }).reason || (body as { title?: string }).title || 'The Topic could not be created.')
    await loadTopics(tangentKey)
  }

  /** Save a Tangent's card (rooms.js edit-tangent-form). A 403 sends the viewer home. */
  async function updateTangent(key: string, body: { name: string; description: string; motto: string; accent: string; artwork: string }) {
    const response = await fetch('/api/v1/tangents/' + encodeURIComponent(key), {
      method: 'PATCH',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    })
    if (response.status === 403) {
      window.location.assign('/')
      return
    }
    const payload = await response.json().catch(() => ({}))
    if (!response.ok) throw new Error((payload as { reason?: string; title?: string }).reason || (payload as { title?: string }).title || 'The Tangent card could not be saved.')
    await loadTangents()
  }

  /** Set a participant's role across a Tangent (rooms.js tangent-member-form). */
  async function setMemberRole(tangentKey: string, did: string, role: string) {
    const response = await fetch('/api/tangents/' + encodeURIComponent(tangentKey) + '/roles/' + encodeURIComponent(did), {
      method: 'PUT',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ role }),
    })
    const payload = await response.json().catch(() => ({}))
    if (!response.ok) throw new Error((payload as { reason?: string; title?: string }).reason || (payload as { title?: string }).title || 'The role could not be saved.')
    await loadTangents()
  }

  const directories = reactive({
    tangents, tangentsNextPage, tangentsCanCreate, topics, topicsNextPage, loading,
    loadTangents, createTangent, loadTopics, createTopic, updateTangent, setMemberRole,
  })
  return directories
}

export type Directories = ReturnType<typeof createDirectories>
