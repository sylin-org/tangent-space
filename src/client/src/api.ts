/**
 * The only fetch layer in the workspace (D19). Every server call goes through here,
 * typed against the experience envelope. Never add fetch outside this module.
 */

export function httpRequest(path: RequestInfo | URL, init?: RequestInit): Promise<Response> {
  return fetch(path, init)
}

export interface StorageProvider {
  id: string
  name: string
  fields: Array<{ name: string; label: string; required: boolean; secret: boolean; type: string; placeholder?: string | null }>
}

export interface StorageState {
  activeProvider: string
  providers: StorageProvider[]
  changeRequiresRestart: boolean
  firstRun: boolean
  pendingRestart: boolean
}

async function storageRequest<T>(suffix = '', body?: unknown): Promise<T> {
  const headers: Record<string, string> = { Accept: 'application/json' }
  if (body !== undefined) {
    const sessionResponse = await httpRequest('/api/roles/ui/session', { credentials: 'same-origin', cache: 'no-store' })
    if (!sessionResponse.ok) throw new Error('Your session cannot change database settings. Sign in again and retry.')
    const session = await sessionResponse.json() as { headerName: string; requestToken: string }
    if (!session.headerName || !session.requestToken) throw new Error('The server did not provide a session token. Sign in again and retry.')
    headers[session.headerName] = session.requestToken
    headers['Content-Type'] = 'application/json'
  }
  const response = await httpRequest('/api/server/storage' + suffix, {
    method: body === undefined ? 'GET' : 'POST',
    credentials: 'same-origin', cache: 'no-store', headers,
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  const value = await response.json().catch(() => ({}))
  if (!response.ok) throw new Error(value.message || value.reason || value.title || 'Database settings are unavailable. Retry when the server is ready.')
  return value as T
}

export const storageApi = {
  get: () => storageRequest<StorageState>(),
  test: (provider: string, settings: Record<string, string>) =>
    storageRequest<{ testId: string; provider: string; message: string }>('/test', { provider, settings }),
  activate: (testId: string) =>
    storageRequest<{ provider: string; restartRequired: boolean; message: string }>('/activate', { testId }),
}

export interface ContentSourceState { id: string; status: string; read: boolean; publish: string }
export interface BlueskyAuthor { actorDid: string; handle: string; refreshedAt: string; postsUrl: string }
export interface ContentSourcePage {
  source: string
  posts: Array<{ source: string; id: string; author: string; text: string; createdAt: string; url: string }>
  olderBefore?: string | null
  newerAfter?: string | null
}
export interface BlueskyConnection {
  status: 'connected' | 'disconnected' | 'reauthorize' | 'unavailable'
  actorDid: string | null
  canPublish: boolean
  connectUrl: string | null
}

async function contentRequest<T>(path: string, refresh = false): Promise<T> {
  const response = await httpRequest('/api/v1/content' + path, {
    method: refresh ? 'POST' : 'GET', credentials: 'same-origin', cache: 'no-store',
    headers: { Accept: 'application/json', ...(refresh ? { 'Content-Type': 'application/json' } : {}) },
    body: refresh ? '{}' : undefined,
  })
  const value = await response.json().catch(() => ({}))
  if (!response.ok) throw new Error(value.reason || value.message || 'This request could not be completed. Retry when the service is available.')
  return value as T
}

export const contentApi = {
  sources: () => contentRequest<{ sources: ContentSourceState[] }>('/sources'),
  blueskyAuthors: () => contentRequest<{ authors: BlueskyAuthor[] }>('/bluesky/authors?limit=25'),
  refreshAuthor: (author: string) => contentRequest<{ actorDid: string; imported: number; refreshedAt: string; backfill: string; publish: string }>(
    '/bluesky/authors/' + encodeURIComponent(author) + '/refresh', true),
  readAuthor: (actorDid: string, direction?: 'before' | 'after', postId?: string) => {
    const query = new URLSearchParams({ actorDid, limit: '25' })
    if (direction && postId) query.set(direction, postId)
    return contentRequest<ContentSourcePage>('/bluesky/posts?' + query)
  },
  blueskyConnection: () => contentRequest<BlueskyConnection>('/bluesky/connection'),
  publishPost: async (postId: string) => {
    const response = await httpRequest('/api/v1/content/bluesky/publish', {
      method: 'POST', credentials: 'same-origin', cache: 'no-store',
      headers: { Accept: 'application/json', 'Content-Type': 'application/json' },
      body: JSON.stringify({ postId, requestId: 'post:' + postId }),
    })
    const result = await response.json().catch(() => ({}))
    if (!response.ok) throw new Error(result.reason || result.message || 'Publication could not be confirmed. Retry this Post to check the same publication.')
    return result as { uri: string; cid: string; publishedAt: string; replayed: boolean }
  },
}

const base = '/api/v1/experience'

export interface Identity {
  participantRef: string
  did: string | null
  displayName: string
  handle: string | null
}

export interface Place {
  serverRef: string
  tangentRef: string | null
  topicRef: string | null
  label: string
  role: string
  allowedActions: string[]
}

export interface TangentDto {
  tangentRef: string
  name: string
  description: string
  membership: string
  admission: string
  canJoin: boolean
  hasReadableTopics: boolean
  hasWritableTopics: boolean
}

export interface TopicDto {
  topicRef: string
  title: string
  topic: string
  canRead: boolean
  canWrite: boolean
  isLocked: boolean
  allowPostEditing: boolean
}

export interface PostDto {
  ref: string
  authorRef: string
  authorName: string
  text: string
  replyTo: string | null
  url: string
  createdAt: string
  editedAt: string | null
  removed: boolean
}

export interface Problem {
  code: string
  message: string
  field?: string
  retryable: boolean
}

export interface Envelope<T> {
  experienceVersion: string
  operation: string
  status: 'ok' | 'pending' | 'blocked' | 'error'
  identity: Identity | null
  place: Place | null
  result: { data: T | null; problem: Problem | null }
  attention: { waitingCount: { value: number; atLeast: boolean } }
}

export class EnvelopeError extends Error {
  readonly problem: Problem
  constructor(problem: Problem) {
    super(problem.message)
    this.problem = problem
  }
}

async function call<T>(path: string, init?: RequestInit): Promise<Envelope<T>> {
  const response = await httpRequest(base + path, {
    credentials: 'same-origin',
    headers: { Accept: 'application/json' },
    ...init,
  })
  if (!response.ok) {
    const body = await response.json().catch(() => null)
    throw new EnvelopeError({
      code: body?.code ?? `http_${response.status}`,
      message: body?.message ?? `The server answered HTTP ${response.status}.`,
      retryable: response.status >= 500,
    })
  }
  const envelope = (await response.json()) as Envelope<T>
  if (envelope.status === 'blocked' && envelope.result.problem) {
    throw new EnvelopeError(envelope.result.problem)
  }
  return envelope
}

export const api = {
  arrive: () => call<{ tangents: TangentDto[]; continuation: string | null }>('?scopeRef='),
  topics: (tangentKey: string) =>
    call<{ topics: TopicDto[]; continuation: string | null }>(`/tangents/${encodeURIComponent(tangentKey)}/topics`),
  readTopic: (topicKey: string) =>
    call<{ title: string; brief: string; position: string; posts: PostDto[] }>(
      `/topics/${encodeURIComponent(topicKey)}?limit=25`),
  createPost: (topicKey: string, requestId: string, text: string, replyTo?: string) =>
    call<{ postRef: string | null; url: string | null }>(`/topics/${encodeURIComponent(topicKey)}/posts`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ requestId, text, ...(replyTo ? { replyTo } : {}) }),
    }),
  report: (topicKey: string, requestId: string, postRef: string, reasonCode: string, statement: string) =>
    call<{ postRef: string; accepted: boolean; alreadyReported: boolean; caseSaturated: boolean }>(
      `/topics/${encodeURIComponent(topicKey)}/reports`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ requestId, postRef, reasonCode, statement }),
    }),
  watch: (scopeRef: string, mode: 'all' | 'replies' | 'none') =>
    call<{ scopeRef: string; mode: string }>('/watches', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ scopeRef, mode }),
    }),
  readPosition: (topicKey: string, readCursor: string) =>
    call<{ throughPostRef: string | null; topicRef: string }>(`/topics/${encodeURIComponent(topicKey)}/read-position`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ readCursor }),
    }),
}

/** The path fragment for a Post reference, for aroundPostRef-style parameters. */
export function postKeyOf(postRef: string): string {
  const parts = postRef.split('::')
  return parts[parts.length - 1] ?? postRef
}

/** The Topic key of a Post or Topic reference. */
export function topicKeyOf(ref: string): string {
  const parts = ref.split('::')
  return parts.length >= 3 ? parts[2] : parts[parts.length - 1]
}

/** The Tangent key of a Tangent reference. */
export function tangentKeyOf(ref: string): string {
  const parts = ref.split('::')
  return parts[1] ?? parts[parts.length - 1]
}
