import { defineComponent, type PropType } from 'vue'

// A button that copies the text of the element its target names. The server renders it hidden, since without
// script it does nothing and the text stays selectable; the island shows it, and after a copy it reads "Copied"
// for a moment.
export const copy = defineComponent({
  name: 'CopyIsland',
  props: {
    host: { type: Object as PropType<HTMLButtonElement>, required: true },
    target: { type: String, required: true },
  },
  setup(props) {
    const label = props.host.textContent ?? 'Copy'
    let timer: ReturnType<typeof setTimeout> | undefined
    props.host.hidden = false
    props.host.addEventListener('click', async () => {
      const text = document.getElementById(props.target)?.textContent ?? ''
      try {
        await navigator.clipboard.writeText(text)
        props.host.textContent = 'Copied'
      } catch {
        props.host.textContent = 'Select and copy'
      }
      clearTimeout(timer)
      timer = setTimeout(() => (props.host.textContent = label), 2000)
    })
    return () => null
  },
})
