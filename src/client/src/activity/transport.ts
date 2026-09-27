import { httpRequest } from '../api'
// Participant activity runtime, ported from activity-transport.js: one connection
// that cycles snapshot → SSE → poll with watchdogs, stream rotation, retry backoff,
// and identity handling. Delivery is explicit: onSnapshot must await all
// acceptance-critical work; false/rejection/timeout leaves the checkpoint replayable.

export interface ActivityEvent {
  sequence: string
  kind: string
  topicKey: string
  tangentKey: string
  actorParticipantId?: string | null
  targetParticipantId?: string | null
  postSequence?: number | null
  occurredAt: string
}

export interface ActivityTopic {
  topicKey: string
  tangentKey: string
  unreadCount: number
  directReplies: number
  lastSequence: number
  readSequence: number
  unreadCountCapped: boolean
  lastPostAt?: string | null
}

export interface ActivitySnapshot {
  participantRef: string
  checkpoint: string
  events: ActivityEvent[]
  topics: ActivityTopic[]
  hasMore: boolean
  resetRequired: boolean
  nextCursor?: string | null
  nextTopicCursor?: string | null
  topicsTruncated?: boolean
  topicsHasMore?: boolean
  topicsIncomplete?: boolean
}

export type ActivityMode = 'snapshot' | 'sse' | 'poll' | 'stopped'
export type ActivityStatus = 'connecting' | 'live' | 'reconnecting' | 'paused' | 'stopped' | 'identity' | 'error'

export interface ActivityState {
  mode: ActivityMode
  status: ActivityStatus
  reason?: string
  retryInMs?: number
  participant: string | null
  generation: number
  checkpoint: string | null
}

export interface SnapshotContext {
  participant: string
  generation: number
  signal: AbortSignal
  isCurrent(): boolean
}

export interface TransportOptions {
  fetch?: typeof fetch
  onSnapshot(value: ActivitySnapshot, context: SnapshotContext): boolean | void | Promise<boolean | void>
  onIdentity?(detail: { reason: string; status?: number; participantRef?: string }): void
  onState?(state: ActivityState): void
  onEvent?(name: string, value: unknown): void
  streamQuery?(...args: unknown[]): unknown
  supportsStreaming?: boolean
  timers?: { setTimeout: typeof setTimeout; clearTimeout: typeof clearTimeout }
  now?: () => number
  random?: () => number
  timings?: Partial<typeof DEFAULTS>
}

const MAX_JSON = 512 * 1024
const MAX_FRAME = 512 * 1024
const MAX_CURSOR = 4096
const DEFAULTS = {
  connectMs: 10000,
  watchdogMs: 45000,
  pollTimeoutMs: 45000,
  callbackMs: 15000,
  reprobeMs: 60000,
  streamLifetimeMs: 60000,
  retryMinMs: 1000,
  retryMaxMs: 30000,
  idlePollMs: 250,
}

type Obj = Record<string, any>

const object = (value: unknown): value is Obj => value !== null && typeof value === 'object' && !Array.isArray(value)
const string = (value: unknown, max = 4096): value is string => typeof value === 'string' && value.length <= max
const cursor = (value: unknown): value is string => string(value, MAX_CURSOR) && value.length > 0 && !/[\u0000-\u0020]/.test(value)
const nullableString = (value: unknown): boolean => value == null || string(value)
const integer = (value: unknown): boolean => Number.isSafeInteger(value) && (value as number) >= 0

