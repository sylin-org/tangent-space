'use strict';
(() => {
  const $ = id => document.getElementById(id);
  let current;
  function text(id, value) { $(id).textContent = value || ''; }
  async function send(path, body) {
    const response = await fetch(path, { method: 'POST', credentials: 'same-origin',
      headers: { 'content-type': 'application/json', 'X-Tangent-Participant': current.participant.participantRef },
      body: JSON.stringify({ ...body, expectedParticipant: current.participant.participantRef }) });
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
  function preview() {
    text('first-card-name', $('first-tangent-name').value.trim() || 'Your first Tangent');
    text('first-card-description', $('first-tangent-description').value.trim() || 'A place for the conversations that matter to you.');
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
        text('sign-in-button', 'Sign in and get started');
        text('sign-in-hint', 'Bluesky accounts work here. You can also enter a DID. Your password stays with your account provider.');
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
      if (state === 'create_tangent') preview();
      return true;
    }
  };
  $('confirm-owner').addEventListener('click', () => perform(async () => {
    await send('/api/server/claim', { humanDeclaration: true });
    location.assign('/onboarding/');
  }));
  $('first-tangent-form').addEventListener('input', preview);
  async function finish(skip) {
    await send('/api/onboarding/tangent', { skip, name: $('first-tangent-name').value.trim(), description: $('first-tangent-description').value.trim() });
    try { sessionStorage.setItem('tangent-created', current.participant.did); } catch (_) { }
    location.assign('/');
  }
  $('first-tangent-form').addEventListener('submit', event => { event.preventDefault(); perform(() => finish(false)); });
  $('skip-first-tangent').addEventListener('click', () => perform(() => finish(true)));
})();
