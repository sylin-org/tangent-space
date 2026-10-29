import { defineComponent, type PropType } from 'vue'

// A form that says it is on its way: once submitted, the form is busy, its text fields are read-only, and its button
// reads its busy label and takes no second press, until the page is left. A page restored from the back-forward
// cache is ready again.
export const submitting = defineComponent({
  name: 'SubmittingIsland',
  props: {
    host: { type: Object as PropType<HTMLFormElement>, required: true },
  },
  setup(props) {
    const button = props.host.querySelector<HTMLButtonElement>('[data-busy-label]')
    const label = button?.textContent ?? ''
    const fields = () => props.host.querySelectorAll<HTMLInputElement>('input:not([type="hidden"])')
    props.host.addEventListener('submit', (event) => {
      if (props.host.getAttribute('aria-busy') === 'true') {
        event.preventDefault()
        return
      }
      props.host.setAttribute('aria-busy', 'true')
      for (const field of fields()) field.readOnly = true
      if (button) {
        button.setAttribute('aria-disabled', 'true')
        button.textContent = button.dataset.busyLabel ?? label
      }
    })
    window.addEventListener('pageshow', (event) => {
      if (!event.persisted) return
      props.host.removeAttribute('aria-busy')
      for (const field of fields()) field.readOnly = false
      if (button) {
        button.removeAttribute('aria-disabled')
        button.textContent = label
      }
    })
    return () => null
  },
})
