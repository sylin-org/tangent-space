// Bringing an image in: what is sent, how long it may take, and what comes back or why not.

export const imageTypes = ['image/png', 'image/jpeg', 'image/webp']

/** The most bytes an image holds as it is sent, as the server keeps to. */
export const imageLimit = 20 * 1024 * 1024

export const bringing = {
  /** How long a bringing waits for its answer before it says the image did not come. */
  timeout: 120_000,
}

export interface Brought {
  id: string
  reference: string
  address: string
}

/** Why this file cannot be brought, or null where it can. */
export function refusedHere(file: File): string | null {
  if (!imageTypes.includes(file.type)) return 'Choose a PNG, JPEG or WebP image.'
  if (file.size > imageLimit) return 'Choose an image smaller than 20 MiB.'
  return null
}

/** Sends the image as a multipart form with the form's token, answered as JSON; throws an Error whose words a person
 *  reads where it did not come. */
export async function bring(action: string, file: File, token: { field: string; value: string } | null, signal: AbortSignal): Promise<Brought> {
  const fields = new FormData()
  if (token) fields.append(token.field, token.value)
  fields.append('file', file)
  const bound = AbortSignal.any([signal, AbortSignal.timeout(bringing.timeout)])
  let response: Response
  try {
    response = await fetch(action, { method: 'POST', body: fields, headers: { Accept: 'application/json' }, credentials: 'same-origin', signal: bound })
  } catch {
    throw new Error('The Tangent Space didn’t answer, so the image didn’t come. Try it again.')
  }
  const answer = (await response.json().catch(() => ({}))) as Partial<Brought> & { detail?: string; title?: string }
  if (!response.ok || !answer.id || !answer.reference || !answer.address) throw new Error(answer.detail || answer.title || 'The image could not be brought in.')
  return { id: answer.id, reference: answer.reference, address: answer.address }
}
