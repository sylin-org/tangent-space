'use strict';
(() => {
  const $ = id => document.getElementById(id);
  let site, csrf, loadToken = 0, openToken = 0, loadedFor = '';
  const route = () => window.TangentPages?.route || { kind: 'home' };
  const errorMessage = (response, data) => data.message || data.error || data.reason || data.title ||
    (response.status === 403 || response.status === 404 ? 'You cannot change access here.' : 'This change could not be saved.');

  async function session() {
    if (csrf) return csrf;
    const response = await fetch('/api/roles/ui/session', { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } });
    const data = await response.json().catch(() => ({}));
    if (!response.ok || !data.requestToken || !data.headerName) throw new Error(errorMessage(response, data));
    return (csrf = data);
  }

  async function request(path, options = {}) {
    const method = options.method || 'GET';
    const headers = { Accept: 'application/json', ...(options.headers || {}) };
    if (!['GET', 'HEAD'].includes(method)) {
      const token = await session(); headers[token.headerName] = token.requestToken; headers['Content-Type'] = 'application/json';
    }
    const response = await fetch(path, { method, credentials: 'same-origin', cache: 'no-store', headers,
      body: options.body === undefined ? undefined : JSON.stringify(options.body) });
    const data = await response.json().catch(() => ({}));
    if (!response.ok) { const error = new Error(errorMessage(response, data)); error.status = response.status; throw error; }
    return data;
  }

  function contextFor(current) {
    if (current.kind === 'settings') return {
      kind: 'server', title: 'Server settings', eyebrow: 'MAKE YOURSELF AT HOME', intro: 'Name your place, shape its roles, and decide who can enter.', accessPath: '/api/server/access',
      decisions: [
        ['see', 'Who can enter', 'Choose who can discover and open this server.', 'Owner and roles with See every server can always enter.'],
        ['createTangents', 'Who can create Tangents', 'Choose who may start a new Tangent here.', 'Owner and roles with Create Tangents anywhere can always create one.'],
        ['manage', 'Who can manage', 'Choose who may change server settings and access.', 'The Owner can always manage this server.']
      ]
    };
    if (current.kind === 'tangent-settings') return {
      kind: 'tangent', tangent: current.tangent, title: 'Tangent settings', eyebrow: 'SHAPE THIS PLACE', intro: 'Tune this Tangent and choose who finds a place here.',
      accessPath: '/api/v1/tangents/' + encodeURIComponent(current.tangent) + '/access',
      decisions: [
        ['see', 'Who can see', 'Choose who can discover and open this Tangent.', 'Owner, Administrators, and roles with See every Tangent can always see it.'],
        ['post', 'Who can post', 'Set the default for posting in Topics here.', 'Owner, Administrators, and roles with Post anywhere can always post.'],
        ['createTopics', 'Who can create Topics', 'Choose who can begin a new conversation here.', 'Owner, Administrators, and roles with Create Topics anywhere can always create one.'],
        ['manage', 'Who can manage', 'Choose who can change this Tangent’s details and access.', 'Owner, Administrators, and roles with Manage every Tangent can always manage it.']
      ]
    };
    if (current.kind === 'topic-settings') return {
      kind: 'topic', tangent: current.tangent, topic: current.topic, title: 'Topic settings', eyebrow: 'TEND THIS CONVERSATION', intro: 'Keep the conversation clear, welcoming, and appropriately private.',
      accessPath: '/api/v1/tangents/' + encodeURIComponent(current.tangent) + '/topics/' + encodeURIComponent(current.topic) + '/access',
      decisions: [
        ['see', 'Who can see', 'Choose who can discover and read this Topic.', 'Owner, Administrators, and roles with See every Topic can always see it.'],
        ['post', 'Who can post', 'Choose who can add Posts to this Topic.', 'Owner, Administrators, and roles with Post anywhere can always post.'],
        ['manage', 'Who can manage', 'Choose who can change this Topic’s details and access.', 'Owner, Administrators, and roles with Manage every Topic can always manage it.']
      ]
    };
    return null;
  }

  const contextSignature = context => context ? [context.kind, context.tangent, context.topic].filter(Boolean).join(':') : '';
  function activate(name) { if (name === 'access') load(); }
  function tab(name) { document.querySelector('[data-settings-tab="' + name + '"]')?.click(); }

  function field(label, name, value = '', options = {}) {
    const wrapper = document.createElement('label'); wrapper.className = 'settings-field';
    const caption = document.createElement('span'); caption.textContent = label;
    const control = document.createElement(options.rows ? 'textarea' : 'input');
    control.className = 'input settings-control'; control.name = name;
    if (options.type) control.type = options.type;
    if (options.rows) control.rows = options.rows;
    if (options.maxLength) control.maxLength = options.maxLength;
    if (options.required) control.required = true;
    if (control.type === 'checkbox') { control.checked = value === true; wrapper.className = 'check-row settings-check'; wrapper.append(control, caption); }
    else { control.value = value || ''; wrapper.append(caption, control); }
    return wrapper;
  }

  function renderContextEditor(context, resource) {
    const host = $('settings-context-editor'); host.replaceChildren(); if (context.kind === 'server') return;
    const form = document.createElement('form'); form.className = 'settings-context-form stack';
    const fields = document.createElement('div'); fields.className = 'settings-context-fields';
    if (context.kind === 'tangent') fields.append(
      field('Tangent name', 'name', resource.tangent.name, { maxLength: 120, required: true }),
      field('Description', 'description', resource.tangent.description, { rows: 4, maxLength: 1000 }),
      field('Motto', 'motto', resource.tangent.motto, { maxLength: 240 }),
      field('Accent color', 'accent', resource.tangent.accent || '#f4b942', { type: 'color' }),
      field('Artwork URL', 'artwork', resource.tangent.artwork, { maxLength: 2048 })
    );
    else fields.append(
      field('Topic title', 'title', resource.topic.title, { maxLength: 120, required: true }),
      field('Topic description', 'topic', resource.topic.topic, { rows: 5, maxLength: 2000 }),
      field('Allow authors to edit their Posts', 'allowPostEditing', resource.topic.allowPostEditing, { type: 'checkbox' }),
      field('Lock this Topic', 'isLocked', resource.topic.isLocked, { type: 'checkbox' })
    );
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
          motto: String(values.get('motto') || '').trim(), accent: String(values.get('accent') || ''), artwork: String(values.get('artwork') || '').trim()
        } : { title: String(values.get('title') || '').trim(), topic: String(values.get('topic') || '').trim(),
          allowPostEditing: form.elements.allowPostEditing.checked, isLocked: form.elements.isLocked.checked };
        const path = context.kind === 'tangent' ? '/api/v1/tangents/' + encodeURIComponent(context.tangent)
          : '/api/v1/tangents/' + encodeURIComponent(context.tangent) + '/topics/' + encodeURIComponent(context.topic) + '/settings';
        await request(path, { method: 'PATCH', body });
        if (contextSignature(context) !== contextSignature(contextFor(route()))) return;
        resource.name = body.name || body.title; resource.description = body.description ?? body.topic;
        Object.assign(context.kind === 'tangent' ? resource.tangent : resource.topic, body);
        $('settings-context-name').textContent = resource.name; $('settings-context-copy').textContent = resource.description || (context.kind === 'topic' ? 'A shared conversation.' : 'A place for conversation.');
        status.textContent = (context.kind === 'topic' ? 'Topic' : 'Tangent') + ' details saved.';
      } catch (error) { status.textContent = error.message; status.classList.add('error'); }
      finally { save.disabled = false; }
    });
  }

  async function resourceFor(context) {
    if (context.kind === 'server') return { name: site?.server?.name || site?.name || 'Tangent Space', description: site?.server?.welcomeMessage || '', open: '/', canManage: site?.participant?.isOwner === true };
    const tangent = await request('/api/v1/tangents/' + encodeURIComponent(context.tangent));
    if (context.kind === 'tangent') return { tangent, name: tangent.name || tangent.key, description: tangent.description,
      open: window.TangentPages.tangentUrl(tangent.key), canManage: tangent.canManage === true || tangent.isOwner === true };
    const topic = await request('/api/v1/tangents/' + encodeURIComponent(context.tangent) + '/topics/' + encodeURIComponent(context.topic));
    if (topic.tangentKey !== context.tangent) throw new Error('This Topic does not belong to this Tangent.');
    return { tangent, topic, name: topic.title || topic.key, description: topic.topic,
      open: window.TangentPages.topicUrl(topic.tangentKey, topic.key), canManage: topic.canManage === true };
  }

  function configureShell(context, resource) {
    $('settings-title').textContent = context.title; $('settings-eyebrow').textContent = context.eyebrow; $('settings-intro').textContent = context.intro;
    const crumbs = $('settings-breadcrumbs'); crumbs.replaceChildren(); const home = document.createElement('a');
    home.href = '/'; home.textContent = site?.server?.name || site?.name || 'Home'; crumbs.append(home);
    if (context.kind !== 'server') {
      const tangent = document.createElement(context.kind === 'topic' ? 'a' : 'span'); tangent.textContent = resource.tangent?.name || resource.name || context.tangent;
      if (context.kind === 'topic') tangent.href = window.TangentPages.tangentUrl(context.tangent); crumbs.append(tangent);
    }
    const current = document.createElement('span'); current.textContent = context.title; current.setAttribute('aria-current', 'page'); crumbs.append(current);
    $('settings-context-label').textContent = context.kind === 'topic' ? 'Topic' : 'Tangent'; $('settings-context-kicker').textContent = context.kind.toUpperCase();
    $('settings-context-name').textContent = resource.name; $('settings-context-copy').textContent = resource.description || (context.kind === 'topic' ? 'A shared conversation.' : 'A place for conversation.');
    renderContextEditor(context, resource);
    $('settings-server-tab').hidden = context.kind !== 'server'; $('settings-roles-tab').hidden = context.kind !== 'server'; $('settings-context-tab').hidden = context.kind === 'server'; $('settings-access-tab').hidden = false;
    $('settings-shell').hidden = false; $('settings-denied').hidden = true;
    $('access-scope-badge').textContent = context.kind === 'server' ? 'Server' : resource.name + ' · ' + (context.kind === 'topic' ? 'Topic' : 'Tangent');
    $('access-intro').textContent = context.kind === 'topic' ? 'Choose local roles, or keep a field inherited from this Tangent.'
      : 'Use the server roles people already understand. Global stewards keep their built-in authority.';
  }

  const colorOf = role => /^#[0-9a-f]{6}$/i.test(role?.metadata?.color || role?.presentation?.color || '') ? (role.metadata?.color || role.presentation.color) : '#a98df0';
  async function rolesFor() {
    const page = await request('/api/identity/roles?page=1&pageSize=100');
    return (Array.isArray(page) ? page : page.items || []).map(role => ({ ...role, id: role.id || role.key, name: role.name || role.id || role.key, color: colorOf(role) }))
      .filter(role => role.id && (role.id.startsWith('role:') || role.id.startsWith('group:')));
  }
  const localTokens = tokens => (tokens || []).filter(token => !String(token).startsWith('global:'));
  function tokenLabel(token, roles) {
    if (token === 'everyone') return '@everyone'; if (token === 'authenticated') return '@authenticated';
    const role = roles.find(value => value.id === token); return '@' + (role?.name || token.replace(/^(role|group):/, ''));
  }
  function optionsFor(roles) {
    return [
      { id: 'everyone', name: '@everyone', description: 'Anyone, even before signing in', color: '#6fd9b3' },
      { id: 'authenticated', name: '@authenticated', description: 'Anyone signed in to this server', color: '#d7c8ff' },
      ...roles.map(role => ({ id: role.id, name: '@' + role.name, description: role.metadata?.purpose || role.purpose || role.description || role.id, color: role.color }))
    ];
  }

  function renderAccess(context, view, roles) {
    const root = $('access-decisions'); root.replaceChildren(); const form = document.createElement('form'); form.className = 'access-map-card';
    const choices = optionsFor(roles), draft = {};
    for (const [key, label, description, bypass] of context.decisions) {
      const inherited = context.kind === 'topic' && view.selected?.[key] == null; draft[key] = inherited ? null : [...(view.selected?.[key] || [])];
      const action = document.createElement('section'); action.className = 'access-action'; action.dataset.action = key;
      const head = document.createElement('div'); head.className = 'access-action-head'; const title = document.createElement('h3'); title.textContent = label; title.id = 'access-label-' + key;
      const state = document.createElement('span'); state.className = 'access-action-state'; head.append(title, state);
      const note = document.createElement('p'); note.className = 'access-action-note'; note.textContent = description; action.append(head, note);
      const chips = document.createElement('div'); chips.className = 'access-chip-row'; chips.setAttribute('aria-label', label + ' selections'); action.append(chips);
      const combo = document.createElement('div'); combo.className = 'access-combobox'; const input = document.createElement('input'); input.className = 'input'; input.type = 'search'; input.placeholder = 'Type @role to add…';
      input.setAttribute('role', 'combobox'); input.setAttribute('aria-autocomplete', 'list'); input.setAttribute('aria-expanded', 'false');
      input.setAttribute('aria-labelledby', title.id);
      const list = document.createElement('div'); list.className = 'access-suggestions'; list.id = 'access-options-' + key; list.setAttribute('role', 'listbox'); list.hidden = true;
      input.setAttribute('aria-controls', list.id); combo.append(input, list); action.append(combo);
      const footer = document.createElement('div'); footer.className = 'access-action-footer'; const bypassNote = document.createElement('p'); bypassNote.className = 'access-action-note'; bypassNote.textContent = bypass;
      const inherit = document.createElement('button'); inherit.type = 'button'; inherit.className = 'access-inherit'; inherit.textContent = 'Use Tangent default'; inherit.hidden = context.kind !== 'topic'; footer.append(bypassNote, inherit); action.append(footer); form.append(action);
      let activeIndex = 0; const chosen = () => draft[key] == null ? localTokens(view.parent?.[key] || view.effective?.[key]) : draft[key];
      const renderChips = () => {
        chips.replaceChildren(); const isInherited = draft[key] == null; state.textContent = isInherited ? 'Inherited from Tangent' : 'Set here'; chips.classList.toggle('is-inherited', isInherited);
        const values = chosen(); if (!values.length) { const empty = document.createElement('span'); empty.className = 'access-chip-empty'; empty.textContent = isInherited ? 'No inherited roles' : 'Only global stewards'; chips.append(empty); }
        for (const token of values) {
          const role = roles.find(value => value.id === token), chip = document.createElement('span'); chip.className = 'access-chip'; chip.style.setProperty('--role-color', role?.color || (token === 'everyone' ? '#6fd9b3' : '#d7c8ff'));
          const dot = document.createElement('i'); dot.className = 'access-chip-dot'; dot.setAttribute('aria-hidden', 'true'); const text = document.createElement('strong'); text.textContent = tokenLabel(token, roles); chip.append(dot, text);
          if (!isInherited) { const remove = document.createElement('button'); remove.type = 'button'; remove.textContent = '×'; remove.setAttribute('aria-label', 'Remove ' + text.textContent);
            remove.addEventListener('click', () => { draft[key] = draft[key].filter(value => value !== token); renderChips(); }); chip.append(remove); }
          chips.append(chip);
        }
      };
      const matches = () => { const query = input.value.trim().replace(/^@/, '').toLocaleLowerCase(), selected = new Set(draft[key] || []);
        return choices.filter(option => !selected.has(option.id) && (!query || option.name.toLocaleLowerCase().includes(query) || option.id.toLocaleLowerCase().includes(query))).slice(0, 12); };
      const choose = option => { if (draft[key] == null) draft[key] = [...chosen()]; if (!draft[key].includes(option.id)) draft[key].push(option.id);
        input.value = ''; list.hidden = true; input.setAttribute('aria-expanded', 'false'); input.removeAttribute('aria-activedescendant'); renderChips(); input.focus(); };
      const renderSuggestions = () => {
        list.replaceChildren(); const values = matches(); activeIndex = Math.min(activeIndex, Math.max(values.length - 1, 0));
        values.forEach((option, index) => { const button = document.createElement('button'); button.type = 'button'; button.id = list.id + '-' + index; button.className = 'access-suggestion'; button.setAttribute('role', 'option'); button.setAttribute('aria-selected', String(index === activeIndex)); button.style.setProperty('--role-color', option.color);
          const dot = document.createElement('i'), copy = document.createElement('span'), strong = document.createElement('strong'), small = document.createElement('small'); strong.textContent = option.name; small.textContent = option.description;
          copy.append(strong, small); button.append(dot, copy); button.addEventListener('mousedown', event => event.preventDefault()); button.addEventListener('click', () => choose(option)); list.append(button); });
        list.hidden = !values.length; input.setAttribute('aria-expanded', String(values.length > 0));
        if (values.length) input.setAttribute('aria-activedescendant', list.id + '-' + activeIndex); else input.removeAttribute('aria-activedescendant');
      };
      input.addEventListener('focus', renderSuggestions); input.addEventListener('input', () => { activeIndex = 0; renderSuggestions(); });
      input.addEventListener('blur', () => setTimeout(() => { list.hidden = true; input.setAttribute('aria-expanded', 'false'); input.removeAttribute('aria-activedescendant'); }, 100));
      input.addEventListener('keydown', event => { const values = matches();
        if (event.key === 'Escape') { list.hidden = true; input.setAttribute('aria-expanded', 'false'); input.removeAttribute('aria-activedescendant'); return; }
        if (event.key === 'ArrowDown' || event.key === 'ArrowUp') { event.preventDefault(); activeIndex = (activeIndex + (event.key === 'ArrowDown' ? 1 : -1) + values.length) % Math.max(values.length, 1); renderSuggestions(); }
        if (event.key === 'Enter' && !list.hidden && values[activeIndex]) { event.preventDefault(); choose(values[activeIndex]); }
      });
      inherit.addEventListener('click', () => { draft[key] = null; input.value = ''; renderChips(); }); renderChips();
    }
    const actions = document.createElement('div'); actions.className = 'access-actions'; const save = document.createElement('button'); save.className = 'btn btn-primary'; save.textContent = 'Save access';
    const feedback = document.createElement('p'); feedback.className = 'access-save-feedback'; feedback.setAttribute('role', 'status'); actions.append(save, feedback); form.append(actions); root.append(form);
    form.addEventListener('submit', async event => { event.preventDefault(); save.disabled = true; feedback.textContent = 'Saving…'; feedback.className = 'access-save-feedback'; const signature = contextSignature(context);
      try { const body = Object.fromEntries(context.decisions.map(([key]) => [key, draft[key]])); view = await request(context.accessPath, { method: 'PUT', body });
        if (signature !== contextSignature(contextFor(route()))) return;
        feedback.textContent = 'Access saved.'; feedback.classList.add('success'); $('access-status').textContent = 'Access saved.'; renderAccess(context, view, roles); }
      catch (error) { feedback.textContent = error.message; feedback.classList.add('error'); } finally { save.disabled = false; }
    });
  }

  async function load(force = false) {
    const context = contextFor(route()); if (!context || $('settings-access-panel').hidden) return; const signature = contextSignature(context); if (!force && loadedFor === signature) return;
    const token = ++loadToken; $('access-loading').hidden = false; $('access-loading').classList.remove('error'); $('access-loading').textContent = 'Loading access…'; $('access-decisions').replaceChildren();
    try { const [view, roles] = await Promise.all([request(context.accessPath), rolesFor()]);
      if (token !== loadToken || signature !== contextSignature(contextFor(route()))) return; renderAccess(context, view, roles); $('access-loading').hidden = true; loadedFor = signature;
      $('access-status').textContent = roles.length >= 100 ? 'Showing the first 100 server roles.' : '';
    } catch (error) { if (token !== loadToken || signature !== contextSignature(contextFor(route()))) return;
      $('access-loading').hidden = false; $('access-loading').textContent = error.message || 'Access settings could not be loaded.'; $('access-loading').classList.add('error'); }
  }

  async function openFor(nextSite) {
    const token = ++openToken; loadToken++; site = nextSite; csrf = undefined; loadedFor = ''; const context = contextFor(route()); if (!context) return;
    const signature = contextSignature(context); if (!site?.participant) { $('settings-shell').hidden = true; return; }
    try { const resource = await resourceFor(context); if (token !== openToken || signature !== contextSignature(contextFor(route()))) return;
      if (!resource.canManage) throw new Error('Your current role cannot change settings here.'); configureShell(context, resource);
      const requested = new URL(location.href).searchParams.get('tab'); tab(requested === 'access' ? 'access' : context.kind === 'server' && requested === 'roles' ? 'roles' : context.kind === 'server' ? 'server' : 'context');
      document.title = context.title + ' · ' + resource.name + ' · Tangent Space';
    } catch (error) { if (token !== openToken || signature !== contextSignature(contextFor(route()))) return;
      $('settings-shell').hidden = true; $('settings-denied').hidden = false; $('settings-denied').textContent = error.message || 'These settings are not available.'; }
  }

  window.TangentAccess = { activate };
  window.addEventListener('tangent:welcome', event => openFor(event.detail)); window.addEventListener('tangent:route', event => openFor(event.detail));
})();
