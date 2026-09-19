/**
 * The only fetch layer in the workspace (D19). Every server call goes through here,
 * typed against the experience envelope. Never add fetch outside this module.
 */

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
  continuation: { readCursor: string | null } | null
}

export class EnvelopeError extends Error {
  readonly problem: Problem
  constructor(problem: Problem) {
    super(problem.message)
    this.problem = problem
  }
}

async function call<T>(path: string, init?: RequestInit): Promise<Envelope<T>> {
  const response = await fetch(base + path, {
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
