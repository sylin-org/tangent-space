import { defineComponent, type PropType } from 'vue'

// A form that says it is on its way: once submitted, the form is busy, its text fields are read-only, and its button
// reads its busy label and takes no second press, until the page is left. A page restored from the back-forward
// cache is ready again. A hidden field with data-carry is an address that takes another field's value along, under
// the name data-carry-as, so a failed sign-in comes back with the handle that was typed.
export const submitting = defineComponent({
  name: 'SubmittingIsland',
  props: {
    host: { type: Object as PropType<HTMLFormElement>, required: true },
  },
  setup(props) {
    const button = props.host.querySelector<HTMLButtonElement>('[data-busy-label]')
    const label = button?.textContent ?? ''
    const carry = (carrier: HTMLInputElement) => {
      const carried = props.host.elements.namedItem(carrier.dataset.carry ?? '')
      const value = carried instanceof HTMLInputElement ? carried.value.trim() : ''
      if (!value || !carrier.dataset.carryAs || !carrier.value) return
      const separator = carrier.value.includes('?') ? '&' : '?'
      carrier.value = `${carrier.value}${separator}${encodeURIComponent(carrier.dataset.carryAs)}=${encodeURIComponent(value)}`
    }
    const fields = () => props.host.querySelectorAll<HTMLInputElement>('input:not([type="hidden"])')
    props.host.addEventListener('submit', (event) => {
      if (props.host.getAttribute('aria-busy') === 'true') {
        event.preventDefault()
        return
      }
      props.host.setAttribute('aria-busy', 'true')
      for (const carrier of props.host.querySelectorAll<HTMLInputElement>('input[type="hidden"][data-carry]')) carry(carrier)
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