export function validateSnapshot(value: unknown): ActivitySnapshot {
  const v = value as Obj
  if (
    !object(v) || !string(v.participantRef) || !v.participantRef || !cursor(v.checkpoint) || !Array.isArray(v.events) || v.events.length > 25
    || !Array.isArray(v.topics) || v.topics.length > 100 || typeof v.hasMore !== 'boolean'
    || typeof v.resetRequired !== 'boolean' || !(v.nextCursor == null || cursor(v.nextCursor))
    || (v.hasMore && v.nextCursor !== v.checkpoint)
    || (!v.hasMore && v.nextCursor != null)
    || !(v.nextTopicCursor == null || cursor(v.nextTopicCursor))
  ) throw new Error('invalid-snapshot')
  for (const name of ['topicsTruncated', 'topicsHasMore', 'topicsIncomplete'] as const) {
    if (v[name] !== undefined && typeof v[name] !== 'boolean') throw new Error('invalid-snapshot')
  }
  for (const event of v.events) {
    if (
      !object(event) || !string(event.sequence, 80) || !event.sequence || !string(event.kind, 128) || !event.kind
      || !string(event.topicKey, 1024) || !string(event.tangentKey, 1024) || !nullableString(event.actorParticipantId)
      || !nullableString(event.targetParticipantId) || !(event.postSequence == null || integer(event.postSequence))
      || !string(event.occurredAt, 80)
    ) throw new Error('invalid-event')
  }
  const seen = new Set<string>()
  for (const topic of v.topics) {
    if (
      !object(topic) || !string(topic.topicKey, 1024) || !topic.topicKey || seen.has(topic.topicKey)
      || !string(topic.tangentKey, 1024) || !integer(topic.unreadCount) || !integer(topic.directReplies)
      || !integer(topic.lastSequence) || !integer(topic.readSequence) || typeof topic.unreadCountCapped !== 'boolean'
      || !nullableString(topic.lastPostAt)
    ) throw new Error('invalid-topic')
    seen.add(topic.topicKey)
  }
  return value as ActivitySnapshot
}

interface Run {
  generation: number
  participant: string
  controller: AbortController
  request: {
    controller: AbortController
    reader: ReadableStreamDefaultReader<Uint8Array> | null
    response: Response | null
    timeoutReason: string | null
    timer: ReturnType<typeof setTimeout> | null
  } | null
  failures: number
  streamEnabled: boolean
}

interface StateInternal {
  mode: ActivityMode
  status: ActivityStatus
  reason?: string
  retryInMs?: number
}

