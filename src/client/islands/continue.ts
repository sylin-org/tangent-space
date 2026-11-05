import { defineComponent, type PropType } from 'vue'
import { keepPlace, readPlace } from '../reading'

// The door's one link back to the Topic the reader left mid-way, where this browser kept their place and the place
// still answers at that address for them; a place that no longer answers is forgotten. The door refreshes itself, so
// the link is put back on each door it renders.
export const continueReading = defineComponent({
  name: 'ContinueIsland',
  props: {
    host: { type: Object as PropType<HTMLElement>, required: true },
    participant: { type: String, required: true },
  },
  setup(props) {
    const place = readPlace(props.participant)
    if (!place) return () => null
    const main = props.host.closest('main')
    const show = () => {
      const line = main?.querySelector<HTMLElement>('.door-continue') ?? props.host
      const link = line.querySelector('a')
      if (!link) return
      link.href = place.address
      const title = document.createElement('strong')
      title.textContent = place.title
      link.replaceChildren('Continue reading ', title, ' →')
      line.hidden = false
    }
    void fetch(place.address, { method: 'HEAD', credentials: 'same-origin', cache: 'no-store' })
      .then((answer) => {
        if (!answer.ok) return keepPlace(props.participant, null)
        show()
        if (main) new MutationObserver(() => main.querySelector('.door-continue[hidden]') && show()).observe(main, { childList: true })
      })
      .catch(() => {})
    return () => null
  },
})
