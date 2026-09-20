// Sylin's cursor-following white sheen + rainbow foil, ported from card-foil.js,
// for the dynamically rendered cards.
export function installCardFoil(): void {
  const reduced = matchMedia('(prefers-reduced-motion: reduce)')
  let active: HTMLElement | undefined
  document.addEventListener('pointermove', event => {
    const target = event.target as HTMLElement | null
    const card = target?.closest?.('.sylin-card') as HTMLElement | null
    if (active && active !== card) active.style.setProperty('--holo', '0')
    active = card ?? undefined
    if (!card || reduced.matches || event.pointerType === 'touch') return
    const rect = card.getBoundingClientRect()
    card.style.setProperty('--mx', ((event.clientX - rect.left) / rect.width * 100) + '%')
    card.style.setProperty('--my', ((event.clientY - rect.top) / rect.height * 100) + '%')
    card.style.setProperty('--holo', '1')
  })
  document.addEventListener('pointerout', event => {
    if (active && !active.contains(event.relatedTarget as Node)) {
      active.style.setProperty('--holo', '0')
      active = undefined
    }
  })
  reduced.addEventListener('change', () => {
    if (active) active.style.setProperty('--holo', '0')
  })
}
