// Main-thread adapter for the origin-wide activity SharedWorker, ported from
// activity-coordinator.js. Falls back to a single-tab transport where
// SharedWorker is unavailable or cannot start.
import { createActivityTransport, type ActivityState, type ActivitySnapshot, type SnapshotContext } from './transport'
import sharedWorkerUrl from './shared-worker?worker&url'

export interface CoordinatorOptions {
  onSnapshot(value: ActivitySnapshot, context: SnapshotContext): boolean | void | Promise<boolean | void>
  onIdentity?(detail: { reason: string; status?: number; participantRef?: string }): void
  onState?(state: ActivityState): void
  onEvent?(name: string, value: unknown): void
}

export interface ActivityCoordinator {
  start(options: { participant: string }): void
  stop(): void
  setVisible(value: boolean): void
  setProfiles(dids: string[]): boolean
  snapshot(): Record<string, unknown>
}

interface WireMessage {
  type: string
  participant?: string
  snapshot?: ActivitySnapshot
  profile?: unknown
  detail?: { reason: string; status?: number; participantRef?: string }
  state?: ActivityState
  id?: number
  accepted?: boolean
  dids?: string[]
  visible?: boolean
}

let current: ActivityCoordinator | null = null
let pendingProfiles: string[] = []

function dispatchEvent(name: string, detail?: unknown): void {
  self.dispatchEvent(new CustomEvent(name, { detail }))
}

function create(options: CoordinatorOptions): ActivityCoordinator {
  let participant = ''
  let desired = false
  let visible = !document.hidden
  let generation = 0
  let delivery: { id: number; controller: AbortController } | undefined
  let direct: ReturnType<typeof createActivityTransport> | null = null
  let worker: SharedWorker | null = null
  let port: MessagePort | null = null
  let ready = false
  let profiles: string[] = []
  let profileKey = ''
  let heartbeat: ReturnType<typeof setInterval> | undefined
  let startup: ReturnType<typeof setTimeout> | undefined
  let sharedState: Record<string, unknown> = {}

  const emitProfile = (profile: unknown) => dispatchEvent('tangent:profile-stream', profile)

  const context = (signal: AbortSignal): SnapshotContext => {
    const own = generation
    return { participant, generation: own, signal, isCurrent: () => desired && visible && own === generation && !signal.aborted }
  }

  function startDirect(): void {
    if (direct) return
    ready = false
    sharedState = {}
    direct = createActivityTransport({
      ...options,
      onEvent(name, value) {
        if (name === 'profile') emitProfile(value)
        options.onEvent?.(name, value)
      },
    })
    if (desired && participant) {
      direct.setVisible(visible)
      direct.start({ participant })
    }
    dispatchEvent('tangent:live-ready')
  }

  function send(value: unknown): void {
    if (ready && port) port.postMessage(value)
  }

  function abortDelivery(): void {
    delivery?.controller.abort()
    delivery = undefined
  }

  async function receive(message: WireMessage): Promise<void> {
    if (!message || typeof message !== 'object') return
    if (message.type === 'ready') {
      ready = true
      clearTimeout(startup)
      if (desired && participant) send({ type: 'start', participant, visible })
      if (profiles.length) send({ type: 'profiles', dids: profiles })
    } else if (message.participant && message.participant !== participant) {
      return
    } else if (message.type === 'snapshot' && desired && visible) {
      abortDelivery()
      const controller = new AbortController()
      const pending = { id: message.id!, controller }
      delivery = pending
      let accepted = false
      try {
        accepted = (await options.onSnapshot?.(message.snapshot!, context(controller.signal))) !== false
      } catch {
        accepted = false
      }
      if (delivery === pending) {
        delivery = undefined
        send({ type: 'ack', id: pending.id, accepted })
      }
    } else if (message.type === 'profile') {
      emitProfile(message.profile)
    } else if (message.type === 'cancel') {
      const cancelling = delivery
      if (cancelling && cancelling.id === message.id) {
        cancelling.controller.abort()
        delivery = undefined
      }
    } else if (message.type === 'identity') {
      options.onIdentity?.(message.detail || { reason: 'identity_changed' })
    } else if (message.type === 'state') {
      sharedState = { ...message.state, shared: true }
      options.onState?.(sharedState as unknown as ActivityState)
    } else if (message.type === 'error') {
      ready = false
      try {
        port?.close()
      } catch {
        /* already closed */
      }
      startDirect()
    }
  }

  if (typeof SharedWorker === 'function') {
    try {
      worker = new SharedWorker(sharedWorkerUrl, { type: 'module', name: 'tangent-activity-shared' })
      port = worker.port
      port.onmessage = event => void receive(event.data as WireMessage)
      port.start()
      heartbeat ??= setInterval(() => send({ type: 'heartbeat' }), 10000)
      startup = setTimeout(() => {
        if (!ready) {
          try {
            port?.close()
          } catch {
            /* already closed */
          }
          startDirect()
        }
      }, 2000)
    } catch {
      startDirect()
    }
  } else {
    startDirect()
  }

  return Object.freeze({
    start({ participant: next }: { participant: string }) {
      if (typeof next !== 'string' || !next || next.length > 4096) throw new TypeError('A participant reference is required.')
      if (next !== participant) {
        participant = next
        generation++
        abortDelivery()
      }
      desired = true
      if (direct) {
        direct.setVisible(visible)
        direct.start({ participant })
      } else {
        send({ type: 'start', participant, visible })
      }
    },
    stop() {
      desired = false
      generation++
      abortDelivery()
      if (direct) direct.stop()
      else send({ type: 'stop' })
    },
    setVisible(value: boolean) {
      const next = Boolean(value)
      if (next === visible) return
      visible = next
      generation++
      if (!visible) abortDelivery()
      if (direct) direct.setVisible(visible)
      else send({ type: 'visibility', visible })
    },
    setProfiles(dids: string[]) {
      const next = Array.isArray(dids) ? [...new Set(dids.filter(value => typeof value === 'string' && value.length <= 256))].slice(0, 64) : []
      if (direct) return false
      const nextKey = next.join('|')
      if (nextKey !== profileKey) {
        profiles = next
        profileKey = nextKey
        send({ type: 'profiles', dids: profiles })
      }
      return true
    },
    snapshot() {
      return direct?.snapshot() || { participant, generation, visible, desired, shared: true, ready, ...sharedState }
    },
  })
}

export const activityCoordinator = {
  create(options: CoordinatorOptions): ActivityCoordinator {
    current = create(options)
    if (pendingProfiles.length) current.setProfiles(pendingProfiles)
    dispatchEvent('tangent:live-ready')
    return current
  },
  /** Set the profile stream selection before the coordinator exists: it rides the worker once one is created. */
  setProfiles(dids: string[]): boolean {
    pendingProfiles = Array.isArray(dids) ? dids : []
    if (current) return current.setProfiles(pendingProfiles) === true
    // With no session there is nothing to multiplex onto, so delivery is not
    // claimed here; the caller opens its own stream and hands over on ready.
    return false
  },
  snapshot(): Record<string, unknown> | undefined {
    return current?.snapshot()
  },
}
