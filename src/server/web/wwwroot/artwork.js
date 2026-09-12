'use strict';
(() => {
  const refreshers = new WeakMap();
  window.TangentArtwork = { refresh: form => refreshers.get(form)?.() };
  for (const [id, fieldName, scope] of [['server-settings-form', 'coverImageUrl', 'server'], ['create-tangent', 'artwork', 'tangent'], ['edit-tangent-form', 'artwork', 'tangent']]) {
    const form = document.getElementById(id), field = form?.elements.namedItem(fieldName);
    const urlLabel = field?.closest('label');
    if (!urlLabel) continue;
    const upload = document.createElement('div'); upload.className = 'artwork-upload';
    const label = document.createElement('label'); label.textContent = scope === 'server' ? 'Card and cover image' : 'Card artwork';
    const picker = document.createElement('input'); picker.type = 'file'; picker.accept = 'image/png,image/jpeg,image/webp'; picker.className = 'input';
    label.append(picker);
    const hint = document.createElement('p'); hint.className = 'hint'; hint.textContent = 'PNG, JPEG, or WebP · up to 5 MB. Card images are public.';
    const feedback = document.createElement('p'); feedback.className = 'hint'; feedback.setAttribute('role', 'status');
    const alternate = document.createElement('details'); alternate.className = 'artwork-url';
    const summary = document.createElement('summary'); summary.textContent = 'Or use an image URL'; alternate.append(summary);
    urlLabel.before(upload); alternate.append(urlLabel); upload.append(label, hint, feedback, alternate);
    const choices = document.createElement('div'); choices.className = 'artwork-choices'; choices.setAttribute('aria-label', 'Curated artwork');
    const heading = document.createElement('p'); heading.className = 'artwork-legend'; heading.textContent = 'Choose a cover, or bring your own.';
    const catalogue = window.TangentArtworkPresets || [];
    const library = document.createElement('details'); library.className = 'artwork-library';
    const libraryTitle = document.createElement('summary'); libraryTitle.textContent = 'Browse ' + catalogue.length + ' covers';
    const filters = document.createElement('label'); filters.className = 'artwork-filter'; filters.textContent = 'Mood';
    const mood = document.createElement('select'); mood.className = 'input';
    for (const group of ['All moods', ...new Set(catalogue.map(entry => entry.group))]) { const option = document.createElement('option'); option.textContent = group; option.value = group; mood.append(option); }
    filters.append(mood);
    const count = document.createElement('p'); count.className = 'hint'; count.setAttribute('role', 'status');
    library.append(libraryTitle, filters, count, choices);
    const current = document.createElement('p'); current.className = 'artwork-current';
    const buttons = [];
    for (const {name, slug, group} of catalogue) {
      const url = '/tangent-art/' + slug + '.png';
      const button = document.createElement('button'); button.type = 'button'; button.className = 'artwork-choice'; button.dataset.artwork = url;
      button.dataset.mood = group;
      if (url) { const image = document.createElement('img'); image.src = url; image.alt = ''; image.loading = 'lazy'; button.append(image); }
      else { const sigil = document.createElement('span'); sigil.className = 'artwork-no-image'; sigil.textContent = '✦'; sigil.setAttribute('aria-hidden', 'true'); button.append(sigil); }
      const title = document.createElement('span'); title.textContent = name; button.append(title);
      button.onclick = () => { field.value = url; field.dispatchEvent(new Event('input', {bubbles:true})); feedback.textContent = 'Preview updated. Save your changes to keep it.'; };
      choices.append(button); buttons.push(button);
    }
    const clear = document.createElement('button'); clear.type = 'button'; clear.className = 'btn btn-quiet artwork-clear'; clear.textContent = 'No artwork'; clear.dataset.artwork = '';
    clear.onclick = () => { field.value = ''; field.dispatchEvent(new Event('input', {bubbles:true})); feedback.textContent = 'Preview updated.'; };
    const filter = () => { let visible = 0; for (const button of buttons) { button.hidden = mood.value !== 'All moods' && button.dataset.mood !== mood.value; if (!button.hidden) visible++; } count.textContent = visible + ' covers · Select one to preview it.'; };
    mood.addEventListener('change', filter); filter();
    const refresh = () => {
      [...buttons, clear].forEach(button => button.setAttribute('aria-pressed', String(field.value === button.dataset.artwork)));
      const selected = catalogue.find(entry => field.value === '/tangent-art/' + entry.slug + '.png');
      current.textContent = 'Selected: ' + (selected?.name || (field.value ? 'Custom image' : 'No artwork'));
    };
    refreshers.set(form, refresh); form.addEventListener('input', refresh); refresh();
    upload.prepend(heading, current, library, clear);
    let uploading = false;
    // Capture blocks save handlers while the image is in flight, including Enter-key submit.
    form.addEventListener('submit', event => { if (uploading) { event.preventDefault(); event.stopImmediatePropagation(); feedback.textContent = 'Your image is still uploading…'; } }, true);
    picker.addEventListener('change', async () => {
      const file = picker.files[0]; if (!file) return;
      if (!['image/png', 'image/jpeg', 'image/webp'].includes(file.type) || file.size > 5 * 1024 * 1024 || !file.size) {
        feedback.textContent = 'Choose a PNG, JPEG, or WebP image no larger than 5 MB.'; picker.value = ''; return;
      }
      const key = form.dataset.artworkKey, before = field.value;
      uploading = true; picker.disabled = true; feedback.textContent = 'Uploading your image…';
      try {
        const content = await new Promise((resolve, reject) => {
          const reader = new FileReader(); reader.onload = () => resolve(reader.result.split(',')[1]); reader.onerror = () => reject(new Error('This file could not be read. Try choosing it again.')); reader.readAsDataURL(file);
        });
        const response = await fetch('/api/artwork', {method:'POST', credentials:'same-origin', headers:{'Content-Type':'application/json'}, body:JSON.stringify({scope, tangentKey:key || null, content})});
        const result = await response.json().catch(() => ({}));
        if (!response.ok || typeof result.url !== 'string') throw new Error(result.reason || result.title || 'The upload did not finish. Please try again.');
        if (form.dataset.artworkKey !== key || field.value !== before) { feedback.textContent = 'The card changed during upload. Choose the image again to use it here.'; return; }
        field.value = result.url; field.dispatchEvent(new Event('input', {bubbles:true}));
        feedback.textContent = 'Image ready. Save your changes to use it on the card.';
      } catch (error) { feedback.textContent = error.message; }
      finally { uploading = false; picker.disabled = false; picker.value = ''; }
    });
  }
})();
