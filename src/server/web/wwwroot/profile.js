// Internal participant profile (ADR 0008): identity card, roles, policy-scoped posts,
// and the viewer's permitted operational actions — all served by the experience API.
'use strict';
(() => {
  const $ = id => document.getElementById(id);
  const element = (tag, className, textContent) => {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (textContent != null) node.textContent = textContent;
    return node;
  };
  const route = () => window.TangentPages.route;
  let revision = 0;

  async function request(path) {
    const response = await fetch(path, { headers: { accept: 'application/json' }, credentials: 'same-origin' });
    if (!response.ok) throw Object.assign(new Error('Profile request failed'), { status: response.status });
    return response.json();
  }

  async function show() {
    const current = route();
    const section = $('participant-profile');
    if (current.kind !== 'participant') {
      if (section) section.hidden = true;
      return;
    }
    if (!section) return;
    const version = ++revision;
    const place = $('place');
    if (place) place.hidden = true;
    section.hidden = false;
    $('server-welcome').hidden = true;
    document.title = 'Participant · Tangent';
    for (const id of ['profile-handle', 'profile-did', 'profile-description', 'profile-classification', 'profile-joined', 'profile-roles', 'profile-posts', 'profile-actions']) text(id, '');
    text('profile-name', 'Getting acquainted…');
    text('profile-avatar', '✦');
    try {
      const data = await request('/api/participants/' + encodeURIComponent(current.identifier) + '/profile');
      if (version !== revision) return;
      // The API reports an honest miss as a 200 blocked outcome, not a transport error.
      if (data?.status === 'blocked') failed('No participant is visible under that address.');
      else render(data);
    } catch (error) {
      if (version !== revision) return;
      failed(error.status === 403 || error.status === 404 ? 'No participant is visible under that address.' : 'The profile could not be loaded.');
    }
  }

  function failed(message) {
    for (const id of ['profile-handle', 'profile-classification', 'profile-joined']) text(id, '');
    text('profile-roles', message);
    $('profile-posts').replaceChildren();
    $('profile-actions').replaceChildren();
    text('profile-name', 'Profile unavailable');
    const retry = element('button', 'btn btn-quiet', 'Try again');
    retry.type = 'button'; retry.addEventListener('click', show); $('profile-actions').append(retry);
  }

  function text(id, value) { const node = $(id); if (node) node.textContent = value; }

  function render(envelope) {
    const data = envelope?.result?.data;
    if (!data) return;
    const name = data.displayName || data.handle || 'Fellow participant';
    document.title = name + ' · Tangent';
    text('profile-name', name);
    text('profile-handle', data.handle ? '@' + data.handle.replace(/^@/, '') : 'An account in this community');
    text('profile-did', data.did);
    text('profile-description', data.description || ''); $('profile-description').hidden = !data.description;
    const avatar = $('profile-avatar'); avatar.replaceChildren(); avatar.textContent = name.slice(0, 1).toUpperCase();
    if (typeof data.avatar === 'string' && /^https:\/\//i.test(data.avatar)) {
      const image = document.createElement('img'); image.src = data.avatar; image.alt = ''; image.referrerPolicy = 'no-referrer';
      image.addEventListener('error', () => { avatar.textContent = name.slice(0, 1).toUpperCase(); }); avatar.replaceChildren(image);
    }
    const classification = String(data.classification || 'undeclared').toLowerCase();
    text('profile-classification', (classification === 'agent' ? 'Agent' : classification === 'human' ? 'Human' : 'Participant') + (data.suspended ? ' · suspended' : '') + (data.self ? ' · this is you' : ''));
    text('profile-joined', data.joinedAt ? 'Joined ' + new Date(data.joinedAt).toLocaleDateString() : '');

    const roles = $('profile-roles');
    roles.replaceChildren();
    for (const role of data.roles || []) {
      const chip = element('span', 'role-chip role-' + role.role, role.scope === 'server' ? 'server owner' : role.label + ' · ' + role.role);
      roles.append(chip);
    }

    const posts = $('profile-posts');
    posts.replaceChildren();
    for (const post of data.posts || []) {
      const item = element('li', 'profile-post');
      const byline = element('div', 'profile-post-byline', new Date(post.createdAt).toLocaleString());
      const link = document.createElement('a');
      const target = new URL(post.url || '/', location.origin);
      link.href = target.origin === location.origin ? target.pathname : '/tangents/';
      link.textContent = 'Open in Topic';
      byline.append(link);
      item.append(byline);
      item.append(post.removed ? element('p', 'profile-post-text', 'This post was removed.') : window.TangentFacets?.renderFacetedText?.(post.text, post.facets, data.resolved) || element('p', 'profile-post-text', post.text));
      posts.append(item);
    }
    if (!(data.posts || []).length) posts.append(element('li', 'hint', 'No visible posts yet.'));
    if (data.morePosts) posts.append(element('li', 'hint', 'Showing recent posts only.'));

    const actions = $('profile-actions');
    actions.replaceChildren();
    for (const action of envelope?.actions || []) {
      if (action.name === 'declare_self') {
        const details = element('details', 'profile-declaration');
        details.append(element('summary', '', 'About your participation'));
        const isServerOwner = (data.roles || []).some(role => role.scope === 'server' && role.role === 'owner');
        if (isServerOwner || classification === 'agent') {
          details.append(element('p', 'hint', isServerOwner ? 'The server owner participates as a human and remains responsible for this place.' : 'This account participates as an agent. Its declaration stays with the account.'));
          actions.append(details); continue;
        }
        details.append(element('p', 'hint', 'Let others know whether this account represents a human or an agent. This is your declaration, not verification.'));
        const form = element('form', 'profile-declaration-form'), label = element('label', '', 'This account represents');
        const select = document.createElement('select'); select.className = 'input';
        for (const [value, title] of [['Undeclared', 'Not declared'], ['Human', 'Human'], ['Agent', 'Agent']]) {
          const option = element('option', '', title); option.value = value; option.selected = value.toLowerCase() === classification; select.append(option);
        }
        label.append(select); const save = element('button', 'btn btn-quiet', 'Save declaration'); save.type = 'submit';
        const feedback = element('p', 'hint'); feedback.setAttribute('role', 'status');
        form.append(label, save, feedback); details.append(form); actions.append(details);
        form.addEventListener('submit', async event => {
          event.preventDefault(); save.disabled = true;
          try {
            const response = await fetch('/api/participants/me', { method: 'PATCH', credentials: 'same-origin', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ classification: select.value }) });
            const result = await response.json().catch(() => ({}));
            if (!response.ok) throw new Error(result.error || 'Your declaration could not be saved.');
            await show();
          } catch (error) { feedback.textContent = error.message; }
          finally { save.disabled = false; }
        });
      } else if (action.name === 'set_role' && action.targetRef && data.did) {
        const key = action.targetRef.split('::')[1];
        if (!key) continue;
        const link = element('a', 'btn btn-quiet', action.label);
        link.href = '/t/' + encodeURIComponent(key) + '/topics?participant=' + encodeURIComponent(data.did) + '#tangent-members'; actions.append(link);
      }
    }
    if (!(envelope?.actions || []).length) actions.append(element('span', 'hint', ''));
  }

  window.addEventListener('tangent:welcome', () => show());
  window.addEventListener('popstate', () => show());
  document.addEventListener('DOMContentLoaded', () => show());
  window.TangentProfile = { show };
})();
