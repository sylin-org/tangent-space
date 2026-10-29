import { defineComponent, type PropType } from 'vue'

// The seen control at the end of a Topic's window, posted by itself: once the form, and so the end of the window, has
// been on screen while the page is visible, the island posts its fields once, asking for JSON so the page stays where
// it is, and the form leaves the page when the checkpoint has moved. A post that does not succeed leaves the form as
// it was, its button still there to press. Without script, or without IntersectionObserver, the button is the control.
export const seen = defineComponent({
  name: 'SeenIsland',
  props: {
    host: { type: Object as PropType<HTMLFormElement>, required: true },
  },
  setup(props) {
    let posted = false
    let inView = false

    async function post(): Promise<void> {
      if (posted) return
      posted = true
      observer?.disconnect()
      document.removeEventListener('visibilitychange', onVisible)
      const body = new URLSearchParams()
      for (const field of props.host.querySelectorAll<HTMLInputElement>('input[name]')) body.append(field.name, field.value)
      try {
        const response = await fetch(props.host.action, {
          method: 'POST',
          body,
          headers: { Accept: 'application/json' },
          credentials: 'same-origin',
          cache: 'no-store',
        })
        if (response.ok) props.host.hidden = true
      } catch {
        // Left as it was: the button still moves the checkpoint.
      }
    }

    function onVisible(): void {
      if (inView && document.visibilityState === 'visible') void post()
    }

    const observer =
      typeof IntersectionObserver === 'undefined'
        ? undefined
        : new IntersectionObserver((entries) => {
            inView = entries.some((entry) => entry.isIntersecting)
            onVisible()
          })
    observer?.observe(props.host)
    document.addEventListener('visibilitychange', onVisible)

    return () => null
  },
})
