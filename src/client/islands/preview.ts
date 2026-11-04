import { defineComponent, type PropType } from 'vue'

// The frame that shows the door in the appearance being chosen: its form's preview button sends the choices to the
// frame, and once script runs, the frame follows each change by itself, half a second after the last, and the button
// stands aside. Nothing is saved until the form's own button is pressed.
export const preview = defineComponent({
  name: 'PreviewIsland',
  props: {
    host: { type: Object as PropType<HTMLIFrameElement>, required: true },
    button: { type: String, required: true },
  },
  setup(props) {
    const button = props.host.ownerDocument.getElementById(props.button)
    const form = button instanceof HTMLButtonElement ? button.form : null
    if (button instanceof HTMLButtonElement && form) {
      let pending: ReturnType<typeof setTimeout> | undefined
      const follow = () => {
        clearTimeout(pending)
        pending = setTimeout(() => form.requestSubmit(button), 500)
      }
      form.addEventListener('input', follow)
      form.addEventListener('change', follow)
      button.hidden = true
    }
    return () => null
  },
})
