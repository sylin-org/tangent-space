import { defineComponent, onUnmounted, type PropType } from 'vue'
import { openComposer } from '../named'
import { fetchFragment } from './fragment'

// A participant's card, beside any name, mark or mention of them in the page: a click, a right-click or a long press on a
// link to their profile (door + p/{handle}) or on a mark that names it (data-person) opens it, read from their profile's
// own fragment, which holds what the card shows: their mark, name, agent tag, handle, roles here, since when, and
// description; for a reader who may change their standing or roles, what the profile offers them, coming back to this
// page. It offers their profile, their page on Bluesky by DID, a mention of them in the composer open in the page, and
// a copy of their profile's address. Escape, a click outside it or focus leaving it closes it. Never a dialog: it sits in
// the page, and nothing behind it stops answering. A click with a modifier key, or without script, follows the link.

// How long a press holds before it opens the card.
export const longPress = 500

// The profiles read while the page is open, by address.
const read = new Map<string, Promise<DocumentFragment>>()

export const person = defineComponent({
  name: 'PersonIsland',
  props: {
    host: { type: Object as PropType<HTMLElement>, required: true },
    door: { type: String, required: true },
  },
  setup(props) {
    const profiles = new URL('p/', new URL(props.door, location.href)).pathname
    let card: HTMLElement | null = null
    let opener: HTMLElement | null = null
    let pressed: ReturnType<typeof setTimeout> | undefined
    let pressedAt = { x: 0, y: 0 }
    // Whether the touch press in hand opened the card, and until when the click its own lift makes is its own.
    let pressOpened = false
    let pressClickUntil = 0

    // The profile address an element names, where it is a link to one or a mark that carries one; a link with a query or a
    // fragment, as a profile's own pages are, is never a person's link.
    function profileOf(element: Element | null): { at: HTMLElement; address: string } | null {
      const at = element?.closest<HTMLElement>('a[href], [data-person]')
      if (!at || at.closest('.person-card, .masthead, .article-surface, [contenteditable="true"]')) return null
      const address = at.dataset.person ?? at.getAttribute('href') ?? ''
      let url: URL
      try {
        url = new URL(address, location.href)
      } catch {
        return null
      }
      if (url.origin !== location.origin || url.search || url.hash || !url.pathname.startsWith(profiles)) return null
      const rest = url.pathname.slice(profiles.length)
      return rest.length > 0 && !rest.includes('/') ? { at, address: url.href } : null
    }

    function close(returnFocus = false) {
      if (!card) return
      card.remove()
      card = null
      opener?.setAttribute('aria-expanded', 'false')
      if (returnFocus) opener?.focus()
      opener = null
    }

    function place(at: HTMLElement) {
      if (!card) return
      const box = at.getBoundingClientRect()
      const width = card.offsetWidth
      const left = Math.max(8, Math.min(box.left, document.documentElement.clientWidth - width - 8))
      card.style.left = `${left + window.scrollX}px`
      card.style.top = `${box.bottom + window.scrollY + 6}px`
    }

    function action(label: string, run: () => void): HTMLButtonElement {
      const button = document.createElement('button')
      button.type = 'button'
      button.className = 'button person-card-action'
      button.textContent = label
      button.addEventListener('click', run)
      return button
    }

    function link(label: string, href: string, outside = false): HTMLAnchorElement {
      const anchor = document.createElement('a')
      anchor.className = 'button person-card-action'
      anchor.href = href
      anchor.textContent = label
      if (outside) {
        anchor.target = '_blank'
        anchor.rel = 'noopener noreferrer'
      }
      return anchor
    }

    // The card's content, from the profile's fragment: its head, what the reader may change of them, and the actions.
    function fill(into: HTMLElement, fragment: DocumentFragment, address: string) {
      const head = fragment.querySelector<HTMLElement>('.profile-head')
      if (!head) throw new Error('The profile holds no head.')
      const name = head.dataset.name ?? ''
      const who = document.createElement('div')
      who.className = 'person-card-who'
      const mark = head.querySelector('.mark')
      if (mark) who.append(mark.cloneNode(true))
      const text = document.createElement('div')
      text.className = 'person-card-text'
      const title = document.createElement('p')
      title.className = 'person-card-name'
      title.append(...[...(head.querySelector('h1')?.childNodes ?? [])].map((node) => node.cloneNode(true)))
      text.append(title)
      const meta = head.querySelector('.tangent-meta')
      if (meta) text.append(Object.assign(meta.cloneNode(true) as HTMLElement, { className: 'person-card-meta' }))
      who.append(text)
      into.append(who)
      const description = head.querySelector('.profile-description')
      if (description) into.append(Object.assign(description.cloneNode(true) as HTMLElement, { className: 'person-card-description' }))

      const standing = fragment.querySelector<HTMLElement>('.profile-standing .door-offer')
      if (standing) {
        const changes = standing.cloneNode(true) as HTMLElement
        changes.classList.add('person-card-standing')
        for (const back of changes.querySelectorAll<HTMLInputElement>('input[name="back"]')) back.value = location.pathname + location.search
        into.append(changes)
      }

      const actions = document.createElement('div')
      actions.className = 'person-card-actions'
      actions.append(link('Profile', head.dataset.address ?? address))
      const composer = openComposer()
      if (composer && head.dataset.mention)
        actions.append(action('Mention', () => {
          const target = head.dataset.mention!
          close()
          composer.put({ words: '@' + name, target })
        }))
      const copy = action('Copy link', async () => {
        try {
          await navigator.clipboard.writeText(head.dataset.address ?? address)
          copy.textContent = 'Copied'
        } catch {
          copy.textContent = 'Select and copy'
        }
      })
      actions.append(copy)
      if (head.dataset.elsewhere) actions.append(link('On Bluesky ↗', head.dataset.elsewhere, true))
      into.append(actions)
    }

    // A click on the name that opened the card closes it; anything else that opens it on that name leaves it open.
    function toggle(at: HTMLElement, address: string) {
      if (opener === at) close()
      else void open(at, address)
    }

    async function open(at: HTMLElement, address: string) {
      if (opener === at) return
      close()
      opener = at
      card = document.createElement('section')
      card.className = 'person-card panel'
      card.tabIndex = -1
      card.setAttribute('aria-label', (at.textContent ?? '').trim() || 'Participant')
      card.setAttribute('aria-busy', 'true')
      card.addEventListener('focusout', (event) => {
        const next = event.relatedTarget as Node | null
        if (next && !card?.contains(next) && next !== opener) close()
      })
      document.body.append(card)
      at.setAttribute('aria-expanded', 'true')
      place(at)
      card.focus({ preventScroll: true })
      const shown = card
      try {
        if (!read.has(address)) read.set(address, fetchFragment(address))
        const fragment = (await read.get(address)!).cloneNode(true) as DocumentFragment
        if (card !== shown) return
        fill(shown, fragment, address)
      } catch {
        read.delete(address)
        if (card !== shown) return
        const said = document.createElement('p')
        said.className = 'person-card-said'
        said.textContent = 'Their profile can’t be read right now.'
        const actions = document.createElement('div')
        actions.className = 'person-card-actions'
        actions.append(link('Profile', address))
        shown.append(said, actions)
      }
      shown.removeAttribute('aria-busy')
      place(at)
    }

    const onClick = (event: MouseEvent) => {
      // The click a long press's own lift makes on the name is the press's, already answered.
      if (performance.now() < pressClickUntil && opener?.contains(event.target as Node)) {
        pressClickUntil = 0
        event.preventDefault()
        return
      }
      if (card && !card.contains(event.target as Node) && !opener?.contains(event.target as Node)) close()
      if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return
      const found = profileOf(event.target as Element)
      if (!found) return
      event.preventDefault()
      toggle(found.at, found.address)
    }

    const onContextMenu = (event: MouseEvent) => {
      const found = profileOf(event.target as Element)
      if (!found) return
      event.preventDefault()
      void open(found.at, found.address)
    }

    const onPointerDown = (event: PointerEvent) => {
      if (card && !card.contains(event.target as Node) && !opener?.contains(event.target as Node)) close()
      clearTimeout(pressed)
      pressOpened = false
      if (event.pointerType !== 'touch') return
      const found = profileOf(event.target as Element)
      if (!found) return
      pressedAt = { x: event.clientX, y: event.clientY }
      pressed = setTimeout(() => {
        pressOpened = true
        void open(found.at, found.address)
      }, longPress)
    }

    const endPress = () => clearTimeout(pressed)
    const onPointerUp = () => {
      endPress()
      if (pressOpened) pressClickUntil = performance.now() + 400
      pressOpened = false
    }
    // A press that moves is a scroll, not a hold.
    const onPointerMove = (event: PointerEvent) => {
      if (Math.hypot(event.clientX - pressedAt.x, event.clientY - pressedAt.y) > 10) endPress()
    }

    const onKeydown = (event: KeyboardEvent) => {
      if (event.key !== 'Escape' || !card) return
      event.preventDefault()
      close(true)
    }

    const onResize = () => opener && place(opener)

    document.addEventListener('click', onClick)
    document.addEventListener('contextmenu', onContextMenu)
    document.addEventListener('pointerdown', onPointerDown)
    document.addEventListener('pointerup', onPointerUp)
    document.addEventListener('pointercancel', endPress)
    document.addEventListener('pointermove', onPointerMove)
    document.addEventListener('keydown', onKeydown)
    window.addEventListener('resize', onResize)
    onUnmounted(() => {
      close()
      document.removeEventListener('click', onClick)
      document.removeEventListener('contextmenu', onContextMenu)
      document.removeEventListener('pointerdown', onPointerDown)
      document.removeEventListener('pointerup', onPointerUp)
      document.removeEventListener('pointercancel', endPress)
      document.removeEventListener('pointermove', onPointerMove)
      document.removeEventListener('keydown', onKeydown)
      window.removeEventListener('resize', onResize)
    })
    return () => null
  },
})
