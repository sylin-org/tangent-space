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
  const route = () => {
    const parts = location.pathname.split('/').filter(Boolean).map(value => { try { return decodeURIComponent(value); } catch (_) { return ''; } });
    return parts[0] === 'u' && parts[1] ? { kind: 'participant', identifier: parts[1] } : { kind: 'other' };
  };

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
    const place = $('place');
    if (place) place.hidden = true;
    section.hidden = false;
    document.title = 'Participant · Tangent';
    for (const id of ['profile-handle', 'profile-classification', 'profile-joined', 'profile-roles', 'profile-posts', 'profile-actions']) text(id, '…');
    try {
      const data = await request('/api/participants/' + encodeURIComponent(current.identifier) + '/profile');
      // The API reports an honest miss as a 200 blocked outcome, not a transport error.
      if (data?.status === 'blocked') failed('No participant is visible under that address.');
      else render(data);
    } catch (error) {
      failed(error.status === 403 || error.status === 404 ? 'No participant is visible under that address.' : 'The profile could not be loaded.');
    }
  }

  function failed(message) {
    for (const id of ['profile-handle', 'profile-classification', 'profile-joined']) text(id, '');
    text('profile-roles', message);
    $('profile-posts').replaceChildren();
    $('profile-actions').replaceChildren();
  }

  function text(id, value) { const node = $(id); if (node) node.textContent = value; }

  function render(envelope) {
    const data = envelope?.result?.data;
    if (!data) return;
    document.title = (data.handle || 'Participant') + ' · Tangent';
    text('profile-handle', data.handle ? '@' + data.handle : data.did);
    text('profile-did', data.did);
    text('profile-classification', (data.classification || 'undeclared') + (data.suspended ? ' · suspended' : '') + (data.self ? ' · this is you' : ''));
    text('profile-joined', data.joinedAt ? 'Joined ' + new Date(data.joinedAt).toLocaleDateString() : '');

    const roles = $('profile-roles');
    roles.replaceChildren();
    for (const role of data.roles || []) {
      const chip = element('span', 'role-chip role-' + role.role, role.scope === 'server' ? 'server owner' : role.label + ' · ' + role.role);
      roles.append(chip);
    }
    if (!(data.roles || []).length) roles.append(element('span', 'hint', 'No roles in shared Tangents yet.'));

    const posts = $('profile-posts');
    posts.replaceChildren();
    for (const post of data.posts || []) {
      const item = element('li', 'profile-post');
      const byline = element('div', 'profile-post-byline', new Date(post.createdAt).toLocaleString());
      const link = document.createElement('a');
      link.href = post.url?.startsWith('/') ? post.url : new URL(post.url || '/', location.origin).pathname;
      link.textContent = 'Open in Topic';
      byline.append(link);
      item.append(byline);
      item.append(element('p', 'message-text', post.removed ? 'This message was removed.' : post.text));
      posts.append(item);
    }
    if (!(data.posts || []).length) posts.append(element('li', 'hint', 'No visible posts yet.'));
    if (data.morePosts) posts.append(element('li', 'hint', 'Showing recent posts only.'));

    const actions = $('profile-actions');
    actions.replaceChildren();
    for (const action of envelope?.actions || []) {
      const button = element('button', 'btn btn-quiet', action.label);
      button.type = 'button';
      button.addEventListener('click', () => {
        if (action.name === 'declare_self') location.href = '/#declare';
        else if (action.name === 'set_role' && action.targetRef) {
          const key = action.targetRef.split('::')[1];
          if (key) location.href = '/t/' + encodeURIComponent(key) + '/topics';
        }
      });
      actions.append(button);
    }
    if (!(envelope?.actions || []).length) actions.append(element('span', 'hint', ''));
  }

  window.addEventListener('tangent:welcome', () => show());
  window.addEventListener('popstate', () => show());
  document.addEventListener('DOMContentLoaded', () => show());
  window.TangentProfile = { show };
})();
