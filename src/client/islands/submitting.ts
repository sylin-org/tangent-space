import { defineComponent, type PropType } from 'vue'

// A form that says where it stands. A button with a pending label reads it, and is marked pending, until every
// field the form requires is filled, and then reads its ready label. Once submitted, the form is busy and its
// button reads its busy label and takes no second press, until the page is left. A page restored from the
// back-forward cache is ready again.
export const submitting = defineComponent({
  name: 'SubmittingIsland',
  props: {
    host: { type: Object as PropType<HTMLFormElement>, required: true },
  },
  setup(props) {
    const button = props.host.querySelector<HTMLButtonElement>('[data-busy-label]')
    const label = button?.textContent ?? ''
    const standing = () => {
      if (!button?.dataset.pendingLabel || props.host.getAttribute('aria-busy') === 'true') return
      const ready = props.host.checkValidity()
      button.dataset.state = ready ? 'ready' : 'pending'
      button.textContent = (ready ? button.dataset.readyLabel : button.dataset.pendingLabel) ?? label
    }
    props.host.addEventListener('change', standing)
    props.host.addEventListener('input', standing)
    standing()
    props.host.addEventListener('submit', (event) => {
      if (props.host.getAttribute('aria-busy') === 'true') {
        event.preventDefault()
        return
      }
      props.host.setAttribute('aria-busy', 'true')
      if (button) {
        button.setAttribute('aria-disabled', 'true')
        button.textContent = button.dataset.busyLabel ?? label
      }
    })
    window.addEventListener('pageshow', (event) => {
      if (!event.persisted) return
      props.host.removeAttribute('aria-busy')
      if (button) {
        button.removeAttribute('aria-disabled')
        button.textContent = label
      }
      standing()
    })
    return () => null
  },
})
