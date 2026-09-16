'use strict';
(() => {
  const $ = id => document.getElementById(id);
  let current;
  function text(id, value) { $(id).textContent = value || ''; }
  async function send(path, body) {
    const response = await fetch(path, { method: 'POST', credentials: 'same-origin',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(body) });
    const result = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(result.error || result.reason || 'That step could not be completed. Please try again.');
    return result;
  }
  async function perform(work) {
    const buttons = [...$('onboarding').querySelectorAll('button')];
    buttons.forEach(button => button.disabled = true);
    text('onboarding-error', '');
    try { await work(); }
    catch (error) { text('onboarding-error', error.message); $('onboarding-error').focus(); }
    finally { buttons.forEach(button => button.disabled = false); }
  }
  function card(profile) {
    $('owner-confirmation').dataset.profileDid = profile.did || '';
    const name = profile.displayName?.trim();
    text('owner-greeting', name ? `Welcome, ${name}!` : 'Welcome!');
    text('owner-display-name', name || (profile.handle ? '@' + profile.handle : 'Your Atmosphere account'));
    text('owner-handle', profile.handle ? '@' + profile.handle : profile.did);
    text('owner-bio', profile.description);
    $('owner-bio').hidden = !profile.description;
    text('owner-did', profile.did);
    const avatar = $('owner-avatar');
    avatar.hidden = true;
    $('owner-avatar-fallback').hidden = false;
    if (profile.avatar) {
      avatar.onload = () => { avatar.hidden = false; $('owner-avatar-fallback').hidden = true; };
      avatar.onerror = () => { avatar.hidden = true; $('owner-avatar-fallback').hidden = false; };
      avatar.src = profile.avatar;
    }
    text('owner-profile-status', profile.status === 'loading' ? 'Fetching your profile…'
      : profile.status === 'unavailable' ? 'Your account is connected. Its profile details aren’t available right now.' : '');
  }
  window.addEventListener('tangent:profile', event => {
    if (current?.onboarding === 'confirm_owner' && current.participant?.did === event.detail.did)
      card({ ...event.detail, handle: current.participant.handle });
  });
  // Starting points, not templates: each one fills the two fields and leaves them editable.
  const starters = [
    { name: 'The Gateway', description: 'Open to anyone who finds their way here. Introductions, questions, and where to go next.' },
    { name: 'Projects', description: 'What we are building, where each thing stands, and what it needs next.' },
    { name: 'Reading Room', description: 'Links, long reads, and the things worth keeping.' },
    { name: 'Small Hours', description: 'Half-formed thoughts, welcome as they are.' },
    { name: 'The Lab', description: 'Experiments with people and companions, including the ones that do not work.' }
  ];
  // One cover from each mood before any mood repeats, so eight squares span the collection
  // instead of showing its first page. The full picker, with uploads, URLs and all sixteen,
  // lives in Tangent settings; this is the first-run taste of it.
  function starterArtwork(catalogue, wanted) {
    const moods = new Map();
    for (const entry of catalogue) {
      if (!moods.has(entry.group)) moods.set(entry.group, []);
      moods.get(entry.group).push(entry);
    }
    const rows = [...moods.values()], picked = [];
    for (let depth = 0; picked.length < wanted; depth += 1) {
      if (!rows.some(entries => entries.length > depth)) break;
      for (const entries of rows) {
        if (picked.length === wanted) break;
        if (entries[depth]) picked.push(entries[depth]);
      }
    }
    return picked;
  }
  function choices() {
    const row = $('first-tangent-presets');
    if (row && !row.childElementCount) {
      for (const starter of starters) {
        const button = document.createElement('button');
        button.type = 'button'; button.className = 'choice'; button.textContent = starter.name;
        button.title = starter.description;
        button.onclick = () => {
          $('first-tangent-name').value = starter.name;
          $('first-tangent-description').value = starter.description;
          preview();
        };
        row.append(button);
      }
      const clear = document.createElement('button');
      clear.type = 'button'; clear.className = 'choice choice-clear'; clear.textContent = 'Clear';
      clear.onclick = () => {
        $('first-tangent-name').value = ''; $('first-tangent-description').value = '';
        preview(); $('first-tangent-name').focus();
      };
      row.append(clear);
    }
    const art = $('first-tangent-art');
    if (art && !art.childElementCount) {
      const pick = value => { $('first-tangent-artwork').value = value; preview(); };
      const none = document.createElement('button');
      none.type = 'button'; none.className = 'art-choice art-none'; none.dataset.artwork = '';
      none.title = 'No artwork'; none.setAttribute('aria-label', 'No artwork'); none.textContent = '✦';
      none.onclick = () => pick('');
      art.append(none);
      for (const entry of starterArtwork(window.TangentArtworkPresets || [], 8)) {
        const url = '/tangent-art/' + entry.slug + '.png';
        const button = document.createElement('button');
        button.type = 'button'; button.className = 'art-choice'; button.dataset.artwork = url;
        button.title = entry.name; button.setAttribute('aria-label', entry.name);
        const image = document.createElement('img');
        image.src = url; image.alt = ''; image.loading = 'lazy';
        button.append(image);
        button.onclick = () => pick(url);
        art.append(button);
      }
    }
  }
  function preview() {
    text('first-card-name', $('first-tangent-name').value.trim() || 'Your first Tangent');
    text('first-card-description', $('first-tangent-description').value.trim() || 'A place for the conversations that matter to you.');
    const chosen = $('first-tangent-artwork')?.value || '';
    const art = $('first-card-art'), face = $('first-card-face');
    if (art && face) {
      if (chosen && art.getAttribute('src') !== chosen) art.src = chosen;
      art.hidden = !chosen;
      face.classList.toggle('has-art', !!chosen);
    }
    for (const button of document.querySelectorAll('#first-tangent-art .art-choice'))
      button.setAttribute('aria-pressed', String(button.dataset.artwork === chosen));
  }
  window.TangentOnboarding = {
    show(site) {
      if (window.TangentPages.route.kind !== 'onboarding') return false;
      current = site;
      const state = site.onboarding || 'complete';
      document.body.classList.toggle('onboarding-active', state !== 'complete');
      $('onboarding').hidden = true;
      if (state === 'complete') { location.replace('/'); return true; }
      $('place').hidden = true; $('server-welcome').hidden = true;
      document.body.classList.remove('has-rooms', 'conversation-open');
      if (state === 'sign_in') {
        text('anonymous-title', 'Welcome to your own Tangents.');
        text('anonymous-copy', 'A home for your people, your companions, and the conversations you want to keep.');
        text('note-unestablished', 'Sign in with your Atmosphere account. You’ll confirm this server’s owner next.');
        text('sign-in-button', 'Log in with Bluesky');
        text('sign-in-hint', 'Choose your account on Bluesky. You’ll confirm it here before becoming the owner.');
        return true;
      }
      $('state-signed-in').hidden = true;
      $('onboarding').hidden = false;
      $('owner-confirmation').hidden = state !== 'confirm_owner';
      $('first-tangent').hidden = state !== 'create_tangent';
      $('owner-waiting').hidden = state !== 'waiting_owner';
      if (state === 'confirm_owner') {
        card({ ...site.participant, status: 'loading' });
        const did = site.participant.did;
        fetch('/api/participants/me/profile', { credentials: 'same-origin', cache: 'no-store' })
          .then(response => { if (!response.ok) throw new Error(); return response.json(); })
          .then(profile => { if (current.participant?.did === did && profile.did === did) card(profile); })
          .catch(() => { if (current.participant?.did === did) card({ ...site.participant, status: 'unavailable' }); });
      }
      if (state === 'create_tangent') { choices(); preview(); }
      return true;
    }
  };
  $('confirm-owner').addEventListener('click', () => perform(async () => {
    await send('/api/server/claim', { humanDeclaration: true });
    location.assign('/onboarding/');
  }));
  $('first-tangent-form').addEventListener('input', preview);
  async function finish(skip) {
    await send('/api/onboarding/tangent', { skip, name: $('first-tangent-name').value.trim(), description: $('first-tangent-description').value.trim(), artwork: $('first-tangent-artwork').value });
    try { sessionStorage.setItem('tangent-created', current.participant.participantRef); } catch (_) { }
    location.assign('/');
  }
  $('first-tangent-form').addEventListener('submit', event => { event.preventDefault(); perform(() => finish(false)); });
  $('skip-first-tangent').addEventListener('click', () => perform(() => finish(true)));
})();
