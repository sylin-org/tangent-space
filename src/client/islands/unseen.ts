import { defineComponent, onUnmounted, type PropType } from 'vue'
import type { LiveEvent } from '../live'

// The name of the event the live updater dispatches on the document with each run of rows the page's stream delivers.
export const logEvent = 'tangent:log'

// The highest count said as it is; past it the count reads 99+, as the server writes it.
export const countLimit = 99

// The count as the masthead shows it: the number, with a plus where it is a lower bound, and 99+ past the limit.
export function badge(count: number, atLeast: boolean): string {
  return count > countLimit ? `${countLimit}+` : `${count}${atLeast ? '+' : ''}`
}

// The count in words, for whoever does not see the badge.
export function label(count: number, atLeast: boolean): string {
  return `${atLeast ? 'At least ' : ''}${count} ${count === 1 && !atLeast ? 'new notification' : 'new notifications'}`
}

// The standing an arrival leaves an applicant with, as the stream writes it.
export const applied = 'Applied'

// How many of the reader's notifications they have not seen, in the masthead: the server's count, raised by one for each
// row the page's stream delivers past where the count reads through that targets the reader and that someone else did,
// and, for a reader whose count holds the applications that wait (applications), by one for each application, the
// rule the server counts by; the stream only delivers rows that reach the reader, and an application only to whoever
// decides it. Hidden while it is zero. It only ever counts up: opening the notifications is what brings it down, on the
// next page, as does a decision about an application, which the next page no longer counts.
export const unseen = defineComponent({
  name: 'UnseenIsland',
  props: {
    host: { type: Object as PropType<HTMLElement>, required: true },
    reader: { type: String, required: true },
    count: { type: Number, default: 0 },
    atLeast: { type: Boolean, default: false },
    through: { type: Number, default: 0 },
    applications: { type: Boolean, default: false },
  },
  setup(props, { expose }) {
    let count = props.count
    let through = props.through

    function show(): void {
      props.host.hidden = count === 0
      props.host.setAttribute('aria-label', label(count, props.atLeast))
      const shown = props.host.querySelector('.masthead-unseen-count')
      if (shown) shown.textContent = badge(count, props.atLeast)
    }

    function onLog(event: Event): void {
      const rows = (event as CustomEvent<LiveEvent[]>).detail
      if (!Array.isArray(rows)) return
      let raised = 0
      for (const row of rows) {
        if (row.seq <= through) continue
        through = row.seq
        if (row.actor === props.reader) continue
        if (row.target === props.reader || (props.applications && row.kind === 'standing.changed' && row.standing === applied)) raised++
      }
      if (raised === 0) return
      count += raised
      show()
    }

    document.addEventListener(logEvent, onLog)
    onUnmounted(() => document.removeEventListener(logEvent, onLog))
    expose({ count: () => count })
    return () => null
  },
})
