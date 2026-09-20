import { defineStore } from 'pinia'

// The workspace router, ported from spa-router.js: one parser for the workspace's
// paths, in-app navigation for same-origin links, popstate for the browser's back
// button. Route-driven panel loads live with the panels that own them.

export type WorkspaceRoute =
  | { kind: 'home' }
  | { kind: 'tangents' }
  | { kind: 'settings' }
  | { kind: 'participant'; identifier: string }
  | { kind: 'tangent-settings'; tangent: string }
  | { kind: 'topics'; tangent: string }
  | { kind: 'topic'; tangent: string; topic: string }
  | { kind: 'topic-settings'; tangent: string; topic: string }
  | { kind: 'post'; tangent: string; post: string }
  // Full page loads in the vanilla router: the app never client-navigates into them.
  | { kind: 'sign-in' }
  | { kind: 'onboarding' }

// The build hosts the workspace under a fixed base (/app today, / at the swap);
// routes are always parsed relative to it.
const BASE = import.meta.env.BASE_URL

function decodeSegment(value: string): string | null {
  try {
    const decoded = decodeURIComponent(value)
    return !decoded || /[\\/\u0000-\u001f\u007f]/.test(decoded) ? null : decoded
  } catch {
    return null
  }
}

/** The workspace route a pathname names, or null when nothing lives there. */
export function parseWorkspaceRoute(pathname: string): WorkspaceRoute | null {
  const parts = pathname.split('/').filter(Boolean).map(decodeSegment)
  if (parts.some(value => value == null)) return null
  if (parts.length === 0) return { kind: 'home' }
  if (parts.length === 1 && parts[0] === 'tangents') return { kind: 'tangents' }
  if (parts.length === 1 && parts[0] === 'settings') return { kind: 'settings' }
  if (parts.length === 2 && parts[0] === 'u') return { kind: 'participant', identifier: parts[1]! }
  if (parts[0] !== 't' || !parts[1]) return null
  if (parts.length === 3 && parts[2] === 'settings') return { kind: 'tangent-settings', tangent: parts[1] }
  if (parts[2] === 'topics') {
    if (parts.length === 3) return { kind: 'topics', tangent: parts[1] }
    if (parts.length === 4) return { kind: 'topic', tangent: parts[1], topic: parts[3]! }
    if (parts.length === 5 && parts[4] === 'settings') return { kind: 'topic-settings', tangent: parts[1], topic: parts[3]! }
    return null
  }
  return parts.length === 3 ? { kind: 'post', tangent: parts[1]!, post: parts[2]! } : null
}

function parseHere(pathname: string): WorkspaceRoute | null {
  const rest = BASE !== '/' && pathname.startsWith(BASE) ? pathname.slice(BASE.length - 1) : pathname
  const workspace = parseWorkspaceRoute(rest)
  if (workspace) return workspace
  // Authentication pages are full page loads; the app still needs their kind when
  // it is the one serving them.
  const first = rest.split('/').filter(Boolean)[0]
  if (first === 'sign-in') return { kind: 'sign-in' }
  if (first === 'onboarding') return { kind: 'onboarding' }
  return null
}

function baseHref(path: string): string {
  return BASE !== '/' && path.startsWith('/') ? BASE.slice(0, -1) + path : path
}

export function workspaceDestination(href: string, origin: string): URL | null {
  let destination: URL
  try {
    destination = new URL(href, origin)
  } catch {
    return null
  }
  return destination.origin === origin && parseWorkspaceRoute(destination.pathname) ? destination : null
}

export const useRouter = defineStore('router', {
  state: () => ({
    route: (parseHere(window.location.pathname) ?? { kind: 'home' }) as WorkspaceRoute,
  }),
  getters: {
    isSettings: (s): boolean =>
      s.route.kind === 'settings' || s.route.kind === 'tangent-settings' || s.route.kind === 'topic-settings',
  },
  actions: {
    apply(pathname: string): boolean {
      const next = parseHere(pathname)
      if (!next) return false
      this.route = next
      return true
    },
    navigate(href: string, { replace = false }: { replace?: boolean } = {}): boolean {
      const destination = workspaceDestination(href, window.location.origin)
      if (!destination) return false
      const nextPath = baseHref(destination.pathname)
      const nextHref = nextPath + destination.search + destination.hash
      if (nextHref === window.location.pathname + window.location.search + window.location.hash) return true
      window.history[replace ? 'replaceState' : 'pushState']({ tangentRoute: nextHref }, '', nextHref)
      return this.apply(nextPath)
    },
    /** Install the document-level listeners once. */
    bind() {
      window.addEventListener('popstate', () => {
        this.apply(window.location.pathname)
      })
      document.addEventListener('click', (event: MouseEvent) => {
        if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return
        const anchor = (event.target as HTMLElement | null)?.closest?.('a[href]') as HTMLAnchorElement | null
        if (!anchor || anchor.target || anchor.hasAttribute('download')) return
        const destination = workspaceDestination(anchor.href, window.location.origin)
        if (!destination) return
        if (
          destination.pathname === window.location.pathname &&
          destination.search === window.location.search &&
          destination.hash !== window.location.hash
        ) {
          return
        }
        event.preventDefault()
        this.navigate(destination.href)
      })
    },
  },
})

export type RouterStore = ReturnType<typeof useRouter>
