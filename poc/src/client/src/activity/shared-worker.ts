// One participant activity connection per browser origin, ported from
// activity-shared-worker.js as a Vite module worker. Tabs remain responsible
// for rendering and explicitly acknowledge each delivery before the durable
// cursor advances.
import { createActivityTransport } from './transport'

const MAX_PORTS = 64
const MAX_PROFILES = 64
const PORT_TTL = 45000
const HEARTBEAT = 10000

interface Client {
  id: string
  port: MessagePort
  participant: string
  desired: boolean
  visible: boolean
  profiles: string[]
  needsReset: boolean
  touched: number
  lastSeen: number
  deliveries: Map<number, (accepted: boolean) => void>
}

const ports = new Map<string, Client>()
let transport: ReturnType<typeof createActivityTransport> | null = null
let participant = ''
let lastSnapshot: unknown
let deliverySequence = 0
let activationSequence = 0
let profileSelection = ''
let profileReconnect: ReturnType<typeof setTimeout> | undefined

const validString = (value: unknown, max = 4096): value is string => typeof value === 'string' && value.length > 0 && value.length <= max
const post = (client: Client, value: unknown) => {
  try {
    client.port.postMessage(value)
  } catch {
    remove(client.id)
  }
}
const broadcast = (value: unknown) => {
  for (const client of ports.values()) if (client.desired) post(client, value)
}
const currentClients = () => [...ports.values()].filter(client => client.desired && client.visible && client.participant === participant)

function remove(id: string): void {
  const client = ports.get(id)
  if (!client) return
  ports.delete(id)
  for (const delivery of client.deliveries.values()) delivery(false)
  maintain()
}

function profileQuery(): string {
  const dids = [...new Set(currentClients().flatMap(client => client.profiles))].sort().slice(0, MAX_PROFILES)
  return dids.map(did => 'profileDid=' + encodeURIComponent(did)).join('&')
}

function refreshProfiles(): void {
  const next = profileQuery()
  if (next === profileSelection) return
  profileSelection = next
  clearTimeout(profileReconnect)
  const active = transport
  profileReconnect = setTimeout(() => active?.reconnect(), 100)
}

function deliver(client: Client, snapshot: unknown, forcedReset = false): { id: number; promise: Promise<boolean> } {
  if (!client.desired || !client.visible || client.participant !== participant) return { id: 0, promise: Promise.resolve(true) }
  const id = ++deliverySequence
  const value =
    forcedReset && !((snapshot as { resetRequired?: boolean }).resetRequired)
      ? { ...(snapshot as Record<string, unknown>), resetRequired: true, events: [] }
      : snapshot
  const promise = new Promise<boolean>(resolve => {
    const finish = (accepted: boolean) => {
      if (!client.deliveries.delete(id)) return
      resolve(accepted)
    }
    client.deliveries.set(id, finish)
    post(client, { type: 'snapshot', id, participant, snapshot: value })
  })
  return { id, promise }
}

async function accept(snapshot: unknown, context: { signal: AbortSignal; isCurrent(): boolean }): Promise<boolean> {
  const clients = currentClients()
  if (!clients.length) return false
  const deliveries = clients.map(client => ({ client, ...deliver(client, snapshot, client.needsReset) }))
  const abort = () => {
    for (const item of deliveries) {
      if (!item.id) continue
      post(item.client, { type: 'cancel', id: item.id })
      item.client.deliveries.get(item.id)?.(false)
    }
  }
  context.signal.addEventListener('abort', abort, { once: true })
  try {
    const results = await Promise.all(deliveries.map(item => item.promise))
    if (!context.isCurrent() || results.some(value => value !== true)) return false
    for (const client of clients) client.needsReset = false
    lastSnapshot = snapshot
    return true
  } finally {
    context.signal.removeEventListener('abort', abort)
  }
}

function createTransport(nextParticipant: string): void {
  transport?.stop()
  participant = nextParticipant
  lastSnapshot = undefined
  profileSelection = profileQuery()
  transport = createActivityTransport({
    onSnapshot: (value, context) => accept(value, context),
    streamQuery: () => profileSelection,
    onEvent(name, value) {
      if (name === 'profile') broadcast({ type: 'profile', participant, profile: value })
    },
    onIdentity(detail) {
      broadcast({ type: 'identity', participant, detail })
    },
    onState(state) {
      broadcast({ type: 'state', participant, state })
    },
  })
  transport.start({ participant })
}

function maintain(): void {
  const clients = [...ports.values()].filter(client => client.desired && client.visible)
  if (!clients.length) {
    transport?.setVisible(false)
    refreshProfiles()
    return
  }
  const nextParticipant = clients.sort((a, b) => b.touched - a.touched)[0]!.participant
  if (!transport || participant !== nextParticipant) createTransport(nextParticipant)
  transport!.setVisible(true)
  for (const client of clients) {
    if (client.participant !== participant) {
      post(client, { type: 'identity', participant: client.participant, detail: { reason: 'participant-generation-changed' } })
    } else if (client.needsReset && lastSnapshot && client.deliveries.size === 0) {
      deliver(client, lastSnapshot, true).promise.then(accepted => {
        if (accepted) client.needsReset = false
      })
    }
  }
  refreshProfiles()
}

interface WorkerMessage {
  type: string
  participant?: string
  visible?: boolean
  dids?: string[]
  id?: number
  accepted?: boolean
}

function receive(client: Client, message: WorkerMessage): void {
  if (!message || typeof message !== 'object') return
  client.lastSeen = Date.now()
  if (message.type === 'start' && validString(message.participant)) {
    client.touched = ++activationSequence
    client.desired = true
    client.visible = message.visible === true
    client.participant = message.participant
    client.needsReset = true
    maintain()
  } else if (message.type === 'visibility') {
    if (message.visible === true) client.touched = ++activationSequence
    client.visible = message.visible === true
    if (client.visible) client.needsReset = true
    if (!client.visible) for (const finish of client.deliveries.values()) finish(true)
    maintain()
  } else if (message.type === 'profiles' && Array.isArray(message.dids)) {
    client.profiles = [...new Set(message.dids.filter(did => validString(did, 256)))].slice(0, MAX_PROFILES)
    refreshProfiles()
  } else if (message.type === 'ack' && Number.isSafeInteger(message.id)) {
    client.deliveries.get(message.id!)?.(message.accepted === true)
  } else if (message.type === 'stop') {
    client.desired = false
    client.visible = false
    client.profiles = []
    for (const finish of client.deliveries.values()) finish(true)
    maintain()
  } else if (message.type === 'heartbeat') {
    maintain()
  }
}

;(self as unknown as { onconnect: (event: MessageEvent) => void }).onconnect = (event: MessageEvent) => {
  const port: MessagePort = event.ports[0]
  if (ports.size >= MAX_PORTS) {
    port.postMessage({ type: 'error', reason: 'too-many-tabs' })
    port.close()
    return
  }
  const id = crypto.randomUUID()
  const client: Client = {
    id,
    port,
    participant: '',
    desired: false,
    visible: false,
    profiles: [],
    needsReset: true,
    touched: ++activationSequence,
    lastSeen: Date.now(),
    deliveries: new Map(),
  }
  ports.set(id, client)
  port.onmessage = message => receive(client, message.data as WorkerMessage)
  port.start()
  port.postMessage({ type: 'ready', id })
}

setInterval(() => {
  const expired = Date.now() - PORT_TTL
  for (const client of ports.values()) if (client.lastSeen < expired) remove(client.id)
}, HEARTBEAT)
