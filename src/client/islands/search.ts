import { defineComponent, onUnmounted, type PropType } from 'vue'

// How long after the last keystroke the box asks, in milliseconds.
export const searchDelay = 200

// The most suggestions the panel shows.
export const suggestionLimit = 5

// The fewest characters worth asking about: one letter is too many words to suggest from.
export const minimumText = 2

interface Run {
  text: string
  marked: boolean
}

interface Hit {
  id: string
  topic: string
  tangent: string
  address: string
  snippet: Run[]
}

// Suggestions under the masthead's search box as someone types: the server's HTML is a plain form that goes to the search
// page, and that stays how Enter works; this adds a panel that opens in the page with the first few Posts the same
// resource answers as JSON. It waits for a pause, drops whatever an older keystroke asked, and says nothing of a failure,
// since the form still works. Arrow keys move through the suggestions, Enter opens the chosen one, Escape or leaving the
// box closes them, and nothing is asked while an input method is composing. Everything from the server is set as text.
export const search = defineComponent({
  name: 'SearchIsland',
  props: {
    host: { type: Object as PropType<HTMLFormElement>, required: true },
  },
  setup(props) {
    const form = props.host
    const input = form.querySelector<HTMLInputElement>('input[name="q"]')!
    const panel = document.createElement('div')
    panel.className = 'masthead-search-panel'
    panel.id = 'masthead-search-panel'
    panel.hidden = true
    form.append(panel)
    input.setAttribute('role', 'combobox')
    input.setAttribute('aria-autocomplete', 'list')
    input.setAttribute('aria-controls', panel.id)
    input.setAttribute('aria-expanded', 'false')

    let timer: ReturnType<typeof setTimeout> | undefined
    let request: AbortController | undefined
    let composing = false
    let active = -1

    function options(): HTMLAnchorElement[] {
      return [...panel.querySelectorAll<HTMLAnchorElement>('[role="option"]')]
    }

    function choose(index: number): void {
      active = index
      options().forEach((option, at) => option.setAttribute('aria-selected', String(at === index)))
      if (index >= 0) input.setAttribute('aria-activedescendant', options()[index]!.id)
      else input.removeAttribute('aria-activedescendant')
    }

    function close(): void {
      clearTimeout(timer)
      request?.abort()
      request = undefined
      panel.hidden = true
      panel.replaceChildren()
      input.setAttribute('aria-expanded', 'false')
      choose(-1)
    }

    function suggestion(hit: Hit, index: number): HTMLAnchorElement {
      const link = document.createElement('a')
      link.className = 'masthead-search-hit'
      link.id = `masthead-search-hit-${index}`
      link.href = hit.address
      link.setAttribute('role', 'option')
      link.setAttribute('aria-selected', 'false')
      link.tabIndex = -1
      const title = document.createElement('span')
      title.className = 'masthead-search-topic'
      title.textContent = hit.topic
      const where = document.createElement('span')
      where.className = 'masthead-search-where'
      where.textContent = hit.tangent
      const words = document.createElement('span')
      words.className = 'masthead-search-words'
      for (const run of hit.snippet) {
        if (!run.marked) {
          words.append(run.text)
          continue
        }
        const mark = document.createElement('mark')
        mark.className = 'search-mark'
        mark.textContent = run.text
        words.append(mark)
      }
      link.append(title, where, words)
      return link
    }

    function show(text: string, hits: Hit[]): void {
      const list = document.createElement('div')
      list.setAttribute('role', 'listbox')
      list.setAttribute('aria-label', 'Suggestions')
      list.append(...hits.slice(0, suggestionLimit).map(suggestion))
      const closing = document.createElement(hits.length > 0 ? 'a' : 'p')
      closing.className = 'masthead-search-all'
      if (closing instanceof HTMLAnchorElement) {
        closing.href = `${form.action}?q=${encodeURIComponent(text)}`
        closing.textContent = `All results for “${text}”`
      } else {
        closing.setAttribute('role', 'status')
        closing.textContent = `Nothing you can read here holds “${text}”`
      }
      panel.replaceChildren(list, closing)
      panel.hidden = false
      input.setAttribute('aria-expanded', 'true')
      choose(-1)
    }

    async function ask(text: string): Promise<void> {
      const controller = new AbortController()
      request = controller
      try {
        const response = await fetch(`${form.action}?q=${encodeURIComponent(text)}`, {
          headers: { Accept: 'application/json' },
          cache: 'no-store',
          signal: controller.signal,
        })
        if (request !== controller) return
        if (!response.ok) {
          close()
          return
        }
        const page = (await response.json()) as { hits?: Hit[] }
        if (request === controller) show(text, page.hits ?? [])
      } catch {
        if (request === controller) close()
      }
    }

    function schedule(): void {
      close()
      const text = input.value.trim()
      if (composing || text.length < minimumText || !/[\p{L}\p{N}]/u.test(text)) return
      timer = setTimeout(() => void ask(input.value.trim()), searchDelay)
    }

    function onKey(event: KeyboardEvent): void {
      if (event.isComposing || composing) return
      if (event.key === 'Escape') {
        if (!panel.hidden) event.preventDefault()
        close()
        return
      }
      const count = options().length
      if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
        if (count === 0) return
        event.preventDefault()
        choose(event.key === 'ArrowDown' ? (active + 1) % count : (active <= 0 ? count : active) - 1)
        return
      }
      if (event.key === 'Enter' && active >= 0) {
        event.preventDefault()
        options()[active]!.click()
      }
    }

    function onFocusOut(event: FocusEvent): void {
      if (!form.contains(event.relatedTarget as Node | null)) close()
    }

    function onCompositionStart(): void {
      composing = true
      close()
    }

    function onCompositionEnd(): void {
      composing = false
      schedule()
    }

    // A press on a suggestion keeps the focus in the box, so leaving the box never closes the panel before the click lands.
    function keepFocus(event: Event): void {
      event.preventDefault()
    }

    input.addEventListener('input', schedule)
    input.addEventListener('keydown', onKey)
    input.addEventListener('compositionstart', onCompositionStart)
    input.addEventListener('compositionend', onCompositionEnd)
    form.addEventListener('focusout', onFocusOut)
    panel.addEventListener('mousedown', keepFocus)
    onUnmounted(() => {
      close()
      input.removeEventListener('input', schedule)
      input.removeEventListener('keydown', onKey)
      input.removeEventListener('compositionstart', onCompositionStart)
      input.removeEventListener('compositionend', onCompositionEnd)
      form.removeEventListener('focusout', onFocusOut)
      panel.removeEventListener('mousedown', keepFocus)
      panel.remove()
    })
    return () => null
  },
})
