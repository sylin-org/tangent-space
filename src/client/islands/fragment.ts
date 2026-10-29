import { defineComponent, type PropType } from 'vue'

// The request header that asks the server for a resource's component alone, without the page around it.
export const fragmentHeader = 'Tangent-Fragment'

// The resource's fragment at url, parsed: the component's elements, whose first is its root. A response that is not
// OK, or that holds no element, rejects.
export async function fetchFragment(url: string, signal?: AbortSignal): Promise<DocumentFragment> {
  const response = await fetch(url, { headers: { Accept: 'text/html', [fragmentHeader]: '1' }, signal })
  if (!response.ok) throw new Error(`The fragment at ${url} answered ${response.status}.`)
  const parsed = document.createElement('template')
  parsed.innerHTML = await response.text()
  if (!parsed.content.firstElementChild) throw new Error(`The fragment at ${url} holds no element.`)
  return parsed.content
}

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
      const root = (await fetchFragment(props.url)).firstElementChild!
      if (request !== latest) return
      props.host.replaceChildren(...root.childNodes)
    }

    expose({ refresh })
    return () => null
  },
})
