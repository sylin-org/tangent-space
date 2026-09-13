'use strict';
(() => {
  const $ = id => document.getElementById(id);
  const tenant = 'tangent-space';
  const groupOrder = ['host', 'tangent', 'topic', 'post', 'other'];
  const groupLabels = { host: 'Server', tangent: 'Tangents', topic: 'Topics', post: 'Posts', other: 'Other' };
  let site, descriptor, roles = [], bindings = [], selected, tangents = [], topics = [], loadVersion = 0, csrf;
  let roleTotal = 0, bindingTotal = 0, editorTab = 'appearance', dirty = false;
  let draftGrantKeys = new Set();

  const color = value => /^#[0-9a-f]{6}$/i.test(value || '') ? value : '#a98df0';
  const systemRole = role => role?.presentation?.system === 'true';
  const activeBinding = binding => binding && binding.revoked !== true;
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
  const roleBindings = role => bindings.filter(binding => binding.roleId === role.id);
  const memberCount = role => (bindingTotal > bindings.length ? '≥' : '') + roleBindings(role).length;

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

  async function ensureTopics() {
    const tangent = $('role-tangent-scope').value;
    if (!tangent) topics = [];
    else {
      const listing = (await request('/api/v1/tangents/' + encodeURIComponent(tangent) + '/topics')).data;
      topics = listing.rooms || listing.channels || [];
    }
    const select = $('role-topic-scope'); select.replaceChildren();
    for (const topic of topics) option(select, topic.key, topic.title || topic.key);
  }

  async function changeScope() {
    const kind = currentKind();
    $('role-tangent-field').hidden = kind === 'host';
    $('role-topic-field').hidden = kind !== 'topic';
    if (kind !== 'host') await ensureTangents();
    if (kind === 'topic') await ensureTopics();
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
      roles = []; bindings = []; roleTotal = bindingTotal = 0; selected = undefined;
      showOverview(false); message('role-list-status', 'There is nothing at this scope yet.'); return;
    }
    const version = ++loadVersion;
    message('role-list-status', 'Loading roles…'); message('role-status', ''); message('role-members-status', '');
    try {
      const [description, rolePage, bindingPage] = await Promise.all([
        descriptor ? Promise.resolve({ data: descriptor }) : request('/api/identity/scoped-roles/descriptor'),
        request(api('roles?pageSize=100')),
        request(api('bindings?pageSize=100'))
      ]);
      if (version !== loadVersion) return;
      descriptor = description.data;
      roles = (rolePage.data.items || []).filter(role => role.status !== 'Retired' && role.status !== 2);
      bindings = (bindingPage.data.items || []).filter(activeBinding);
      roleTotal = rolePage.data.totalCount ?? roles.length;
      bindingTotal = bindingPage.data.totalCount ?? bindings.length;
      const requested = new URL(location.href).searchParams.get('role');
      const target = requested === 'new' && selected?._new ? selected : roles.find(role => role.id === requested);
      if (target) showEditor(target, null, selected?._new ? editorTab : 'appearance');
      else {
        if (requested) roleUrl(undefined);
        showOverview(false);
      }
      const truncated = roleTotal > roles.length || bindingTotal > bindings.length;
      message('role-list-status', roles.length ? (truncated ? 'Showing the first bounded page. Counts marked ≥ may be higher.' : '') : 'No roles have been created at this scope.');
    } catch (error) {
      if (version !== loadVersion) return;
      roles = []; bindings = []; roleTotal = bindingTotal = 0; selected = undefined; showOverview(false);
      message('role-list-status', error.message, true);
    }
  }

  async function refreshBindings() {
    const page = (await request(api('bindings?pageSize=100'))).data;
    bindings = (page.items || []).filter(activeBinding);
    bindingTotal = page.totalCount ?? bindings.length;
    renderOverview(); renderSiblingList(); renderMembers();
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
    copy.append(line, make('small', 'role-directory-purpose', role.purpose || (systemRole(role) ? 'Managed by Tangent Space' : 'Custom role')));
    return copy;
  }

  function renderOverview() {
    const list = $('role-directory-list'); list.replaceChildren();
    const term = $('role-search').value.trim().toLocaleLowerCase();
    const values = roles.filter(role => !term || `${role.name} ${role.purpose || ''}`.toLocaleLowerCase().includes(term));
    $('role-overview-heading').textContent = `Roles — ${roleTotal || roles.length}`;
    for (const role of values) {
      const button = make('button', 'role-directory-row'); button.type = 'button'; button.setAttribute('role', 'option');
      button.style.setProperty('--role-color', color(role.presentation?.color));
      const identity = make('span', 'role-directory-identity'); identity.append(make('span', 'role-list-dot'), roleCopy(role));
      const count = make('span', 'role-directory-members', memberCount(role)); count.setAttribute('aria-label', `${memberCount(role)} direct members`);
      button.append(identity, count, make('span', 'role-directory-chevron', '›'));
      button.addEventListener('click', () => showEditor(role)); list.append(button);
    }
    if (!values.length && roles.length) list.append(make('p', 'role-directory-empty', 'No roles match that search.'));
  }

  function renderSiblingList() {
    const list = $('role-list'); list.replaceChildren();
    const values = [...(selected?._new ? [selected] : []), ...roles];
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
    $('role-color-dot').style.setProperty('--role-color', roleColor);
    $('role-preview-dot').style.setProperty('--role-color', roleColor);
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
    for (const field of [$('role-name'), $('role-purpose'), $('role-color')]) field.disabled = managed;
    $('role-save').textContent = selected._new ? 'Create role' : 'Save changes';
    $('role-reset').hidden = managed;
    $('role-retire').hidden = managed || selected._new;
    $('role-member-open').hidden = managed || selected._new;
    $('role-member-invite').hidden = true;
    updateRolePreview(); renderCapabilities(selected, managed); renderMembers(); renderSiblingList();
  }

  async function loadMemberLabels(ids, roleId) {
    if (!ids.length) return;
    try {
      const query = ids.map(id => 'id=' + encodeURIComponent(id)).join('&');
      const rows = (await request('/api/roles/ui/participants?' + query)).data || [];
      if (selected?.id !== roleId) return;
      const labels = new Map(rows.map(row => [row.id, row.label]));
      document.querySelectorAll('#role-members [data-subject]').forEach(node => {
        const label = labels.get(node.dataset.subject); if (!label) return;
        node.querySelector('strong').textContent = label;
        node.querySelector('.role-member-avatar').textContent = label.slice(0, 1).toUpperCase();
      });
    } catch (_) { /* Stable subject IDs remain an honest fallback. */ }
  }

  function renderMembers() {
    const root = $('role-members'); root.replaceChildren();
    if (!selected) return;
    const values = selected._new ? [] : roleBindings(selected);
    $('role-member-count').textContent = memberCount(selected);
    const managed = systemRole(selected);
    $('role-member-identifier').disabled = managed || selected._new;
    $('role-member-add').disabled = managed || selected._new;
    if (!values.length) root.append(make('p', 'hint', selected._new ? 'Create this role before adding members.' : 'Nobody has this role directly in this scope.'));
    for (const binding of values) {
      const row = make('div', 'role-member'); row.dataset.subject = binding.subject;
      row.style.setProperty('--role-color', color(selected.presentation?.color));
      row.append(make('span', 'role-member-avatar', binding.subject.slice(0, 1).toUpperCase()));
      const copy = make('span'); copy.append(make('strong', '', binding.subject), make('small', '', binding.subject)); row.append(copy);
      const remove = make('button', 'btn btn-quiet', 'Remove'); remove.type = 'button'; remove.hidden = managed;
      remove.addEventListener('click', () => removeMember(binding, remove)); row.append(remove); root.append(row);
    }
    loadMemberLabels(values.map(binding => binding.subject).slice(0, 50), selected.id);
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

  async function addMember(button) {
    if (!selected || selected._new || systemRole(selected)) return;
    const identifier = $('role-member-identifier').value.trim();
    if (!identifier) { message('role-members-status', 'Enter a handle or DID.', true); return; }
    button.disabled = true; message('role-members-status', 'Finding participant…');
    try {
      const person = (await request('/api/roles/ui/resolve?identifier=' + encodeURIComponent(identifier))).data;
      await request(api('bindings'), { method: 'POST', body: { subject: person.id, roleId: selected.id, propagation: 0 } });
      $('role-member-identifier').value = ''; $('role-member-invite').hidden = true;
      await refreshBindings(); roleEditorTab('members');
      message('role-members-status', (person.label || identifier) + ' now has this role.');
    } catch (error) { message('role-members-status', error.message, true); }
    finally { button.disabled = false; }
  }

  async function removeMember(binding, button) {
    button.disabled = true; message('role-members-status', 'Removing…');
    try {
      await request(api('bindings/' + encodeURIComponent(binding.id)), { method: 'DELETE', headers: { 'If-Match': '"' + binding.version + '"' } });
      await refreshBindings(); roleEditorTab('members'); message('role-members-status', 'Membership removed.');
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
  $('role-scope-kind').addEventListener('change', () => changeScope().catch(error => message('role-list-status', error.message, true)));
  $('role-tangent-scope').addEventListener('change', () => (currentKind() === 'topic' ? ensureTopics() : Promise.resolve()).then(changeScope).catch(error => message('role-list-status', error.message, true)));
  $('role-topic-scope').addEventListener('change', () => changeScope().catch(error => message('role-list-status', error.message, true)));
  $('role-search').addEventListener('input', renderOverview);
  $('role-permission-search').addEventListener('input', () => selected && renderCapabilities(selected, systemRole(selected)));
  $('role-create').addEventListener('click', createRole);
  $('role-back').addEventListener('click', leaveEditor);
  for (const field of [$('role-name'), $('role-purpose'), $('role-color')]) field.addEventListener('input', () => {
    dirty = true; updateRolePreview(); message('role-status', 'Unsaved changes');
  });
  $('role-form').addEventListener('submit', event => { event.preventDefault(); saveRole(event.submitter || $('role-save')); });
  $('role-reset').addEventListener('click', resetRole);
  $('role-retire').addEventListener('click', event => retireRole(event.currentTarget));
  $('role-member-open').addEventListener('click', () => { $('role-member-invite').hidden = false; $('role-member-identifier').focus(); });
  $('role-member-cancel').addEventListener('click', () => { $('role-member-invite').hidden = true; $('role-member-identifier').value = ''; message('role-members-status', ''); });
  $('role-member-add').addEventListener('click', event => addMember(event.currentTarget));
  $('role-member-identifier').addEventListener('keydown', event => { if (event.key === 'Enter') { event.preventDefault(); addMember($('role-member-add')); } });
  window.addEventListener('tangent:welcome', event => openFor(event.detail));
  window.addEventListener('tangent:route', event => openFor(event.detail));
  window.addEventListener('popstate', syncRoleHistory);
})();
