// The live updater's transport, apart from any page: one connection that follows the log for a page, as the stream
// while it can, as the poll at the same address when the stream fails, and as the door when the poll fails too,
// going back to the stream every so often. It guards every request with watchdogs, caps what it reads, rotates the
// stream, backs off between failures and pauses while the page is hidden. Delivery is explicit: onEvents awaits all
// the work a page does with the rows, and the position on the log advances only once it has accepted them, so a
// rejected or late delivery is asked for again. Nothing it sends writes: it only reads.

export interface LiveEvent {
  seq: number
  kind: string
  tangent?: string | null
  topic?: string | null
  post?: string | null
  actor?: string | null
  target?: string | null
  version?: number | null
  at: string
  audience?: string | null
}

export interface PollPage {
  since: number
  through: number
  more: boolean
  events: LiveEvent[]
  next: string
}

export type LiveMode = 'stream' | 'poll' | 'door' | 'stopped'
export type LiveStatus = 'connecting' | 'live' | 'reconnecting' | 'paused' | 'stopped' | 'gated' | 'error'

export interface LiveState {
  mode: LiveMode
  status: LiveStatus
  reason?: string
  retryInMs?: number
  since: number | null
  generation: number
}

export interface DeliveryContext {
  generation: number
  signal: AbortSignal
  isCurrent(): boolean
}

export interface TransportOptions {
  // The stream's and the poll's address, and the door's, both absolute.
  events: string
  door: string
  fetch?: typeof fetch
  // Rows past the position, oldest first; false or a rejection leaves them to be asked for again.
  onEvents(events: LiveEvent[], context: DeliveryContext): boolean | void | Promise<boolean | void>
  // The door says the log moved past the position, without naming the rows.
  onMoved?(context: DeliveryContext): boolean | void | Promise<boolean | void>
  onState?(state: LiveState): void
  supportsStreaming?: boolean
  timers?: { setTimeout: typeof setTimeout; clearTimeout: typeof clearTimeout }
  now?: () => number
  random?: () => number
  timings?: Partial<typeof DEFAULTS>
}

// The most rows a poll page holds, as the server bounds it.
export const PAGE_SIZE = 50
const MAX_JSON = 1024 * 1024
const MAX_FRAME = 64 * 1024
const DEFAULTS = {
  connectMs: 10000,
  watchdogMs: 45000,
  requestMs: 30000,
  callbackMs: 15000,
  reprobeMs: 60000,
  streamLifetimeMs: 120000,
  retryMinMs: 1000,
  retryMaxMs: 30000,
  pollMs: 5000,
  doorMs: 15000,
}

type Obj = Record<string, any>
type Mode = Exclude<LiveMode, 'stopped'>

const object = (value: unknown): value is Obj => value !== null && typeof value === 'object' && !Array.isArray(value)
const string = (value: unknown, max = 4096): value is string => typeof value === 'string' && value.length <= max
const nullableString = (value: unknown, max = 4096): boolean => value == null || string(value, max)
const sequence = (value: unknown): value is number => Number.isSafeInteger(value) && (value as number) >= 0

export function validateEvent(value: unknown): LiveEvent {
  const v = value as Obj
  if (
    !object(v) || !sequence(v.seq) || !string(v.kind, 128) || !v.kind || !string(v.at, 80)
    || !nullableString(v.tangent, 1024) || !nullableString(v.topic, 1024) || !nullableString(v.post, 1024)
    || !nullableString(v.actor) || !nullableString(v.target) || !(v.version == null || sequence(v.version))
    || !nullableString(v.audience, 64)
  ) throw new Error('invalid-event')
  return v as LiveEvent
}

export function validatePage(value: unknown): PollPage {
  const v = value as Obj
  if (
    !object(v) || !sequence(v.since) || !sequence(v.through) || v.through < v.since || typeof v.more !== 'boolean'
    || !Array.isArray(v.events) || v.events.length > PAGE_SIZE || !string(v.next)
  ) throw new Error('invalid-page')
  let last = v.since
  for (const event of v.events) {
    validateEvent(event)
    if (event.seq <= last || event.seq > v.through) throw new Error('invalid-page')
    last = event.seq
  }
  return v as PollPage
}

// The door's word on where it stands in the log, the sequence it read through.
export function doorThrough(value: unknown): number {
  const v = value as Obj
  if (!object(v) || !object(v.poll) || !sequence(v.poll.through)) throw new Error('invalid-door')
  return v.poll.through
}

interface Run {
  generation: number
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
  mode: LiveMode
  status: LiveStatus
  reason?: string
  retryInMs?: number
}

