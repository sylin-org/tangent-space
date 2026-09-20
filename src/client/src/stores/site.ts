import { defineStore } from 'pinia'

const SIGN_IN_PATH = '/auth/atproto/challenge'
const SIGN_OUT_PATH = '/auth/logout'

export interface SiteParticipant {
  participantRef: string
  did: string | null
  handle: string | null
  isOwner: boolean
  joinedAt: string
}

export interface Site {
  name: string
  established: boolean
  signIn: string
  signOut: string | null
  participant: SiteParticipant | null
}

type SiteState = 'loading' | 'error' | 'anonymous' | 'signedIn'

function isAllowedPath(value: unknown, allowed: string): value is string {
  return typeof value === 'string' && value === allowed
}

export function compactDid(did: string): string {
  const parts = did.split(':')
  if (parts.length < 3 || parts[0] !== 'did') return did
  const tail = parts.slice(2).join(':')
  if (tail.length <= 16) return did
  return `${parts[0]}:${parts[1]}:${tail.slice(0, 8)}…${tail.slice(-4)}`
}

export function formatJoined(value: string | null): string | null {
  if (typeof value !== 'string') return null
  const parsed = new Date(value)
  if (isNaN(parsed.getTime())) return null
  try {
    return parsed.toLocaleDateString(undefined, { year: 'numeric', month: 'long', day: 'numeric' })
  } catch {
    return parsed.toISOString().slice(0, 10)
  }
}

function parseSite(payload: unknown): Site | null {
  if (!payload || typeof payload !== 'object') return null
  const p = payload as Record<string, unknown>
  if (typeof p.name !== 'string' || p.name.length === 0) return null
  if (typeof p.established !== 'boolean') return null
  if (!isAllowedPath(p.signIn, SIGN_IN_PATH)) return null
  if (p.participant === undefined || p.participant === null) {
    return { name: p.name, established: p.established, signIn: p.signIn, signOut: null, participant: null }
  }
  if (typeof p.participant !== 'object') return null
  const participant = p.participant as Record<string, unknown>
  if (typeof participant.participantRef !== 'string' || participant.participantRef.length === 0) return null
  if (participant.handle !== undefined && participant.handle !== null && typeof participant.handle !== 'string') return null
  if (typeof participant.isOwner !== 'boolean') return null
  if (typeof participant.joinedAt !== 'string') return null
  return {
    name: p.name,
    established: p.established,
    signIn: p.signIn,
    signOut: isAllowedPath(p.signOut, SIGN_OUT_PATH) ? p.signOut : null,
    participant: {
      participantRef: participant.participantRef,
      did: typeof participant.did === 'string' ? participant.did : null,
      handle: (participant.handle as string | null) ?? null,
      isOwner: participant.isOwner,
      joinedAt: participant.joinedAt,
    },
  }
}

export const useSite = defineStore('site', {
  state: () => ({
    state: 'loading' as SiteState,
    site: null as Site | null,
    announcement: '',
  }),
  getters: {
    established: (s) => s.site?.established ?? false,
    displayName: (s) => {
      const participant = s.site?.participant
      if (!participant) return ''
      return participant.handle && participant.handle.length > 0 ? participant.handle : compactDid(participant.did ?? participant.participantRef)
    },
    joinedLabel: (s) => formatJoined(s.site?.participant?.joinedAt ?? null),
  },
  actions: {
    async load() {
      this.state = 'loading'
      this.announcement = 'Loading the welcome.'
      try {
        const response = await fetch('/api/site', {
          credentials: 'same-origin',
          headers: { Accept: 'application/json' },
          cache: 'no-store',
        })
        if (!response.ok) throw new Error(`HTTP ${response.status}`)
        const site = parseSite(await response.json())
        if (!site) throw new Error('Unexpected site payload')
        this.site = site
        if (!site.established && location.pathname === '/') {
          location.replace('/onboarding/')
          return
        }
        this.state = site.participant ? 'signedIn' : 'anonymous'
        this.announcement = site.participant ? 'You are signed in.' : 'Welcome. Sign in with your AT account to take part.'
      } catch {
        this.state = 'error'
        this.announcement = 'The welcome could not be loaded.'
      }
    },
  },
})
