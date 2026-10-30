import { defineComponent, onUnmounted, type PropType } from 'vue'
import { createLiveTransport, type LiveEvent, type LiveTransport } from '../live'
import { fetchFragment } from './fragment'
import { logEvent } from './unseen'

// The live updater, one per page: it follows the log from where the page's request found it (live.ts) and patches the
// page as rows that concern it arrive, fetching what changed as the server renders it. On a Topic's page, a new reply
// in the Topic is fetched as its Post's fragment and appended when the page shows the newest window, and otherwise
// counted in the notice below the replies, which links to it; a Post on the page that is edited or removed is fetched
// again in place. On a Tangent's page, its list of Topics is fetched again, which marks what is new. On the door, the
// door is fetched again from its own fragment. When the transport has fallen back to the door, which names no rows,
// the Tangent's list and the door are fetched again and a Topic's notice says there may be new replies. Every run of
// rows is also dispatched on the document (logEvent), where the masthead's count of unseen notifications reads it. It
// only ever reads: a checkpoint moves where the reader says so (the seen island), never here.
export const live = defineComponent({
  name: 'LiveIsland',
  props: {
    host: { type: Object as PropType<HTMLElement>, required: true },
    events: { type: String, required: true },
    door: { type: String, required: true },
    since: { type: Number, default: null },
    page: { type: String as PropType<'door' | 'tangent' | 'topic'>, required: true },
    tangent: { type: String, default: null },
    topic: { type: String, default: null },
    address: { type: String, default: null },
    newest: { type: Boolean, default: false },
  },
  setup(props, { expose }) {
    const waiting: string[] = []
    let refreshing: Promise<void> | null = null
    let again = false

    // The whole of what this page shows of a Tangent or the door, fetched again: one fetch at a time, and one more after
    // it when rows arrived meanwhile.
    function refresh(): Promise<void> {
      if (refreshing) {
        again = true
        return refreshing
      }
      refreshing = (async () => {
        try {
          do {
            again = false
            await (props.page === 'door' ? refreshDoor() : refreshTangent())
          } while (again)
        } catch {
          // The page keeps what it shows; the next row fetches it again.
        } finally {
          refreshing = null
        }
      })()
      return refreshing
    }

    async function refreshDoor(): Promise<void> {
      const main = document.querySelector<HTMLElement>('main[data-island="fragment"]')
      if (!main) return
      const root = (await fetchFragment(props.door)).firstElementChild!
      main.replaceChildren(...root.childNodes)
    }

    async function refreshTangent(): Promise<void> {
      const list = document.querySelector<HTMLElement>('main.tangent-main')
      if (!list) return
      const fresh = (await fetchFragment(location.href)).querySelector('main.tangent-main')
      if (fresh) list.replaceChildren(...fresh.childNodes)
    }

    // A Post of this Topic as the server renders it at its own address, without the marks of being that address's
    // anchor; null when the page there does not hold it.
    async function post(id: string): Promise<HTMLElement | null> {
      const found = (await fetchFragment(props.address + '/' + encodeURIComponent(id))).querySelector<HTMLElement>(`article[id="${id}"]`)
      if (!found) return null
      found.classList.remove('is-anchor')
      found.removeAttribute('aria-current')
      return found
    }

    function shown(id: string): HTMLElement | null {
      const element = document.getElementById(id)
      return element?.tagName === 'ARTICLE' ? element : null
    }

    function append(article: HTMLElement): void {
      const replies = props.host.closest('.topic-replies') ?? props.host.parentElement
      if (!replies) return
      replies.querySelector('.topic-replies-empty')?.remove()
      const before = replies.querySelector('form[data-island="seen"]') ?? props.host
      replies.insertBefore(article, before.parentElement === replies ? before : null)
    }

    function notice(text: string, href: string): void {
      const nav = props.host.querySelector<HTMLElement>('nav')
      const link = nav?.querySelector<HTMLAnchorElement>('a')
      if (!nav || !link) return
      link.textContent = text
      link.href = href
      nav.hidden = false
    }

    async function onTopic(event: LiveEvent): Promise<void> {
      if (event.topic !== props.topic || !event.post || !props.address) return
      if (event.kind === 'post.created' && event.post !== event.topic) {
        if (shown(event.post) || waiting.includes(event.post)) return
        if (props.newest) {
          const article = await post(event.post)
          if (article && !shown(event.post)) append(article)
          return
        }
        waiting.push(event.post)
        notice(`${waiting.length} new ${waiting.length === 1 ? 'reply' : 'replies'} →`, props.address + '/' + encodeURIComponent(waiting[0]))
      } else if (event.kind === 'post.edited' || event.kind === 'post.removed') {
        if (!shown(event.post)) return
        const article = await post(event.post)
        const existing = shown(event.post)
        if (!article || !existing) return
        if (existing.classList.contains('is-anchor')) {
          article.classList.add('is-anchor')
          article.setAttribute('aria-current', 'true')
        }
        existing.replaceWith(article)
      }
    }

    async function onEvents(events: LiveEvent[]): Promise<boolean> {
      document.dispatchEvent(new CustomEvent(logEvent, { detail: events }))
      if (props.page === 'topic') {
        for (const event of events) {
          try {
            await onTopic(event)
          } catch {
            // A Post that could not be fetched stays as the page shows it.
          }
        }
      } else if (props.page === 'door' || events.some(event => event.tangent === props.tangent && event.kind.startsWith('post.'))) {
        await refresh()
      }
      return true
    }

    async function onMoved(): Promise<boolean> {
      if (props.page === 'topic') {
        if (props.address) notice('There may be new replies →', props.address)
      } else await refresh()
      return true
    }

    const transport: LiveTransport = createLiveTransport({ events: props.events, door: props.door, onEvents, onMoved })
    const visibility = () => transport.setVisible(document.visibilityState !== 'hidden')
    document.addEventListener('visibilitychange', visibility)
    transport.start({ since: props.since })
    onUnmounted(() => {
      document.removeEventListener('visibilitychange', visibility)
      transport.stop()
    })

    expose({ transport })
    return () => null
  },
})
