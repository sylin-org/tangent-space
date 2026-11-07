import { defineComponent, onUnmounted, type PropType } from 'vue'
import { createLiveTransport, type LiveEvent, type LiveTransport } from '../live'
import { fetchFragment } from './fragment'
import { logEvent } from './unseen'

// What the live island dispatches on the document with each Post it places in the page, appended or in place of its
// earlier self: the Post's article as the server renders it, its islands not yet mounted.
export const postPlaced = 'post-placed'

// The rows that change a Post as a page shows it, which fetch it again in place.
const changesPost = new Set(['post.edited', 'post.removed', 'removal.undone'])

// Whether a row of a Tangent changes its list of Topics: every row of a Post, an undone removal and a deleted Topic.
const changesList = (kind: string) => kind.startsWith('post.') || kind === 'removal.undone' || kind === 'topic.deleted'

// The live updater, one per page: it follows the log from where the page's request found it (live.ts) and patches the
// page as rows that concern it arrive, fetching what changed as the server renders it. On a Topic's page, a new reply
// in the Topic is fetched as its Post's fragment and appended when the page shows the newest window, and otherwise
// counted in the notice below the replies, which links to it; a Post on the page that is edited, removed or has its
// removal undone is fetched again in place. On a Tangent's page, its list of Topics is fetched again as a row of its
// Posts arrives, or one that undoes a removal or deletes a Topic, which marks what is new. On the door, the door is
// fetched again from its own fragment at every row. When the transport has fallen back to the door, which names no rows,
// the Tangent's list and the door are fetched again and a Topic's notice says there may be new replies. Every run of
// rows is also dispatched on the document (logEvent), where the masthead's count of what is new for the reader reads it,
// and each Post placed in the page (postPlaced), whose islands the page entry mounts and to which the composer offers a
// reply; an appended reply also brings the Topic's count, the window's range and the rail's row for it. It only ever
// reads: a checkpoint moves where the reader says so (the seen island), never here.
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
      const list = document.querySelector<HTMLElement>('.tangent-main')
      if (!list) return
      const fresh = (await fetchFragment(location.href)).querySelector('.tangent-main')
      if (fresh) list.replaceChildren(...fresh.childNodes)
    }

    // A Post of this Topic as the server renders it at its own address, without the marks of being that address's
    // anchor, with the page it stands in there; null when that page does not hold it.
    async function post(id: string): Promise<{ article: HTMLElement; page: DocumentFragment } | null> {
      const page = await fetchFragment(props.address + '/' + encodeURIComponent(id))
      const article = page.querySelector<HTMLElement>(`article[id="${id}"]`)
      if (!article) return null
      unmark(article)
      return { article, page }
    }

    function unmark(element: Element): void {
      element.classList.remove('is-current')
      element.removeAttribute('aria-current')
    }

    function placed(article: HTMLElement): void {
      document.dispatchEvent(new CustomEvent(postPlaced, { detail: article }))
    }

    // What the page says of its replies, brought to the page an appended one was fetched from: the Topic's count in its
    // head, the window's range through the new reply, and the reply's row in the rail.
    function counted(page: DocumentFragment, id: string): void {
      const meta = page.querySelector('.topic-meta')
      if (meta) document.querySelector('.topic-meta')?.replaceWith(meta)
      const range = page.querySelector('.topic-replies-range')
      const shownRange = document.querySelector('.topic-replies-range')
      if (range && shownRange) shownRange.textContent = `${shownRange.textContent!.split('–')[0]}–${range.textContent!.split('–').at(-1)!.replace('#', '')}`
      else if (range) document.querySelector('.topic-replies-head')?.append(range)
      const row = page.querySelector(`.topic-rail-replies a[href="#${id}"]`)
      if (!row?.parentElement) return
      unmark(row)
      const list = document.querySelector('.topic-rail-replies')
      if (list) return list.append(row.parentElement)
      // The first reply brings the rail's list too, holding only itself.
      const fresh = row.closest('ul')!
      fresh.replaceChildren(row.parentElement)
      const rail = document.querySelector('aside.rail')
      const heading = fresh.previousElementSibling
      rail?.insertBefore(fresh, rail.querySelector('.article-outline'))
      if (heading) rail?.insertBefore(heading, fresh)
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
          const found = await post(event.post)
          if (!found || shown(event.post)) return
          append(found.article)
          counted(found.page, event.post)
          placed(found.article)
          return
        }
        waiting.push(event.post)
        notice(`${waiting.length} new ${waiting.length === 1 ? 'reply' : 'replies'} →`, props.address + '/' + encodeURIComponent(waiting[0]))
      } else if (changesPost.has(event.kind)) {
        if (!shown(event.post)) return
        const found = await post(event.post)
        const existing = shown(event.post)
        if (!found || !existing) return
        if (existing.classList.contains('is-current')) {
          found.article.classList.add('is-current')
          found.article.setAttribute('aria-current', 'true')
        }
        existing.replaceWith(found.article)
        placed(found.article)
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
      } else if (props.page === 'door' || events.some(event => event.tangent === props.tangent && changesList(event.kind))) {
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
