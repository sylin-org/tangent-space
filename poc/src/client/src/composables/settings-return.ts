import { parseWorkspaceRoute, type WorkspaceRoute } from '../stores/router'

export interface SettingsOrigin {
  href: string
  scrollY: number
  label: string
  anchor?: { post: string; offset: number }
}

export function settingsScope(route: WorkspaceRoute): string {
  if (route.kind === 'settings') return 'host'
  if (route.kind === 'tangent-settings') return `tangent:${route.tangent}`
  if (route.kind === 'topic-settings') return `topic:${route.tangent}/${route.topic}`
  return ''
}

function key(actor: string, scope: string): string {
  return `tangent:settings-origin:${encodeURIComponent(actor)}:${encodeURIComponent(scope)}`
}

export function rememberSettingsOrigin(actor: string, scope: string): void {
  if (!actor || !scope) return
  const route = parseWorkspaceRoute(window.location.pathname)
  const label = route?.kind === 'topic' || route?.kind === 'post' ? 'conversation'
    : route?.kind === 'topics' ? 'Tangent' : route?.kind === 'home' ? 'Space' : 'Tangent directory'
  const posts = route?.kind === 'topic' || route?.kind === 'post'
    ? [...document.querySelectorAll<HTMLElement>('.messages .message[id]')]
      .filter(post => post.getBoundingClientRect().height > 0) : []
  const visiblePost = posts.find(post => post.getBoundingClientRect().bottom > 0) ?? posts.at(-1)
  try {
    sessionStorage.setItem(key(actor, scope), JSON.stringify({
      href: window.location.pathname + window.location.search + window.location.hash,
      scrollY: window.scrollY,
      label,
      ...(visiblePost ? { anchor: { post: visiblePost.id, offset: visiblePost.getBoundingClientRect().top } } : {}),
    } satisfies SettingsOrigin))
  } catch { /* Navigation remains available without session storage. */ }
}

export function settingsOrigin(actor: string, scope: string): SettingsOrigin | null {
  if (!actor || !scope) return null
  try {
    const value = JSON.parse(sessionStorage.getItem(key(actor, scope)) || 'null') as SettingsOrigin | null
    if (!value || typeof value.href !== 'string' || !Number.isFinite(value.scrollY)) return null
    const url = new URL(value.href, window.location.origin)
    const route = parseWorkspaceRoute(url.pathname)
    if (url.origin !== window.location.origin || !route || ['settings', 'tangent-settings', 'topic-settings'].includes(route.kind)) return null
    if (scope.startsWith('tangent:') && 'tangent' in route && route.tangent !== scope.slice(8)) return null
    if (scope.startsWith('topic:')) {
      const [tangent, topic] = scope.slice(6).split('/')
      if (!('tangent' in route) || route.tangent !== tangent) return null
      if (route.kind === 'topic' && route.topic !== topic) return null
    }
    const anchor = (route.kind === 'topic' || route.kind === 'post') && value.anchor
      && typeof value.anchor.post === 'string' && /^[a-zA-Z0-9-]{1,128}$/.test(value.anchor.post)
      && Number.isFinite(value.anchor.offset) ? value.anchor : undefined
    return { href: url.pathname + url.search + url.hash, scrollY: Math.max(0, value.scrollY), label: value.label || 'Tangent', anchor }
  } catch { return null }
}
