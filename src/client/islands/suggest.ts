import { defineComponent, onUnmounted, type PropType } from 'vue'
import { attachSuggestions, textareaSurface } from '../suggest'

// A body field that offers what its writer can name as they type @ or #, read from its Tangent's suggestions at
// address (suggest.ts). Without script the field is a plain field, and a typed handle or slug is still written as its
// link when the Post is.
export const suggest = defineComponent({
  name: 'SuggestIsland',
  props: {
    host: { type: Object as PropType<HTMLTextAreaElement>, required: true },
    address: { type: String, required: true },
  },
  setup(props) {
    onUnmounted(attachSuggestions(textareaSurface(props.host), props.address))
    return () => null
  },
})
