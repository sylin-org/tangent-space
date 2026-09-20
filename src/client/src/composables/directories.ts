import { reactive, ref } from 'vue'

// The Tangent and Topic directories, ported from rooms.js's directory flows.
// Same endpoints as the vanilla app (the browser data plane).

interface TangentCard {
  key: string
  name: string
  description: string
  motto?: string
  artwork?: string
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
  const topics = ref<TopicCard[]>([])
  const topicsNextPage = ref<number | null>(null)
  const loading = ref(false)

  async function loadTangents(page?: number) {
    loading.value = true
    try {
      const data = (await fetch('/api/v1/tangents' + (page ? `?page=${encodeURIComponent(page)}` : ''), {
        credentials: 'same-origin', cache: 'no-store',
      }).then(r => r.ok ? r.json() : null)) as { tangents?: TangentCard[]; nextPage?: number | null } | null
      if (!data) return
      tangents.value = page ? [...tangents.value, ...(data.tangents ?? [])] : (data.tangents ?? [])
      tangentsNextPage.value = data.nextPage ?? null
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

  const directories = reactive({
    tangents, tangentsNextPage, topics, topicsNextPage, loading,
    loadTangents, createTangent, loadTopics, createTopic,
  })
  return directories
}

export type Directories = ReturnType<typeof createDirectories>
