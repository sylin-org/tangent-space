// The composer's rules, apart from any page: a draft kept in localStorage by participant and scope, a send that
// says whether the server confirmed the Post, refused it or left it uncertain, and the reconciliation of an
// uncertain send by a GET of the Post's address for its minted TID. A send is the same request every time it is
// repeated, since the TID is its idempotency key, so a retry never makes a second Post.

// A draft: the words a person has not posted yet, the TID they will post under, the Post they reply to, and whether
// they were sent without an answer that settled them.
export interface Draft {
  id: string
  body: string
  title?: string
  parent?: string
  sent: boolean
}

// What a send or a reconciliation settles: the Post is there, at its address; the server refused it, with its words
// and whether the TID already names other words; the GET found no Post under the TID; or nothing is known.
export type Settled =
  | { kind: 'posted'; address: string }
  | { kind: 'refused'; message: string; conflict: boolean }
  | { kind: 'missing' }
  | { kind: 'uncertain' }

const alphabet = '234567abcdefghijklmnopqrstuvwxyz'
const tidShape = /^[234567abcdefghij][234567abcdefghijklmnopqrstuvwxyz]{12}$/
const clock = BigInt(Math.floor(Math.random() * 1024))
let lastMicros = 0n

// A TID minted here: microseconds since the epoch in the high bits and this page's clock id in the low ten, each
// higher than the last this page minted. The server mints the one a form is rendered with; this one replaces it
// only where that TID already names other words.
export function mintTid(now: number = Date.now()): string {
  let micros = BigInt(Math.floor(now)) * 1000n
  if (micros <= lastMicros) micros = lastMicros + 1n
  lastMicros = micros
  let value = (micros << 10n) | clock
  let tid = ''
  for (let position = 0; position < 13; position++) {
    tid = alphabet[Number(value & 31n)] + tid
    value >>= 5n
  }
  return tid
}

export function isTid(value: unknown): value is string {
  return typeof value === 'string' && tidShape.test(value)
}

// Where a participant's draft for a scope is kept: the Topic (topic:{id}), the Post a reply answers (post:{id}),
// or the Tangent a Topic starts in (tangent:{slug}).
export function draftKey(participant: string, scope: string): string {
  return `tangent:draft:${encodeURIComponent(participant)}:${scope}`
}

function store(): Storage | null {
  try {
    return window.localStorage
  } catch {
    return null
  }
}

// The draft kept under a key, or null; a value that is not a draft is removed. Without storage there is none.
export function readDraft(key: string): Draft | null {
  const storage = store()
  let text: string | null = null
  try {
    text = storage?.getItem(key) ?? null
  } catch {
    return null
  }
  if (text === null) return null
  try {
    const value = JSON.parse(text) as Partial<Draft>
    if (
      isTid(value.id) &&
      typeof value.body === 'string' &&
      (value.title === undefined || typeof value.title === 'string') &&
      (value.parent === undefined || typeof value.parent === 'string') &&
      typeof value.sent === 'boolean'
    )
      return { id: value.id, body: value.body, title: value.title, parent: value.parent, sent: value.sent }
  } catch {}
  try {
    storage?.removeItem(key)
  } catch {}
  return null
}

// Keeps a draft under a key, or removes it: a draft exists while it holds words or was sent unsettled. Without
// storage the composer still works; the words are simply not kept.
export function writeDraft(key: string, draft: Draft | null): void {
  const storage = store()
  try {
    if (draft === null || (!draft.sent && draft.body === '' && !draft.title)) storage?.removeItem(key)
    else storage?.setItem(key, JSON.stringify(draft))
  } catch {}
}

// fetch, bounded: a request that has not answered within the bound is abandoned, and its outcome is uncertain.
async function bounded(url: string, init: RequestInit, timeout: number): Promise<Response> {
  const controller = new AbortController()
  const timer = setTimeout(() => controller.abort(), timeout)
  try {
    return await fetch(url, { ...init, signal: controller.signal, credentials: 'same-origin' })
  } finally {
    clearTimeout(timer)
  }
}

// The words of a refusal the server sent as a problem: its detail, else its title.
async function problemWords(response: Response): Promise<string> {
  try {
    const problem = (await response.json()) as { detail?: unknown; title?: unknown }
    if (typeof problem.detail === 'string' && problem.detail) return problem.detail
    if (typeof problem.title === 'string' && problem.title) return problem.title
  } catch {}
  return 'Nothing was posted.'
}

// Sends a form's fields to its action as JSON is answered: a 2xx is the Post, at the address it reads; a 4xx is a
// refusal, nothing written; anything else — no answer, a timeout, a server failure — leaves the Post uncertain.
export async function send(action: string, fields: URLSearchParams, timeout: number): Promise<Settled> {
  let response: Response
  try {
    response = await bounded(action, { method: 'POST', body: fields, headers: { Accept: 'application/json' } }, timeout)
  } catch {
    return { kind: 'uncertain' }
  }
  if (response.ok) {
    try {
      const post = (await response.json()) as { address?: unknown }
      if (typeof post.address === 'string') return { kind: 'posted', address: post.address }
    } catch {}
    return response.redirected ? { kind: 'posted', address: response.url } : { kind: 'uncertain' }
  }
  if (response.status >= 400 && response.status < 500)
    return { kind: 'refused', message: await problemWords(response), conflict: response.status === 409 }
  return { kind: 'uncertain' }
}

// Asks whether a Post is at its address: found, not found, or not known.
export async function reconcile(address: string, timeout: number): Promise<Settled> {
  try {
    const response = await bounded(address, { method: 'GET', headers: { Accept: 'application/json' } }, timeout)
    if (response.ok) return { kind: 'posted', address }
    if (response.status === 404) return { kind: 'missing' }
  } catch {}
  return { kind: 'uncertain' }
}
