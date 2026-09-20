import { reactive, watch } from 'vue'
import type { Activity } from './activity'

// One decoration stream for the identities rendered on this page, ported from
// profile-live.js to the reactive grain: components read profile snapshots from
// this store instead of the DOM watching node attributes.

export interface ProfileSnapshot {
  did: string
  displayName?: string
  handle?: string
  avatar?: string
  classification?: string
  status?: string
}

const localAvatar = (value: unknown): value is string => typeof value === 'string' && value.startsWith('/api/profile-cache/avatar?')

export function createProfiles(activity: Activity) {
  const snapshots = reactive(new Map<string, ProfileSnapshot>())
  const fallbacks = reactive(new Map<string, string>())
  let ownDid = ''

  function receive(profile: ProfileSnapshot | unknown): void {
    const p = profile as ProfileSnapshot
    if (!p?.did || p.status !== 'loaded') return
    snapshots.set(p.did, p)
    // The viewer's own profile also announces itself through the page event.
    void p
  }

  /** The activity coordinator's profile stream feeds this store (tangent:profile-stream). */
  function wire(): void {
    // Profile frames arrive on the shared connection; the activity composable
    // collects them, and this store mirrors them into its map.
    watch(activity.profileEvents, values => {
      for (const value of values) {
        const p = value as unknown as ProfileSnapshot
        if (p?.did && p.status === 'loaded') snapshots.set(p.did, p)
      }
    }, { immediate: true })
  }

  /** Pull the signed-in participant's own profile once their DID is known. */
  function loadOwn(did: string): void {
    if (!did || ownDid === did) return
    ownDid = did
    fetch('/api/participants/me/profile', { credentials: 'same-origin', cache: 'no-store' })
      .then(response => (response.ok ? response.json() : null))
      .then(profile => {
        if (ownDid === did && profile) receive(profile)
      })
      .catch(() => {})
  }

  /** A component's stable fallback label for a DID (the letter before data arrives). */
  function registerFallback(did: string, label: string): void {
    fallbacks.set(did, label)
  }

  function snapshotOf(did: string | null | undefined): ProfileSnapshot | undefined {
    return did ? snapshots.get(did) : undefined
  }

  return reactive({
    snapshots, profileEvents: activity.profileEvents,
    receive, wire, loadOwn, registerFallback, snapshotOf,
    localAvatar,
  })
}

export type Profiles = ReturnType<typeof createProfiles>