export function createLiveTransport(options: TransportOptions) {
  const fetcher = options.fetch ?? ((path: RequestInfo | URL, init?: RequestInit) => fetch(path, init))
  if (typeof options.onEvents !== 'function') throw new TypeError('onEvents is required.')
  if (!string(options.events) || !string(options.door)) throw new TypeError('The stream and the door are required.')
  const timers = options.timers ?? null
  const rawSetTimer = timers?.setTimeout ?? setTimeout
  const rawClearTimer = timers?.clearTimeout ?? clearTimeout
  const setTimer = rawSetTimer.bind(timers ?? globalThis) as typeof setTimeout
  const clearTimer = rawClearTimer.bind(timers ?? globalThis) as typeof clearTimeout
  const now = options.now ?? Date.now
  const random = options.random ?? Math.random
  const timing = { ...DEFAULTS, ...options.timings }
  for (const [name, value] of Object.entries(timing)) {
    if (!(name in DEFAULTS) || !Number.isFinite(value) || value <= 0 || value > 600000) throw new TypeError('Invalid timing: ' + name)
  }
  if (timing.retryMaxMs < timing.retryMinMs) throw new TypeError('Invalid retry bounds.')
  const encoder = new TextEncoder()
  let since: number | null = null
  let generation = 0
  let desired = false
  let visible = true
  let running: Run | null = null
  let sinceRecovered = false
  let state: StateInternal = { mode: 'stopped', status: 'stopped' }

  const current = (run: Run | null): boolean => running === run && run !== null && run.generation === generation && !run.controller.signal.aborted

  const ignore = (callback: () => unknown): void => {
    try {
      Promise.resolve(callback()).catch(() => {})
    } catch {
      // A page's callbacks do not own the transport's cleanup.
    }
  }

  function emit(run: Run | null, next: StateInternal): void {
    if (run && !current(run)) return
    state = { ...next }
    if (options.onState) ignore(() => options.onState!({ ...state, since, generation }))
  }

  function halt(status: LiveStatus, reason?: string): void {
    const old = running
    running = null
    generation++
    if (old) old.controller.abort()
    emit(null, { mode: 'stopped', status, ...(reason ? { reason } : {}) })
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

  // Hands work to the page, bounded by callbackMs and cancelled with the request; true once the page accepted it.
  async function deliver(run: Run, req: NonNullable<Run['request']>, work: (context: DeliveryContext) => unknown): Promise<void> {
    if (!current(run)) throw new Error('aborted')
    let deliveryValid = true
    const context: DeliveryContext = {
      generation: run.generation,
      signal: req.controller.signal,
      isCurrent: () => deliveryValid && current(run) && !req.controller.signal.aborted,
    }
    try {
      const accepted = await guarded(
        Promise.resolve().then(() => {
          if (!context.isCurrent()) throw new Error('aborted')
          return work(context)
        }),
        req.controller.signal,
        timing.callbackMs,
        'delivery-timeout',
      )
      if (accepted === false) throw new Error('delivery-rejected')
      if (!context.isCurrent()) throw new Error('aborted')
      run.failures = 0
    } catch (error) {
      deliveryValid = false
      throw error
    }
  }

  async function acceptEvents(run: Run, req: NonNullable<Run['request']>, events: LiveEvent[], through: number): Promise<void> {
    const fresh = events.filter(event => since === null || event.seq > since)
    if (fresh.length > 0) await deliver(run, req, context => options.onEvents(fresh, context))
    else if (!current(run)) throw new Error('aborted')
    since = since === null ? through : Math.max(since, through)
  }

  async function json(run: Run, req: NonNullable<Run['request']>, response: Response): Promise<unknown> {
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
      text = await guarded(response.text(), req.controller.signal)
      if (text.length > MAX_JSON || encoder.encode(text).byteLength > MAX_JSON) throw new Error('json-too-large')
    }
    if (!current(run)) throw new Error('aborted')
    return JSON.parse(text)
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
    let id = ''
    let data: string[] = []
    let skipLF = false

    async function completeLine(): Promise<void> {
      if (!line) {
        const payload = data.join('\n')
        const named = id
        id = ''
        data = []
        frameBytes = 0
        if (!payload) {
          // A frame with an id and no row: the stream read a whole scan with nothing for this page, and it resumes past it.
          if (!named) return
          const past = Number(named)
          if (!sequence(past)) throw new Error('invalid-frame-id')
          if (since === null || past > since) since = past
          run.failures = 0
          return
        }
        const event = validateEvent(JSON.parse(payload))
        if (named && named !== String(event.seq)) throw new Error('invalid-frame-id')
        await acceptEvents(run, req, [event], event.seq)
        emit(run, { mode: 'stream', status: 'live' })
        return
      }
      if (!line.startsWith(':')) {
        const colon = line.indexOf(':')
        const field = colon < 0 ? line : line.slice(0, colon)
        let value = colon < 0 ? '' : line.slice(colon + 1)
        if (value.startsWith(' ')) value = value.slice(1)
        if (field === 'data') data.push(value)
        else if (field === 'id') id = value
        // The frame's name repeats the row's kind, and retry is the transport's own to decide.
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

  function address(mode: Mode): string {
    if (mode === 'door') return options.door
    if (since === null) return options.events
    return options.events + (options.events.includes('?') ? '&' : '?') + 'since=' + encodeURIComponent(String(since))
  }

  async function request(run: Run, mode: Mode, deadline: number | null): Promise<unknown> {
    if (!current(run)) throw new Error('aborted')
    if (run.request) throw new Error('duplicate-live-request')
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
    try {
      const budget = deadline ? Math.max(1, deadline - now()) : Infinity
      arm(Math.min(timing.connectMs, budget), budget <= timing.connectMs ? 'reprobe' : 'connect-timeout')
      const response = await guarded(
        Promise.resolve()
          .then(() => {
            if (req.controller.signal.aborted) throw new Error('aborted')
            return fetcher(address(mode), {
              method: 'GET',
              credentials: 'same-origin',
              cache: 'no-store',
              signal: req.controller.signal,
              headers: { Accept: mode === 'stream' ? 'text/event-stream' : 'application/json' },
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
      if (mode === 'stream') {
        const rotateAt = now() + timing.streamLifetimeMs
        const touch = () => {
          const remaining = Math.max(1, rotateAt - now())
          arm(Math.min(timing.watchdogMs, remaining), remaining <= timing.watchdogMs ? 'stream-rotation' : 'stream-stalled')
        }
        touch()
        emit(run, { mode, status: 'connecting' })
        await stream(run, req, response, touch)
        return undefined
      }
      const remaining = Math.max(1, deadline ? deadline - now() : Infinity)
      arm(Math.min(timing.requestMs, remaining), remaining <= timing.requestMs ? 'reprobe' : 'body-timeout')
      const value = await json(run, req, response)
      if (mode === 'poll') {
        const page = validatePage(value)
        await acceptEvents(run, req, page.events, page.through)
        emit(run, { mode, status: 'live' })
        return page
      }
      const through = doorThrough(value)
      if (since === null) since = through
      else if (through > since) {
        await deliver(run, req, context => options.onMoved?.(context))
        since = through
      }
      emit(run, { mode, status: 'live' })
      return value
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

  // Stream first; a stream that fails falls to the poll, a poll that fails to the door, and the stream is tried
  // again once reprobeMs has passed since it failed. A stream that ends is opened again from where it reached,
  // unless it keeps ending without a row, when it falls to the poll as a failure does.
  async function loop(run: Run): Promise<void> {
    let mode: Mode = run.streamEnabled ? 'stream' : 'poll'
    let reprobeAt = Infinity
    while (current(run)) {
      if (mode !== 'stream' && run.streamEnabled && now() >= reprobeAt) mode = 'stream'
      emit(run, { mode, status: mode !== 'stream' && state.mode === mode && state.status === 'live' ? 'live' : mode === 'stream' ? 'connecting' : 'reconnecting' })
      try {
        const value = await request(run, mode, mode !== 'stream' && run.streamEnabled ? reprobeAt : null)
        if (!current(run)) return
        if (mode === 'poll') {
          await sleep(run, Math.min((value as PollPage).more ? 1 : timing.pollMs, Math.max(1, reprobeAt - now())))
          continue
        }
        if (mode === 'door') {
          await sleep(run, Math.min(timing.doorMs, Math.max(1, reprobeAt - now())))
          continue
        }
      } catch (error) {
        if (!current(run)) return
        const err = error as Error & { status?: number }
        if ([401, 403, 404].includes(err.status ?? 0)) {
          desired = false
          halt('gated', 'http-' + err.status)
          return
        }
        if (err.status === 400) {
          if (since !== null && !sinceRecovered) {
            sinceRecovered = true
            since = null
            continue
          }
          desired = false
          halt('error', 'invalid-since')
          return
        }
        if (err.message === 'reprobe' && run.streamEnabled) {
          mode = 'stream'
          continue
        }
        if (err.message === 'stream-rotation') continue
        run.failures = Math.min(run.failures + 1, 16)
        if (mode === 'stream' && (err.message !== 'stream-ended' || run.failures > 1)) {
          reprobeAt = run.streamEnabled ? now() + timing.reprobeMs : Infinity
          mode = 'poll'
        } else if (mode === 'poll') mode = 'door'
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
    if (!desired || !visible || running) return
    const run: Run = {
      generation: ++generation,
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
    start({ since: from }: { since?: number | null }) {
      if (from != null && !sequence(from)) throw new TypeError('A position on the log is a whole number.')
      since = from ?? null
      sinceRecovered = false
      halt('stopped')
      desired = true
      activate()
    },
    stop() {
      desired = false
      halt('stopped')
    },
    reconnect() {
      if (!desired) return
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
      return { ...state, since, generation, visible, desired, requestActive: Boolean(running?.request) }
    },
  })
}

export type LiveTransport = ReturnType<typeof createLiveTransport>
