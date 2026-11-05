// What a writer can name as they type: after an @ that begins a word, the people and roles of the place, and after a #,
// the Tangents they may view, read from the Tangent's suggestions (/t/{tangent}/suggest) and shown as a short list under
// the caret. Arrows move through it, Enter or Tab picks, Escape closes it, and so does leaving the field; picking writes
// the handle, the role or the Tangent's slug after its sign, and a space. Never a dialog: the list sits in the page beside
// the field, a listbox the field controls. A field is a surface: a textarea (textareaSurface) or the article editor.

export interface Suggestion {
  kind: 'person' | 'role' | 'tangent'
  insert: string
  name: string
  note: string | null
}

// What the caret stands after, when it ends a sign that begins a word and what follows it: the sign, and the words typed
// after it.
export interface Typed {
  sign: '@' | '#'
  prefix: string
}

const typedPattern = /(^|[\s([>"'“‘])([@#])([\w.-]{0,64})$/u

export function typedAt(before: string): Typed | null {
  const match = typedPattern.exec(before)
  return match ? { sign: match[2] as '@' | '#', prefix: match[3] ?? '' } : null
}

// A field suggestions serve: the element that holds the focus and the words, where its keys are heard first, the words
// before the caret in its line, the point on the screen under the character so many characters back from the caret, how
// it replaces those characters with words, whether it takes words now, and how it says that its words or caret moved.
export interface Surface {
  element: HTMLElement
  keys: HTMLElement
  before(): string
  pointBack(characters: number): { left: number; bottom: number }
  replaceBack(characters: number, words: string): void
  writable(): boolean
  onChange(listener: () => void): () => void
}

// Where the suggestions are read and how long one read waits, replaceable where it is tested.
export const suggesting = {
  read: async (address: string, signal: AbortSignal): Promise<Suggestion[]> => {
    const answer = await fetch(address, { headers: { Accept: 'application/json' }, credentials: 'same-origin', signal })
    if (!answer.ok) return []
    const read = (await answer.json()) as { suggestions?: Suggestion[] }
    return read.suggestions ?? []
  },
  timeout: 5_000,
}

// A textarea as a surface, its caret measured on a copy of its text.
export function textareaSurface(field: HTMLTextAreaElement): Surface {
  return {
    element: field,
    keys: field,
    before: () => field.value.slice(0, field.selectionStart ?? 0),
    pointBack(characters) {
      const caret = (field.selectionStart ?? 0) - characters
      const mirror = document.createElement('div')
      const style = getComputedStyle(field)
      for (const key of ['fontFamily', 'fontSize', 'fontWeight', 'lineHeight', 'letterSpacing', 'paddingTop', 'paddingLeft', 'paddingRight', 'borderTopWidth', 'borderLeftWidth', 'boxSizing'] as const)
        mirror.style[key] = style[key]
      mirror.style.position = 'absolute'
      mirror.style.visibility = 'hidden'
      mirror.style.whiteSpace = 'pre-wrap'
      mirror.style.overflowWrap = 'break-word'
      mirror.style.width = field.offsetWidth + 'px'
      mirror.textContent = field.value.slice(0, Math.max(0, caret))
      const marker = document.createElement('span')
      marker.textContent = '​'
      mirror.append(marker)
      document.body.append(mirror)
      const lineHeight = parseFloat(style.lineHeight) || parseFloat(style.fontSize) * 1.4
      const box = field.getBoundingClientRect()
      const point = { left: box.left + marker.offsetLeft, bottom: box.top + Math.min(marker.offsetTop - field.scrollTop + lineHeight, field.offsetHeight) }
      mirror.remove()
      return point
    },
    replaceBack(characters, words) {
      const caret = field.selectionStart ?? field.value.length
      const start = caret - characters
      const rest = field.value.slice(caret).replace(/^[\w.-]*/u, '').replace(/^ /u, '')
      field.value = field.value.slice(0, start) + words + rest
      field.setSelectionRange(start + words.length, start + words.length)
      field.dispatchEvent(new Event('input', { bubbles: true }))
    },
    writable: () => !field.readOnly && !field.disabled,
    onChange(listener) {
      field.addEventListener('input', listener)
      return () => field.removeEventListener('input', listener)
    },
  }
}

// The note a suggestion is shown with: a role says so, beside what the answer notes of it.
function noteOf(entry: Suggestion): string {
  return [entry.kind === 'role' ? 'role' : '', entry.note].filter(Boolean).join(' · ')
}

let lists = 0

// Shows suggestions for one field, read from address, the Tangent's suggestions with its Topic where there is one; returns
// what ends it.
export function attachSuggestions(surface: Surface, address: string): () => void {
  const field = surface.element
  const id = `suggestions-${++lists}`
  const list = document.createElement('ul')
  list.id = id
  list.className = 'suggestions'
  list.setAttribute('role', 'listbox')
  list.hidden = true
  field.insertAdjacentElement('afterend', list)
  field.setAttribute('aria-autocomplete', 'list')
  field.setAttribute('aria-controls', id)
  field.setAttribute('aria-expanded', 'false')

  let shown: Suggestion[] = []
  let active = 0
  let typed: Typed | null = null
  let reading: AbortController | null = null

  function close() {
    reading?.abort()
    reading = null
    shown = []
    typed = null
    list.hidden = true
    list.replaceChildren()
    field.setAttribute('aria-expanded', 'false')
    field.removeAttribute('aria-activedescendant')
  }

  function mark() {
    list.querySelectorAll<HTMLElement>('[role="option"]').forEach((row, index) => {
      row.setAttribute('aria-selected', String(index === active))
      if (index === active) {
        field.setAttribute('aria-activedescendant', row.id)
        row.scrollIntoView?.({ block: 'nearest' })
      }
    })
  }

  function show(entries: Suggestion[], at: Typed) {
    shown = entries
    active = 0
    list.setAttribute('aria-label', at.sign === '@' ? 'People and roles to mention' : 'Tangents to link')
    list.replaceChildren(...entries.map((entry, index) => {
      const row = document.createElement('li')
      row.id = `${id}-${index}`
      row.setAttribute('role', 'option')
      row.dataset.kind = entry.kind
      const name = document.createElement('span')
      name.className = 'suggestion-name'
      name.textContent = entry.name
      const insert = document.createElement('span')
      insert.className = 'suggestion-insert'
      insert.textContent = at.sign + entry.insert
      const note = document.createElement('span')
      note.className = 'suggestion-note'
      note.textContent = noteOf(entry)
      row.append(name, insert, note)
      // A press picks before the field loses its focus.
      row.addEventListener('mousedown', (event) => {
        event.preventDefault()
        pick(index)
      })
      return row
    }))
    list.hidden = false
    const point = surface.pointBack(at.prefix.length + 1)
    const frame = (list.offsetParent ?? document.body).getBoundingClientRect()
    list.style.left = `${Math.max(0, Math.min(point.left - frame.left, frame.width - list.offsetWidth))}px`
    list.style.top = `${point.bottom - frame.top + 4}px`
    field.setAttribute('aria-expanded', 'true')
    mark()
  }

  function pick(index: number) {
    const entry = shown[index]
    const at = typed
    close()
    if (!entry || !at) return
    surface.replaceBack(at.prefix.length + 1, at.sign + entry.insert + ' ')
    field.focus()
  }

  async function read(at: Typed) {
    reading?.abort()
    const controller = new AbortController()
    reading = controller
    const timer = setTimeout(() => controller.abort(), suggesting.timeout)
    const joined = address + (address.includes('?') ? '&' : '?') + 'q=' + encodeURIComponent(at.sign + at.prefix)
    let entries: Suggestion[] = []
    try {
      entries = await suggesting.read(joined, controller.signal)
    } catch {
      entries = []
    } finally {
      clearTimeout(timer)
    }
    if (reading !== controller) return
    reading = null
    if (entries.length === 0 || !field.contains(document.activeElement)) return close()
    typed = at
    show(entries, at)
  }

  const onChange = () => {
    if (!surface.writable() || !field.contains(document.activeElement)) return close()
    const at = typedAt(surface.before())
    if (!at) return close()
    if (typed && typed.sign === at.sign && typed.prefix === at.prefix) return
    typed = at
    void read(at)
  }

  const onKeydown = (event: KeyboardEvent) => {
    if (list.hidden || shown.length === 0) return
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      active = (active + (event.key === 'ArrowDown' ? 1 : -1) + shown.length) % shown.length
      mark()
    } else if (event.key === 'Enter' || event.key === 'Tab') {
      pick(active)
    } else if (event.key === 'Escape') {
      close()
    } else return
    event.preventDefault()
    event.stopPropagation()
  }

  const onBlur = () => close()

  const stopChange = surface.onChange(onChange)
  surface.keys.addEventListener('keydown', onKeydown, true)
  field.addEventListener('blur', onBlur)
  return () => {
    close()
    stopChange()
    surface.keys.removeEventListener('keydown', onKeydown, true)
    field.removeEventListener('blur', onBlur)
    list.remove()
  }
}
