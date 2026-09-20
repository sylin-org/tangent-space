import { describe, expect, it, vi } from 'vitest'
import { createActivityTransport, validateSnapshot, type ActivitySnapshot } from '../src/activity/transport'

// The transport's contract, ported from activity-transport.js's header: delivery
// is explicit, the checkpoint is opaque, and a rejected snapshot stays replayable.

function snapshot(overrides: Partial<ActivitySnapshot> = {}): ActivitySnapshot {
  return {
    participantRef: 'viewer-1',
    checkpoint: 'cp-1',
    events: [],
    topics: [{ topicKey: 't1', tangentKey: 'g1', unreadCount: 2, directReplies: 0, lastSequence: 9, readSequence: 7, unreadCountCapped: false, lastPostAt: null }],
    hasMore: false,
    resetRequired: false,
    nextCursor: null,
    nextTopicCursor: null,
    ...overrides,
  }
}

function hang(): Promise<Response> {
  return new Promise(() => {})
}

function jsonOnce(body: unknown, later: () => Promise<Response> = hang): { fetch: ReturnType<typeof vi.fn>; body: Response } {
  const response = new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
  const fetch = vi.fn().mockResolvedValueOnce(response).mockImplementation(later)
  return { fetch, body: response }
}

describe('validateSnapshot', () => {
  it('accepts a well-formed snapshot', () => {
    expect(() => validateSnapshot(snapshot())).not.toThrow()
  })

  it('rejects malformed shapes and duplicate topics', () => {
    expect(() => validateSnapshot({ ...snapshot(), participantRef: '' })).toThrow('invalid-snapshot')
    expect(() => validateSnapshot({ ...snapshot(), hasMore: true, nextCursor: null })).toThrow('invalid-snapshot')
    expect(() => validateSnapshot({ ...snapshot(), topics: [...snapshot().topics!, { ...snapshot().topics![0]! }] })).toThrow('invalid-topic')
    expect(() => validateSnapshot({ ...snapshot(), checkpoint: 'has space' })).toThrow('invalid-snapshot')
  })
})

describe('the activity transport', () => {
  it('fetches a snapshot, delivers it, and carries the checkpoint on the next request', async () => {
    const delivered: ActivitySnapshot[] = []
    const first = jsonOnce(snapshot())
    const transport = createActivityTransport({
      fetch: first.fetch as unknown as typeof fetch,
      supportsStreaming: false,
      onSnapshot: value => {
        delivered.push(value)
        return true
      },
    })
    transport.start({ participant: 'viewer-1' })
    await new Promise(resolve => setTimeout(resolve, 20))
    expect(delivered.length).toBe(1)
    expect(first.fetch).toHaveBeenCalledWith('/api/activity', expect.objectContaining({ method: 'GET' }))
    // The loop's next request is the poll hold, carrying the accepted checkpoint.
    const secondPath = String(first.fetch.mock.calls[1]?.[0])
    expect(secondPath).toBe('/api/activity/wait?cursor=cp-1')
    transport.stop()
  })

  it('keeps the checkpoint replayable when the consumer rejects the snapshot', async () => {
    let accept = false
    const body = snapshot({ hasMore: true, nextCursor: 'cp-1' })
    const fetch = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) => new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    const transport = createActivityTransport({
      fetch: fetch as unknown as typeof fetch,
      supportsStreaming: false,
      onSnapshot: () => accept,
      timings: { retryMinMs: 10, retryMaxMs: 30, idlePollMs: 5 },
    })
    transport.start({ participant: 'viewer-1' })
    await new Promise(resolve => setTimeout(resolve, 20))
    expect(delivered_count()).toBe(0)
    // The retry after a rejection asks again without a cursor: nothing was accepted.
    expect(String(fetch.mock.calls[1]?.[0] ?? '')).toBe('/api/activity/wait')
    accept = true
    await new Promise(resolve => setTimeout(resolve, 60))
    expect(delivered_count()).toBe(1)
    transport.stop()

    function delivered_count(): number {
      return fetch.mock.calls.filter(call => String(call[0]).includes('cursor=cp-1')).length > 0 ? 1 : 0
    }
  })

  it('treats authorization failures as an identity change, not a retry', async () => {
    const identities: Array<{ reason: string; status?: number }> = []
    const response = new Response('{}', { status: 401 })
    const fetch = vi.fn().mockResolvedValue(response)
    const transport = createActivityTransport({
      fetch: fetch as unknown as typeof fetch,
      supportsStreaming: false,
      onSnapshot: () => true,
      onIdentity: detail => identities.push(detail),
    })
    transport.start({ participant: 'viewer-1' })
    await new Promise(resolve => setTimeout(resolve, 20))
    expect(identities).toEqual([{ reason: 'authorization', status: 401 }])
    expect(transport.snapshot().desired).toBe(false)
  })

  it('re-signs in as another participant only through a fresh snapshot', async () => {
    const delivered: ActivitySnapshot[] = []
    const first = jsonOnce(snapshot({ participantRef: 'someone-else' }))
    const transport = createActivityTransport({
      fetch: first.fetch as unknown as typeof fetch,
      supportsStreaming: false,
      onSnapshot: value => {
        delivered.push(value)
        return true
      },
    })
    transport.start({ participant: 'viewer-1' })
    await new Promise(resolve => setTimeout(resolve, 20))
    expect(delivered.length).toBe(0)
    expect(transport.snapshot().desired).toBe(false)
  })

  it('pauses while hidden and keeps the checkpoint', async () => {
    const first = jsonOnce(snapshot())
    const transport = createActivityTransport({
      fetch: first.fetch as unknown as typeof fetch,
      supportsStreaming: false,
      onSnapshot: () => true,
    })
    transport.start({ participant: 'viewer-1' })
    await new Promise(resolve => setTimeout(resolve, 20))
    transport.setVisible(false)
    expect(transport.snapshot()).toMatchObject({ status: 'paused', checkpoint: 'cp-1', visible: false })
    transport.stop()
  })
})
