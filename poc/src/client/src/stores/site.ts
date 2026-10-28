import { httpRequest } from '../api'
import { defineStore } from 'pinia'
import { useRouter } from './router'

const SIGN_IN_PATH = '/auth/atproto/challenge'
const SIGN_OUT_PATH = '/auth/logout'

export interface SiteParticipant {
  participantRef: string
  did: string | null
  handle: string | null
  displayName?: string
  isOwner: boolean
  joinedAt: string
}

export interface Site {
  name: string
  established: boolean
  workspaceAvailable?: boolean
  signIn: string
  signOut: string | null
  participant: SiteParticipant | null
  onboarding?: string
  initialTangent?: { audience: string; audienceDescription: string; admission: string; admissionDescription: string }
  initialTangentKey?: string | null
}

export interface ServerPermissions {
  role?: string
  allowedActions?: string[]
  restrictions?: string[]
}

export interface ServerView {
  name?: string
  byline?: string
  markImageUrl?: string
  accent?: string
  coverImageUrl?: string
  welcomeMessage?: string
  motd?: string
  allowAgentTangentOwnership?: boolean
  role?: string
  canManage?: boolean
  canManageParticipants?: boolean
  permissions?: ServerPermissions | Array<{ role?: string; scope?: string; allowedActions?: string[] }>
  backgroundScene?: string
  backgroundColor?: string
  backgroundIntensity?: number
  backgroundMotion?: boolean
  backgroundMouseSpotlight?: boolean
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
  const initial = p.initialTangent as Record<string, unknown> | undefined
  const initialTangent = initial && ['audience', 'audienceDescription', 'admission', 'admissionDescription'].every(key => typeof initial[key] === 'string')
    ? initial as unknown as Site['initialTangent'] : undefined
  if (p.participant === undefined || p.participant === null) {
    return { name: p.name, established: p.established, workspaceAvailable: p.workspaceAvailable !== false, signIn: p.signIn, signOut: null, participant: null, onboarding: typeof p.onboarding === 'string' ? p.onboarding : undefined, initialTangent }
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
    workspaceAvailable: p.workspaceAvailable !== false,
    signIn: p.signIn,
    signOut: isAllowedPath(p.signOut, SIGN_OUT_PATH) ? p.signOut : null,
    participant: {
      participantRef: participant.participantRef,
      did: typeof participant.did === 'string' ? participant.did : null,
      handle: (participant.handle as string | null) ?? null,
      displayName: typeof participant.displayName === 'string' ? participant.displayName : undefined,
      isOwner: participant.isOwner,
      joinedAt: participant.joinedAt,
    },
    onboarding: typeof p.onboarding === 'string' ? p.onboarding : undefined,
    initialTangent,
    initialTangentKey: typeof p.initialTangentKey === 'string' ? p.initialTangentKey : null,
  }
}

export const useSite = defineStore('site', {
  state: () => ({
    state: 'loading' as SiteState,
    authorizationFailure: null as 401 | 403 | null,
    site: null as Site | null,
    server: null as ServerView | null,
    siteLoadVersion: 0,
    serverLoadVersion: 0,
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
      const loadVersion = ++this.siteLoadVersion
      this.authorizationFailure = null
      this.state = 'loading'
      this.announcement = 'Loading the welcome.'
      try {
        const response = await httpRequest('/api/site', {
          credentials: 'same-origin',
          headers: { Accept: 'application/json' },
          cache: 'no-store',
        })
        if (!response.ok) throw Object.assign(new Error(`HTTP ${response.status}`), { status: response.status })
        const site = parseSite(await response.json())
        if (!site) throw new Error('Unexpected site payload')
        if (loadVersion !== this.siteLoadVersion) return
        if (this.site?.participant?.participantRef !== site.participant?.participantRef || site.workspaceAvailable === false) {
          this.server = null
          this.serverLoadVersion++
        }
        this.site = site
        if (!site.established && useRouter().route.kind === 'home') {
          location.replace('/onboarding/')
          return
        }
        this.state = site.participant ? 'signedIn' : 'anonymous'
        this.announcement = site.participant ? 'You are signed in.' : 'Welcome. Sign in with your AT account to take part.'
        if (site.workspaceAvailable === false) return
        const actor = site.participant?.participantRef ?? ''
        const refresh = this.loadServer()
        const serverVersion = this.serverLoadVersion
        await refresh.catch(() => {
          if (loadVersion === this.siteLoadVersion && serverVersion === this.serverLoadVersion
            && actor === (this.site?.participant?.participantRef ?? '')) this.server = null
        })
      } catch (error) {
        if (loadVersion !== this.siteLoadVersion) return
        const status = (error as { status?: number }).status
        if (status === 401 || status === 403) {
          this.authorizationFailure = status
          this.site = null
          this.serverLoadVersion++
        }
        this.state = 'error'
        this.server = null
        this.announcement = this.authorizationFailure
          ? 'Your current account or session cannot open this Space.' : 'The welcome could not be loaded.'
      }
    },
    /** The server's own details. Throws when the read fails. */
    async loadServer() {
      const actor = this.site?.participant?.participantRef ?? ''
      const version = ++this.serverLoadVersion
      const response = await httpRequest('/api/server', {
        credentials: 'same-origin',
        headers: { Accept: 'application/json' },
        cache: 'no-store',
      })
      const value = await response.json().catch(() => null)
      if (!response.ok || !value || typeof value !== 'object' || Array.isArray(value)) {
        if (version === this.serverLoadVersion && actor === (this.site?.participant?.participantRef ?? '')) this.server = null
        throw new Error('Server settings could not be read.')
      }
      if (version !== this.serverLoadVersion || actor !== (this.site?.participant?.participantRef ?? '')) return
      this.server = value
    },
  },
})

export type SiteStore = ReturnType<typeof useSite>
