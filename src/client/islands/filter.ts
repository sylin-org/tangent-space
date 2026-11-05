import { defineComponent, type PropType } from 'vue'

/** Whether an article row stands under the month and the author chosen, an empty choice taking every one. */
export function matches(row: HTMLElement, month: string, author: string): boolean {
  return (!month || row.dataset.month === month) && (!author || row.dataset.author === author)
}

// The articles filter in an articles Tangent's rail: a month and an author, each pressed to narrow the articles on this
// page and pressed again to let them all back. The server renders it hidden, since without script it does nothing.
export const filter = defineComponent({
  name: 'FilterIsland',
  props: {
    host: { type: Object as PropType<HTMLElement>, required: true },
  },
  setup(props) {
    const rows = [...document.querySelectorAll<HTMLElement>('.article-list > article[data-month]')]
    const note = props.host.querySelector<HTMLElement>('.article-filter-note')
    const choices = [...props.host.querySelectorAll<HTMLButtonElement>('.article-filter-choice')]
    let month = ''
    let author = ''

    function apply() {
      let shown = 0
      for (const row of rows) {
        row.hidden = !matches(row, month, author)
        if (!row.hidden) shown++
      }
      for (const choice of choices)
        choice.setAttribute('aria-pressed', String(choice.dataset.month ? choice.dataset.month === month : choice.dataset.author === author))
      if (note)
        note.textContent = !month && !author ? '' : shown === 0 ? 'No articles on this page match.' : `${shown} of ${rows.length} on this page.`
    }

    for (const choice of choices)
      choice.addEventListener('click', () => {
        if (choice.dataset.month !== undefined) month = month === choice.dataset.month ? '' : choice.dataset.month
        else author = author === choice.dataset.author ? '' : (choice.dataset.author ?? '')
        apply()
      })
    props.host.hidden = false
    return () => null
  },
})
