'use strict';
(() => {
  const $ = id => document.getElementById(id);
  const tenant = 'tangent-space';
  const groupOrder = ['host', 'tangent', 'topic', 'post', 'other'];
  const groupLabels = { host: 'Server', tangent: 'Tangents', topic: 'Topics', post: 'Posts', other: 'Other' };
  const roleMemberPageSize = 50;
  let site, descriptor, roles = [], selected, tangents = [], topics = [], loadVersion = 0, csrf, pendingMember;
  const rolePageSize = 100;
  let roleTotal = 0, editorTab = 'appearance', dirty = false;
  let roleListPage = 1, roleListHasPrevious = false, roleListHasNext = false;
  let membersByRole = new Map(), memberTotals = new Map(), loadedMemberRoles = new Set(), roleMemberState = new Map(), memberProfileCache = new Map();
  let memberProfileFailures = new Set();
  let lastMemberScope = '', lastMemberIdentity = '', lastTangentScope = '';
  let draftGrantKeys = new Set();

  const color = value => /^#[0-9a-f]{6}$/i.test(value || '') ? value : '#a98df0';
  const systemRole = role => role?.presentation?.system === 'true';
  const protectedOwnerRole = role => systemRole(role) && /:owner$/i.test(role?.id || '');
  const canManageMembers = role => !!role && !role._new && !protectedOwnerRole(role);
  const currentKind = () => $('role-scope-kind').value;
  const currentScope = () => {
    const kind = currentKind();
    return { type: kind, id: kind === 'host' ? 'site' : kind === 'tangent' ? $('role-tangent-scope').value : $('role-topic-scope').value };
  };
  const api = suffix => {
    const scope = currentScope();
    return '/api/identity/scoped-roles/' + encodeURIComponent(tenant) + '/' + encodeURIComponent(scope.type) + '/' + encodeURIComponent(scope.id) + '/' + suffix;
  };
  const message = (id, value, error = false) => { const node = $(id); node.textContent = value || ''; node.classList.toggle('error', error); };
  const make = (tag, className, text) => { const node = document.createElement(tag); node.className = className || ''; if (text !== undefined) node.textContent = text; return node; };
  const roleMembers = role => membersByRole.get(role?.id) || [];
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
  const normalizeLookupIdentifier = value => String(value || '').trim().replace(/^@/, '').toLocaleLowerCase();
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
    membersByRole = new Map();
    memberTotals = new Map();
    roleMemberState = new Map();
    loadedMemberRoles = new Set();
    memberProfileCache = new Map();
    memberProfileFailures = new Set();
  }

  function memberPaginationPath(roleId, page) {
    const params = new URLSearchParams({ pageSize: String(roleMemberPageSize) });
    if (typeof page === 'number' && page > 1) params.set('page', String(page));
    return `roles/${encodeURIComponent(roleId)}/members?${params}`;
  }

  function hydrateMemberRows(roleId) {
    if (!selected || selected.id !== roleId) return;
    document.querySelectorAll('#role-members [data-subject]').forEach(node => {
      const subject = node.dataset.subject;
      const person = memberProfileCache.get(subject);
      const failed = memberProfileFailures.has(subject);
      const label = person?.displayName || person?.label || person?.handle || 'Participant';
      const byline = person ? memberByline(person) : failed ? 'Profile unavailable' : 'Loading profile…';
      const displayName = node.querySelector('strong');
      const bylineNode = node.querySelector('small');
      const removeButton = node.querySelector('.role-member-remove');
      if (displayName) displayName.textContent = label;
      if (bylineNode) bylineNode.textContent = byline;
      showAvatar(node.querySelector('.role-member-avatar'), person, label);
      if (removeButton) {
        const title = person ? `Remove ${label} from ${selected.name}` : 'Profile required before removing this membership';
        removeButton.title = title;
        removeButton.setAttribute('aria-label', title);
        removeButton.disabled = !person;
      }
    });
    $('role-member-profiles-retry').hidden = !roleMembers(selected).some(member => memberProfileFailures.has(member.subject));
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

  function settingsTab(name, updateUrl = true) {
    if (!['server', 'roles'].includes(name)) name = 'server';
    document.querySelectorAll('[data-settings-tab]').forEach(button => {
      const active = button.dataset.settingsTab === name;
      button.setAttribute('aria-selected', String(active));
      button.tabIndex = active ? 0 : -1;
    });
    $('settings-server-panel').hidden = name !== 'server';
    $('settings-roles-panel').hidden = name !== 'roles';
    if (updateUrl) {
      const url = new URL(location.href);
      if (name === 'roles') url.searchParams.set('tab', 'roles');
      else { url.searchParams.delete('tab'); url.searchParams.delete('role'); }
      history.replaceState({ ...(history.state || {}), tangentRoleEditor: false }, '', url.pathname + url.search + url.hash);
    }
    if (name === 'roles') loadScope();
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
  }

  function option(select, value, label) {
    const node = document.createElement('option'); node.value = value; node.textContent = label; select.append(node);
  }

  async function ensureTangents() {
    if (tangents.length) return;
    const available = site?.tangents?.tangents;
    tangents = Array.isArray(available) ? available : (await request('/api/v1/tangents')).data.tangents || [];
    const select = $('role-tangent-scope'); select.replaceChildren();
    for (const tangent of tangents) option(select, tangent.key, tangent.name || tangent.key);
  }

  async function ensureTopics(force = false) {
    const tangent = $('role-tangent-scope').value;
    if (!tangent) {
      topics = [];
      $('role-topic-scope').replaceChildren();
      lastTangentScope = '';
      return;
    }
    const selected = $('role-topic-scope').value;
    if (!force && lastTangentScope === tangent && topics.length) return;
    const listing = (await request('/api/v1/tangents/' + encodeURIComponent(tangent) + '/topics')).data;
    topics = listing.rooms || listing.channels || [];
    const select = $('role-topic-scope'); const keep = selected && topics.some(topic => topic.key === selected) ? selected : '';
    select.replaceChildren();
    for (const topic of topics) option(select, topic.key, topic.title || topic.key);
    if (keep) select.value = keep;
    lastTangentScope = tangent;
  }

  async function changeScope() {
    const kind = currentKind();
    $('role-tangent-field').hidden = kind === 'host';
    $('role-topic-field').hidden = kind !== 'topic';
    if (kind !== 'host') await ensureTangents();
    if (kind === 'topic' && !$('role-topic-scope').options.length) await ensureTopics();
    const notes = {
      host: 'Server roles can flow into every Tangent and Topic.',
      tangent: 'Tangent roles apply here and may flow into its Topics.',
      topic: 'Topic roles stay local to this conversation.'
    };
    message('role-scope-note', notes[kind]);
    roleUrl(undefined);
    selected = undefined; dirty = false;
    await loadScope();
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
      const [description, rolePage] = await Promise.all([
        descriptor ? Promise.resolve({ data: descriptor }) : request('/api/identity/scoped-roles/descriptor'),
        request(api('roles?pageSize=' + rolePageSize + '&page=1'))
      ]);
      if (version !== loadVersion) return;
      descriptor = description.data;
      roles = (rolePage.data.items || []).filter(role => role.status !== 'Retired' && role.status !== 2);
      roleTotal = rolePage.data.totalCount ?? roles.length;
      roleListPage = 1;
      roleListHasPrevious = false;
      roleListHasNext = roles.length < roleTotal;
      membersByRole = new Map();
      memberTotals = new Map();
      loadedMemberRoles = new Set();
      roleMemberState = new Map();
      const requested = new URL(location.href).searchParams.get('role');
      let target = requested === 'new' && selected?._new ? selected : roles.find(role => role.id === requested);
      if (requested && requested !== 'new' && !target) {
        try {
          const direct = (await request(api('roles/' + encodeURIComponent(requested)))).data;
          if (direct && direct.status !== 'Retired' && direct.status !== 2) target = direct;
        } catch (_) { /* The normal unavailable route below keeps the deep link honest. */ }
      }
      if (target) showEditor(target, null, selected?._new ? editorTab : 'appearance');
      else {
        if (requested) roleUrl(undefined);
        showOverview(false);
      }
      message('role-list-status', roles.length ? `Showing ${roles.length} of ${roleTotal}` : 'No roles have been created at this scope.');
      loadMemberCounts(roles, version).then(() => {
        if (version === loadVersion) { renderOverview(); updateCountRetry(); }
      });
    } catch (error) {
      if (version !== loadVersion) return;
      roles = []; roleTotal = 0; roleListPage = 1; roleListHasPrevious = false; roleListHasNext = false;
      membersByRole = new Map(); memberTotals = new Map(); loadedMemberRoles = new Set(); roleMemberState = new Map(); selected = undefined; showOverview(false);
      message('role-list-status', error.message, true);
    }
  }

  async function loadMemberCounts(values, version) {
    const countTargets = [...values];
    const totalConcurrency = 6;
    for (let cursor = 0; cursor < countTargets.length && version === loadVersion; cursor += totalConcurrency) {
      const batch = countTargets.slice(cursor, cursor + totalConcurrency);
      await Promise.all(batch.map(async role => {
        try {
          const page = (await request(api('roles/' + encodeURIComponent(role.id) + '/members?pageSize=1')).data);
          if (version !== loadVersion) return;
          const total = page?.totalCount ?? (page?.items || []).length;
          memberTotals.set(role.id, total);
          const state = memberState(role.id);
          roleMemberState.set(role.id, { ...state, total, countUnavailable: false });
        } catch (_) {
          roleMemberState.set(role.id, { ...(memberState(role.id)), countUnavailable: true });
        }
      }));
      if (version === loadVersion) renderOverview();
    }
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
      roles = (rolePage.items || []).filter(role => role.status !== 'Retired' && role.status !== 2);
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

  async function refreshMembers(role = selected, stepBackIfEmpty = false) {
    if (!role || role._new) return;
    const page = Math.max(memberState(role.id).page || 1, 1);
    await loadRoleMembers(role, page);
    if (stepBackIfEmpty && page > 1 && !roleMembers(role).length) await loadRoleMembers(role, page - 1);
    renderOverview(); renderSiblingList(); renderMembers();
  }

  async function loadRoleMembers(role, page = 1) {
    if (!role || role._new) return;
    const state = memberState(role.id);
    const data = (await request(memberPaginationPath(role.id, page))).data;
    const items = Array.isArray(data?.items) ? data.items : [];
    const total = data?.totalCount ?? memberTotals.get(role.id);
    const hasNext = typeof total === 'number' ? page * roleMemberPageSize < total : items.length === roleMemberPageSize;
    roleMemberState.set(role.id, { ...state, page, total, hasPrevious: page > 1, hasNext, pageSize: roleMemberPageSize });
    membersByRole = new Map([[role.id, items]]);
    if (typeof total === 'number') memberTotals.set(role.id, total);
    loadedMemberRoles = new Set([role.id]);
    const subjects = new Set(items.map(member => member.subject));
    memberProfileCache = new Map([...memberProfileCache].filter(([id]) => subjects.has(id)));
    memberProfileFailures = new Set([...memberProfileFailures].filter(id => subjects.has(id)));
    await loadMemberProfiles([...subjects], role.id);
  }

  async function changeMemberPage(delta, button) {
    if (!selected || selected._new) return;
    const state = memberState(selected.id);
    const page = state.page + delta;
    if (page < 1 || (delta < 0 && !state.hasPrevious) || (delta > 0 && !state.hasNext)) return;
    button.disabled = true;
    message('role-members-status', 'Loading members…');
    try {
      await loadRoleMembers(selected, page);
      renderMembers();
      message('role-members-status', '');
    } catch (error) {
      message('role-members-status', error.message, true);
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
    if (!role._new && !loadedMemberRoles.has(role.id)) {
      refreshMembers(role).catch(error => message('role-members-status', error.message, true));
    }
  }

  function roleCopy(role) {
    const copy = make('span', 'role-directory-copy');
    const line = make('span', 'role-directory-name'); line.append(make('strong', '', role.name));
    if (systemRole(role)) line.append(make('small', 'role-kind', 'Built in'));
    else if (role._new) line.append(make('small', 'role-kind', 'Unsaved'));
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
    const prefix = String(key || '').split('.')[0];
    return groupOrder.includes(prefix) ? prefix : 'other';
  }

  function renderCapabilities(role, managed) {
    const root = $('role-capabilities'); root.replaceChildren();
    const scope = currentKind();
    const term = $('role-permission-search').value.trim().toLocaleLowerCase();
    const available = (descriptor?.capabilities || []).filter(item => (item.scopeTypes || []).includes(scope))
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
    $('role-save').textContent = selected._new ? 'Create role' : 'Save changes';
    $('role-reset').hidden = managed;
    $('role-retire').hidden = managed || selected._new;
    $('role-member-open').hidden = !canManageMembers(selected);
    closeMemberInvite();
    updateRolePreview(); renderCapabilities(selected, managed); renderMembers(); renderSiblingList();
  }

  async function loadMemberProfiles(ids, roleId, retry = false) {
    if (!ids.length) return;
    const needed = [...new Set(ids)].filter(id => !memberProfileCache.has(id) && (retry || !memberProfileFailures.has(id)));
    if (!needed.length) { hydrateMemberRows(roleId); return; }
    for (let cursor = 0; cursor < needed.length; cursor += roleMemberPageSize) {
      const batch = needed.slice(cursor, cursor + roleMemberPageSize);
      try {
        const query = batch.map(id => 'id=' + encodeURIComponent(id)).join('&');
        const rows = (await request('/api/roles/ui/participants?' + query)).data || [];
        const returned = new Set();
        for (const row of rows) {
          if (row?.id) { memberProfileCache.set(row.id, row); memberProfileFailures.delete(row.id); returned.add(row.id); }
        }
        batch.filter(id => !returned.has(id)).forEach(id => memberProfileFailures.add(id));
      } catch (_) {
        batch.forEach(id => memberProfileFailures.add(id));
      }
    }
    if (selected?.id === roleId) hydrateMemberRows(roleId);
  }

  async function retryMemberProfiles(button) {
    if (!selected) return;
    const ids = roleMembers(selected).map(member => member.subject).filter(id => memberProfileFailures.has(id));
    button.disabled = true;
    message('role-members-status', 'Retrying profiles…');
    try {
      await loadMemberProfiles(ids, selected.id, true);
      const stillFailed = ids.some(id => memberProfileFailures.has(id));
      message('role-members-status', stillFailed ? 'Some profiles are still unavailable.' : '', stillFailed);
    } finally {
      button.disabled = false;
    }
  }

  function renderMembers() {
    const root = $('role-members'); root.replaceChildren();
    if (!selected) return;
    const values = selected._new ? [] : roleMembers(selected);
    const state = memberState(selected.id);
    const total = state.total == null ? (memberTotals.get(selected.id) ?? values.length) : state.total;
    const first = values.length ? (Math.max(state.page, 1) - 1) * roleMemberPageSize + 1 : 0;
    const last = first ? first + values.length - 1 : 0;
    $('role-member-count').textContent = state.countUnavailable ? '—' : String(total);
    $('role-members-heading-count').textContent = `${first}–${last} of ${total}`;
    $('role-members-previous').hidden = !state.hasPrevious;
    $('role-members-next').hidden = !state.hasNext;
    const scope = currentScope();
    $('role-members-scope').textContent = `Direct assignments in ${scope.type === 'host' ? 'this server' : `this ${scope.type}`}.`;
    const protectedOwner = protectedOwnerRole(selected), manageable = canManageMembers(selected);
    $('role-member-open').hidden = !manageable;
    $('role-member-protected').hidden = !protectedOwner;
    $('role-member-identifier').disabled = !manageable;
    $('role-member-resolve').disabled = !manageable;
    $('role-member-add').disabled = !manageable;
    if (!values.length) root.append(make('p', 'role-member-empty', selected._new
      ? 'Create this role before adding members.'
      : loadedMemberRoles.has(selected.id) ? 'No direct members yet.' : 'Loading members…'));
    for (const member of values) {
      const person = memberProfileCache.get(member.subject);
      const label = person?.displayName || person?.label || person?.handle || 'Participant';
      const row = make('div', 'role-member'); row.dataset.subject = member.subject;
      row.style.setProperty('--role-color', color(selected.presentation?.color));
      const avatar = make('span', 'role-member-avatar', label.slice(0, 1).toUpperCase()); showAvatar(avatar, person, label); row.append(avatar);
      const copy = make('span'); copy.append(make('strong', '', label), make('small', '', person ? memberByline(person) : 'Loading profile…')); row.append(copy);
      const remove = make('button', 'role-member-remove', '×'); remove.type = 'button'; remove.hidden = !manageable;
      const removeLabel = person ? `Remove ${label} from ${selected.name}` : 'Profile required before removing this membership';
      remove.title = removeLabel;
      remove.setAttribute('aria-label', removeLabel);
      remove.disabled = !person;
      remove.addEventListener('click', () => removeMember(member.subject, remove)); row.append(remove); root.append(row);
    }
    loadMemberProfiles(values.map(member => member.subject), selected.id);
    if (values.length) hydrateMemberRows(selected.id);
  }

  function roleGrants() {
    const known = new Set((descriptor?.capabilities || []).map(capability => capability.key));
    const grants = (selected.grants || []).filter(grant => !known.has(grant.capability));
    for (const capability of draftGrantKeys) {
      const original = (selected.grants || []).find(grant => grant.capability === capability);
      grants.push(original || { capability });
    }
    return grants;
  }

  async function saveRole(button) {
    if (!selected || systemRole(selected)) return;
    const name = $('role-name').value.trim(), purpose = $('role-purpose').value.trim();
    if (!name) { message('role-status', 'Give this role a name.', true); return; }
    button.disabled = true; message('role-status', selected._new ? 'Creating…' : 'Saving…');
    try {
      const body = { name, purpose: purpose || null, grants: roleGrants(), presentation: { ...(selected.presentation || {}), color: $('role-color').value } };
      const result = selected._new
        ? await request(api('roles'), { method: 'POST', body })
        : await request(api('roles/' + encodeURIComponent(selected.id)), { method: 'PUT', body, headers: { 'If-Match': '"' + selected.version + '"' } });
      selected = result.data; dirty = false; roleUrl(selected, 'replace');
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
      ? { _new: true, id: '', name: 'New role', purpose: '', grants: [], presentation: { color: '#a98df0' } }
      : roles.find(role => role.id === selected.id) || selected;
    dirty = false; renderRole(); roleEditorTab(tab); message('role-status', 'Changes reset.');
  }

  async function retireRole(button) {
    if (!selected || selected._new || systemRole(selected)) return;
    button.disabled = true; message('role-status', 'Retiring…');
    try {
      await request(api('roles/' + encodeURIComponent(selected.id) + '/retire'), { method: 'POST', headers: { 'If-Match': '"' + selected.version + '"' } });
      selected = undefined; dirty = false; roleUrl(undefined, 'replace'); await loadScope();
      message('role-list-status', 'Role retired.');
    } catch (error) { message('role-status', error.message, true); }
    finally { button.disabled = false; }
  }

  function clearResolvedMember() {
    pendingMember = undefined;
    $('role-member-resolved').hidden = true;
    $('role-member-resolved-name').textContent = '';
    $('role-member-resolved-byline').textContent = '';
  }

  function closeMemberInvite() {
    $('role-member-invite').hidden = true;
    $('role-member-identifier').value = '';
    message('role-member-lookup-status', '');
    clearResolvedMember();
  }

  async function resolveMember(button) {
    if (!canManageMembers(selected)) return;
    const identifier = $('role-member-identifier').value.trim();
    const normalizedIdentifier = normalizeLookupIdentifier(identifier);
    message('role-member-lookup-status', '');
    if (!normalizedIdentifier) { message('role-member-lookup-status', 'Enter a handle or DID.', true); return; }
    const currentSubjects = new Set(roleMembers(selected).map(member => member.subject));
    const duplicate = [...memberProfileCache.values()].find(person => currentSubjects.has(person.id)
      && [person.id, person.did, person.handle].some(value => normalizeLookupIdentifier(value) === normalizedIdentifier));
    if (duplicate) {
      const duplicateLabel = duplicate.displayName || duplicate.label || duplicate.handle || 'Participant';
      clearResolvedMember(); message('role-member-lookup-status', `${duplicateLabel} already has this role`, true); return;
    }
    button.disabled = true; message('role-member-lookup-status', 'Finding participant…');
    try {
      const person = (await request('/api/roles/ui/resolve?identifier=' + encodeURIComponent(identifier))).data;
      if (roleMembers(selected).some(member => member.subject === person.id)) {
        const existingLabel = person.displayName || person.label || person.handle || 'Participant';
        clearResolvedMember(); message('role-member-lookup-status', existingLabel + ' already has this role', true); return;
      }
      pendingMember = person;
      const label = person.displayName || person.label || person.handle || 'Participant';
      showAvatar($('role-member-resolved-avatar'), person, label);
      $('role-member-resolved-name').textContent = label;
      $('role-member-resolved-byline').textContent = memberByline(person);
      $('role-member-add').textContent = 'Add to role';
      $('role-member-resolved').hidden = false;
      message('role-member-lookup-status', 'Participant found. Confirm the assignment.');
    } catch (error) { clearResolvedMember(); message('role-member-lookup-status', error.message, true); }
    finally { button.disabled = false; }
  }

  async function addMember(button) {
    if (!canManageMembers(selected) || !pendingMember) return;
    const person = pendingMember;
    const personLabel = person.displayName || person.label || person.handle || 'Participant';
    button.disabled = true; message('role-members-status', `Adding ${personLabel}…`);
    try {
      await request(api('roles/' + encodeURIComponent(selected.id) + '/members/' + encodeURIComponent(person.id)), { method: 'PUT' });
      closeMemberInvite();
      await refreshMembers(); roleEditorTab('members');
      message('role-members-status', `${personLabel} now has this role.`);
    } catch (error) { message('role-members-status', error.message, true); }
    finally { button.disabled = false; }
  }

  async function removeMember(subject, button) {
    if (!canManageMembers(selected)) return;
    button.disabled = true; message('role-members-status', 'Removing…');
    try {
      await request(api('roles/' + encodeURIComponent(selected.id) + '/members/' + encodeURIComponent(subject)), { method: 'DELETE' });
      await refreshMembers(selected, true); roleEditorTab('members'); message('role-members-status', 'Member removed from this role.');
    } catch (error) { message('role-members-status', error.message, true); }
    finally { button.disabled = false; }
  }

  function createRole() {
    const role = { _new: true, id: '', name: 'New role', purpose: '', grants: [], presentation: { color: '#a98df0' } };
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
    const initial = new URL(location.href).searchParams.get('tab') === 'roles' ? 'roles' : 'server';
    settingsTab(initial, false);
  }

  document.querySelectorAll('[data-settings-tab]').forEach(button => {
    button.addEventListener('click', () => settingsTab(button.dataset.settingsTab));
    button.addEventListener('keydown', event => {
      if (!['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key)) return;
      event.preventDefault();
      const tabs = [...document.querySelectorAll('[data-settings-tab]')], index = tabs.indexOf(button);
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
  $('role-scope-kind').addEventListener('change', () => changeScope().catch(error => message('role-list-status', error.message, true)));
  $('role-tangent-scope').addEventListener('change', () => ensureTopics(true).then(changeScope).catch(error => message('role-list-status', error.message, true)));
  $('role-topic-scope').addEventListener('change', () => changeScope().catch(error => message('role-list-status', error.message, true)));
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
  $('role-form').addEventListener('submit', event => { event.preventDefault(); saveRole(event.submitter || $('role-save')); });
  $('role-reset').addEventListener('click', resetRole);
  $('role-retire').addEventListener('click', event => retireRole(event.currentTarget));
  $('role-member-open').addEventListener('click', () => { message('role-member-lookup-status', ''); $('role-member-invite').hidden = false; clearResolvedMember(); $('role-member-identifier').focus(); });
  $('role-member-cancel').addEventListener('click', () => { closeMemberInvite(); message('role-members-status', ''); $('role-member-open').focus(); });
  $('role-member-resolve').addEventListener('click', event => resolveMember(event.currentTarget));
  $('role-member-add').addEventListener('click', event => addMember(event.currentTarget));
  $('role-member-identifier').addEventListener('input', () => { message('role-member-lookup-status', ''); clearResolvedMember(); });
  $('role-member-identifier').addEventListener('keydown', event => { if (event.key === 'Enter') { event.preventDefault(); resolveMember($('role-member-resolve')); } });
  $('role-members-previous').addEventListener('click', event => changeMemberPage(-1, event.currentTarget).catch(error => message('role-members-status', error.message, true)));
  $('role-members-next').addEventListener('click', event => changeMemberPage(1, event.currentTarget).catch(error => message('role-members-status', error.message, true)));
  $('role-member-profiles-retry').addEventListener('click', event => retryMemberProfiles(event.currentTarget).catch(error => message('role-members-status', error.message, true)));
  window.addEventListener('tangent:welcome', event => openFor(event.detail));
  window.addEventListener('tangent:route', event => openFor(event.detail));
  window.addEventListener('popstate', syncRoleHistory);
})();
