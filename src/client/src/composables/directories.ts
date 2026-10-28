import { httpRequest } from '../api'
import { reactive, ref } from 'vue'

// The Tangent and Topic directories, with bounded directory pages.

export interface DirectoryCount {
  value: number
  atLeast: boolean
}

export interface DirectoryParticipant {
  id: string
  displayName: string
  handle: string | null
  avatar: string | null
  classification: string
  profileUrl: string
}

export interface DirectoryPost {
  id: string
  topicKey: string
  topicTitle: string
  url: string
  excerpt: string
  createdAt: string
  author: DirectoryParticipant
}

export interface TangentCard {
  key: string
  readAudience?: string
  kind?: 'discussions' | 'articles'
  name: string
  description: string
  motto?: string
  accent?: string
  artwork?: string
  canManage?: boolean
  isOwner?: boolean
  canCreateTopic?: boolean
  permissions?: { role?: string; allowedActions?: string[] }
  topics?: Array<{ key: string; title?: string }>
  summary?: {
    topics: DirectoryCount | null
    posts: DirectoryCount | null
    recentTopics: TopicCard[]
    latestPost: DirectoryPost | null
    incomplete: boolean
  } | null
}

export interface TopicCard {
  key: string
  title: string
  presentation?: 'conversation' | 'article'
  topic?: string
  canRead?: boolean
  canWrite?: boolean
  tangentKey?: string
  summary?: {
    unread?: number
    following?: boolean
    posts: DirectoryCount | null
    participants: DirectoryCount | null
    starter: DirectoryParticipant | null
    openingPost?: DirectoryPost | null
    replies?: DirectoryCount | null
    createdAt: string
    latestPost: DirectoryPost | null
    article?: {
      openingPostId: string
      coverImageUrl?: string | null
      author: DirectoryParticipant
      createdAt: string
      excerpt?: string
      readingMinutes: number
      removed: boolean
    } | null
  } | null
}

export interface ArticleFilter { month: string; author: string }
export function articleMatches(topic: TopicCard, filter: ArticleFilter) { const article=topic.summary?.article; return (!filter.month || article?.createdAt.slice(0,7)===filter.month) && (!filter.author || article?.author.id===filter.author) }

export function directoryCount(count?: DirectoryCount | null): string {
  return count && Number.isInteger(count.value) && count.value >= 0
    ? `${count.value}${count.atLeast ? '+' : ''}` : '—'
}

export function directoryDate(value?: string | null): string {
  if (!value) return ''
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? '' : date.toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })
}

export function directoryRelativeDate(value?: string | null): string {
  if (!value) return ''
  const elapsed = Date.now() - new Date(value).getTime()
  if (!Number.isFinite(elapsed)) return ''
  if (elapsed < 0) return directoryDate(value)
  const minutes = Math.floor(elapsed / 60000)
  if (minutes < 1) return 'just now'
  if (minutes < 60) return `${minutes}m ago`
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `${hours}h ago`
  const days = Math.floor(hours / 24)
  return days < 7 ? `${days}d ago` : directoryDate(value)
}

