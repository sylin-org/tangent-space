// Only creative drafts and arrival hints live in browser storage; connection settings never do.
export interface SetupDraft { name: string; description: string; artwork: string; step: 'data' | 'tangent'; submitted?: boolean; creationName?: string }
export interface SetupArrival { key: string; name: string }
const key = (kind: string, did: string) => `tangent:setup:${kind}:${did}`
function read<T>(kind: string, did: string): T | null {
  if (!did) return null
  try {
    const saved = JSON.parse(sessionStorage.getItem(key(kind, did)) || 'null')
    return saved && Date.now() - saved.savedAt < 86_400_000 ? saved.value as T : null
  } catch { return null }
}
function write(kind: string, did: string, value: unknown) {
  if (!did) return
  try { sessionStorage.setItem(key(kind, did), JSON.stringify({ value, savedAt: Date.now() })) } catch { /* Storage may be unavailable. */ }
}
export function clearSetupState(kind: 'draft' | 'arrival', did: string) {
  try { sessionStorage.removeItem(key(kind, did)) } catch { /* Storage may be unavailable. */ }
}
export function readSetupDraft(did: string): SetupDraft | null {
  const value = read<SetupDraft>('draft', did)
  return value && typeof value.name === 'string' && value.name.length <= 120
    && typeof value.description === 'string' && value.description.length <= 1000
    && typeof value.artwork === 'string' && (value.artwork === '' || /^\/tangent-art\/[a-z0-9-]+\.png$/.test(value.artwork))
    && (value.creationName === undefined || typeof value.creationName === 'string' && value.creationName.length <= 120)
    && ['data', 'tangent'].includes(value.step) ? value : null
}
export const saveSetupDraft = (did: string, draft: SetupDraft) => write('draft', did, draft)
export const saveSetupArrival = (did: string, arrival: SetupArrival) => write('arrival', did, arrival)
export function readSetupArrival(did: string): SetupArrival | null {
  const value = read<SetupArrival>('arrival', did)
  return value && typeof value.key === 'string' && /^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(value.key)
    && typeof value.name === 'string' ? value : null
}
