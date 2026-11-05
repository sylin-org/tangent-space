// Who or what a body names, as a pick writes it: a link whose words are the sign and the name shown, and whose target
// says who or what it means — a participant's page by their id, a role's key, a Tangent's address — so the body keeps it
// whatever the name or handle becomes. In the source it is Markdown, the name's punctuation escaped so it reads as it
// is; in the visual editor it is one chip (article/chip.ts) that writes the same Markdown.

export interface Named {
  // The sign and the name, as @Lumen, @Moderators or #General.
  words: string
  target: string
}

// The sign a target is named after: @ for a participant or a role, # for a Tangent.
export function signOf(target: string): '@' | '#' {
  return target.startsWith('/t/') ? '#' : '@'
}

// A name as Markdown reads it as it is inside a link's words: each mark that could mean something there escaped — a
// backslash, a bracket, emphasis, code, an entity, a tag, a strike, a cell, an image — and runs of whitespace as one
// space, so a name stays readable in the source.
export function escapedName(name: string): string {
  return name.replace(/\s+/gu, ' ').trim().replace(/[\\[\]*_`<>&~|!]/gu, (mark) => '\\' + mark)
}

// What escapedName wrote, read back.
export function unescapedName(written: string): string {
  return written.replace(/\\([!-/:-@[-`{-~])/gu, '$1')
}

// The named one as Markdown: [@Lumen](/p/0193…).
export function markdownOf(named: Named): string {
  const sign = named.words.startsWith('@') || named.words.startsWith('#') ? named.words[0] : ''
  return `[${sign}${escapedName(named.words.slice(sign.length))}](${named.target})`
}

// A link target a chip stands for: a participant's (/p/ and their id), a role's key, or a Tangent's address.
const targetPattern = /^(?:\/p\/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}|role:[\p{L}\p{N}-]+|\/t\/[\p{L}\p{N}-]+)$/u

// What [words](target) at the start of src names, where its words are a sign and a name and its target one a chip stands
// for, with the sign the target is named after; null otherwise.
const linkPattern = /^\[([@#])((?:\\[!-/:-@[-`{-~]|[^\\[\]\n])+)\]\(([^()\s]+)\)/u

export function namedAt(src: string): { raw: string; named: Named } | null {
  const match = linkPattern.exec(src)
  if (!match || !targetPattern.test(match[3]) || signOf(match[3]) !== match[1]) return null
  return { raw: match[0], named: { words: match[1] + unescapedName(match[2]), target: match[3] } }
}

// A field a name can be put into: the page's open composers, by the order they were made, the one written in last first.
export interface Composer {
  element: HTMLElement
  put(named: Named): void
}

const composers: Composer[] = []
let lastWritten: Composer | null = null

// Adds a composer; returns what removes it.
export function addComposer(composer: Composer): () => void {
  composers.push(composer)
  const written = () => (lastWritten = composer)
  composer.element.addEventListener('focusin', written)
  return () => {
    composer.element.removeEventListener('focusin', written)
    composers.splice(composers.indexOf(composer), 1)
    if (lastWritten === composer) lastWritten = null
  }
}

// The composer a name goes into: the one written in last, while it stands in the page, else the first that does.
export function openComposer(): Composer | null {
  const standing = (composer: Composer | null) => !!composer && composer.element.isConnected && !composer.element.closest('[hidden]')
  return standing(lastWritten) ? lastWritten : composers.find(standing) ?? null
}