export function directoryPostHref(tangentKey: string, post: DirectoryPost): string {
  const canonical = post.url
  if (/^\/t\/[^\s?#]+\/[^\s?#]+$/.test(canonical) && !/[\\\u0000-\u001f]/.test(canonical)) return canonical
  return '/t/' + encodeURIComponent(tangentKey) + '/' + encodeURIComponent(post.id)
}

export function createDirectories() {
  const tangents = ref<TangentCard[]>([])
  const tangentsNextPage = ref<number | null>(null)
  const tangentsCanCreate = ref(false)
  const tangentsIncomplete = ref(false)
  const tangentsLoading = ref(false)
  const tangentsLoaded = ref(false)
  const tangentsError = ref('')
  const topics = ref<TopicCard[]>([])
  const topicsNextPage = ref<number | null>(null)
  const topicsLoading = ref(false)
  const topicsLoaded = ref(false)
  const topicsError = ref('')
  const topicsIncomplete = ref(false)
  const loading = ref(false)
  let tangentRequest = 0
  let topicRequest = 0
  let topicTangent = ''

  function resetForIdentity() {
    tangentRequest++
    topicRequest++
    topicTangent = ''
    tangents.value = []
    tangentsNextPage.value = null
    tangentsCanCreate.value = false
    tangentsIncomplete.value = false
    tangentsLoading.value = false
    tangentsLoaded.value = false
    tangentsError.value = ''
    topics.value = []
    topicsNextPage.value = null
    topicsLoading.value = false
    topicsLoaded.value = false
    topicsError.value = ''
    topicsIncomplete.value = false
    loading.value = false
  }

  async function loadTangents(page?: number) {
    const request = ++tangentRequest
    loading.value = true
    tangentsLoading.value = true
    tangentsError.value = ''
    try {
      const data = (await httpRequest('/api/v1/tangents' + (page ? `?page=${encodeURIComponent(page)}` : ''), {
        credentials: 'same-origin', cache: 'no-store',
      }).then(r => r.ok ? r.json() : null)) as { tangents?: TangentCard[]; nextPage?: number | null; canCreate?: boolean; directoryIncomplete?: boolean } | null
      if (request !== tangentRequest) return
      if (!data) { tangentsError.value = 'Tangents could not be loaded. Try again.'; return }
      tangents.value = page ? [...tangents.value, ...(data.tangents ?? [])] : (data.tangents ?? [])
      tangentsNextPage.value = data.nextPage ?? null
      tangentsCanCreate.value = data.canCreate === true
      tangentsIncomplete.value = data.directoryIncomplete === true
      tangentsLoaded.value = true
    } catch {
      if (request === tangentRequest) tangentsError.value = 'Tangents could not be loaded. Try again.'
    } finally {
      if (request === tangentRequest) { loading.value = false; tangentsLoading.value = false }
    }
  }

  async function createTangent(card: { name: string; key: string; kind: 'discussions' | 'articles'; description: string; motto: string; artwork: string }) {
    const response = await httpRequest('/api/v1/tangents', {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(card),
    })
    const body = await response.json().catch(() => ({}))
    if (!response.ok) throw new Error((body as { reason?: string; title?: string }).reason || (body as { title?: string }).title || 'The Tangent could not be created.')
    await loadTangents()
  }

  async function loadTopics(tangentKey: string, page?: number, append = false) {
    if (topicTangent !== tangentKey || !append) {
      topicTangent = tangentKey
      topics.value = []
      topicsNextPage.value = null
      topicsLoaded.value = false
      topicsIncomplete.value = false
    }
    const request = ++topicRequest
    loading.value = true
    topicsLoading.value = true
    topicsError.value = ''
    try {
      const data = (await httpRequest(`/api/v1/tangents/${encodeURIComponent(tangentKey)}/topics` + (page ? `?page=${encodeURIComponent(page)}` : ''), {
        credentials: 'same-origin', cache: 'no-store',
      }).then(r => r.ok ? r.json() : null)) as { topics?: TopicCard[]; nextPage?: number | null; scanLimited?: boolean } | null
      if (request !== topicRequest || tangentKey !== topicTangent) return
      if (!data) { topicsError.value = 'Topics could not be loaded. Try refreshing.'; return }
      topics.value = append && topicsLoaded.value ? [...topics.value, ...(data.topics ?? [])] : (data.topics ?? [])
      topicsNextPage.value = data.nextPage ?? null
      topicsIncomplete.value = data.scanLimited === true
      topicsLoaded.value = true
    } catch {
      if (request === topicRequest && tangentKey === topicTangent) topicsError.value = 'Topics could not be loaded. Try refreshing.'
    } finally {
      if (request === topicRequest) { loading.value = false; topicsLoading.value = false }
    }
  }

  async function createTopic(tangentKey: string, topic: { key: string; title: string }) {
    const response = await httpRequest(`/api/v1/tangents/${encodeURIComponent(tangentKey)}/topics`, {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(topic),
    })
    const body = await response.json().catch(() => ({}))
    if (!response.ok) throw new Error((body as { reason?: string; title?: string }).reason || (body as { title?: string }).title || 'The Topic could not be created.')
    await loadTopics(tangentKey)
  }

  /** Save a Tangent's card. A 403 sends the viewer home. */
  async function updateTangent(key: string, body: { name: string; kind: 'discussions' | 'articles'; description: string; motto: string; accent: string; artwork: string }) {
    const response = await httpRequest('/api/v1/tangents/' + encodeURIComponent(key), {
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

  /** Set a participant's role across a Tangent. */
  async function setMemberRole(tangentKey: string, did: string, role: string) {
    const response = await httpRequest('/api/tangents/' + encodeURIComponent(tangentKey) + '/roles/' + encodeURIComponent(did), {
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
    tangents, tangentsNextPage, tangentsCanCreate, tangentsIncomplete, tangentsLoading, tangentsLoaded, tangentsError, topics, topicsNextPage, topicsLoading, topicsLoaded, topicsError, topicsIncomplete, loading,
    loadTangents, createTangent, loadTopics, createTopic, updateTangent, setMemberRole, resetForIdentity,
  })
  return directories
}

export type Directories = ReturnType<typeof createDirectories>
