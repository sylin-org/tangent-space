import { defineComponent, type PropType } from 'vue'
import { fetchFragment } from './fragment'

// A Post's earlier versions, in the closed disclosure under it: read the first time it opens, from the history's own
// address, and put in place of the link that reads them without script. A read that fails leaves the link.
export const history = defineComponent({
  name: 'HistoryIsland',
  props: {
    host: { type: Object as PropType<HTMLDetailsElement>, required: true },
    url: { type: String, required: true },
  },
  setup(props) {
    let read = false
    props.host.addEventListener('toggle', async () => {
      if (!props.host.open || read) return
      read = true
      const body = props.host.querySelector<HTMLElement>('.post-history-body')
      if (!body) return
      body.setAttribute('aria-busy', 'true')
      try {
        body.replaceChildren((await fetchFragment(props.url)).firstElementChild!)
      } catch {
        read = false
      } finally {
        body.removeAttribute('aria-busy')
      }
    })
    return () => null
  },
})
