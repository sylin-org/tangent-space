'use strict';
(() => {
  const $ = id => document.getElementById(id);
  const tenant = 'tangent-space';
  let site, csrf, descriptor, loadToken = 0, openToken = 0, loadedFor = '';

  const route = () => window.TangentPages?.route || { kind: 'home' };
  const scopeApi = (scope, suffix) => '/api/identity/scoped-roles/' + [tenant, scope.type, scope.id]
    .map(encodeURIComponent).join('/') + '/' + suffix;
  const roleColor = role => /^#[0-9a-f]{6}$/i.test(role?.presentation?.color || '') ? role.presentation.color : '#a98df0';
  const errorMessage = (response, data) => data.message || data.error || data.reason || data.title ||
    (response.status === 403 || response.status === 404 ? 'You cannot change access here.' : 'Access could not be updated.');

  async function session() {
    if (csrf) return csrf;
    const response = await fetch('/api/roles/ui/session', { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } });
    const data = await response.json().catch(() => ({}));
    if (!response.ok || !data.requestToken || !data.headerName) throw new Error(errorMessage(response, data));
    return (csrf = data);
  }

  async function request(path, options = {}, missingIsNull = false) {
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
    if (missingIsNull && response.status === 404) return null;
    if (!response.ok) { const error = new Error(errorMessage(response, data)); error.status = response.status; throw error; }
    return { data, etag: response.headers.get('ETag') };
  }

  function contextFor(current) {
    if (current.kind === 'settings') return {
      kind: 'server', scope: { type: 'host', id: 'site' }, parentScopes: [],
      title: 'Server settings', eyebrow: 'MAKE YOURSELF AT HOME', intro: 'Name your place, shape its roles, and decide who can enter.',
      decisions: [
        ['host.read', 'Enter this server', 'Who may enter this Tangent Space.'],
        ['tangent.create', 'Create Tangents', 'Who may make a new Tangent on this server.'],
        ['host.manage', 'Manage this server', 'Who may change server settings and access.']
      ]
    };
    if (current.kind === 'tangent-settings') return {
      kind: 'tangent', tangent: current.tangent, scope: { type: 'tangent', id: current.tangent }, parentScopes: [{ type: 'host', id: 'site', origin: 'Server' }],
      title: 'Tangent settings', eyebrow: 'SHAPE THIS PLACE', intro: 'Tune this Tangent and choose who finds a place here.',
      decisions: [
        ['tangent.read', 'Enter this Tangent', 'Who can see and open this Tangent.'],
        ['topic.create', 'Create Topics', 'Who can begin a new conversation here.'],
        ['tangent.manage', 'Manage this Tangent', 'Who can change its details and access.']
      ]
    };
    if (current.kind === 'topic-settings') return {
      kind: 'topic', tangent: current.tangent, topic: current.topic, scope: { type: 'topic', id: current.topic },
      parentScopes: [{ type: 'host', id: 'site', origin: 'Server' }, { type: 'tangent', id: current.tangent, origin: 'Tangent' }],
      title: 'Topic settings', eyebrow: 'TEND THIS CONVERSATION', intro: 'Keep the conversation clear, welcoming, and appropriately private.',
      decisions: [
        ['topic.read', 'Read this Topic', 'Who can discover and read this conversation.'],
        ['topic.reply', 'Reply here', 'Who can add Posts to this Topic.'],
        ['topic.manage', 'Manage this Topic', 'Who can change its details and access.']
      ]
    };
    return null;
  }

  const contextSignature = context => context ? context.scope.type + ':' + context.scope.id + ':' + (context.tangent || '') : '';

  function kindValue(value) {
    if (typeof value === 'number') return value;
    return { Anonymous: 0, Authenticated: 1, Subject: 2, Role: 3 }[value] ?? -1;
  }

  function selectedMode(policy) {
    if (!policy || policy.data.mode === 'Inherit' || policy.data.mode === 0) return 'inherit';
    const clauses = policy.data.audience || [];
    const kinds = new Set(clauses.map(clause => kindValue(clause.kind)));
    if (kinds.has(3)) return 'roles';
    if (kinds.has(0) && kinds.has(1)) return 'everyone';
    if (kinds.has(1)) return 'signed-in';
    return 'roles';
  }

  function activate(name) {
    if (name === 'access') load();
  }

  function tab(name) {
    const button = document.querySelector('[data-settings-tab="' + name + '"]');
    button?.click();
  }

  function configureShell(context, resource) {
    $('settings-title').textContent = context.title;
    $('settings-eyebrow').textContent = context.eyebrow;
    $('settings-intro').textContent = context.intro;
    const crumbs = $('settings-breadcrumbs'); crumbs.replaceChildren();
    const home = document.createElement('a'); home.href = '/'; home.textContent = site?.server?.name || site?.name || 'Home'; crumbs.append(home);
    if (context.kind !== 'server') {
      const tangent = document.createElement(context.kind === 'topic' ? 'a' : 'span');
      tangent.textContent = resource.tangent?.name || resource.name || route().tangent;
      if (context.kind === 'topic') tangent.href = window.TangentPages.tangentUrl(context.tangent);
      crumbs.append(tangent);
    }
    const current = document.createElement('span'); current.textContent = context.title; current.setAttribute('aria-current', 'page'); crumbs.append(current);
    $('settings-context-label').textContent = context.kind === 'topic' ? 'Topic' : 'Tangent';
    $('settings-context-kicker').textContent = context.kind.toUpperCase();
    $('settings-context-name').textContent = resource.name;
    $('settings-context-copy').textContent = resource.description || (context.kind === 'topic' ? 'A shared conversation.' : 'A place for conversation.');
    renderContextEditor(context, resource);
    $('settings-server-tab').hidden = context.kind !== 'server';
    $('settings-roles-tab').hidden = context.kind !== 'server';
    $('settings-context-tab').hidden = context.kind === 'server';
    $('settings-access-tab').hidden = false;
    $('settings-shell').hidden = false;
    $('settings-denied').hidden = true;
    $('access-scope-badge').textContent = context.kind === 'server' ? 'Server' : (resource.name + ' · ' + (context.kind === 'topic' ? 'Topic' : 'Tangent'));
    $('access-intro').textContent = context.kind === 'topic'
      ? 'This Topic inherits its Tangent until you make a local choice.'
      : context.kind === 'tangent' ? 'Use server roles here; changes affect only this Tangent and its inherited Topics.'
      : 'Set server-wide defaults. Tangents and Topics can inherit or narrow them.';
  }

  function field(label, name, value = '', options = {}) {
    const wrapper = document.createElement('label');
    wrapper.textContent = label;
    const control = document.createElement(options.rows ? 'textarea' : 'input');
    control.className = 'input'; control.name = name;
    if (options.type) control.type = options.type;
    if (options.rows) control.rows = options.rows;
    if (options.maxLength) control.maxLength = options.maxLength;
    if (options.required) control.required = true;
    if (control.type === 'checkbox') {
      control.checked = value === true;
      wrapper.className = 'check-row'; wrapper.replaceChildren(control, document.createTextNode(' ' + label));
    } else control.value = value || '';
    return wrapper;
  }

  function renderContextEditor(context, resource) {
    const host = $('settings-context-editor');
    host.replaceChildren();
    if (context.kind === 'server') return;
    const form = document.createElement('form'); form.className = 'settings-context-form stack';
    const fields = document.createElement('div'); fields.className = 'settings-context-fields';
    if (context.kind === 'tangent') {
      fields.append(
        field('Tangent name', 'name', resource.tangent.name, { maxLength: 120, required: true }),
        field('Description', 'description', resource.tangent.description, { rows: 3, maxLength: 500 }),
        field('Motto', 'motto', resource.tangent.motto, { maxLength: 240 }),
        field('Accent color', 'accent', resource.tangent.accent, { type: 'color' }),
        field('Artwork URL', 'artwork', resource.tangent.artwork, { maxLength: 2048 }),
        field('Allow members to create Topics', 'allowMemberTopics', resource.tangent.allowMemberTopics, { type: 'checkbox' })
      );
    } else {
      fields.append(
        field('Topic title', 'title', resource.topic.title, { maxLength: 120, required: true }),
        field('Topic description', 'topic', resource.topic.topic, { rows: 5, maxLength: 2000 }),
        field('Allow authors to edit their Posts', 'allowPostEditing', resource.topic.allowPostEditing, { type: 'checkbox' }),
        field('Lock this Topic', 'isLocked', resource.topic.isLocked, { type: 'checkbox' })
      );
    }
    const actions = document.createElement('div'); actions.className = 'settings-context-actions';
    const save = document.createElement('button'); save.className = 'btn btn-primary'; save.textContent = 'Save details';
    const open = document.createElement('a'); open.className = 'btn btn-quiet'; open.href = resource.open; open.textContent = 'Open';
    const status = document.createElement('p'); status.className = 'hint'; status.setAttribute('role', 'status');
    actions.append(save, open, status); form.append(fields, actions); host.append(form);
    form.addEventListener('submit', async event => {
      event.preventDefault(); save.disabled = true; status.textContent = 'Saving…'; status.classList.remove('error');
      try {
        const values = new FormData(form);
        const body = context.kind === 'tangent' ? {
          name: String(values.get('name') || '').trim(), description: String(values.get('description') || '').trim(),
          motto: String(values.get('motto') || '').trim(), accent: String(values.get('accent') || ''),
          artwork: String(values.get('artwork') || '').trim(), allowMemberTopics: form.elements.allowMemberTopics.checked
        } : {
          title: String(values.get('title') || '').trim(), topic: String(values.get('topic') || '').trim(),
          allowPostEditing: form.elements.allowPostEditing.checked, isLocked: form.elements.isLocked.checked
        };
        const path = context.kind === 'tangent'
          ? '/api/v1/tangents/' + encodeURIComponent(context.tangent)
          : '/api/v1/tangents/' + encodeURIComponent(context.tangent) + '/topics/' + encodeURIComponent(context.topic) + '/settings';
        await request(path, { method: 'PATCH', body });
        if (contextSignature(context) !== contextSignature(contextFor(route()))) return;
        resource.name = body.name || body.title; resource.description = body.description ?? body.topic;
        if (context.kind === 'tangent') Object.assign(resource.tangent, body); else Object.assign(resource.topic, body);
        $('settings-context-name').textContent = resource.name;
        $('settings-context-copy').textContent = resource.description || (context.kind === 'topic' ? 'A shared conversation.' : 'A place for conversation.');
        status.textContent = (context.kind === 'topic' ? 'Topic' : 'Tangent') + ' details saved.';
      } catch (error) { status.textContent = error.message; status.classList.add('error'); }
      finally { save.disabled = false; }
    });
  }

  async function resourceFor(context) {
    if (context.kind === 'server') return {
      name: site?.server?.name || site?.name || 'Tangent Space', description: site?.server?.welcomeMessage || '', open: '/', canManage: site?.participant?.isOwner === true
    };
    const tangentResult = await request('/api/v1/tangents/' + encodeURIComponent(context.tangent));
    const tangent = tangentResult.data;
    if (context.kind === 'tangent') return {
      tangent, name: tangent.name || tangent.key, description: tangent.description, open: window.TangentPages.tangentUrl(tangent.key),
      canManage: tangent.canManage === true || tangent.isOwner === true
    };
    const topicResult = await request('/api/v1/tangents/' + encodeURIComponent(context.tangent) + '/topics/' + encodeURIComponent(context.topic));
    const topic = topicResult.data;
    if (topic.tangentKey !== context.tangent) throw new Error('This Topic does not belong to this Tangent.');
    return {
      tangent, topic, name: topic.title || topic.key, description: topic.topic,
      open: window.TangentPages.topicUrl(topic.tangentKey, topic.key), canManage: topic.canManage === true
    };
  }

  async function rolesFor(context) {
    const scopes = [...context.parentScopes, { ...context.scope, origin: context.kind === 'server' ? 'Server' : context.kind === 'tangent' ? 'Tangent' : 'Topic' }];
    const pages = await Promise.all(scopes.map(async scope => {
      try { return { scope, page: (await request(scopeApi(scope, 'roles?page=1&pageSize=100'))).data }; }
      catch (error) { if (error.status === 403 || error.status === 404) return { scope, page: { items: [], totalCount: 0 } }; throw error; }
    }));
    return pages.flatMap(({ scope, page }) => (page.items || []).filter(role => role.status === 'Active' || role.status === 0)
      .map(role => ({ ...role, origin: scope.origin, truncated: page.totalCount > 100 })));
  }

  function radio(name, value, label, checked, description) {
    const row = document.createElement('label'); row.className = 'access-choice';
    const input = document.createElement('input'); input.type = 'radio'; input.name = name; input.value = value; input.checked = checked;
    const copy = document.createElement('span'); const strong = document.createElement('strong'); strong.textContent = label;
    const small = document.createElement('small'); small.textContent = description; copy.append(strong, small); row.append(input, copy); return row;
  }

  function renderDecision(context, definition, policy, roles, capability) {
    const [key, label, description] = definition;
    const mode = selectedMode(policy);
    const card = document.createElement('form'); card.className = 'access-decision'; card.dataset.capability = key;
    const heading = document.createElement('header'); const copy = document.createElement('div');
    const title = document.createElement('h3'); title.textContent = label; const note = document.createElement('p'); note.textContent = description; copy.append(title, note);
    const state = document.createElement('span'); state.className = 'access-state'; state.textContent = mode === 'inherit' ? (context.kind === 'server' ? 'Server default' : 'Inherited') : 'Customized here'; heading.append(copy, state);
    const choices = document.createElement('fieldset'); const legend = document.createElement('legend'); legend.textContent = 'Audience'; legend.className = 'visually-hidden'; choices.append(legend);
    const name = 'audience-' + key;
    choices.append(radio(name, 'inherit', context.kind === 'server' ? 'Role permissions' : 'Inherit', 'inherit' === mode,
      context.kind === 'server' ? 'Use the permissions granted by server roles.' : context.kind === 'topic' ? 'Follow this Tangent’s rule.' : 'Follow the server rule.'));
    if (capability?.allowsAnonymous) choices.append(radio(name, 'everyone', 'Everyone', 'everyone' === mode, 'People may enter without signing in.'));
    choices.append(radio(name, 'signed-in', 'Signed-in participants', 'signed-in' === mode, 'Any participant with an account may enter.'));
    choices.append(radio(name, 'roles', 'Selected roles', 'roles' === mode, 'Only people in one of the roles below.'));
    const roleBox = document.createElement('div'); roleBox.className = 'access-role-picker';
    const selected = new Set((policy?.data.audience || []).filter(clause => kindValue(clause.kind) === 3).map(clause => clause.value));
    const visibleRoleIds = new Set(roles.map(role => role.id));
    const unshownSelected = [...selected].filter(roleId => !visibleRoleIds.has(roleId));
    for (const role of roles) {
      const item = document.createElement('label'); item.className = 'access-role';
      const input = document.createElement('input'); input.type = 'checkbox'; input.value = role.id; input.checked = selected.has(role.id);
      const dot = document.createElement('i'); dot.style.setProperty('--role-color', roleColor(role));
      const text = document.createElement('span'); text.textContent = role.name; const origin = document.createElement('small'); origin.textContent = role.origin; item.append(input, dot, text, origin); roleBox.append(item);
    }
    if (!roles.length) { const empty = document.createElement('p'); empty.className = 'hint'; empty.textContent = 'No manageable roles are available at this scope.'; roleBox.append(empty); }
    if (unshownSelected.length) { const note = document.createElement('p'); note.className = 'access-preserved hint'; note.textContent = unshownSelected.length + ' selected role' + (unshownSelected.length === 1 ? ' is' : 's are') + ' outside this bounded list and will be preserved.'; roleBox.append(note); }
    const actions = document.createElement('div'); actions.className = 'access-actions'; const save = document.createElement('button'); save.className = 'btn btn-primary'; save.textContent = 'Save access';
    const status = document.createElement('p'); status.className = 'hint'; status.setAttribute('role', 'status'); actions.append(save, status);
    const syncPicker = () => { roleBox.hidden = choices.querySelector('input:checked')?.value !== 'roles'; };
    choices.addEventListener('change', syncPicker); syncPicker();
    card.append(heading, choices, roleBox, actions);
    card.addEventListener('submit', async event => {
      event.preventDefault(); save.disabled = true; status.textContent = 'Saving…'; status.classList.remove('error');
      try {
        const chosen = card.elements[name].value;
        if (chosen === 'inherit') {
          if (policy) policy = await request(scopeApi(context.scope, 'policies/' + encodeURIComponent(key)), { method: 'DELETE', headers: { 'If-Match': policy.etag } });
        } else {
          const audience = chosen === 'everyone' ? [{ kind: 0 }, { kind: 1 }]
            : chosen === 'signed-in' ? [{ kind: 1 }]
            : [...new Set([...roleBox.querySelectorAll('input:checked')].map(input => input.value).concat(unshownSelected))].map(value => ({ kind: 3, value }));
          if (chosen === 'roles' && !audience.length) throw new Error('Choose at least one role.');
          const headers = policy?.etag ? { 'If-Match': policy.etag } : {};
          policy = await request(scopeApi(context.scope, 'policies/' + encodeURIComponent(key)), { method: 'PUT', headers, body: { audience } });
        }
        state.textContent = chosen === 'inherit' ? (context.kind === 'server' ? 'Server default' : 'Inherited') : 'Customized here';
        status.textContent = label + ' saved.'; $('access-status').textContent = label + ' saved.';
      } catch (error) { status.textContent = error.message; status.classList.add('error'); }
      finally { save.disabled = false; }
    });
    return card;
  }

  async function load(force = false) {
    const context = contextFor(route());
    if (!context || $('settings-access-panel').hidden) return;
    const signature = contextSignature(context);
    if (!force && loadedFor === signature) return;
    const token = ++loadToken; $('access-loading').hidden = false; $('access-loading').textContent = 'Loading access rules…'; $('access-decisions').replaceChildren();
    try {
      descriptor ||= (await request('/api/identity/scoped-roles/descriptor')).data;
      const [resource, roles] = await Promise.all([resourceFor(context), rolesFor(context)]);
      if (token !== loadToken || signature !== contextSignature(contextFor(route()))) return;
      if (!resource.canManage) throw new Error('Your current role cannot change access here.');
      const capabilities = new Map((descriptor.capabilities || []).map(value => [value.key, value]));
      const policies = await Promise.all(context.decisions.map(definition => request(scopeApi(context.scope, 'policies/' + encodeURIComponent(definition[0])), {}, true)));
      if (token !== loadToken || signature !== contextSignature(contextFor(route()))) return;
      context.decisions.forEach((definition, index) => $('access-decisions').append(renderDecision(context, definition, policies[index], roles, capabilities.get(definition[0]))));
      $('access-loading').hidden = true; $('access-status').textContent = roles.some(role => role.truncated) ? 'Showing the first 100 roles from a scope. Narrower role search is coming.' : '';
      loadedFor = signature;
    } catch (error) {
      if (token === loadToken && signature === contextSignature(contextFor(route()))) showFailure(error);
    }
  }

  function showFailure(error) {
    $('access-loading').hidden = false; $('access-loading').textContent = error.message || 'Access settings could not be loaded.';
    $('access-loading').classList.add('error');
  }

  async function openFor(nextSite) {
    const token = ++openToken; loadToken++;
    site = nextSite; csrf = undefined; loadedFor = '';
    const context = contextFor(route()); if (!context) return;
    const signature = contextSignature(context);
    if (!site?.participant) { $('settings-shell').hidden = true; return; }
    try {
      const resource = await resourceFor(context);
      if (token !== openToken || signature !== contextSignature(contextFor(route()))) return;
      if (!resource.canManage) throw new Error('Your current role cannot change settings here.');
      configureShell(context, resource);
      const requested = new URL(location.href).searchParams.get('tab');
      tab(requested === 'access' ? 'access' : context.kind === 'server' && requested === 'roles' ? 'roles' : context.kind === 'server' ? 'server' : 'context');
      document.title = context.title + ' · ' + resource.name + ' · Tangent Space';
    } catch (error) {
      if (token !== openToken || signature !== contextSignature(contextFor(route()))) return;
      $('settings-shell').hidden = true; $('settings-denied').hidden = false; $('settings-denied').textContent = error.message || 'These settings are not available.';
    }
  }

  window.TangentAccess = { activate };
  window.addEventListener('tangent:welcome', event => openFor(event.detail));
  window.addEventListener('tangent:route', event => openFor(event.detail));
})();
