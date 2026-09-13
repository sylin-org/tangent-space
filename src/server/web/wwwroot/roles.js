'use strict';
(() => {
  const $ = id => document.getElementById(id);
  const tenant = 'tangent-space';
  const groupOrder = ['host', 'tangent', 'topic', 'post', 'other'];
  const groupLabels = { host: 'Server', tangent: 'Tangents', topic: 'Topics', post: 'Posts', other: 'Other' };
  let site, descriptor, roles = [], bindings = [], selected, tangents = [], topics = [], loadVersion = 0, csrf;

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
      if (name === 'roles') url.searchParams.set('tab', 'roles'); else url.searchParams.delete('tab');
      history.replaceState(history.state, '', url.pathname + url.search + url.hash);
    }
    if (name === 'roles') loadScope();
  }

  function roleEditorTab(name) {
    document.querySelectorAll('[data-role-editor-tab]').forEach(button => {
      const active = button.dataset.roleEditorTab === name;
      button.setAttribute('aria-selected', String(active));
      button.tabIndex = active ? 0 : -1;
    });
    $('role-permissions-panel').hidden = name !== 'permissions';
    $('role-members-panel').hidden = name !== 'members';
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
    await loadScope();
  }

  async function loadScope() {
    if (window.TangentPages?.route?.kind !== 'settings' || $('settings-roles-panel').hidden) return;
    const scope = currentScope();
    if (!scope.id) { roles = []; bindings = []; selected = undefined; render(); message('role-list-status', 'There is nothing at this layer yet.'); return; }
    const version = ++loadVersion;
    message('role-list-status', 'Loading roles…');
    message('role-status', ''); message('role-members-status', '');
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
      selected = roles.find(role => role.id === selected?.id) || roles[0];
      render();
      message('role-list-status', roles.length ? '' : 'No roles have been created at this layer.');
    } catch (error) {
      if (version !== loadVersion) return;
      roles = []; bindings = []; selected = undefined; render();
      message('role-list-status', error.message, true);
    }
  }

  function render() { renderList(); renderRole(); }

  function renderList() {
    const list = $('role-list'); list.replaceChildren();
    const term = $('role-search').value.trim().toLocaleLowerCase();
    const values = [...(selected?._new ? [selected] : []), ...roles].filter(role => !term || role.name.toLocaleLowerCase().includes(term));
    for (const role of values) {
      const button = make('button', 'role-list-item'); button.type = 'button'; button.setAttribute('role', 'option');
      button.setAttribute('aria-selected', String(role === selected)); button.style.setProperty('--role-color', color(role.presentation?.color));
      button.append(make('span', 'role-list-dot'));
      const copy = make('span', 'role-list-copy'); copy.append(make('strong', '', role.name), make('small', '', systemRole(role) ? 'Built in' : role._new ? 'Unsaved' : 'Custom role')); button.append(copy);
      button.append(make('span', 'role-list-count', String(bindings.filter(binding => binding.roleId === role.id).length)));
      button.addEventListener('click', () => { selected = role; render(); roleEditorTab('permissions'); });
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
    const available = (descriptor?.capabilities || []).filter(item => (item.scopeTypes || []).includes(scope));
    const granted = new Set((role.grants || []).map(grant => grant.capability));
    for (const group of groupOrder) {
      const values = available.filter(item => capabilityGroup(item.key) === group);
      if (!values.length) continue;
      const section = make('section', 'role-capability-group'); section.append(make('h4', '', groupLabels[group]));
      const list = make('div', 'role-capability-list');
      for (const capability of values) {
        const label = make('label', 'role-capability');
        const copy = make('span'); copy.append(make('strong', '', capability.label || capability.key), make('small', '', capability.description || capability.key));
        const toggle = document.createElement('input'); toggle.type = 'checkbox'; toggle.value = capability.key; toggle.checked = granted.has(capability.key); toggle.disabled = managed;
        label.append(copy, toggle); list.append(label);
      }
      section.append(list); root.append(section);
    }
  }

  function renderRole() {
    $('role-empty').hidden = !!selected; $('role-form').hidden = !selected;
    if (!selected) return;
    const managed = systemRole(selected);
    $('role-editor-title').textContent = selected.name;
    $('role-editor-kicker').textContent = selected._new ? 'NEW ROLE' : currentKind().toUpperCase() + ' ROLE';
    $('role-name').value = selected.name || '';
    $('role-purpose').value = selected.purpose || '';
    $('role-color').value = color(selected.presentation?.color);
    $('role-color-dot').style.setProperty('--role-color', $('role-color').value);
    $('role-managed').hidden = !managed;
    for (const field of [$('role-name'), $('role-purpose'), $('role-color')]) field.disabled = managed;
    $('role-save').hidden = managed;
    $('role-retire').hidden = managed || selected._new;
    renderCapabilities(selected, managed);
    renderMembers();
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
    const values = selected?._new ? [] : bindings.filter(binding => binding.roleId === selected.id);
    $('role-member-count').textContent = String(values.length);
    const managed = systemRole(selected);
    $('role-member-identifier').disabled = managed || selected?._new;
    $('role-member-add').disabled = managed || selected?._new;
    if (!values.length) root.append(make('p', 'hint', selected?._new ? 'Save this role before adding people.' : managed ? 'No direct members at this layer.' : 'Nobody has this role yet.'));
    for (const binding of values) {
      const row = make('div', 'role-member'); row.dataset.subject = binding.subject; row.style.setProperty('--role-color', color(selected.presentation?.color));
      row.append(make('span', 'role-member-avatar', binding.subject.slice(0, 1).toUpperCase()));
      const copy = make('span'); copy.append(make('strong', '', binding.subject), make('small', '', binding.subject)); row.append(copy);
      const remove = make('button', 'btn btn-quiet', 'Remove'); remove.type = 'button'; remove.hidden = managed;
      remove.addEventListener('click', () => removeMember(binding, remove)); row.append(remove); root.append(row);
    }
    loadMemberLabels(values.map(binding => binding.subject).slice(0, 50), selected.id);
  }

  function roleGrants() {
    const toggles = [...document.querySelectorAll('#role-capabilities input[type=checkbox]')];
    const visible = new Set(toggles.map(toggle => toggle.value));
    const checked = new Set(toggles.filter(toggle => toggle.checked).map(toggle => toggle.value));
    const grants = (selected.grants || []).filter(grant => !visible.has(grant.capability) || checked.has(grant.capability));
    for (const capability of checked) if (!grants.some(grant => grant.capability === capability)) grants.push({ capability });
    return grants;
  }

  async function saveRole(button) {
    if (!selected || systemRole(selected)) return;
    const name = $('role-name').value.trim(), purpose = $('role-purpose').value.trim();
    if (!name) { message('role-status', 'Give this role a name.', true); return; }
    button.disabled = true; message('role-status', 'Saving…');
    try {
      const body = { name, purpose: purpose || null, grants: roleGrants(), presentation: { ...(selected.presentation || {}), color: $('role-color').value } };
      const result = selected._new
        ? await request(api('roles'), { method: 'POST', body })
        : await request(api('roles/' + encodeURIComponent(selected.id)), { method: 'PUT', body, headers: { 'If-Match': '"' + selected.version + '"' } });
      selected = result.data; await loadScope(); message('role-status', 'Role saved.');
    } catch (error) { if (error.status === 400 && /antiforgery/i.test(error.message)) csrf = undefined; message('role-status', error.message, true); }
    finally { button.disabled = false; }
  }

  async function retireRole(button) {
    if (!selected || selected._new || systemRole(selected)) return;
    button.disabled = true; message('role-status', 'Retiring…');
    try {
      await request(api('roles/' + encodeURIComponent(selected.id) + '/retire'), { method: 'POST', headers: { 'If-Match': '"' + selected.version + '"' } });
      selected = undefined; await loadScope(); message('role-status', 'Role retired.');
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
      $('role-member-identifier').value = ''; await loadScope(); roleEditorTab('members'); message('role-members-status', (person.label || identifier) + ' now has this role.');
    } catch (error) { message('role-members-status', error.message, true); }
    finally { button.disabled = false; }
  }

  async function removeMember(binding, button) {
    button.disabled = true; message('role-members-status', 'Removing…');
    try {
      await request(api('bindings/' + encodeURIComponent(binding.id)), { method: 'DELETE', headers: { 'If-Match': '"' + binding.version + '"' } });
      await loadScope(); roleEditorTab('members'); message('role-members-status', 'Membership removed.');
    } catch (error) { message('role-members-status', error.message, true); }
    finally { button.disabled = false; }
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
  $('role-tangent-scope').addEventListener('change', () => (currentKind() === 'topic' ? ensureTopics() : Promise.resolve()).then(loadScope).catch(error => message('role-list-status', error.message, true)));
  $('role-topic-scope').addEventListener('change', loadScope);
  $('role-search').addEventListener('input', renderList);
  $('role-create').addEventListener('click', () => {
    selected = { _new: true, id: '', name: 'New role', purpose: '', grants: [], presentation: { color: '#a98df0' } };
    render(); roleEditorTab('permissions'); $('role-name').select();
  });
  $('role-color').addEventListener('input', () => $('role-color-dot').style.setProperty('--role-color', $('role-color').value));
  $('role-form').addEventListener('submit', event => { event.preventDefault(); saveRole(event.submitter || $('role-save')); });
  $('role-retire').addEventListener('click', event => retireRole(event.currentTarget));
  $('role-member-add').addEventListener('click', event => addMember(event.currentTarget));
  $('role-member-identifier').addEventListener('keydown', event => { if (event.key === 'Enter') { event.preventDefault(); addMember($('role-member-add')); } });
  window.addEventListener('tangent:welcome', event => openFor(event.detail));
  window.addEventListener('tangent:route', event => openFor(event.detail));
})();