export function createActivityTransport(options: TransportOptions) {
  const fetcher = options.fetch ?? ((path: RequestInfo | URL, init?: RequestInit) => httpRequest(path, init))
  if (typeof options.onSnapshot !== 'function') throw new TypeError('onSnapshot is required.')
  const timers = options.timers ?? null
  const rawSetTimer = timers?.setTimeout ?? setTimeout
  const rawClearTimer = timers?.clearTimeout ?? clearTimeout
  const setTimer = rawSetTimer.bind(timers ?? globalThis) as typeof setTimeout
  const clearTimer = rawClearTimer.bind(timers ?? globalThis) as typeof clearTimeout
  const now = options.now ?? Date.now
  const random = options.random ?? Math.random
  const timing = { ...DEFAULTS, ...options.timings }
  for (const [name, value] of Object.entries(timing)) {
    if (!(name in DEFAULTS) || !Number.isFinite(value) || value <= 0 || value > 300000) throw new TypeError('Invalid timing: ' + name)
  }
  if (timing.retryMaxMs < timing.retryMinMs) throw new TypeError('Invalid retry bounds.')
  const encoder = new TextEncoder()
  let participant: string | null = null
  let checkpoint: string | null = null
  let generation = 0
  let desired = false
  let visible = true
  let running: Run | null = null
  let cursorRecovered = false
  let state: StateInternal = { mode: 'stopped', status: 'stopped' }

  const current = (run: Run | null): boolean => running === run && run !== null && run.generation === generation && !run.controller.signal.aborted

  const ignore = (callback: () => unknown): void => {
    try {
      Promise.resolve(callback()).catch(() => {})
    } catch {
      // UI callbacks do not own transport cleanup.
    }
  }

  function emit(run: Run | null, next: StateInternal): void {
    if (run && !current(run)) return
    state = { ...next }
    if (options.onState) ignore(() => options.onState!({ ...state, participant, generation, checkpoint }))
  }

  function halt(status: ActivityStatus, reason?: string): void {
    const old = running
    running = null
    generation++
    if (old) old.controller.abort()
    emit(null, { mode: 'stopped', status, ...(reason ? { reason } : {}) })
  }

  function identity(run: Run | null, detail: { reason: string; status?: number; participantRef?: string }): void {
    if (run && !current(run)) return
    desired = false
    checkpoint = null
    halt('identity', detail.reason)
    const stoppedGeneration = generation
    // onState may synchronously start a replacement. Do not send an old identity notification into it.
    if (generation === stoppedGeneration && !running && options.onIdentity) ignore(() => options.onIdentity!(detail))
  }

  function guarded<T>(promise: Promise<T>, signal: AbortSignal, timeoutMs?: number, reason = 'timeout'): Promise<T> {
    return new Promise<T>((resolve, reject) => {
      let timer: ReturnType<typeof setTimeout> | undefined
      let settled = false
      const finish = (callback: (value: T) => void, value: T) => {
        if (settled) return
        settled = true
        if (timer) clearTimer(timer)
        signal.removeEventListener('abort', abort)
        callback(value)
      }
      const abort = () => finish(() => reject(new Error('aborted')), undefined as unknown as T)
      if (signal.aborted) {
        Promise.resolve(promise).catch(() => {})
        return abort()
      }
      signal.addEventListener('abort', abort, { once: true })
      if (timeoutMs) timer = setTimer(() => finish(() => reject(new Error(reason)), undefined as unknown as T), timeoutMs)
      Promise.resolve(promise).then(value => finish(resolve, value), (error: Error) => finish(() => reject(error), undefined as unknown as T))
    })
  }

  const sleep = (run: Run, ms: number): Promise<void> =>
    guarded(
      new Promise<void>(resolve => {
        const abort = () => {
          clearTimer(id)
          resolve()
        }
        const id = setTimer(() => {
          run.controller.signal.removeEventListener('abort', abort)
          resolve()
        }, ms)
        run.controller.signal.addEventListener('abort', abort, { once: true })
      }),
      run.controller.signal,
    )

  function disposeReader(reader: ReadableStreamDefaultReader<Uint8Array> | null): void {
    if (!reader) return
    try {
      Promise.resolve(reader.cancel()).catch(() => {}).finally(() => {
        try {
          reader.releaseLock()
        } catch {
          /* already released */
        }
      })
    } catch {
      try {
        reader.releaseLock()
      } catch {
        /* already released */
      }
    }
  }

  function disposeBody(response: Response | null): void {
    try {
      const body = (response as (Response & { body?: ReadableStream<Uint8Array> }) | null)?.body
      if (body && !body.locked) Promise.resolve(body.cancel()).catch(() => {})
    } catch {
      /* the body is already gone */
    }
  }

  async function accept(run: Run, req: NonNullable<Run['request']>, value: unknown): Promise<void> {
    if (!current(run)) throw new Error('aborted')
    if (object(value) && string(value.participantRef) && value.participantRef && value.participantRef !== run.participant) {
      identity(run, { reason: 'participant-mismatch', participantRef: value.participantRef })
      throw new Error('aborted')
    }
    const snapshot = validateSnapshot(value)
    const acceptedCheckpoint = snapshot.checkpoint
    if (snapshot.hasMore && acceptedCheckpoint === checkpoint) throw new Error('non-progressing-cursor')
    let deliveryValid = true
    // Consumers await their invalidation work. Request cancellation/timeout also cancels that work.
    const context: SnapshotContext = {
      participant: run.participant,
      generation: run.generation,
      signal: req.controller.signal,
      isCurrent: () => deliveryValid && current(run) && !req.controller.signal.aborted,
    }
    try {
      const accepted = await guarded(
        Promise.resolve().then(() => {
          if (!context.isCurrent()) throw new Error('aborted')
          return options.onSnapshot(snapshot, context)
        }),
        req.controller.signal,
        timing.callbackMs,
        'snapshot-callback-timeout',
      )
      if (accepted === false) throw new Error('snapshot-rejected')
      if (!context.isCurrent() || req.controller.signal.aborted) throw new Error('aborted')
      checkpoint = acceptedCheckpoint
      run.failures = 0
    } catch (error) {
      deliveryValid = false
      throw error
    }
  }

  async function json(run: Run, req: NonNullable<Run['request']>, response: Response): Promise<Obj> {
    const length = response.headers?.get('content-length')
    if (length && (!/^\d+$/.test(length) || Number(length) > MAX_JSON)) throw new Error('json-too-large')
    let text = ''
    const body = response.body as ReadableStream<Uint8Array> | null
    if (body && typeof body.getReader === 'function') {
      req.reader = body.getReader()
      const decoder = new TextDecoder('utf-8', { fatal: true })
      let bytes = 0
      while (current(run)) {
        const packet = await guarded(req.reader.read(), req.controller.signal)
        if (packet.done) {
          text += decoder.decode()
          break
        }
        bytes += packet.value.byteLength
        if (bytes > MAX_JSON) throw new Error('json-too-large')
        text += decoder.decode(packet.value, { stream: true })
      }
    } else {
      // Older fetch implementations buffer text internally. Limit headers first, then bytes before JSON parsing.
      text = await guarded(response.text(), req.controller.signal)
      if (text.length > MAX_JSON || encoder.encode(text).byteLength > MAX_JSON) throw new Error('json-too-large')
    }
    if (!current(run)) throw new Error('aborted')
    const value = JSON.parse(text)
    await accept(run, req, value)
    return value as Obj
  }

  async function stream(run: Run, req: NonNullable<Run['request']>, response: Response, touch: () => void): Promise<void> {
    const body = response.body as ReadableStream<Uint8Array> | null
    if (!body || typeof body.getReader !== 'function') {
      run.streamEnabled = false
      throw new Error('stream-unavailable')
    }
    if (!/^text\/event-stream(?:\s*;|$)/i.test(response.headers?.get('content-type') || '')) throw new Error('invalid-stream-type')
    req.reader = body.getReader()
    const decoder = new TextDecoder('utf-8', { fatal: true })
    let line = ''
    let frameBytes = 0
    let event = 'message'
    let data: string[] = []
    let skipLF = false

    async function completeLine(): Promise<void> {
      if (!line) {
        const name = event
        const payload = data.join('\n')
        event = 'message'
        data = []
        frameBytes = 0
        if (name === 'identity_changed') {
          let detail: Obj = {}
          try {
            const parsed = JSON.parse(payload)
            if (object(parsed)) detail = parsed
          } catch {
            /* an unreadable identity frame still ends the identity */
          }
          identity(run, { ...detail, reason: 'identity_changed' })
          return
        }
        if ((name === 'activity' || name === 'reset') && payload) {
          await accept(run, req, JSON.parse(payload))
          emit(run, { mode: 'sse', status: 'live' })
        } else if (name === 'activity' || name === 'reset') throw new Error('empty-activity-frame')
        else if (name === 'profile' && payload && options.onEvent) {
          const value = JSON.parse(payload)
          if (!object(value)) throw new Error('invalid-profile-frame')
          ignore(() => options.onEvent!(name, value))
        }
        return
      }
      if (!line.startsWith(':')) {
        const colon = line.indexOf(':')
        const field = colon < 0 ? line : line.slice(0, colon)
        let value = colon < 0 ? '' : line.slice(colon + 1)
        if (value.startsWith(' ')) value = value.slice(1)
        if (field === 'event') event = value
        else if (field === 'data') data.push(value)
        // id/retry are deliberately ignored: only an accepted payload can advance the opaque checkpoint.
      }
      line = ''
    }

    while (current(run)) {
      const packet = await guarded(req.reader!.read(), req.controller.signal)
      if (packet.done) throw new Error('stream-ended')
      if (packet.value.byteLength > MAX_FRAME * 2) throw new Error('stream-chunk-too-large')
      if (packet.value.byteLength) touch()
      const chunk = decoder.decode(packet.value, { stream: true })
      let from = 0
      for (let index = 0; index < chunk.length; index++) {
        const char = chunk[index]
        if (skipLF) {
          skipLF = false
          if (char === '\n') {
            from = index + 1
            continue
          }
        }
        if (char !== '\n' && char !== '\r') continue
        const part = chunk.slice(from, index)
        line += part
        frameBytes += encoder.encode(part).byteLength + 1
        if (frameBytes > MAX_FRAME) throw new Error('frame-too-large')
        await completeLine()
        if (!current(run)) return
        skipLF = char === '\r'
        from = index + 1
      }
      const part = chunk.slice(from)
      line += part
      frameBytes += encoder.encode(part).byteLength
      if (frameBytes > MAX_FRAME) throw new Error('frame-too-large')
    }
  }

  async function request(run: Run, mode: 'snapshot' | 'sse' | 'poll', deadline: number | null): Promise<Obj | undefined> {
    if (!current(run)) throw new Error('aborted')
    if (run.request) throw new Error('duplicate-activity-request')
    const req: NonNullable<Run['request']> = { controller: new AbortController(), reader: null, response: null, timeoutReason: null, timer: null }
    run.request = req
    const abort = () => req.controller.abort()
    run.controller.signal.addEventListener('abort', abort, { once: true })
    const arm = (ms: number, reason: string) => {
      clearTimer(req.timer ?? undefined)
      req.timer = setTimer(() => {
        req.timeoutReason = reason
        req.controller.abort()
      }, ms)
    }
    const suffix = mode === 'sse' ? '/events' : mode === 'poll' ? '/wait' : ''
    let path = '/api/activity' + suffix + (checkpoint ? '?cursor=' + encodeURIComponent(checkpoint) : '')
    if (mode === 'sse' && options.streamQuery) {
      const extra = String(options.streamQuery() || '')
      if (extra.length > 16384 || /[\u0000-\u001f#]/.test(extra)) throw new Error('invalid-stream-query')
      if (extra) path += (path.includes('?') ? '&' : '?') + extra
    }
    const requestUntil = now() + timing.pollTimeoutMs
    try {
      // /wait intentionally holds its response headers for up to 15 seconds on the server.
      const headerBudget = mode === 'poll' ? timing.pollTimeoutMs : timing.connectMs
      arm(Math.min(headerBudget, deadline ? Math.max(1, deadline - now()) : Infinity), deadline && deadline - now() <= headerBudget ? 'reprobe' : 'connect-timeout')
      const response = await guarded(
        Promise.resolve()
          .then(() => {
            if (req.controller.signal.aborted) throw new Error('aborted')
            return fetcher(path, {
              method: 'GET',
              credentials: 'same-origin',
              cache: 'no-store',
              signal: req.controller.signal,
              headers: { Accept: mode === 'sse' ? 'text/event-stream' : 'application/json' },
            })
          })
          .then(value => {
            if (req.controller.signal.aborted) {
              disposeBody(value as Response)
              throw new Error('aborted')
            }
            return value as Response
          }),
        req.controller.signal,
      )
      req.response = response
      if (!response.ok) {
        const error = new Error('http-' + response.status) as Error & { status?: number }
        error.status = response.status
        throw error
      }
      if (!current(run)) throw new Error('aborted')
      if (mode === 'sse') {
        const rotateAt = now() + timing.streamLifetimeMs
        const touch = () => {
          const remaining = Math.max(1, rotateAt - now())
          arm(Math.min(timing.watchdogMs, remaining), remaining <= timing.watchdogMs ? 'stream-rotation' : 'stream-stalled')
        }
        touch()
        emit(run, { mode, status: 'connecting' })
        await stream(run, req, response, touch)
      } else {
        const budget = deadline ? Math.max(1, deadline - now()) : Infinity
        const remaining = mode === 'poll' ? Math.max(1, requestUntil - now()) : timing.pollTimeoutMs
        arm(Math.min(remaining, budget), budget <= remaining ? 'reprobe' : 'body-timeout')
        const value = await json(run, req, response)
        emit(run, { mode, status: 'live' })
        return value
      }
    } catch (error) {
      if (req.timeoutReason) throw new Error(req.timeoutReason)
      throw error
    } finally {
      clearTimer(req.timer ?? undefined)
      req.controller.abort()
      run.controller.signal.removeEventListener('abort', abort)
      disposeReader(req.reader)
      if (!req.reader) disposeBody(req.response)
      if (current(run) && run.request === req) run.request = null
    }
  }

  async function loop(run: Run): Promise<void> {
    let mode: 'snapshot' | 'sse' | 'poll' = 'snapshot'
    let reprobeAt = Infinity
    while (current(run)) {
      if (mode === 'poll' && run.streamEnabled && now() >= reprobeAt) mode = 'sse'
      emit(run, { mode, status: mode === 'poll' ? (state.mode === 'poll' && state.status === 'live' ? 'live' : 'reconnecting') : 'connecting' })
      try {
        const value = await request(run, mode, mode === 'poll' && run.streamEnabled ? reprobeAt : null)
        if (!current(run)) return
        if (mode === 'snapshot') {
          mode = run.streamEnabled ? 'sse' : 'poll'
          continue
        }
        if (mode === 'poll') {
          await sleep(run, Math.min(value?.hasMore ? 1 : timing.idlePollMs, Math.max(1, reprobeAt - now())))
          continue
        }
      } catch (error) {
        if (!current(run)) return
        const err = error as Error & { status?: number }
        if ([401, 403].includes(err.status ?? 0)) {
          identity(run, { reason: 'authorization', status: err.status })
          return
        }
        if (err.status === 400) {
          if (checkpoint && !cursorRecovered) {
            cursorRecovered = true
            checkpoint = null
            mode = 'snapshot'
            continue
          }
          desired = false
          halt('error', 'invalid-cursor')
          return
        }
        if (err.message === 'reprobe' && run.streamEnabled) {
          mode = 'sse'
          continue
        }
        if (err.message === 'stream-rotation') {
          mode = 'snapshot'
          continue
        }
        if (mode !== 'poll') reprobeAt = run.streamEnabled ? now() + timing.reprobeMs : Infinity
        mode = 'poll'
        run.failures = Math.min(run.failures + 1, 16)
        const jitter = 0.75 + Math.max(0, Math.min(1, Number(random()) || 0)) * 0.5
        const retryInMs = Math.min(timing.retryMaxMs, Math.max(1, timing.retryMinMs * 2 ** (run.failures - 1) * jitter))
        emit(run, { mode, status: 'reconnecting', reason: err.message, retryInMs })
        try {
          await sleep(run, Math.min(retryInMs, Math.max(1, reprobeAt - now())))
        } catch {
          return
        }
      }
    }
  }

  function activate(): void {
    if (!desired || !visible || !participant || running) return
    const run: Run = {
      generation: ++generation,
      participant,
      controller: new AbortController(),
      request: null,
      failures: 0,
      streamEnabled: options.supportsStreaming ?? typeof ReadableStream !== 'undefined',
    }
    running = run
    loop(run).catch(error => {
      if (current(run)) {
        desired = false
        halt('error', (error as Error).message)
      }
    })
  }

  return Object.freeze({
    start({ participant: next }: { participant: string }) {
      if (!string(next) || !next) throw new TypeError('A participant reference is required.')
      if (next !== participant) {
        checkpoint = null
        cursorRecovered = false
        participant = next
        halt('stopped')
      }
      desired = true
      activate()
    },
    stop() {
      desired = false
      halt('stopped')
    },
    reconnect() {
      if (!desired || !participant) return
      halt('stopped')
      desired = true
      activate()
    },
    setVisible(value: boolean) {
      const next = Boolean(value)
      if (next === visible) return
      visible = next
      if (!visible) halt('paused', 'hidden')
      else activate()
    },
    snapshot() {
      return { ...state, participant, generation, checkpoint, visible, desired, requestActive: Boolean(running?.request) }
    },
  })
}

export type ActivityTransport = ReturnType<typeof createActivityTransport>
