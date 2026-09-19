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
  let revision = 0, site, csrf;

  function clear() {
    const section = $('participant-profile');
    if (!section) return;
    delete section.dataset.profileDid;
    for (const id of [
      'profile-handle', 'profile-did', 'profile-identity-handle',
      'profile-identity-classification', 'profile-description',
      'profile-classification', 'profile-joined', 'profile-roles'
    ]) text(id, '');
    for (const node of [$('profile-name'), $('profile-avatar')]) {
      node.replaceChildren();
      delete node.dataset.profileDid; delete node.dataset.profilePart;
      delete node.dataset.profileFallback; delete node.dataset.profileVersion;
    }
    $('profile-posts').replaceChildren();
    $('profile-actions').replaceChildren();
    $('profile-role-management')?.replaceChildren();
  }

  window.addEventListener('tangent:profile', event => {
    if (!$('participant-profile')?.hidden && $('participant-profile')?.dataset.profileDid === event.detail.did) {
      text('profile-description', event.detail.description || '');
      $('profile-description').hidden = !event.detail.description;
      if (event.detail.displayName) document.title = event.detail.displayName + ' · Tangent';
    }
  });

  async function request(path) {
    const response = await fetch(path, { headers: { accept: 'application/json' }, credentials: 'same-origin' });
    if (!response.ok) throw Object.assign(new Error('Profile request failed'), { status: response.status });
    return response.json();
  }

  function profileTab(name, updateUrl = true) {
    if (!['posts', 'roles', 'identity'].includes(name)) name = 'posts';
    document.querySelectorAll('[data-profile-tab]').forEach(button => {
      const active = button.dataset.profileTab === name;
      button.setAttribute('aria-selected', String(active));
      button.tabIndex = active ? 0 : -1;
    });
    if ($('profile-posts-panel')) $('profile-posts-panel').hidden = name !== 'posts';
    if ($('profile-roles-panel')) $('profile-roles-panel').hidden = name !== 'roles';
    if ($('profile-identity-panel')) $('profile-identity-panel').hidden = name !== 'identity';
    if (updateUrl) {
      const url = new URL(location.href);
      if (name !== 'posts') url.searchParams.set('tab', name);
      else url.searchParams.delete('tab');
      history.replaceState({ ...(history.state || {}) }, '', url.pathname + url.search + url.hash);
    }
  }

  async function show() {
    const version = ++revision;
    const current = route();
    const section = $('participant-profile');
    if (current.kind !== 'participant') {
      clear();
      if (section) section.hidden = true;
      return;
    }
    if (!section) return;
    const place = $('place');
    if (place) place.hidden = true;
    section.hidden = false;
    $('server-welcome').hidden = true;
    document.title = 'Participant · Tangent';
    for (const id of ['profile-handle', 'profile-did', 'profile-description', 'profile-classification', 'profile-joined', 'profile-roles', 'profile-posts', 'profile-actions']) text(id, '');
    text('profile-name', 'Getting acquainted…');
    text('profile-avatar', '✦');

    const initialTab = new URL(location.href).searchParams.get('tab') || 'posts';
    profileTab(initialTab, false);

    try {
      const data = await request('/api/participants/' + encodeURIComponent(current.identifier) + '/profile');
      if (version !== revision) return;
      if (data?.status === 'blocked') failed('No participant is visible under that address.');
      else render(data);
    } catch (error) {
      if (version !== revision) return;
      failed(error.status === 403 || error.status === 404 ? 'No participant is visible under that address.' : 'The profile could not be loaded.');
    } finally {
      if (version === revision) window.dispatchEvent(new CustomEvent('tangent:route-ready', {
        detail: { href: location.pathname + location.search + location.hash }
      }));
    }
  }

  function failed(message) {
    for (const id of ['profile-handle', 'profile-classification', 'profile-joined', 'profile-did', 'profile-identity-handle', 'profile-identity-classification']) text(id, '');
    text('profile-roles', message);
    $('profile-posts').replaceChildren();
    $('profile-actions').replaceChildren();
    $('profile-role-management')?.replaceChildren();
    text('profile-name', 'Profile unavailable');
    const retry = element('button', 'btn btn-quiet', 'Try again');
    retry.type = 'button'; retry.addEventListener('click', show); $('profile-actions').append(retry);
  }

  function text(id, value) { const node = $(id); if (node) node.textContent = value; }

  async function session() {
    if (csrf) return csrf;
    const response = await fetch('/api/roles/ui/session', { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } });
    const data = await response.json().catch(() => ({}));
    if (!response.ok || !data.requestToken || !data.headerName) throw new Error('Could not establish session.');
    csrf = data;
    return csrf;
  }

  function formatRoleLabel(role) {
    if (role.scope === 'server') {
      if (role.role === 'owner') return 'Server Owner';
      if (role.role === 'participant') return 'Participant';
      if (role.role === 'administrator') return 'Administrator';
      return role.role.charAt(0).toUpperCase() + role.role.slice(1);
    }
    const rolePart = role.role.charAt(0).toUpperCase() + role.role.slice(1);
    return role.label ? `${role.label} · ${rolePart}` : rolePart;
  }

  function renderRoles(data) {
    const roles = $('profile-roles');
    roles.replaceChildren();
    for (const role of data.roles || []) {
      const chip = element('span', 'role-chip role-' + role.role, formatRoleLabel(role));
      roles.append(chip);
    }
  }

  async function renderRoleManagement(data) {
    const container = $('profile-role-management');
    if (!container) return;
    const isOwner = site?.participant?.isOwner === true;
    const isSelf = data.self === true || (site?.participant?.id && site.participant.id === data.participantRef);
    if (!data.participantRef) {
      container.replaceChildren();
      return;
    }

    if (!site?.participant) {
      container.replaceChildren(
        element('p', 'hint', 'Server roles assigned to this participant are shown in their profile header. Sign in to view and manage roles.')
      );
      return;
    }

    container.replaceChildren();
    const list = element('div', 'profile-role-list');
    list.append(element('p', 'hint', 'Loading roles…'));
    const statusMsg = element('p', 'hint', '');
    statusMsg.id = 'profile-role-status';
    statusMsg.setAttribute('role', 'status');
    container.append(list, statusMsg);

    try {
      const response = await fetch('/api/roles/ui/participant/' + encodeURIComponent(data.participantRef) + '/roles', {
        credentials: 'same-origin',
        headers: { Accept: 'application/json' }
      });
      if (!response.ok) throw new Error('Could not load roles.');
      const roleItems = await response.json();
      list.replaceChildren();

      const visibleRoles = (roleItems || []).filter(role => isOwner || role.canToggle || role.isMember);
      if (!visibleRoles.length) {
        list.append(element('p', 'hint', 'No visible server roles.'));
        return;
      }

      for (const role of visibleRoles) {
        const row = element('div', 'profile-role-row');
        row.style.setProperty('--role-color', role.color || '#a98df0');

        const dot = element('span', 'role-list-dot');
        const info = element('div', 'profile-role-info');
        const nameRow = element('div', 'profile-role-name-row');
        nameRow.append(element('strong', '', role.name));

        if (role.isProtected) {
          nameRow.append(element('span', 'profile-role-badge system', 'System'));
        } else if (role.isSelfEnrollable) {
          nameRow.append(element('span', 'profile-role-badge self-enrollable', 'Self-enrollable'));
        }

        info.append(nameRow, element('small', '', role.purpose || (role.isProtected ? 'System-managed role' : '')));
        row.append(dot, info);

        const actions = element('div', 'profile-role-actions');
        if (role.canToggle) {
          const btnText = isSelf && !isOwner
            ? (role.isMember ? 'Leave role' : 'Join role')
            : (role.isMember ? 'Remove' : 'Add');
          const toggle = element('button', role.isMember ? 'btn btn-quiet profile-role-toggle in-role' : 'btn btn-primary profile-role-toggle', btnText);
          toggle.type = 'button';
          toggle.title = isSelf && !isOwner
            ? (role.isMember ? `Leave ${role.name}` : `Join ${role.name}`)
            : (role.isMember ? `Remove ${role.name} from this participant` : `Add ${role.name} to this participant`);
          toggle.setAttribute('aria-label', toggle.title);

          toggle.addEventListener('click', async () => {
            toggle.disabled = true;
            statusMsg.textContent = '';
            statusMsg.classList.remove('error');
            const willAdd = !role.isMember;
            try {
              const token = await session();
              const res = await fetch('/api/roles/ui/participant/' + encodeURIComponent(data.participantRef) + '/roles/' + encodeURIComponent(role.id), {
                method: willAdd ? 'PUT' : 'DELETE',
                credentials: 'same-origin',
                headers: { Accept: 'application/json', [token.headerName]: token.requestToken }
              });
              if (!res.ok) {
                const errData = await res.json().catch(() => ({}));
                throw new Error(errData.message || errData.error || 'Failed to update role membership.');
              }
              role.isMember = willAdd;
              toggle.textContent = isSelf && !isOwner
                ? (willAdd ? 'Leave role' : 'Join role')
                : (willAdd ? 'Remove' : 'Add');
              toggle.className = willAdd ? 'btn btn-quiet profile-role-toggle in-role' : 'btn btn-primary profile-role-toggle';
              toggle.title = isSelf && !isOwner
                ? (willAdd ? `Leave ${role.name}` : `Join ${role.name}`)
                : (willAdd ? `Remove ${role.name} from this participant` : `Add ${role.name} to this participant`);
              toggle.setAttribute('aria-label', toggle.title);

              const roleKey = role.id.replace(/^role:/, '');
              if (willAdd) {
                if (!data.roles) data.roles = [];
                if (!data.roles.some(r => r.scope === 'server' && r.role === roleKey)) {
                  data.roles.push({ scope: 'server', key: '', label: 'This server', role: roleKey });
                }
              } else {
                data.roles = (data.roles || []).filter(r => !(r.scope === 'server' && r.role === roleKey));
              }
              renderRoles(data);
              statusMsg.textContent = willAdd
                ? (isSelf && !isOwner ? `Joined ${role.name}.` : `${role.name} role granted.`)
                : (isSelf && !isOwner ? `Left ${role.name}.` : `${role.name} role removed.`);
            } catch (err) {
              statusMsg.textContent = err.message;
              statusMsg.classList.add('error');
            } finally {
              toggle.disabled = false;
            }
          });
          actions.append(toggle);
        } else if (role.isProtected) {
          // Protected system role
        } else if (role.isMember) {
          actions.append(element('span', 'profile-role-badge', 'Assigned'));
        }
        row.append(actions);
        list.append(row);
      }
    } catch (err) {
      list.replaceChildren();
      statusMsg.textContent = err.message;
      statusMsg.classList.add('error');
    }
  }

  function render(envelope) {
    const data = envelope?.result?.data;
    if (!data) return;
    const canonical = '/u/' + encodeURIComponent(data.handle || data.did || 'tangent:local:' + data.participantRef);
    if (canonical !== location.pathname) window.TangentNavigation?.canonicalize?.(canonical);
    const name = data.displayName || data.handle || 'Fellow participant';
    $('participant-profile').dataset.profileDid = data.did || '';
    document.title = name + ' · Tangent';
    text('profile-name', name);
    const handleNode = $('profile-handle');
    if (handleNode) {
      handleNode.replaceChildren();
      if (data.handle) {
        const cleanHandle = data.handle.replace(/^@/, '');
        const a = document.createElement('a');
        a.className = 'profile-handle-link';
        a.href = 'https://bsky.app/profile/' + encodeURIComponent(cleanHandle);
        a.target = '_blank';
        a.rel = 'noopener noreferrer';
        a.title = 'View on Bluesky';
        a.append(element('span', 'profile-handle-butterfly', '🦋'));
        a.append('@' + cleanHandle);
        handleNode.append(a);
      } else {
        handleNode.textContent = 'An account in this community';
      }
    }
    text('profile-did', data.did || '—');
    const idHandleNode = $('profile-identity-handle');
    if (idHandleNode) {
      idHandleNode.replaceChildren();
      if (data.handle) {
        const cleanHandle = data.handle.replace(/^@/, '');
        const a = document.createElement('a');
        a.className = 'profile-handle-link';
        a.href = 'https://bsky.app/profile/' + encodeURIComponent(cleanHandle);
        a.target = '_blank';
        a.rel = 'noopener noreferrer';
        a.title = 'View on Bluesky';
        a.append(element('span', 'profile-handle-butterfly', '🦋'));
        a.append('@' + cleanHandle);
        idHandleNode.append(a);
      } else {
        idHandleNode.textContent = '—';
      }
    }
    text('profile-description', data.description || ''); $('profile-description').hidden = !data.description;
    const avatar = $('profile-avatar'); avatar.replaceChildren(); avatar.textContent = name.slice(0, 1).toUpperCase();
    if (typeof data.avatar === 'string' && data.avatar.startsWith('/api/profile-cache/avatar?')) {
      const image = document.createElement('img'); image.src = data.avatar; image.alt = ''; image.referrerPolicy = 'no-referrer';
      image.addEventListener('error', () => { avatar.textContent = name.slice(0, 1).toUpperCase(); }); avatar.replaceChildren(image);
    }
    for (const [node, part] of [[$('profile-name'), 'name'], [avatar, 'avatar']]) {
      node.dataset.profileDid = data.did || ''; node.dataset.profilePart = part; node.dataset.profileFallback = data.handle || 'Participant'; delete node.dataset.profileVersion;
    }
    const classification = String(data.classification || 'undeclared').toLowerCase();
    const classificationNode = $('profile-classification');
    if (classificationNode) {
      classificationNode.replaceChildren();
      if (data.self) {
        const menu = element('details', 'profile-classification-menu');
        const badgeClass = 'profile-classification-badge ' + (classification === 'agent' ? 'agent' : classification === 'human' ? 'human' : 'undeclared');
        const summary = element('summary', badgeClass);
        const badgeText = classification === 'agent' ? '🤖 Agent' : classification === 'human' ? '👤 Human' : '+ Add badge';
        summary.setAttribute('title', 'Change how your account is identified');
        summary.append(element('span', 'badge-text', badgeText));
        summary.append(element('span', 'badge-caret', ' ▾'));
        menu.append(summary);

        const popover = element('div', 'profile-classification-popover');
        const options = [
          { value: 'Agent', label: '🤖 Agent' },
          { value: 'Human', label: '👤 Human' },
          { value: 'Undeclared', label: 'Hide badge' }
        ];
        for (const opt of options) {
          const btn = element('button', 'profile-classification-option' + (opt.value.toLowerCase() === classification ? ' active' : ''), opt.label);
          btn.type = 'button';
          btn.addEventListener('click', async (e) => {
            e.stopPropagation();
            menu.open = false;
            try {
              const res = await fetch('/api/participants/me', {
                method: 'PATCH',
                credentials: 'same-origin',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ classification: opt.value })
              });
              if (!res.ok) {
                const errData = await res.json().catch(() => ({}));
                throw new Error(errData.error || 'Could not update classification');
              }
              await show();
            } catch (err) {
              console.error('Classification error:', err);
            }
          });
          popover.append(btn);
        }
        menu.append(popover);
        classificationNode.append(menu);

        document.addEventListener('click', (e) => {
          if (menu.open && !menu.contains(e.target)) menu.open = false;
        }, { passive: true });
      } else {
        if (classification === 'agent') {
          classificationNode.append(element('span', 'profile-classification-badge agent', '🤖 Agent'));
        } else if (classification === 'human') {
          classificationNode.append(element('span', 'profile-classification-badge human', '👤 Human'));
        }
      }
    }
    text('profile-identity-classification', (classification === 'agent' ? 'Agent' : classification === 'human' ? 'Human' : 'Not declared (hidden)') + (data.suspended ? ' · suspended' : '') + (data.self ? ' · this is you' : ''));
    const meta = [];
    if (data.joinedAt) meta.push('Joined ' + new Date(data.joinedAt).toLocaleDateString());
    if (data.self) meta.push('this is you');
    if (data.suspended) meta.push('suspended');
    text('profile-joined', meta.join(' · '));

    renderRoles(data);
    renderRoleManagement(data);

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
      if (action.name === 'set_role' && action.targetRef && data.did) {
        const key = action.targetRef.split('::')[1];
        if (!key) continue;
        const link = element('a', 'btn btn-quiet', action.label);
        link.href = '/t/' + encodeURIComponent(key) + '/topics?participant=' + encodeURIComponent(data.did) + '#tangent-members'; actions.append(link);
      }
    }
    if (!(envelope?.actions || []).length) actions.append(element('span', 'hint', ''));
  }

  document.querySelectorAll('[data-profile-tab]').forEach(button => {
    button.addEventListener('click', () => profileTab(button.dataset.profileTab));
    button.addEventListener('keydown', event => {
      if (!['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End'].includes(event.key)) return;
      event.preventDefault();
      const tabs = [...document.querySelectorAll('[data-profile-tab]')];
      const index = tabs.indexOf(button);
      let target = button;
      if (event.key === 'Home') target = tabs[0];
      else if (event.key === 'End') target = tabs[tabs.length - 1];
      else {
        const direction = ['ArrowRight', 'ArrowDown'].includes(event.key) ? 1 : -1;
        target = tabs[(index + direction + tabs.length) % tabs.length];
      }
      target.focus();
      profileTab(target.dataset.profileTab);
    });
  });

  window.addEventListener('popstate', () => {
    if (route()?.kind === 'participant') {
      profileTab(new URL(location.href).searchParams.get('tab') || 'posts', false);
    }
  });

  window.addEventListener('tangent:welcome', event => { site = event.detail; show(); });
  window.addEventListener('tangent:route', () => show());
  window.TangentProfile = { show, clear, profileTab };
})();
