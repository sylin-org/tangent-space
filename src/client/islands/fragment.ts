import { defineComponent, type PropType } from 'vue'

// The request header that asks the server for a resource's component alone, without the page around it.
export const fragmentHeader = 'Tangent-Fragment'

// A server-rendered resource that can refresh itself: refresh() fetches the resource's fragment and swaps
// the host's content for it. The fragment is the resource's component, whose root element is the host, so
// the host keeps its own element and takes the root's children. A response that is not OK leaves the
// content as it was and rejects; when refreshes overlap, only the latest one's response is applied.
export const fragment = defineComponent({
  name: 'FragmentIsland',
  props: {
    host: { type: Object as PropType<HTMLElement>, required: true },
    url: { type: String, required: true },
  },
  setup(props, { expose }) {
    let latest = 0

    async function refresh(): Promise<void> {
      const request = ++latest
      const response = await fetch(props.url, { headers: { Accept: 'text/html', [fragmentHeader]: '1' } })
      if (!response.ok) throw new Error(`The fragment at ${props.url} answered ${response.status}.`)
      const html = await response.text()
      if (request !== latest) return
      const parsed = document.createElement('template')
      parsed.innerHTML = html
      const root = parsed.content.firstElementChild
      if (!root) throw new Error(`The fragment at ${props.url} holds no element.`)
      props.host.replaceChildren(...root.childNodes)
    }

    expose({ refresh })
    return () => null
  },
})
