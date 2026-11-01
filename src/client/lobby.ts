// Companion Lobby on this browser's machine: whether it answers, and the address that hands it a place. Its manager
// page takes writes only from itself, so the hand-off is a page it opens, naming the place in the words of its own
// connections: the service, and the address.

export const managerOrigin = 'http://127.0.0.1:5219'
const discoveryUrl = managerOrigin + '/api/discovery'
const largestAnswer = 4 * 1024

export interface FindOptions {
  send?: typeof fetch
  timeoutMs?: number
}

// True only when the answer, within its time and its size, says it is the Lobby at the one origin it may be at. Nothing
// else it says is used.
export async function findLobby({ send = globalThis.fetch.bind(globalThis), timeoutMs = 900 }: FindOptions = {}): Promise<boolean> {
  const controller = new AbortController()
  const timeout = setTimeout(() => controller.abort(), timeoutMs)
  try {
    const response = await send(discoveryUrl, {
      method: 'GET',
      mode: 'cors',
      credentials: 'omit',
      redirect: 'error',
      cache: 'no-store',
      headers: { Accept: 'application/json' },
      signal: controller.signal,
    })
    if (!response.ok || response.redirected) return false
    if (!response.headers.get('content-type')?.toLowerCase().startsWith('application/json')) return false
    if (Number(response.headers.get('content-length')) > largestAnswer) {
      await response.body?.cancel()
      return false
    }
    const answer = await readWithin(response, largestAnswer)
    if (answer === null) return false
    const stated = JSON.parse(answer)
    return stated?.product === 'companion-lobby' && stated?.discoveryVersion === 1 && stated?.managerOrigin === managerOrigin
  } catch {
    return false
  } finally {
    clearTimeout(timeout)
  }
}

// The body as text, read no further than `limit` bytes; null past it, or with no body.
async function readWithin(response: Response, limit: number): Promise<string | null> {
  const reader = response.body?.getReader()
  if (!reader) return null
  const decoder = new TextDecoder()
  let size = 0
  let text = ''
  for (;;) {
    const { done, value } = await reader.read()
    if (done) return text + decoder.decode()
    size += value.byteLength
    if (size > limit) {
      await reader.cancel()
      return null
    }
    text += decoder.decode(value, { stream: true })
  }
}

// The page of the Lobby that receives the place at `address`.
export function handOff(address: string): string {
  const page = new URL('/', managerOrigin)
  page.searchParams.set('service', 'tangent')
  page.searchParams.set('address', address)
  return page.href
}
