import { defineComponent, type PropType } from 'vue'
import { savedTheme, selectTheme, themes, type Theme } from '../appearance'

// The footer's appearance switch: System, Light or Dark, remembered for this browser, so a visitor chooses too.
// The server renders the three buttons with System pressed; the island presses the saved choice and applies a
// click at once.
export const appearance = defineComponent({
  name: 'AppearanceIsland',
  props: {
    host: { type: Object as PropType<HTMLElement>, required: true },
  },
  setup(props) {
    const buttons = [...props.host.querySelectorAll<HTMLButtonElement>('[data-theme-choice]')]
    const press = (theme: Theme) => {
      for (const button of buttons) button.setAttribute('aria-pressed', String(button.dataset.themeChoice === theme))
    }
    for (const button of buttons)
      button.addEventListener('click', () => {
        const theme = button.dataset.themeChoice as Theme
        if (!themes.includes(theme)) return
        selectTheme(theme)
        press(theme)
      })
    press(savedTheme())
    return () => null
  },
})
