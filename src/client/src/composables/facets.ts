import { httpRequest } from '../api'
// Facet composer and renderer: @-autocomplete
// bookkeeping with client-minted byte-range facets, and the read-time renderer
// with fresh labels.

const encoder = new TextEncoder()
const utf8Size = (code: number) => (code < 0x80 ? 1 : code < 0x800 ? 2 : code < 0x10000 ? 3 : 4)
export const byteLength = (text: string) => encoder.encode(text).length

/** Char index where the given UTF-8 byte offset lands. */
export function charIndexAtByte(text: string, byteOffset: number): number {
  let bytes = 0
  let index = 0
  while (index < text.length) {
    if (bytes >= byteOffset) return index
    const code = text.codePointAt(index)!
    bytes += utf8Size(code)
    index += code > 0xffff ? 2 : 1
  }
  return text.length
}

export interface MentionTarget {
  kind: string
  label: string
  insert: string
  value?: string
  did?: string
  count?: number
  badge?: string
  classification?: string
}

export interface DraftFacet {
  kind: 'mention' | 'group' | 'tag'
  start: number
  end: number
  did?: string
  value?: string
  label: string
}

export interface WireFacet {
  kind: string
  start: number
  end: number
  did?: string
  value?: string
}

const draftFacets = new Map<string, DraftFacet[]>()

function facetsOf(key: string): DraftFacet[] {
  if (!draftFacets.has(key)) draftFacets.set(key, [])
  return draftFacets.get(key)!
}

/** Drop facets whose range no longer matches their recorded label; a degraded mention becomes plain text. */
export function sizeFacets(topicKey: string, text: string): void {
  const list = facetsOf(topicKey)
  const kept = list.filter(facet => {
    const from = charIndexAtByte(text, facet.start)
    const to = charIndexAtByte(text, facet.end)
    return text.slice(from, to) === '@' + facet.label
  })
  draftFacets.set(topicKey, kept)
}

/** The wire package for the current draft: validated facets only. */
export function wireFacets(topicKey: string, text: string): WireFacet[] {
  return facetsOf(topicKey || '')
    .filter(facet => {
      const from = charIndexAtByte(text, facet.start)
      const to = charIndexAtByte(text, facet.end)
      return text.slice(from, to) === '@' + facet.label
    })
    .map(facet => ({
      kind: facet.kind,
      start: facet.start,
      end: facet.end,
      ...(facet.did ? { did: facet.did } : {}),
      ...(facet.value ? { value: facet.value } : {}),
    }))
}

export function clearFacets(topicKey: string): void {
  draftFacets.delete(topicKey)
}

export function clearAllFacets(): void {
  draftFacets.clear()
}

/** Insert a reply mention for a known author; ranges of existing facets shift right. */
export function replyMention(topicKey: string, text: string, authorValue: string, handle: string): { text: string; caret: number } | null {
  if (!topicKey || !handle) return null
  const insert = '@' + handle + ' '
  if (text.startsWith(insert)) return null
  for (const facet of facetsOf(topicKey)) {
    facet.start += byteLength(insert)
    facet.end += byteLength(insert)
  }
  const next = insert + text
  facetsOf(topicKey).push({ kind: 'mention', start: 0, end: byteLength(insert.trim()), did: authorValue, label: handle })
  sizeFacets(topicKey, next)
  return { text: next, caret: insert.length }
}

/** Record a chosen mention against the draft at its byte range. */
export function recordFacet(topicKey: string, text: string, tokenStart: number, entry: MentionTarget, insert: string): void {
  const byteStart = byteLength(text.slice(0, tokenStart))
  facetsOf(topicKey).push({
    kind: entry.kind === 'group' ? 'group' : 'mention',
    start: byteStart,
    end: byteStart + byteLength(insert),
    ...(entry.kind === 'group' ? { value: entry.value } : { did: entry.did }),
    label: entry.insert,
  })
}

// ----- renderer -----

export interface ResolvedIdentity {
  handle?: string
  displayName?: string
  value?: string
  profileUrl?: string
  classification?: string
}

export interface TextSegment {
  kind: 'text' | 'mention' | 'group' | 'tag'
  text: string
  href?: string
  title?: string
}

/** The message as verbatim words with facet ranges decorated; mentions show fresh labels. */
export function facetedSegments(text: string, facets: unknown, resolved?: Record<string, ResolvedIdentity>): TextSegment[] {
  if (!Array.isArray(facets) || !facets.length) return [{ kind: 'text', text }]
  const segments: TextSegment[] = []
  const ordered = [...(facets as WireFacet[])].sort((left, right) => left.start - right.start)
  let byteCursor = 0
  for (const facet of ordered) {
    const from = charIndexAtByte(text, byteCursor)
    const start = charIndexAtByte(text, facet.start)
    const end = charIndexAtByte(text, facet.end)
    if (facet.start < byteCursor || end <= start) continue // overlap or stale: skip
    if (start > from) segments.push({ kind: 'text', text: text.slice(from, start) })
    const segment = text.slice(start, end)
    if (facet.kind === 'mention' && facet.did) {
      const resolution = resolved?.[facet.did]
      const href = resolution?.profileUrl?.startsWith('/u/') ? resolution.profileUrl : '/u/' + encodeURIComponent(resolution?.value || facet.did)
      const label = resolution?.handle ? '@' + resolution.handle.replace(/^@/, '') : resolution?.displayName ? '@' + resolution.displayName : segment
      segments.push({ kind: 'mention', text: label, href, title: resolution?.classification ? resolution.classification + (resolution.handle ? ' · ' + resolution.handle : '') : undefined })
    } else if (facet.kind === 'group') {
      segments.push({ kind: 'group', text: segment, title: 'Role group: resolves to its current holders' })
    } else if (facet.kind === 'tag') {
      segments.push({ kind: 'tag', text: segment })
    } else {
      segments.push({ kind: 'text', text: segment })
    }
    byteCursor = facet.end
  }
  const tail = charIndexAtByte(text, byteCursor)
  if (tail < text.length) segments.push({ kind: 'text', text: text.slice(tail) })
  return segments
}

// ----- mention lookup -----

const mentionCache = new Map<string, { at: number; list: MentionTarget[] }>()

export async function mentionables(topicKey: string, prefix?: string): Promise<MentionTarget[]> {
  const cacheKey = topicKey + ':' + (prefix || '')
  const fresh = mentionCache.get(cacheKey)
  if (fresh && Date.now() - fresh.at < 30000) return fresh.list
  const response = await httpRequest('/api/v1/experience/topics/' + encodeURIComponent(topicKey) + '/mentionables?prefix=' + encodeURIComponent(prefix || ''), {
    credentials: 'same-origin',
    cache: 'no-store',
    headers: { Accept: 'application/json' },
  })
  const data = await response.json().catch(() => null)
  if (!response.ok) throw new Error('mentionables-failed')
  const list = (data as { targets?: MentionTarget[] })?.targets ?? []
  mentionCache.set(cacheKey, { at: Date.now(), list })
  return list
}
