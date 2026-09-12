// Sylin's cursor-following white sheen + rainbow foil, adapted for dynamically rendered cards.
'use strict';
(() => {
  const reduced = matchMedia('(prefers-reduced-motion: reduce)');
  let active;
  document.addEventListener('pointermove', event => {
    const card = event.target.closest('.sylin-card');
    if (active && active !== card) active.style.setProperty('--holo', '0');
    active = card;
    if (!card || reduced.matches || event.pointerType === 'touch') return;
    const rect = card.getBoundingClientRect();
    card.style.setProperty('--mx', ((event.clientX - rect.left) / rect.width * 100) + '%');
    card.style.setProperty('--my', ((event.clientY - rect.top) / rect.height * 100) + '%');
    card.style.setProperty('--holo', '1');
  });
  document.addEventListener('pointerout', event => {
    if (active && !active.contains(event.relatedTarget)) { active.style.setProperty('--holo', '0'); active = null; }
  });
  reduced.addEventListener('change', () => { if (active) active.style.setProperty('--holo', '0'); });
})();
