'use strict';
// Two-step confirmation in place. The page never blocks: the control turns into its question
// with a confirming and a keeping button, and focus stays inside the choice.
(() => {
  function attach(trigger, { question, confirmLabel, onConfirm }) {
    const group = document.createElement('div');
    group.className = 'inline-confirm'; group.setAttribute('role', 'group'); group.setAttribute('aria-label', question);
    const prompt = document.createElement('span'); prompt.className = 'inline-confirm-question'; prompt.textContent = question;
    const yes = document.createElement('button'); yes.type = 'button'; yes.className = 'btn inline-confirm-yes'; yes.textContent = confirmLabel;
    const keep = document.createElement('button'); keep.type = 'button'; keep.className = 'btn btn-quiet'; keep.textContent = 'Keep';
    group.append(prompt, yes, keep);
    const reset = () => { if (group.isConnected) group.replaceWith(trigger); };
    trigger.addEventListener('click', () => { trigger.replaceWith(group); yes.focus(); });
    yes.addEventListener('click', () => { reset(); onConfirm(); });
    keep.addEventListener('click', () => { reset(); trigger.focus(); });
    group.addEventListener('keydown', event => {
      if (event.key !== 'Escape') return;
      event.stopPropagation(); reset(); trigger.focus();
    });
    return { reset };
  }
  window.TangentInlineConfirm = { attach };
})();
