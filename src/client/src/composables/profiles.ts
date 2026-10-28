import { httpRequest } from '../api'
import { onScopeDispose, reactive, watch } from 'vue'
import type { Activity } from './activity'

// One decoration feed for the identities rendered on this page. Everything comes from the local server
// cache, never from atproto directly: the viewer's own profile is read once at
// sign-in, rendered DIDs are subscribed on the shared activity connection (or a
// per-tab events stream as fallback), and the server announces when a picture
// lands in the cache — the client then swaps the versioned URL exactly once.

export interface ProfileSnapshot {
  did: string
  displayName?: string
  handle?: string
  avatar?: string
  classification?: string
  status?: string
}

const localAvatar = (value: unknown): value is string => typeof value === 'string' && value.startsWith('/api/profile-cache/avatar?')
const MAX_WATCHED = 64

export function createProfiles(activity: Activity) {
  const snapshots = reactive(new Map<string, ProfileSnapshot>())
  let ownDid = ''
  let watched: string[] = []
  let fallbackStream: EventSource | null = null
  let fallbackSignature = ''
  let wired = false
  let disposed = false

  function closeFallback(): void {
    fallbackStream?.close()
    fallbackStream = null
    fallbackSignature = ''
  }

  const syncTransport = () => { if (!disposed) sync(watched) }
  // The coordinator announces readiness before its caller stores the adapter.
  const onLiveReady = () => queueMicrotask(syncTransport)

  function receive(profile: ProfileSnapshot | unknown): void {
    const p = profile as ProfileSnapshot
    if (!p?.did || p.status !== 'loaded') return
    snapshots.set(p.did, p)
  }

  /** The activity connection's profile frames feed this store (tangent:profile-stream). */
  function wire(): void {
    if (wired) return
    wired = true
    window.addEventListener('tangent:live-ready', onLiveReady)
    document.addEventListener('visibilitychange', syncTransport)
    watch(activity.profileEvents, values => {
      for (const value of values) {
        const p = value as unknown as ProfileSnapshot
        if (p?.did && p.status === 'loaded') snapshots.set(p.did, p)
      }
    }, { immediate: true })
  }

  /** Pull the signed-in participant's own profile once per identity. */
  function loadOwn(did: string): void {
    if (!did || ownDid === did) return
    ownDid = did
    httpRequest('/api/participants/me/profile', { credentials: 'same-origin', cache: 'no-store' })
      .then(response => (response.ok ? response.json() : null))
      .then(profile => {
        if (ownDid === did && profile) receive(profile)
      })
      .catch(() => {})
  }

  /** Keep the server announcing for exactly the DIDs this page renders. */
  function sync(dids: string[]): void {
    const next = [...new Set(dids.filter(value => typeof value === 'string' && value.length <= 256 && value.startsWith('did:')))].sort().slice(0, MAX_WATCHED)
    const signature = next.join('|')
    watched = next
    const multiplexed = activity.setProfiles(next)
    if (multiplexed || document.hidden || !next.length) {
      closeFallback()
      return
    }
    if (fallbackStream && signature === fallbackSignature) return
    // The single-tab transport cannot multiplex profiles; keep a per-tab events stream.
    const wanted = signature
    closeFallback()
    const query = new URLSearchParams()
    next.forEach(did => query.append('did', did))
    const stream = new EventSource('/api/profile-cache/events?' + query)
    stream.addEventListener('profile', event => {
      try {
        const parsed = JSON.parse((event as MessageEvent).data)
        if (wanted === watched.join('|')) receive(parsed)
      } catch {
        /* an unreadable frame is skipped; the next announcement carries the data */
      }
    })
    fallbackStream = stream
    fallbackSignature = signature
  }

  onScopeDispose(() => {
    disposed = true
    window.removeEventListener('tangent:live-ready', onLiveReady)
    document.removeEventListener('visibilitychange', syncTransport)
    closeFallback()
  })

  function snapshotOf(did: string | null | undefined): ProfileSnapshot | undefined {
    return did ? snapshots.get(did) : undefined
  }

  return reactive({
    snapshots, profileEvents: activity.profileEvents,
    receive, wire, loadOwn, sync, snapshotOf,
    localAvatar,
  })
}

export type Profiles = ReturnType<typeof createProfiles>
