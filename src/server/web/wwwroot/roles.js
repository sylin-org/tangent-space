'use strict';
(() => {
  const $ = id => document.getElementById(id);
  const groupOrder = ['host', 'tangent', 'topic', 'post', 'other'];
  const groupLabels = { host: 'Server', tangent: 'Tangents', topic: 'Topics', post: 'Posts', other: 'Other' };
  const roleMemberPageSize = 50;
  let site, descriptor, descriptorError, roles = [], selected, loadVersion = 0, csrf;
  const rolePageSize = 100;
  let roleTotal = 0, editorTab = 'appearance', dirty = false;
  let roleListPage = 1, roleListHasPrevious = false, roleListHasNext = false;
  let memberTotals = new Map(), roleMemberState = new Map();
  let lastMemberScope = '', lastMemberIdentity = '';
  let draftGrantKeys = new Set();

  const color = value => /^#[0-9a-f]{6}$/i.test(value || '') ? value : '#a98df0';
  const systemRole = role => role?.presentation?.system === 'true';
  const protectedOwnerRole = role => systemRole(role) && /:owner$/i.test(role?.id || '');
  const canManageMembers = role => !!role && !role._new && !protectedOwnerRole(role);
  const currentKind = () => 'host';
  const currentScope = () => ({ type: 'host', id: 'site' });
  const api = suffix => '/api/identity/roles' + String(suffix || '').replace(/^roles/, '');
  const normalizeRole = role => {
    const metadata = role?.metadata || {};
    return { ...role, purpose: metadata.purpose || '', presentation: { color: metadata.color || '#a98df0', system: metadata.system || 'false', selfEnrollable: metadata.selfEnrollable || 'false' },
      grants: (role?.permissions || []).map(capability => ({ capability })) };
  };
  const message = (id, value, error = false) => { const node = $(id); node.textContent = value || ''; node.classList.toggle('error', error); };
  const make = (tag, className, text) => { const node = document.createElement(tag); node.className = className || ''; if (text !== undefined) node.textContent = text; return node; };
  const memberCount = role => {
    const total = memberTotals.get(role?.id);
    return total == null ? '—' : String(total);
  };
  const memberState = roleId => roleMemberState.get(roleId) || { page: 0, hasMore: false };
  const currentScopeSignature = () => {
    const scope = currentScope();
    return `${scope.type}:${scope.id}`;
  };
  const currentIdentitySignature = () => (site?.participant?.id || site?.participant?.did || '').toString();
  const memberByline = person => person?.handle
    ? (person.handle.startsWith('@') ? person.handle : '@' + person.handle)
    : 'Tangent Space participant';

  function showAvatar(node, person, label) {
    node.replaceChildren();
    node.textContent = (label || 'P').slice(0, 1).toUpperCase();
    if (typeof person?.avatar !== 'string' || !person.avatar.startsWith('/api/profile-cache/avatar?')) return;
    const image = document.createElement('img'); image.src = person.avatar; image.alt = ''; image.loading = 'lazy'; image.referrerPolicy = 'no-referrer';
    node.replaceChildren(image);
  }

  function clearMemberData() {
    memberTotals = new Map();
    roleMemberState = new Map();
  }



  async function session() {
    if (csrf) return csrf;
    const response = await fetch('/api/roles/ui/session', { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } });
    const data = await response.json().catch(() => ({}));
    if (!response.ok || !data.requestToken || !data.headerName) throw failure(response, data);
    csrf = data;
    return csrf;
  }

  function failure(response, data) {
    const error = new Error(data.message || data.error || data.reason || data.title || (response.status === 403 ? 'Your current role cannot manage this scope.' : 'This request could not be completed.'));
    error.status = response.status;
    return error;
  }

  async function request(path, options = {}) {
    const method = options.method || 'GET';
    const headers = { Accept: 'application/json', ...(options.headers || {}) };
    if (!['GET', 'HEAD'].includes(method)) {
      const token = await session();
      headers[token.headerName] = token.requestToken;
    }
    if (options.body !== undefined) headers['Content-Type'] = 'application/json';
    const response = await fetch(path, { method, credentials: 'same-origin', cache: 'no-store', headers,
      body: options.body === undefined ? undefined : JSON.stringify(options.body) });
    const data = await response.json().catch(() => ({}));
    if (!response.ok) throw failure(response, data);
    return { data, etag: response.headers.get('ETag') };
  }

  function roleUrl(role, mode = 'replace') {
    const url = new URL(location.href);
    url.searchParams.set('tab', 'roles');
    if (role) url.searchParams.set('role', role._new ? 'new' : role.id); else url.searchParams.delete('role');
    const state = { ...(history.state || {}), tangentRoleEditor: !!role };
    history[mode + 'State'](state, '', url.pathname + url.search + url.hash);
  }

  // Which tab the URL asks for, or the one this context opens on. Stated once on purpose:
  // this decision used to live here *and* in access.js with two different lists, so a tab
  // added to one was silently clicked shut by the other the moment both ran.
  function requestedTab(contextKind) {
    const requested = new URL(location.href).searchParams.get('tab');
    const fallback = contextKind === 'server' ? 'server' : 'context';
    if (!requested) return fallback;
    if (requested === 'access') return 'access';
    // Roles and People describe the server itself, so they mean nothing in a Tangent or Topic.
    return contextKind === 'server' && ['roles', 'people'].includes(requested) ? requested : fallback;
  }

  function settingsTab(name, updateUrl = true) {
    if (!['server', 'context', 'roles', 'access', 'people'].includes(name)) name = 'server';
    document.querySelectorAll('[data-settings-tab]').forEach(button => {
      const active = button.dataset.settingsTab === name;
      button.setAttribute('aria-selected', String(active));
      button.tabIndex = active ? 0 : -1;
    });
    $('settings-server-panel').hidden = name !== 'server';
    $('settings-context-panel').hidden = name !== 'context';
    $('settings-roles-panel').hidden = name !== 'roles';
    $('settings-access-panel').hidden = name !== 'access';
    $('settings-people-panel').hidden = name !== 'people';
    if (updateUrl) {
      const url = new URL(location.href);
      if (name !== 'server' && name !== 'context') url.searchParams.set('tab', name);
      else { url.searchParams.delete('tab'); url.searchParams.delete('role'); }
      history.replaceState({ ...(history.state || {}), tangentRoleEditor: false }, '', url.pathname + url.search + url.hash);
    }
    if (name === 'roles' && !(dirty && selected)) loadScope();
    if (name === 'people') window.TangentPeople?.load?.();
    window.TangentAccess?.activate?.(name);
  }

  function roleEditorTab(name) {
    if (!['appearance', 'permissions', 'members'].includes(name)) name = 'appearance';
    editorTab = name;
    document.querySelectorAll('[data-role-editor-tab]').forEach(button => {
      const active = button.dataset.roleEditorTab === name;
      button.setAttribute('aria-selected', String(active));
      button.tabIndex = active ? 0 : -1;
    });
    $('role-appearance-panel').hidden = name !== 'appearance';
    $('role-permissions-panel').hidden = name !== 'permissions';
    $('role-members-panel').hidden = name !== 'members';
    $('role-editor-actions').hidden = name === 'members' || systemRole(selected);
    if (name === 'members' && selected && !selected._new) {
      $('role-member-search').value = '';
      searchMembers('');
    }
  }

  async function loadScope() {
    if (window.TangentPages?.route?.kind !== 'settings' || $('settings-roles-panel').hidden) return;
    const scope = currentScope();
    if (!scope.id) {
      roleListHasPrevious = false;
      roleListHasNext = false;
      roleListPage = 1;
      roles = []; roleTotal = 0; clearMemberData(); selected = undefined;
      showOverview(false); message('role-list-status', 'There is nothing at this scope yet.'); return;
    }
    const scopeSignature = currentScopeSignature();
    const identitySignature = currentIdentitySignature();
    if (scopeSignature !== lastMemberScope || identitySignature !== lastMemberIdentity) {
      clearMemberData();
      lastMemberScope = scopeSignature;
      lastMemberIdentity = identitySignature;
    }
    const version = ++loadVersion;
    message('role-list-status', 'Loading roles…'); message('role-status', ''); message('role-members-status', '');
    try {
      const [descriptionResult, rolePageResult] = await Promise.allSettled([
        descriptor ? Promise.resolve({ data: descriptor }) : request('/api/roles/ui/descriptor'),
        request(api('roles?pageSize=' + rolePageSize + '&page=1'))
      ]);
      if (version !== loadVersion) return;
      if (rolePageResult.status === 'rejected') throw rolePageResult.reason;
      if (descriptionResult.status === 'fulfilled') {
        descriptor = descriptionResult.value.data;
        descriptorError = undefined;
      } else {
        descriptor ||= { capabilities: [] };
        descriptorError = descriptionResult.reason;
      }
      const rolePage = rolePageResult.value;
      roles = (rolePage.data.items || []).map(normalizeRole);
      roleTotal = rolePage.data.totalCount ?? roles.length;
      roleListPage = 1;
      roleListHasPrevious = false;
      roleListHasNext = roles.length < roleTotal;
      clearMemberData();
      const requested = new URL(location.href).searchParams.get('role');
      let target = requested === 'new' && selected?._new ? selected : roles.find(role => role.id === requested);
      if (requested && requested !== 'new' && !target) {
        try {
          const direct = normalizeRole((await request(api('roles/' + encodeURIComponent(requested)))).data);
          if (direct) target = direct;
        } catch (_) { /* The normal unavailable route below keeps the deep link honest. */ }
      }
      if (target) showEditor(target, null, selected?._new ? editorTab : 'appearance');
      else {
        if (requested) roleUrl(undefined);
        showOverview(false);
      }
      message('role-list-status', descriptorError ? 'Roles loaded. Permission details are temporarily unavailable.'
        : roles.length ? `Showing ${roles.length} of ${roleTotal}` : 'No roles have been created at this scope.', !!descriptorError);
      loadMemberCounts(roles, version).then(() => {
        if (version === loadVersion) { renderOverview(); updateCountRetry(); }
      });
    } catch (error) {
      if (version !== loadVersion) return;
      roles = []; roleTotal = 0; roleListPage = 1; roleListHasPrevious = false; roleListHasNext = false;
      memberTotals = new Map(); roleMemberState = new Map(); selected = undefined; showOverview(false);
      message('role-list-status', error.message, true);
    }
  }

  async function loadMemberCounts(values, version) {
    for (const role of values) {
      if (version !== loadVersion) return;
      const total = role.memberCount ?? role.members?.length ?? 0;
      memberTotals.set(role.id, total);
      roleMemberState.set(role.id, { ...memberState(role.id), total, countUnavailable: false });
    }
    renderOverview();
  }

  function updateCountRetry() {
    $('role-counts-retry').hidden = !roles.some(role => memberState(role.id).countUnavailable);
  }

  async function retryMemberCounts(button) {
    const failed = roles.filter(role => memberState(role.id).countUnavailable);
    if (!failed.length) return;
    button.disabled = true;
    message('role-list-status', 'Retrying member counts…');
    try {
      await loadMemberCounts(failed, loadVersion);
      const stillFailed = failed.some(role => memberState(role.id).countUnavailable);
      message('role-list-status', stillFailed ? 'Some member counts are still unavailable.' : `Showing ${roles.length} of ${roleTotal}`, stillFailed);
    } finally {
      button.disabled = false;
      updateCountRetry();
    }
  }

  async function changeRolePage(delta, button) {
    const page = roleListPage + delta;
    if (page < 1 || (delta < 0 && !roleListHasPrevious) || (delta > 0 && !roleListHasNext)) return;
    button.disabled = true;
    message('role-list-status', 'Loading roles…');
    try {
      const rolePage = (await request(api('roles?pageSize=' + rolePageSize + '&page=' + encodeURIComponent(page)))).data;
      roles = (rolePage.items || []).map(normalizeRole);
      roleListPage = page;
      roleTotal = rolePage.totalCount ?? roleTotal;
      roleListHasPrevious = page > 1;
      roleListHasNext = page * rolePageSize < roleTotal;
      clearMemberData();
      await loadMemberCounts(roles, loadVersion);
      renderOverview();
      const first = roles.length ? (page - 1) * rolePageSize + 1 : 0;
      const last = first ? first + roles.length - 1 : 0;
      message('role-list-status', `Showing ${first}–${last} of ${roleTotal}`);
    } catch (error) {
      message('role-list-status', error.message, true);
    } finally {
      button.disabled = false;
    }
  }

  function showOverview(updateUrl = true) {
    selected = undefined; dirty = false;
    $('role-overview').hidden = false;
    $('role-editor-view').hidden = true;
    if (updateUrl) roleUrl(undefined);
    renderOverview();
  }

  function showEditor(role, urlMode = 'push', tab = 'appearance') {
    selected = role; dirty = false;
    $('role-overview').hidden = true;
    $('role-editor-view').hidden = false;
    if (urlMode) roleUrl(role, urlMode);
    renderRole(); roleEditorTab(tab);
  }

  function roleCopy(role) {
    const copy = make('span', 'role-directory-copy');
    const line = make('span', 'role-directory-name'); line.append(make('strong', '', role.name));
    if (systemRole(role)) line.append(make('small', 'role-kind', 'Built in'));
    else if (role._new) line.append(make('small', 'role-kind', 'Unsaved'));
    if (role.presentation?.selfEnrollable === 'true') line.append(make('small', 'role-kind', 'Self-enrollable'));
    copy.append(line, make('small', 'role-directory-purpose', role.purpose || (systemRole(role) ? 'Managed by Tangent Space' : 'Custom role')));
    return copy;
  }

  function renderOverview() {
    const list = $('role-directory-list'); list.replaceChildren();
    const term = $('role-search').value.trim().toLocaleLowerCase();
    const values = roles.filter(role => !term || `${role.name} ${role.purpose || ''}`.toLocaleLowerCase().includes(term));
    const first = roles.length ? (roleListPage - 1) * rolePageSize + 1 : 0;
    const last = first ? first + roles.length - 1 : 0;
    $('role-overview-heading').textContent = `Roles — ${first}–${last} of ${roleTotal}`;
    for (const role of values) {
      const button = make('button', 'role-directory-row'); button.type = 'button'; button.setAttribute('role', 'option');
      button.style.setProperty('--role-color', color(role.presentation?.color));
      const identity = make('span', 'role-directory-identity'); identity.append(make('span', 'role-list-dot'), roleCopy(role));
      const state = memberState(role.id);
      const countText = state.countUnavailable ? '—' : memberCount(role);
      const count = make('span', 'role-directory-members', countText);
      count.setAttribute('aria-label', `${countText} direct members`);
      button.append(identity, count, make('span', 'role-directory-chevron', '›'));
      button.addEventListener('click', () => showEditor(role)); list.append(button);
    }
    $('role-page-previous').hidden = !roleListHasPrevious;
    $('role-page-next').hidden = !roleListHasNext;
    updateCountRetry();
    if (!values.length && roles.length) list.append(make('p', 'role-directory-empty', 'No roles match that search.'));
  }

  function renderSiblingList() {
    const list = $('role-list'); list.replaceChildren();
    const selectedOutsidePage = selected && !selected._new && !roles.some(role => role.id === selected.id) ? [selected] : [];
    const values = [...(selected?._new ? [selected] : selectedOutsidePage), ...roles];
    for (const role of values) {
      const button = make('button', 'role-list-item'); button.type = 'button'; button.setAttribute('role', 'option');
      button.setAttribute('aria-selected', String(role === selected || role.id === selected?.id));
      button.style.setProperty('--role-color', color(role.presentation?.color));
      button.append(make('span', 'role-list-dot'), make('strong', '', role.name));
      button.addEventListener('click', () => {
        if (dirty) { message('role-status', 'Save or reset your changes before choosing another role.', true); return; }
        showEditor(role, 'replace');
      });
      list.append(button);
    }
  }

  function capabilityGroup(key) {
    const prefix = String(key || '').replace(/^global:/, '').split(/[._]/)[0];
    return groupOrder.includes(prefix) ? prefix : 'other';
  }

  function renderCapabilities(role, managed) {
    const root = $('role-capabilities'); root.replaceChildren();
    if (descriptorError) {
      root.append(make('p', 'hint error', 'Permission details could not be loaded. The role and its members remain available; reload to retry.'));
      return;
    }
    const scope = currentKind();
    const term = $('role-permission-search').value.trim().toLocaleLowerCase();
    const available = (descriptor?.capabilities || []).filter(item => !item.scopeTypes || item.scopeTypes.includes(scope))
      .filter(item => !term || `${item.key} ${item.label || ''} ${item.description || ''}`.toLocaleLowerCase().includes(term));
    for (const group of groupOrder) {
      const values = available.filter(item => capabilityGroup(item.key) === group);
      if (!values.length) continue;
      const section = make('section', 'role-capability-group'); section.append(make('h4', '', groupLabels[group]));
      const list = make('div', 'role-capability-list');
      for (const capability of values) {
        const label = make('label', 'role-capability');
        const copy = make('span'); copy.append(make('strong', '', capability.label || capability.key), make('small', '', capability.description || capability.key));
        const toggle = document.createElement('input'); toggle.type = 'checkbox'; toggle.value = capability.key;
        toggle.checked = draftGrantKeys.has(capability.key); toggle.disabled = managed;
        toggle.addEventListener('change', () => {
          if (toggle.checked) draftGrantKeys.add(capability.key); else draftGrantKeys.delete(capability.key);
          dirty = true; message('role-status', 'Unsaved changes');
        });
        label.append(copy, toggle); list.append(label);
      }
      section.append(list); root.append(section);
    }
    if (!available.length) root.append(make('p', 'hint', 'No permissions match that search.'));
  }

  function updateRolePreview() {
    if (!selected) return;
    const name = $('role-name').value.trim() || 'Untitled role';
    const purpose = $('role-purpose').value.trim() || 'What this role is for';
    const roleColor = color($('role-color').value);
    $('role-editor-title').textContent = name;
    $('role-preview-name').textContent = name;
    $('role-preview-purpose').textContent = purpose;
    $('role-preview-chip').querySelector('b').textContent = name;
    $('role-color-value').textContent = roleColor.toLocaleLowerCase();
    $('role-color-dot').style.setProperty('--role-color', roleColor);
    $('role-preview-dot').style.setProperty('--role-color', roleColor);
    $('role-preview').style.setProperty('--role-color', roleColor);
    $('role-preview-chip').style.setProperty('--role-color', roleColor);
  }

  function renderRole() {
    if (!selected) return;
    const managed = systemRole(selected);
    draftGrantKeys = new Set((selected.grants || []).map(grant => grant.capability));
    $('role-editor-kicker').textContent = selected._new ? 'NEW ROLE' : currentKind().toUpperCase() + ' ROLE';
    $('role-name').value = selected.name || '';
    $('role-purpose').value = selected.purpose || '';
    $('role-color').value = color(selected.presentation?.color);
    $('role-managed').hidden = !managed;
    $('role-managed-explanation').hidden = !managed;
    $('role-managed-explanation').textContent = protectedOwnerRole(selected)
      ? 'Appearance and permissions are managed by Tangent. The server owner is set by server administration and this setting is not changeable here.'
      : 'Appearance and permissions are managed by Tangent. Authorized owners can still change direct membership.';
    for (const field of [$('role-name'), $('role-purpose'), $('role-color')]) field.disabled = managed;
    const selfEnrollableInput = $('role-self-enrollable');
    if (selfEnrollableInput) {
      selfEnrollableInput.checked = selected.presentation?.selfEnrollable === 'true' || selected.metadata?.selfEnrollable === 'true';
      selfEnrollableInput.disabled = managed;
    }
    $('role-save').textContent = selected._new ? 'Create role' : 'Save changes';
    $('role-reset').hidden = managed;
    $('role-retire').hidden = managed || selected._new;
    updateRolePreview(); renderCapabilities(selected, managed); renderSiblingList();
    if (editorTab === 'members') searchMembers('');
  }

  let searchSeq = 0;
  async function searchMembers(query = '') {
    const seq = ++searchSeq;
    const root = $('role-members');
    if (!selected || selected._new) {
      root.replaceChildren();
      root.append(make('p', 'role-member-empty', 'Create this role before managing members.'));
      $('role-member-count').textContent = '0';
      $('role-members-heading-count').textContent = '0';
      return;
    }
    const protectedOwner = protectedOwnerRole(selected);
    const manageable = canManageMembers(selected);
    $('role-member-protected').hidden = !protectedOwner;
    $('role-member-search').disabled = !manageable && !protectedOwner;

    message('role-members-status', 'Loading members…');
    try {
      const q = (query || '').trim();
      const params = new URLSearchParams({ role: selected.id });
      if (q) params.set('query', q);
      const res = await fetch('/api/roles/ui/search?' + params, {
        credentials: 'same-origin',
        headers: { Accept: 'application/json' }
      });
      if (!res.ok) {
        const data = await res.json().catch(() => ({}));
        throw failure(res, data);
      }
      const items = await res.json();
      if (seq !== searchSeq) return;

      root.replaceChildren();
      message('role-members-status', '');

      if (!q) {
        $('role-member-count').textContent = String(items.length);
        $('role-members-heading-count').textContent = String(items.length);
        memberTotals.set(selected.id, items.length);
      }

      if (!items.length) {
        root.append(make('p', 'role-member-empty', q ? 'No matching participants found.' : 'No members in this role yet.'));
        return;
      }

      for (const person of items) {
        const label = person.displayName || person.handle || 'Participant';
        const row = make('div', 'role-member');
        row.dataset.subject = person.id;
        row.style.setProperty('--role-color', color(selected.presentation?.color));

        const avatar = make('span', 'role-member-avatar', label.slice(0, 1).toUpperCase());
        showAvatar(avatar, person, label);
        row.append(avatar);

        const copy = make('span');
        copy.append(make('strong', '', label), make('small', '', memberByline(person)));
        row.append(copy);

        if (manageable) {
          const inRole = !!person.inRole;
          const toggle = make('button', inRole ? 'btn btn-quiet role-member-toggle in-role' : 'btn btn-primary role-member-toggle', inRole ? 'Remove' : 'Add');
          toggle.type = 'button';
          toggle.title = inRole ? `Remove ${label} from ${selected.name}` : `Add ${label} to ${selected.name}`;
          toggle.setAttribute('aria-label', toggle.title);
          toggle.addEventListener('click', async () => {
            toggle.disabled = true;
            const willAdd = !person.inRole;
            message('role-members-status', (willAdd ? `Adding ${label}…` : `Removing ${label}…`));
            try {
              await request(api('roles/' + encodeURIComponent(selected.id) + '/members/' + encodeURIComponent(person.id)), {
                method: willAdd ? 'PUT' : 'DELETE'
              });
              person.inRole = willAdd;
              toggle.textContent = willAdd ? 'Remove' : 'Add';
              toggle.className = willAdd ? 'btn btn-quiet role-member-toggle in-role' : 'btn btn-primary role-member-toggle';
              toggle.title = willAdd ? `Remove ${label} from ${selected.name}` : `Add ${label} to ${selected.name}`;
              toggle.setAttribute('aria-label', toggle.title);
              message('role-members-status', willAdd ? `${label} added to ${selected.name}.` : `${label} removed from ${selected.name}.`);
              if (!q && !willAdd) {
                row.remove();
                const newCount = root.querySelectorAll('.role-member').length;
                $('role-member-count').textContent = String(newCount);
                $('role-members-heading-count').textContent = String(newCount);
                memberTotals.set(selected.id, newCount);
                if (newCount === 0) {
                  root.append(make('p', 'role-member-empty', 'No members in this role yet.'));
                }
              } else if (willAdd) {
                const currentCount = (memberTotals.get(selected.id) || 0) + 1;
                memberTotals.set(selected.id, currentCount);
                $('role-member-count').textContent = String(currentCount);
                $('role-members-heading-count').textContent = String(currentCount);
              }
            } catch (err) {
              message('role-members-status', err.message, true);
            } finally {
              toggle.disabled = false;
            }
          });
          row.append(toggle);
        }
        root.append(row);
      }
    } catch (err) {
      if (seq !== searchSeq) return;
      message('role-members-status', err.message, true);
    }
  }

  function roleGrants() {
    if (descriptorError) return [...new Set((selected.grants || []).map(grant => grant.capability))];
    const known = new Set((descriptor?.capabilities || []).map(capability => capability.key));
    const grants = (selected.grants || []).filter(grant => !known.has(grant.capability));
    for (const capability of draftGrantKeys) {
      const original = (selected.grants || []).find(grant => grant.capability === capability);
      grants.push(original || { capability });
    }
    return [...new Set(grants.map(grant => grant.capability))];
  }

  const roleKeyFor = name => 'role:' + name.toLocaleLowerCase().normalize('NFKD').replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '').slice(0, 64);

  async function saveRole(button) {
    if (!selected || systemRole(selected)) return;
    const name = $('role-name').value.trim(), purpose = $('role-purpose').value.trim();
    if (!name) { message('role-status', 'Give this role a name.', true); return; }
    button.disabled = true; message('role-status', selected._new ? 'Creating…' : 'Saving…');
    try {
      const body = {
        name,
        permissions: roleGrants(),
        metadata: {
          purpose: purpose || '',
          color: $('role-color').value,
          selfEnrollable: $('role-self-enrollable')?.checked ? 'true' : 'false'
        }
      };
      let key = selected.id;
      if (selected._new) {
        key = roleKeyFor(name);
        if (key === 'role:') throw new Error('Use at least one letter or number in the role name.');
        try { await request(api('roles/' + encodeURIComponent(key))); throw new Error('A role with that stable key already exists. Choose a more distinct name.'); }
        catch (error) { if (error.status !== 404) throw error; }
      }
      const result = await request(api('roles/' + encodeURIComponent(key)), { method: selected._new ? 'PUT' : 'PATCH', body });
      selected = normalizeRole(result.data); dirty = false; roleUrl(selected, 'replace');
      await loadScope(); message('role-status', 'Role saved.');
    } catch (error) {
      if (error.status === 400 && /antiforgery/i.test(error.message)) csrf = undefined;
      message('role-status', error.message, true);
    } finally { button.disabled = false; }
  }

  function resetRole() {
    if (!selected || systemRole(selected)) return;
    const tab = editorTab;
    selected = selected._new
      ? { _new: true, id: '', name: 'New role', purpose: '', grants: [], presentation: { color: '#a98df0', system: 'false', selfEnrollable: 'false' } }
      : roles.find(role => role.id === selected.id) || selected;
    dirty = false; renderRole(); roleEditorTab(tab); message('role-status', 'Changes reset.');
  }

  async function retireRole(button) {
    if (!selected || selected._new || systemRole(selected)) return;
    button.disabled = true; message('role-status', 'Deleting…');
    try {
      await request(api('roles/' + encodeURIComponent(selected.id)), { method: 'DELETE' });
      selected = undefined; dirty = false; roleUrl(undefined, 'replace'); await loadScope();
      message('role-list-status', 'Role deleted.');
    } catch (error) { message('role-status', error.message, true); }
    finally { button.disabled = false; }
  }


  function createRole() {
    const role = { _new: true, id: '', name: 'New role', purpose: '', grants: [], presentation: { color: '#a98df0', system: 'false', selfEnrollable: 'false' } };
    showEditor(role); $('role-name').select();
  }

  function leaveEditor() {
    if (dirty) { message('role-status', 'Save or reset your changes before returning to all roles.', true); return; }
    if (history.state?.tangentRoleEditor) {
      history.back();
      showOverview(false);
    } else showOverview();
  }

  function syncRoleHistory() {
    if (location.pathname !== '/settings' || $('settings-roles-panel').hidden) return;
    const requested = new URL(location.href).searchParams.get('role');
    if (!requested) { showOverview(false); return; }
    const target = requested === 'new' && selected?._new ? selected : roles.find(role => role.id === requested);
    if (target) showEditor(target, null, 'appearance'); else showOverview(false);
  }

  function openFor(nextSite) {
    site = nextSite;
    if (window.TangentPages?.route?.kind !== 'settings') return;
    const owner = site?.participant?.isOwner === true;
    $('settings-shell').hidden = !owner;
    if (!owner) return;
    settingsTab(requestedTab('server'), false);
  }

  document.querySelectorAll('[data-settings-tab]').forEach(button => {
    button.addEventListener('click', () => settingsTab(button.dataset.settingsTab));
    button.addEventListener('keydown', event => {
      if (!['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key)) return;
      event.preventDefault();
      const tabs = [...document.querySelectorAll('[data-settings-tab]')].filter(tab => !tab.hidden), index = tabs.indexOf(button);
      const next = tabs[(index + (['ArrowRight', 'ArrowDown'].includes(event.key) ? 1 : -1) + tabs.length) % tabs.length];
      next.focus(); settingsTab(next.dataset.settingsTab);
    });
  });
  document.querySelectorAll('[data-role-editor-tab]').forEach(button => button.addEventListener('click', () => roleEditorTab(button.dataset.roleEditorTab)));
  document.querySelectorAll('[data-role-editor-tab]').forEach(button => {
    button.addEventListener('keydown', event => {
      if (!['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End'].includes(event.key)) return;
      event.preventDefault();
      const tabs = [...document.querySelectorAll('[data-role-editor-tab]')];
      const index = tabs.indexOf(button);
      let target = button;
      if (event.key === 'Home') target = tabs[0];
      else if (event.key === 'End') target = tabs[tabs.length - 1];
      else {
        const direction = ['ArrowRight', 'ArrowDown'].includes(event.key) ? 1 : -1;
        target = tabs[(index + direction + tabs.length) % tabs.length];
      }
      target.focus();
      roleEditorTab(target.dataset.roleEditorTab);
    });
  });
  $('role-search').addEventListener('input', renderOverview);
  $('role-permission-search').addEventListener('input', () => selected && renderCapabilities(selected, systemRole(selected)));
  $('role-page-previous').addEventListener('click', event => changeRolePage(-1, event.currentTarget).catch(error => message('role-list-status', error.message, true)));
  $('role-page-next').addEventListener('click', event => changeRolePage(1, event.currentTarget).catch(error => message('role-list-status', error.message, true)));
  $('role-counts-retry').addEventListener('click', event => retryMemberCounts(event.currentTarget).catch(error => message('role-list-status', error.message, true)));
  $('role-create').addEventListener('click', createRole);
  $('role-back').addEventListener('click', leaveEditor);
  for (const field of [$('role-name'), $('role-purpose'), $('role-color')]) field.addEventListener('input', () => {
    dirty = true; updateRolePreview(); message('role-status', 'Unsaved changes');
  });
  $('role-self-enrollable')?.addEventListener('change', () => {
    dirty = true; message('role-status', 'Unsaved changes');
  });
  $('role-form').addEventListener('submit', event => { event.preventDefault(); saveRole(event.submitter || $('role-save')); });
  $('role-reset').addEventListener('click', resetRole);
  $('role-retire').addEventListener('click', event => retireRole(event.currentTarget));
  let memberSearchDebounce;
  $('role-member-search').addEventListener('input', () => {
    clearTimeout(memberSearchDebounce);
    memberSearchDebounce = setTimeout(() => searchMembers($('role-member-search').value), 200);
  });
  window.TangentSettingsTabs = { requested: requestedTab };
  window.addEventListener('tangent:welcome', event => openFor(event.detail));
  window.addEventListener('tangent:route', event => openFor(event.detail));
  window.addEventListener('popstate', syncRoleHistory);
})();
