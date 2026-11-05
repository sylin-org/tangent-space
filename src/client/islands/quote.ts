import { defineComponent, onBeforeUnmount, type PropType } from 'vue'
import { composerChanged } from './composer'

// The most characters of a passage one quote carries.
const passageLimit = 1200

/** A passage as a Markdown quote, then the link back to where it stands: its heading, or the article. */
export function quoted(passage: string, address: string, heading: { id: string; text: string } | null): string {
  const lines = passage.trim().slice(0, passageLimit).split(/\r?\n/).map((line) => (line.trim() ? `> ${line.trim()}` : '>'))
  const words = (heading?.text ?? 'the article').replace(/[[\]]/g, '')
  return `${lines.join('\n')}\n\nFrom [${words}](${address}${heading ? `#${heading.id}` : ''})\n\n`
}

// Quoting an article: a passage the reader selects in its body offers "Quote in reply" beside it, which puts the passage
// in the reply box as a Markdown quote linked to its heading, after any words already there, and takes the reader to
// the box. While the reply is on its way the box takes no quote.
export const quote = defineComponent({
  name: 'QuoteIsland',
  props: {
    host: { type: Object as PropType<HTMLElement>, required: true },
    address: { type: String, required: true },
  },
  setup(props) {
    const body = document.getElementById('reply-body') as HTMLTextAreaElement | null
    if (!body) return () => null
    const form = body.form!
    const offer = document.createElement('button')
    offer.type = 'button'
    offer.className = 'button quote-offer'
    offer.textContent = 'Quote in reply'
    offer.hidden = true
    const holder = props.host.parentElement!
    holder.classList.add('has-quote-offer')
    holder.append(offer)

    let passage = ''
    let heading: { id: string; text: string } | null = null

    function selected() {
      const selection = document.getSelection()
      const range = selection && selection.rangeCount > 0 ? selection.getRangeAt(0) : null
      const text = selection?.toString().trim() ?? ''
      if (!range || !text || !props.host.contains(range.commonAncestorContainer)) {
        offer.hidden = true
        return
      }
      passage = text
      const before = [...props.host.querySelectorAll<HTMLElement>('h2[id], h3[id]')].filter(
        (element) => element.compareDocumentPosition(range.startContainer) & Node.DOCUMENT_POSITION_FOLLOWING,
      )
      const found = before.at(-1)
      heading = found ? { id: found.id, text: found.textContent?.trim() ?? '' } : null
      const box = range.getBoundingClientRect()
      const frame = holder.getBoundingClientRect()
      offer.style.top = `${box.bottom - frame.top + 6}px`
      offer.style.left = `${Math.max(0, Math.min(box.left - frame.left, frame.width - 160))}px`
      offer.hidden = false
    }

    // The button is pressed before the selection goes, so it reads the passage the reader chose.
    offer.addEventListener('mousedown', (event) => event.preventDefault())
    offer.addEventListener('click', () => {
      if (body.readOnly || !passage) return
      const words = quoted(passage, props.address, heading)
      body.value = body.value.trim() ? `${body.value.trimEnd()}\n\n${words}` : words
      body.dispatchEvent(new Event('input', { bubbles: true }))
      form.dispatchEvent(new CustomEvent(composerChanged))
      offer.hidden = true
      document.getSelection()?.removeAllRanges()
      form.scrollIntoView({ block: 'center', behavior: 'smooth' })
      const editor = form.querySelector<HTMLElement>('[contenteditable="true"]')
      ;(body.hidden && editor ? editor : body).focus({ preventScroll: true })
    })

    document.addEventListener('selectionchange', selected)
    onBeforeUnmount(() => document.removeEventListener('selectionchange', selected))
    return () => null
  },
})
