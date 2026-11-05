import { defineComponent, type PropType } from 'vue'
import { keepPlace, readPlace } from '../reading'

// Keeps where the reader stopped in this Topic: the furthest Post that came on screen. Leaving the page keeps it, unless
// they reached the end of the Topic's newest window, which reads the Topic through and forgets this Topic's place.
export const reading = defineComponent({
  name: 'ReadingIsland',
  props: {
    host: { type: Object as PropType<HTMLElement>, required: true },
    participant: { type: String, required: true },
    topic: { type: String, required: true },
    title: { type: String, required: true },
    address: { type: String, required: true },
    newest: { type: Boolean, default: false },
  },
  setup(props) {
    const posts = [...document.querySelectorAll<HTMLElement>('article.post[id]')]
    if (posts.length === 0 || typeof IntersectionObserver === 'undefined') return () => null
    let furthest = -1
    const observer = new IntersectionObserver((entries) => {
      for (const entry of entries) if (entry.isIntersecting) furthest = Math.max(furthest, posts.indexOf(entry.target as HTMLElement))
    })
    for (const post of posts) observer.observe(post)

    const keep = () => {
      if (furthest < 0) return
      if (props.newest && furthest === posts.length - 1) {
        if (readPlace(props.participant)?.topic === props.topic) keepPlace(props.participant, null)
        return
      }
      const post = posts[furthest]
      keepPlace(props.participant, {
        topic: props.topic,
        title: props.title,
        address: post.classList.contains('post-root') ? props.address : `${props.address}/${encodeURIComponent(post.id)}`,
      })
    }
    window.addEventListener('pagehide', keep)
    document.addEventListener('visibilitychange', () => {
      if (document.visibilityState === 'hidden') keep()
    })
    return () => null
  },
})
