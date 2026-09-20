// The roles UI's shared request layer, ported from roles.js and access.js: one CSRF
// session token for every write, one error-message contract, one role shape.

export interface RoleCapability {
  key: string
  label?: string
  description?: string
  scopeTypes?: string[]
}

export interface RoleDescriptor {
  capabilities: RoleCapability[]
}

export interface NormalizedRole {
  id: string
  name: string
  purpose: string
  permissions?: string[]
  grants: Array<{ capability: string }>
  presentation: { color: string; system: string; selfEnrollable: string }
  metadata?: Record<string, string | undefined>
  memberCount?: number
  _new?: boolean
}

export interface RoleOption {
  id: string
  name: string
  color: string
  metadata?: Record<string, string | undefined>
  purpose?: string
  description?: string
}

interface CsrfSession {
  requestToken: string
  headerName: string
}

let csrf: CsrfSession | null = null

function failure(response: Response, data: Record<string, unknown>, kind: 'roles' | 'access'): Error {
  const message =
    (data.message as string) ||
    (data.error as string) ||
    (data.reason as string) ||
    (data.title as string) ||
    (kind === 'access' && (response.status === 403 || response.status === 404)
      ? 'You cannot change access here.'
      : kind === 'roles' && response.status === 403
        ? 'Your current role cannot manage this scope.'
        : kind === 'access'
          ? 'This change could not be saved.'
          : 'This request could not be completed.')
  const error = new Error(message)
  ;(error as Error & { status?: number }).status = response.status
  return error
}

export async function rolesSession(): Promise<CsrfSession> {
  if (csrf) return csrf
  const response = await fetch('/api/roles/ui/session', {
    credentials: 'same-origin',
    cache: 'no-store',
    headers: { Accept: 'application/json' },
  })
  const data = await response.json().catch(() => ({}))
  if (!response.ok || !data.requestToken || !data.headerName) throw failure(response, data, 'roles')
  csrf = data as CsrfSession
  return csrf
}

/** A rejected antiforgery token poisons the cached session; drop it so the next write re-requests. */
export function clearRolesSession(): void {
  csrf = null
}

export async function rolesRequest<T = unknown>(
  path: string,
  options: { method?: string; body?: unknown; scope?: 'roles' | 'access' } = {},
): Promise<T> {
  const kind = options.scope ?? 'roles'
  const method = options.method || 'GET'
  const headers: Record<string, string> = { Accept: 'application/json' }
  if (!['GET', 'HEAD'].includes(method)) {
    const token = await rolesSession()
    headers[token.headerName] = token.requestToken
    headers['Content-Type'] = 'application/json'
  }
  const response = await fetch(path, {
    method,
    credentials: 'same-origin',
    cache: 'no-store',
    headers,
    body: options.body === undefined ? undefined : JSON.stringify(options.body),
  })
  const data = (await response.json().catch(() => ({}))) as Record<string, unknown>
  if (!response.ok) throw failure(response, data, kind)
  return data as T
}

export function normalizeRole(role: Record<string, unknown> | null): NormalizedRole {
  const metadata = (role?.metadata ?? {}) as Record<string, string | undefined>
  return {
    ...(role ?? {}),
    purpose: metadata.purpose || '',
    presentation: {
      color: metadata.color || '#a98df0',
      system: metadata.system || 'false',
      selfEnrollable: metadata.selfEnrollable || 'false',
    },
    grants: ((role?.permissions as string[]) ?? []).map(capability => ({ capability })),
  } as NormalizedRole
}

export function roleColor(value?: string | null): string {
  return /^#[0-9a-f]{6}$/i.test(value || '') ? value! : '#a98df0'
}

export const systemRole = (role: NormalizedRole | null | undefined): boolean => role?.presentation?.system === 'true'

export const protectedOwnerRole = (role: NormalizedRole | null | undefined): boolean =>
  systemRole(role) && /:owner$/i.test(role?.id || '')

export const roleKeyFor = (name: string): string =>
  'role:' +
  name
    .toLocaleLowerCase()
    .normalize('NFKD')
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-|-$/g, '')
    .slice(0, 64)

/** The server's roles as the access editor offers them (access.js rolesFor). */
export async function rolesFor(): Promise<RoleOption[]> {
  const page = await rolesRequest<unknown>('/api/identity/roles?page=1&pageSize=100', { scope: 'access' })
  const items = Array.isArray(page) ? page : ((page as { items?: unknown[] }).items ?? [])
  return (items as Record<string, unknown>[])
    .map(role => ({
      ...role,
      id: (role.id || role.key) as string,
      name: (role.name || role.id || role.key) as string,
      color: roleColor((role.metadata as Record<string, string>)?.color || (role.presentation as Record<string, string>)?.color),
    }))
    .filter(role => role.id && (role.id.startsWith('role:') || role.id.startsWith('group:'))) as RoleOption[]
}
