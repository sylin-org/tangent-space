import { defineComponent, onBeforeUnmount, type PropType } from 'vue'

// The article island: the visual editor over the body field of every form a Post is written in, loaded only where a page has one, since it
// is by far the heaviest thing the client holds. Until it arrives, and without script, the field is a plain field that
// posts.
export const article = defineComponent({
  name: 'ArticleIsland',
  props: {
    host: { type: Object as PropType<HTMLElement>, required: true },
    door: { type: String, default: '/' },
    bring: { type: String, default: null },
    participant: { type: String, default: null },
    edit: { type: String, default: null },
    suggest: { type: String, default: null },
    post: { type: Boolean, default: false },
  },
  setup(props) {
    let stop: (() => void) | null = null
    let gone = false
    onBeforeUnmount(() => {
      gone = true
      stop?.()
    })
    void import('../article/editor').then(({ writeArticle }) => {
      if (!gone) stop = writeArticle(props)
    })
    return () => null
  },
})
