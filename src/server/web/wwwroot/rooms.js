'use strict';
(() => {
  const $ = id => document.getElementById(id);
  let site, room, epoch = 0, identityEpoch = 0, nextRoomPage, nextCursor, resumeCursor, reply, restoring;
  const mentionCache = new Map();
  let liveController;
  let activityController, activityCursor, activityReconnect, activityActive = false;
  let sourceReadiness, activeTangentKey, activitySignature = '', tangentNextPage;
  let routeTangent, routeTopics, routeFailure;
  const route = () => window.TangentPages?.route || { kind: 'home' };
  const tangentPath = key => '/api/v1/tangents/' + encodeURIComponent(key);
  const tangentUrl = key => window.TangentPages.tangentUrl(key);
  const topicUrl = (tangent, key) => window.TangentPages.topicUrl(tangent, key);
  const updateHero = () => routeFailure ? window.TangentPages?.unavailable(routeFailure) : window.TangentPages?.hero(site, route().tangent ? tangentByKey.get(route().tangent) || routeTangent : undefined, room);
  const activityByRoom = new Map();
  const tangentByKey = new Map();
  const pending = new Map();
  const postMutations = new Map();
  const recoveryBlocked = new Set();
  const drafts = new Map();
  const sending = new Map();
  const rendered = new Set();
  const roomPath = key => '/api/rooms/' + encodeURIComponent(key);
  const text = (id, value) => { $(id).textContent = value ?? ''; };
  const show = (id, visible) => { $(id).hidden = !visible; };
  const field = (form, name) => $(form).elements.namedItem(name);
  function status(message, error = false) { text('action-status', message); show('action-status', !!message); $('action-status').classList.toggle('error', error); }
  function element(tag, className, value) { const node = document.createElement(tag); node.className = className; node.textContent = value; return node; }
  function safeAccent(value) { return /^#[0-9a-f]{6}$/i.test(value || '') ? value : '#d88957'; }
  function currentTangents() { return Array.isArray(site?.tangents?.tangents) ? site.tangents.tangents : []; }
  function activityFor(key) { return activityByRoom.get(key) || {}; }
  function can(subject, action) { const actions = subject?.permissions?.allowedActions || subject?.allowedActions; return Array.isArray(actions) && actions.includes(action); }
  async function request(path, body, method = 'POST', signal) {
    const response = await fetch(path, { method: body === undefined ? 'GET' : method, credentials: 'same-origin', cache: 'no-store',
      headers: { Accept: 'application/json', ...(site?.participant?.did ? { 'X-Tangent-Participant': site.participant.did } : {}),
        ...(body === undefined ? {} : { 'Content-Type': 'application/json' }) },
      body: body === undefined ? undefined : JSON.stringify(body), signal });
    const data = await response.json().catch(() => ({}));
    if (!response.ok && !['rejected', 'conflict'].includes(data.state)) { const error = new Error(data.reason || data.error || data.title || (response.status === 403 ? 'Your current access does not permit this action.' : 'This request could not be completed.')); error.status = response.status; throw error; }
    return { data, status: response.status };
  }
  function permissionSummary(view) {
    if (!view) return '';
    const labels = { read: 'read', reply: 'reply', manageServer: 'manage this server', createTangent: 'create Tangents', manageTangent: 'manage this Tangent', createTopic: 'create Topics', manageTopic: 'manage this Topic', manageParticipants: 'manage participants', editOwnPost: 'edit your posts', deleteOwnPost: 'delete your posts', removePost: 'remove posts' };
    return 'Your role: ' + view.role + '. You can ' + (view.allowedActions || []).map(a => labels[a] || a).join(', ') + '.';
  }
  function renderServerSettings(server) {
    const welcome = $('server-welcome'); if (!welcome || !server) return;
    window.TangentAtmosphere?.configure(server, site?.participant);
    text('server-welcome-title', server.name || site?.name || 'Tangent Space');
    text('server-welcome-message', server.welcomeMessage || 'A shared place for durable conversation.');
    text('server-motd', server.motd || ''); show('server-motd', !!server.motd);
    const participant = site?.participant, role = server.role || server.permissions?.role || participant?.role;
    const actions = server.permissions?.allowedActions || server.allowedActions;
    const restriction = server.permissions?.restrictions || server.restrictions;
    const summary = permissionSummary(server.permissions);
    text('server-role', summary); show('server-role', !!summary);
    const canManage = participant?.isOwner === true || can(server, 'manageServer') || can(server, 'editServer');
    show('server-settings-toggle', canManage);
    if (canManage) {
      const form = $('server-settings-form');
      for (const key of ['name', 'byline', 'coverImageUrl', 'welcomeMessage', 'motd', 'creationPolicy']) if (form.elements.namedItem(key)) form.elements.namedItem(key).value = server[key] || (key === 'creationPolicy' ? 'owner_only' : '');
      form.elements.namedItem('allowAgentTangentOwnership').checked = server.allowAgentTangentOwnership === true;
      text('server-permissions-summary', Array.isArray(server.permissions) ? server.permissions.map(p => [p.role, p.scope, (p.allowedActions || []).join(', ')].filter(Boolean).join(': ')).join(' · ') : '');
    }
    show('server-welcome', true);
    updateHero();
  }
  async function refreshServer() { try { const value = (await request('/api/server')).data; if (value && typeof value === 'object') { site.server = value; renderServerSettings(value); } } catch (_) { /* older servers may not expose server settings yet */ } }
  async function action(button, work) {
    if (button) button.disabled = true;
    try { await work(); } catch (error) { status(error.message || 'The site could not be reached. Try again.', true); }
    finally { if (button === $('send-message')) renderDraft(); else if (button) button.disabled = false; }
  }
  function listRooms(listing, append = false) {
    if (!append) $('room-list').replaceChildren();
    for (const entry of listing.rooms || listing.channels || []) {
      const activity = activityFor(entry.key);
      const button = element('a', 'room-link', ''); button.href = topicUrl(entry.tangentKey || activeTangentKey, entry.key); button.dataset.key = entry.key;
      const state = entry.spaceState === 'Pending' ? 'Setup pending' : entry.admission === 'InvitationOnly' ? 'By invitation' : 'Open to signed-in participants';
      const label = element('span', 'room-name', entry.title);
      if (activity.directReplies > 0) label.append(element('span', 'activity-diamond', '◆'));
      else if (activity.unreadCount > 0) label.append(element('span', 'activity-count', activity.unreadCountCapped || activity.unreadCount > 50 ? '50+' : String(activity.unreadCount)));
      button.append(label);
      if (entry.topic) button.append(element('span', 'room-description', entry.topic));
      button.append(element('span', 'room-state', state));
      $('room-list').append(button);
    }
    nextRoomPage = listing.nextPage;
    show('more-rooms', !!nextRoomPage); show('no-rooms', !$('room-list').children.length);
  }
  async function refreshRooms(append = false) {
    if (activeTangentKey) {
      const key = activeTangentKey, identity = identityEpoch;
      const listing = (await request(tangentPath(key) + '/topics' + (append && nextRoomPage ? '?page=' + encodeURIComponent(nextRoomPage) : ''))).data;
      if (identity !== identityEpoch || key !== activeTangentKey) return;
      routeTopics = { ...listing, channels: append ? [...(routeTopics?.channels || []), ...(listing.channels || [])] : listing.channels || [] };
      listRooms(routeTopics);
    } else listRooms((await request('/api/rooms')).data);
  }
  function cardArtwork(card, tangent) {
    if (card.style) card.style.backgroundImage = '';
    card.classList.remove('has-artwork');
    if (typeof tangent.artwork === 'string' && (/^https:\/\//i.test(tangent.artwork) || /^\/tangent-art\/[a-z0-9-]+\.png$/i.test(tangent.artwork))) {
      card.classList.add('has-artwork');
    }
  }
  function validArtwork(value) { return !value || /^https:\/\//i.test(value) || /^\/tangent-art\/[a-z0-9-]+\.png$/i.test(value); }
  function cardContents(value, count = 0, countLabel = '·') {
    const top = element('span', 'tangent-card-top', '');
    const disc = element('span', 'activity-disc', count ? countLabel : '·'); disc.title = count ? countLabel + ' unread activity' : 'No unread activity';
    top.append(element('span', 'card-kicker', 'TANGENT'), disc);
    const face = element('span', 'card-face', '');
    if (value.artwork && validArtwork(value.artwork)) { const image = document.createElement('img'); image.className = 'card-art'; image.src = value.artwork; image.alt = ''; face.append(image); }
    else face.append(element('span', 'card-sigil', '✦'));
    const stripe = element('span', 'card-stripe', '');
    const glass = element('span', 'card-glass', '');
    const unread = count === 1 ? '1 unread message' : count ? countLabel + ' unread messages' : 'No unread messages';
    glass.append(element('strong', 'tangent-card-name', value.name || value.key || 'Your Tangent'), element('span', 'tangent-card-description', value.description || 'A shared place for conversation.'), element('em', 'tangent-card-rule', value.motto ? '“' + value.motto + '”' : 'Make room for the next thought.'), element('span', 'tangent-card-footer', unread));
    return [top, face, stripe, glass];
  }
  function renderEditorPreview() {
    const form = $('edit-tangent-form'), preview = $('edit-card-preview'); if (!form || !preview) return;
    const value = Object.fromEntries(new FormData(form)); preview.replaceChildren();
    if (preview.style) preview.style.setProperty('--tangent-accent', safeAccent(value.accent));
    cardArtwork(preview, value);
    preview.append(...cardContents({ ...value, name: value.name || 'Untitled Tangent' }));
  }
  function renderCreatePreview() {
    const form = $('create-tangent'), preview = $('create-card-preview'); if (!form || !preview) return;
    const value = Object.fromEntries(new FormData(form)); preview.replaceChildren();
    if (preview.style) preview.style.setProperty('--tangent-accent', safeAccent(value.accent));
    cardArtwork(preview, value);
    preview.append(...cardContents(value));
  }
  function configureCreateForm() {
    const form = $('create-tangent'); if (!form) return;
    const key = field('create-tangent', 'key'), home = currentTangents().find(tangent => tangent.key === 'home');
    const onboarding = !!site?.tangents?.setupRequired && !!site?.tangents?.canCreate;
    if (onboarding) { key.value = 'home'; key.readOnly = true; if (home) { field('create-tangent', 'name').value ||= home.name || ''; field('create-tangent', 'description').value ||= home.description || ''; field('create-tangent', 'motto').value ||= home.motto || ''; field('create-tangent', 'accent').value = home.accent || '#d88957'; field('create-tangent', 'artwork').value ||= home.artwork || ''; } }
    else { if (key.readOnly) key.value = ''; key.readOnly = false; }
    renderCreatePreview();
  }
  function openTangentEditor(tangent) {
    const form = $('edit-tangent-form'); if (!form) return;
    for (const name of ['name', 'description', 'motto', 'accent', 'artwork']) field('edit-tangent-form', name).value = tangent[name] || (name === 'accent' ? '#d88957' : '');
    text('edit-tangent-heading', tangent.name || tangent.key); show('tangent-editor', !!(tangent.canManage || tangent.isOwner)); renderEditorPreview();
  }
  function selectTangent(tangent, preferredRoom) {
    activeTangentKey = tangent.key;
    const channels = Array.isArray(tangent.channels) ? tangent.channels : [];
    const key = preferredRoom;
    document.querySelectorAll('.tangent-card').forEach(card => card.setAttribute('aria-current', String(card.dataset.key === tangent.key)));
    listRooms({ rooms: channels });
    text('rooms-heading', route().kind === 'topics' ? 'Conversations' : tangent.name || 'Topics');
    show('community-settings', !!(site?.tangents?.setupRequired || site?.tangents?.canCreate || site?.participant?.isOwner || tangent.canManage || tangent.isOwner || tangent.canCreateTopic === true));
    show('site-setup', site?.participant?.isOwner === true || tangent.canCreateTopic === true || can(tangent, 'createTopic'));
    show('tangent-members', !!(tangent.canManage || tangent.isOwner));
    openTangentEditor(tangent);
    if (key) action(null, () => choose(key));
    else { rememberDraft(); epoch++; resetMessages(); room = null; reply = undefined; document.body.classList.remove('conversation-open'); show('room-content', false); show('choose-room', true); text('choose-room', channels.length ? 'Choose a topic to join the conversation.' : 'No topics here yet. Start one when you are ready.'); }
    updateHero();
  }
  function renderTangents() {
    const holder = $('tangent-list'); if (!holder) return;
    holder.replaceChildren(); tangentByKey.clear();
    const tangents = currentTangents();
    for (const tangent of tangents) {
      tangentByKey.set(tangent.key, tangent);
      const wrap = element('div', 'tangent-card-wrap');
      const card = element('a', 'tangent-card', ''); card.href = tangentUrl(tangent.key); card.dataset.key = tangent.key;
      card.style.setProperty('--tangent-accent', safeAccent(tangent.accent)); cardArtwork(card, tangent);
      const channels = Array.isArray(tangent.channels) ? tangent.channels : [];
      const total = channels.reduce((sum, channel) => sum + (activityFor(channel.key).unreadCount || 0), 0);
      const cap = total > 50 || channels.some(channel => activityFor(channel.key).unreadCountCapped === true) ? '50+' : String(total);
      card.append(...cardContents(tangent, total, cap));
      wrap.append(card);
      if (tangent.canManage || tangent.isOwner || can(tangent, 'manageTangent')) {
        const settings = element('button', 'card-settings btn btn-quiet', '⚙'); settings.type = 'button'; settings.title = 'Tangent settings'; settings.setAttribute('aria-label', 'Open settings for ' + (tangent.name || tangent.key));
        settings.addEventListener('click', event => { event.stopPropagation(); activeTangentKey = tangent.key; show('community-settings', true); openTangentEditor(tangent); $('community-settings').open = true; $('tangent-editor').open = true; }); wrap.append(settings);
      }
      holder.append(wrap);
    }
    if (routeTangent && !tangentByKey.has(routeTangent.key)) tangentByKey.set(routeTangent.key, routeTangent);
    text('no-tangents', site?.participant ? 'No Tangents are available to this account yet.' : 'Sign in to discover the Tangents available to you.');
    show('no-tangents', !tangents.length);
    show('tangent-setup', !!site?.tangents?.setupRequired || !!site?.tangents?.canCreate);
    const active = tangentByKey.get(activeTangentKey);
    show('community-settings', !!(site?.tangents?.setupRequired || site?.tangents?.canCreate || site?.participant?.isOwner || active?.canManage || active?.isOwner || active?.canCreateTopic === true));
    show('site-setup', site?.participant?.isOwner === true || active?.canCreateTopic === true || can(active, 'createTopic'));
    configureCreateForm();
    tangentNextPage = site?.tangents?.nextPage;
    show('more-tangents', !!tangentNextPage);
    const incomplete = site?.tangents?.directoryIncomplete || site?.tangents?.channelsIncomplete;
    text('directory-note', !incomplete ? '' : site?.tangents?.directoryIncomplete ? 'The directory is bounded while the server checks what this account can see. Load more or narrow to a Tangent.' : 'Some channel lists are bounded. Open a Tangent to continue browsing its channels.');
    show('directory-note', !!incomplete);
  }
  async function refreshTangents() {
    const directory = (await request('/api/v1/tangents')).data;
    if (!directory || !Array.isArray(directory.tangents)) throw new Error('Tangent directory could not be read.');
    site.tangents = directory; renderTangents();
    if (route().tangent) {
      routeTangent = (await request(tangentPath(route().tangent))).data;
      tangentByKey.set(routeTangent.key, routeTangent);
      await refreshRooms();
    }
    updateHero();
  }
  async function moreTangents() {
    if (!tangentNextPage) return;
    const page = (await request('/api/v1/tangents?page=' + encodeURIComponent(tangentNextPage))).data;
    if (!page || !Array.isArray(page.tangents)) throw new Error('More Tangents could not be loaded.');
    const known = new Set(currentTangents().map(tangent => tangent.key));
    site.tangents.tangents.push(...page.tangents.filter(tangent => !known.has(tangent.key)));
    site.tangents.nextPage = page.nextPage; site.tangents.directoryIncomplete = !!page.directoryIncomplete; site.tangents.channelsIncomplete = !!page.channelsIncomplete;
    renderTangents();
  }
  function resetMessages() { liveController?.abort(); rendered.clear(); $('messages').replaceChildren(); nextCursor = resumeCursor = undefined; show('more-messages', false); show('acknowledge', false); text('freshness', ''); }
  function unavailableRoute(code) {
    routeFailure = code; epoch++; resetMessages(); stopActivity(); room = null; reply = undefined; sourceReadiness = undefined;
    routeTangent = routeTopics = undefined;
    for (const id of ['room-title', 'room-topic', 'topic-permissions', 'room-access']) text(id, '');
    field('topic-form', 'topic').value = '';
    $('room-list').replaceChildren();
    show('room-content', false); show('more-rooms', false); show('choose-room', true);
    text('choose-room', code === 401 ? 'Sign in to continue.' : 'This conversation isn’t available.');
    updateHero();
  }
  function revokeSelectedRoom(code = 403) {
    const key = room?.key; if (!key) return;
    rememberDraft();
    for (const tangent of currentTangents()) tangent.channels = (tangent.channels || []).filter(channel => channel.key !== key);
    unavailableRoute(code);
  }
  function renderRoom(value) {
    room = value;
    document.body.classList.add('conversation-open');
    text('tangent-return-title', 'Your Tangents');
    if (room.tangentKey) activeTangentKey = room.tangentKey;
    document.querySelectorAll('.room-link').forEach(button => button.setAttribute('aria-current', String(button.dataset.key === room.key)));
    text('topic-permissions', permissionSummary(room.permissions) + (room.isLocked ? ' This Topic is locked.' : room.allowPostEditing ? ' Authors may edit their posts.' : ' Posts are write-once; corrections are new replies.'));
    text('room-title', room.title); text('room-topic', room.topic || 'No topic has been set yet.');
    const tangent = tangentByKey.get(room.tangentKey);
    text('rooms-heading', tangent?.name || 'Topics');
    const mark = $('room-tangent-mark');
    if (mark) { if (mark.style) mark.style.background = safeAccent(tangent?.accent); mark.textContent = tangent?.artwork ? '◈' : '✦'; mark.title = tangent?.name || ''; }
    text('room-admission', room.admission === 'InvitationOnly' ? 'By invitation' : 'Signed-in participants');
    const access = { 'sign-in-required': 'Sign in to enter this room.', 'invitation-required': 'A room manager can invite your DID to join.', removed: 'Your access to this room has been removed.', suspended: 'Your participation at this site is suspended.', 'space-pending': 'Room setup is pending. Its owner can finish connecting it.' };
    text('room-access', access[room.accessState] || (room.canWrite ? 'You can read and take part.' : room.canRead ? 'You can read this room.' : 'Content is not available under your current access.'));
    show('choose-room', false); show('room-content', true); show('room-admin', room.canManage || can(room, 'manageTopic'));
    const topicSettings = $('topic-settings-form');
    if (topicSettings) { topicSettings.elements.namedItem('allowPostEditing').checked = room.allowPostEditing === true; topicSettings.elements.namedItem('isLocked').checked = room.isLocked === true; show('topic-settings-form', room.canManage || can(room, 'manageTopic')); }
    show('provision-room', room.canAppointManagers && room.spaceState === 'Pending');
    show('sync-room', room.canRead); show('message-form', room.canWrite);
    $('channel-details').open = !room.canRead || room.spaceState === 'Pending';
    field('topic-form', 'topic').value = room.topic || '';
    field('admission-form', 'admission').value = room.admission;
    show('admission-form', room.canAppointManagers); show('manager-choice', room.canAppointManagers);
    show('reconnect-room-access', false);
    $('manager-choice').disabled = !room.canAppointManagers;
    if (!room.canAppointManagers && field('member-form', 'role').value === 'Manager') field('member-form', 'role').value = 'Member';
    renderDraft();
    refreshSourceReadiness();
    updateHero();
  }
  async function choose(key) {
    if (route().kind === 'post') return openPost();
    rememberDraft();
    const version = ++epoch; resetMessages(); room = null; reply = undefined;
    show('room-content', false); text('choose-room', 'Opening room…'); show('choose-room', true); status('');
    const data = (await request(route().tangent ? tangentPath(route().tangent) + '/topics/' + encodeURIComponent(key) : roomPath(key))).data;
    if (version !== epoch) return;
    renderRoom(data);
    if (data.canWrite) await restorePending(version, key);
    if (version !== epoch) return;
    if (data.canRead) await historyPage(undefined, version, key, true);
  }
  async function openPost() {
    rememberDraft(); const version = ++epoch; resetMessages(); room = null; reply = undefined;
    show('room-content', false); text('choose-room', 'Opening post…'); show('choose-room', true);
    const result = (await request(tangentPath(route().tangent) + '/posts/' + encodeURIComponent(route().post))).data;
    if (version !== epoch) return;
    renderRoom(result.topic);
    renderPage(result.window, false);
    if (room.canWrite) await restorePending(version, room.key);
  }
  async function restorePending(version, key) {
    if (pending.has(key)) return;
    const lookup = { version, identity: identityEpoch, key }; restoring = lookup;
    recoveryBlocked.add(key);
    renderDraft();
    try {
      const result = (await request(roomPath(key) + '/messages/pending')).data;
      if (version !== epoch || lookup.identity !== identityEpoch || room?.key !== key || !room.canWrite || pending.has(key)) return;
      if (!Array.isArray(result?.messages) || !result.messages.every(saved => saved
        && typeof saved.operationId === 'string' && /^[a-zA-Z0-9_-]{1,128}$/.test(saved.operationId)
        && typeof saved.text === 'string' && saved.text.trim() && new TextEncoder().encode(saved.text).length <= 4096
        && (saved.replyTo == null || (typeof saved.replyTo.uri === 'string' && typeof saved.replyTo.cid === 'string'))))
        throw new Error('Saved messages could not be verified.');
      const saved = result.messages?.[0];
      if (saved && typeof saved.operationId === 'string' && typeof saved.text === 'string') {
        rememberDraft();
        pending.set(key, { operationId: saved.operationId, text: saved.text, ...(saved.replyTo ? { replyTo: saved.replyTo } : {}), detail: saved.detail, saved: true });
      }
      recoveryBlocked.delete(key);
    } catch (error) {
      if (version !== epoch || lookup.identity !== identityEpoch) return;
      if (error.status === 401 || error.status === 403) {
        room.canWrite = false; show('message-form', false);
      }
      if (error.status === 409) {
        room.canRead = room.canWrite = false; show('message-form', false); show('sync-room', false);
        status('Your signed-in account changed. Reload this page before continuing.', true);
      } else status('Saved messages could not be checked. Use Check for updates to try again.', true);
    } finally {
      if (restoring === lookup) restoring = undefined;
      if (version === epoch && lookup.identity === identityEpoch) renderDraft();
    }
  }
  async function historyPage(cursor, version = epoch, key = room?.key, fromStart = false) {
    if (route().kind === 'post') return refreshOpenHistory();
    if (!key) return;
    try {
      const page = (await request(roomPath(key) + '/messages' + (cursor ? '?cursor=' + encodeURIComponent(cursor) : fromStart ? '?from=start' : ''))).data;
      if (version !== epoch || room?.key !== key) return;
      renderPage(page);
      startLive();
    } catch (error) {
      if (version !== epoch) return;
      if (error.status === 403 || error.status === 401 || error.status === 404) revokeSelectedRoom(error.status);
      throw error;
    }
  }
  async function refreshOpenHistory(force = false) {
    if (!room?.canRead || !force && document.querySelector('.message-edit-form')) return;
    const key = room.key, version = epoch;
    if (route().kind === 'post') {
      try {
        const result = (await request(tangentPath(route().tangent) + '/posts/' + encodeURIComponent(route().post))).data;
        if (version !== epoch || room?.key !== key) return;
        resetMessages(); renderRoom(result.topic); renderPage(result.window, false);
      } catch (error) {
        if (version === epoch && [401, 403, 404].includes(error.status)) revokeSelectedRoom(error.status);
        throw error;
      }
    } else { resetMessages(); await historyPage(undefined, version, key, true); }
  }
  async function refreshSourceReadiness() {
    const key = room?.key, identity = identityEpoch, version = epoch;
    if (!key || !site?.participant?.did) return;
    // Local storage has no source to authorize: room policy alone governs posting, and a
    // Spaces readiness answer must never hide the composer here (ADR 0006).
    if (room?.spaceState === 'Local') return;
    try {
      const readiness = (await request('/api/connections/status?room=' + encodeURIComponent(key))).data;
      if (identity !== identityEpoch || version !== epoch || room?.key !== key || !readiness || typeof readiness.state !== 'string') return;
      sourceReadiness = readiness;
      if (!readiness.canWriteSource && room.canWrite) {
        show('message-form', false);
        text('room-access', readiness.reason || (readiness.state === 'provider-unsupported'
          ? 'This account provider cannot grant the room source permission needed to write here.'
          : 'Your account needs room permission before it can post here.'));
        const reconnectable = readiness.state === 'consent-required' || readiness.state === 'disconnected';
        const link = $('reconnect-room-access'); link.href = readiness.connectUrl || '/api/connections/rooms?room=' + encodeURIComponent(key);
        link.textContent = 'Connect room access'; show('reconnect-room-access', reconnectable);
      }
    } catch (_) {
      // The server still makes the final room-policy decision.  A missing
      // readiness endpoint must not be mistaken for account consent.
    }
  }
  function renderPage(page, nativeHistory = true) {
      for (const message of page.messages || []) {
        if (rendered.has(message.id)) continue;
        rendered.add(message.id);
        const li = element('li', 'message', '');
        li.id = 'post-' + message.id;
        if (route().kind === 'post' && message.id === route().post) { li.classList.add('message-anchor'); li.setAttribute('aria-current', 'true'); }
        const byline = element('div', 'message-byline', '');
        let bylineProfileSlot;
        const isYou = message.authorParticipantId === site.participant?.participantRef;
        const handle = typeof page.authorHandles?.[message.authorParticipantId] === 'string' ? page.authorHandles[message.authorParticipantId] : '';
        const shown = isYou ? 'You' : handle ? '@' + handle.replace(/^@/, '') : message.authorParticipantId;
        const initial = (handle || message.authorParticipantId).replace(/^@/, '').slice(0, 1).toUpperCase() || '•';
        const avatar = element('span', 'message-avatar', initial); avatar.title = message.authorParticipantId;
        const author = element('strong', 'message-author', shown); author.title = message.authorParticipantId;
        if (!isYou) { const profileLink = document.createElement('a'); profileLink.href = '/u/' + encodeURIComponent(handle || page.resolved?.[message.authorParticipantId]?.value || message.authorParticipantId); profileLink.className = 'author-link'; profileLink.append(author); bylineProfileSlot = profileLink; }
        else bylineProfileSlot = author;
        byline.append(avatar, bylineProfileSlot, element('time', '', new Date(message.acceptedAt).toLocaleString()));
        const permalink = element('a', 'post-permalink', 'Permalink');
        permalink.href = '/t/' + encodeURIComponent(room.tangentKey) + '/' + encodeURIComponent(message.id);
        byline.append(permalink);
        li.append(byline);
        const content = message.content || {}, deleted = message.removed === true || message.deleted === true;
        if (content.replyTo) li.append(element('p', 'hint reply-label', 'In reply to an earlier message'));
        li.append(deleted ? element('p', 'message-text', 'This message was removed.')
          : window.TangentFacets?.renderFacetedText?.(content.text, message.facets, page.resolved)
            || element('p', 'message-text', content.text));
        // Edit-history affordance (W2-C): the disclosure's meta line reads "Edited · view history"
        // and history.js renders the recorded eras on first open; without history.js the post
        // keeps its plain edited marker. Removed rows never carry the affordance.
        if (!deleted && (message.editedAt || message.changeId)) {
          const historyViewer = window.TangentHistory?.disclosure?.(message,
            { handles: page.authorHandles, resolved: page.resolved, viewerDid: site.participant?.participantRef });
          li.append(historyViewer || element('span', 'message-edited', message.editedAt
            ? 'Edited ' + new Date(message.editedAt).toLocaleString() : 'Edited'));
        }
        const details = element('details', 'source-details', ''); details.append(element('summary', '', 'Source and identity'));
        details.append(element('p', 'did', message.authorParticipantId), element('p', 'did', message.sourceUri), element('p', 'did', message.sourceCid)); li.append(details);
        const actions = message.permissions?.allowedActions || room.permissions?.allowedActions || [];
        const own = isYou;
        if (room.canWrite && !deleted) { const button = element('button', 'btn btn-quiet', 'Reply'); button.type = 'button'; button.addEventListener('click', () => { if (pending.has(room.key)) return status('Finish or retry the pending message first.'); drafts.set(room.key, { text: $('message-text').value, replyTo: { uri: message.sourceUri, cid: message.sourceCid } }); renderDraft(); if (!isYou) window.TangentFacets?.replyMention?.(page.resolved?.[message.authorParticipantId]?.value || message.authorParticipantId, handle); $('message-text').focus(); }); li.append(button); }
        const mayEdit = !deleted && (actions.includes('editOwnPost') && own) && !message._editing;
        const mayDelete = !deleted && ((actions.includes('deleteOwnPost') && own) || actions.includes('removePost'));
        if (mayEdit || mayDelete) {
          const controls = element('span', 'message-controls', '');
          if (mayEdit) { const edit = element('button', 'btn btn-quiet', 'Edit'); edit.type = 'button'; edit.addEventListener('click', () => beginEdit(li, message)); controls.append(edit); }
          if (mayDelete) { const remove = element('button', 'btn btn-quiet', actions.includes('removePost') && !own ? 'Remove' : 'Delete'); remove.type = 'button'; remove.addEventListener('click', () => removeMessage(message)); controls.append(remove); }
          li.append(controls);
        }
        $('messages').append(li);
      }
      nextCursor = nativeHistory ? page.nextCursor : undefined; resumeCursor = nativeHistory ? page.resumeCursor : undefined;
      show('more-messages', !!nextCursor); show('acknowledge', nativeHistory && rendered.size > 0 && !!resumeCursor);
      const freshness = { 'writer-checked': 'Latest notified writer verified against its source.', checked: 'Checked against source repositories.', 'catching-up': 'Catching up with source repositories.', unavailable: 'Source unavailable. Showing retained messages.', 'authority-reauthorization-required': 'The site connection needs to be renewed by its owner.', 'not-yet-checked': 'Source reconciliation has not completed yet.' };
      text('freshness', (freshness[page.freshness] || 'Source status is pending.') + (page.lastCheckedAt ? ' Last complete check: ' + new Date(page.lastCheckedAt).toLocaleString() : '') + (!rendered.size ? ' No messages here yet.' : ''));
  }
  function beginEdit(li, message) {
    const current = li.querySelector('.message-text'); if (!current) return;
    const key = room.key, mutationKey = site.participant.did + ':' + key + ':' + message.id;
    const saved = postMutations.get(mutationKey);
    if (saved?.method === 'DELETE') { status('A deletion is pending. Retry Delete to finish it.'); return; }
    const form = document.createElement('form'); form.className = 'message-edit-form';
    const input = document.createElement('textarea'); input.className = 'input'; input.rows = 3; input.maxLength = 4096;
    input.value = saved?.body.text ?? message.content.text; input.readOnly = !!saved;
    const save = element('button', 'btn btn-primary', saved ? 'Retry edit' : 'Save'), cancel = element('button', 'btn btn-quiet', 'Close');
    save.type = 'submit'; cancel.type = 'button'; cancel.addEventListener('click', () => { form.replaceWith(current); action(null, () => refreshOpenHistory(true)); });
    form.append(input, save, cancel); current.replaceWith(form); input.focus();
    form.addEventListener('submit', event => { event.preventDefault(); action(save, async () => {
      if (!input.value.trim() || new TextEncoder().encode(input.value).length > 4096) throw new Error('Use a post of 1–4096 UTF-8 bytes.');
      const intent = postMutations.get(mutationKey) ?? { method: 'PATCH', body: { text: input.value, operationId: crypto.randomUUID() } };
      postMutations.set(mutationKey, intent); input.readOnly = true; save.textContent = 'Retry edit';
      const response = await request(roomPath(key) + '/messages/' + encodeURIComponent(message.id), intent.body, intent.method);
      if (response.data.state === 'pending') { status('Your edit is saved for this visit. Retry it to confirm the source result.'); return; }
      if (response.data.state !== 'accepted') throw new Error('The source has not confirmed this edit.');
      postMutations.delete(mutationKey); await refreshOpenHistory(true);
    }); });
  }
  function removeMessage(message) {
    if (!confirm(message.permissions?.allowedActions?.includes('removePost') ? 'Remove this post for everyone here?' : 'Delete this post?')) return;
    const key = room.key, mutationKey = site.participant.did + ':' + key + ':' + message.id;
    if (postMutations.get(mutationKey)?.method === 'PATCH') { status('An edit is pending. Retry Edit to finish it.'); return; }
    const intent = postMutations.get(mutationKey) ?? { method: 'DELETE', body: { operationId: crypto.randomUUID() } };
    postMutations.set(mutationKey, intent);
    action(null, async () => {
      const response = await request(roomPath(key) + '/messages/' + encodeURIComponent(message.id), intent.body, intent.method);
      if (response.data.state === 'pending') { status('Deletion is pending. Retry Delete to confirm the source result.'); return; }
      if (!['deleted','moderated'].includes(response.data.state)) throw new Error('The source has not confirmed deletion.');
      postMutations.delete(mutationKey); await refreshOpenHistory(true);
    });
  }
  async function startLive() {
    if (route().kind === 'post' || activityActive || document.hidden || !room?.canRead || !resumeCursor || nextCursor || (liveController && !liveController.signal.aborted)) return;
    const controller = new AbortController(); liveController = controller;
    const version = epoch, key = room.key;
    try {
      while (!controller.signal.aborted && version === epoch) {
        const page = (await request(roomPath(key) + '/updates?cursor=' + encodeURIComponent(resumeCursor), undefined, 'GET', controller.signal)).data;
        if (controller.signal.aborted || version !== epoch) return;
        renderPage(page);
        if (nextCursor) break;
      }
    } catch (error) {
      if (controller.signal.aborted || version !== epoch) return;
      if (error.status === 401 || error.status === 403) {
        revokeSelectedRoom(error.status);
      } else text('freshness', 'Live updates paused. Use Check for updates to reconnect.');
    } finally { controller.abort(); }
  }
  function renderActivity(snapshot) {
    if (!snapshot || typeof snapshot !== 'object') return;
    if (typeof snapshot.checkpoint === 'string') activityCursor = snapshot.checkpoint;
    const previousSequence = room ? activityFor(room.key).lastSequence : undefined;
    const signature = JSON.stringify({ channels: snapshot.channels, truncated: snapshot.channelsTruncated, reset: snapshot.resetRequired });
    const changed = signature !== activitySignature; activitySignature = signature;
    activityByRoom.clear();
    for (const channel of Array.isArray(snapshot.channels) ? snapshot.channels : []) {
      if (typeof channel.roomKey === 'string') activityByRoom.set(channel.roomKey, channel);
    }
    const selectedTangent = tangentByKey.get(room?.tangentKey || activeTangentKey);
    const state = snapshot.channelsIncomplete ? 'Live activity is connected. This overview is incomplete because the directory reached its current scan limit.' : snapshot.channelsTruncated ? 'Live activity is connected. This overview shows up to 100 visible topics.' : selectedTangent?.channelsIncomplete ? 'Live activity is connected. This Tangent’s topic directory is incomplete.' : selectedTangent?.nextChannelsPage ? 'Live activity is connected. This Tangent has more visible topics to load.' : snapshot.resetRequired ? 'Activity reconnected. Some earlier markers may need a fresh check.' : 'Live activity is connected.';
    const compactLive = state === 'Live activity is connected.';
    text('activity-status', compactLive ? 'Live' : state);
    $('activity-status').title = state;
    $('activity-status').setAttribute('aria-label', state);
    if (changed) {
      renderTangents();
      const activeChannels = currentTangents().flatMap(tangent => tangent.channels || []);
      const scope = room?.tangentKey || activeTangentKey;
      listRooms(routeTopics || { rooms: activeChannels.filter(channel => !scope || channel.tangentKey === scope) || [] });
      if (room) document.querySelectorAll('.room-link').forEach(button => button.setAttribute('aria-current', String(button.dataset.key === room.key)));
    }
    const selected = room ? activityByRoom.get(room.key) : undefined;
    const completeOverview = !snapshot.channelsTruncated && !snapshot.channelsHasMore && !snapshot.channelsIncomplete;
    if (room?.canRead && completeOverview && !selected) { revokeSelectedRoom(); refreshTangents().catch(() => {}); return; }
    const shouldRefresh = room && (snapshot.resetRequired || (Array.isArray(snapshot.events) && snapshot.events.some(event => event && event.roomKey === room.key)) || selected?.lastSequence !== previousSequence);
    const messageEvent = Array.isArray(snapshot.events) && snapshot.events.some(event => event && event.roomKey === room?.key && ['MessageChanged', 'MessageEdited', 'MessageDeleted', 'PostChanged', 'PostDeleted'].includes(event.kind));
    if ((messageEvent || route().kind === 'post' && shouldRefresh) && room?.canRead) action(null, refreshOpenHistory);
    else if (shouldRefresh && room.canRead && !nextCursor && resumeCursor) action(null, () => historyPage(resumeCursor, epoch, room.key));
    if (snapshot.resetRequired || snapshot.events?.some(event => event?.kind === 'ParticipantChanged')) refreshServer();
    if (Array.isArray(snapshot.events) && snapshot.events.some(event => ['TangentChanged', 'RoomChanged', 'MembershipChanged', 'ParticipantChanged'].includes(event?.kind))) {
      refreshTangents().then(async () => {
        if (room) {
          const key = room.key, version = epoch;
          try { const current = (await request(tangentPath(room.tangentKey) + '/topics/' + encodeURIComponent(key))).data; if (version === epoch && room?.key === key) renderRoom(current); }
          catch (error) { if (version === epoch && [401, 403, 404].includes(error.status)) revokeSelectedRoom(error.status); }
        }
      }).catch(() => text('activity-status', 'Live activity is connected; the directory will refresh when you reopen it.'));
    }
  }
  function processSseBlock(block) {
    let event = 'message', id, data = '';
    for (const line of block.split(/\r?\n/)) {
      if (line.startsWith('event:')) event = line.slice(6).trim();
      else if (line.startsWith('id:')) id = line.slice(3).trim();
      else if (line.startsWith('data:')) data += line.slice(5).trim();
    }
    if (id) activityCursor = id;
    if ((event === 'activity' || event === 'reset') && data) {
      try { renderActivity(JSON.parse(data)); } catch (_) { text('activity-status', 'Live activity sent an unreadable update. Reconnecting…'); }
    }
  }
  async function startActivity() {
    if (routeFailure || typeof ReadableStream === 'undefined' || activityController || !site?.participant?.did || !site?.tangents || document.hidden) return;
    activityController = new AbortController(); const controller = activityController;
    try {
      const identity = identityEpoch;
      const initial = (await request('/api/activity' + (activityCursor ? '?cursor=' + encodeURIComponent(activityCursor) : ''), undefined, 'GET', controller.signal)).data;
      if (controller.signal.aborted || identity !== identityEpoch) return;
      renderActivity(initial);
      const path = '/api/activity/events' + (activityCursor ? '?cursor=' + encodeURIComponent(activityCursor) : '');
      const response = await fetch(path, { credentials: 'same-origin', cache: 'no-store', signal: controller.signal,
        headers: { Accept: 'text/event-stream', 'X-Tangent-Participant': site.participant.did } });
      if (!response.ok || !response.body) throw new Error('Activity stream could not be opened.');
      activityActive = true;
      // A selected room may have opened its old wait just before the global
      // stream connected.  One page uses one live transport.
      liveController?.abort();
      if ($('freshness').textContent === 'Live updates paused. Use Check for updates to reconnect.') text('freshness', '');
      text('activity-status', 'Live'); $('activity-status').title = 'Live activity is connected.'; $('activity-status').setAttribute('aria-label', 'Live activity is connected.');
      const reader = response.body.getReader(), decoder = new TextDecoder(); let buffer = '';
      while (!controller.signal.aborted) {
        const packet = await reader.read(); if (packet.done) break;
        buffer += decoder.decode(packet.value, { stream: true });
        if (buffer.length > 512 * 1024) throw new Error('Activity response exceeds the live connection limit.');
        const parts = buffer.split(/\r?\n\r?\n/); buffer = parts.pop(); parts.forEach(processSseBlock);
      }
    } catch (_) {
      if (!controller.signal.aborted) text('activity-status', 'Live activity is reconnecting…');
    } finally {
      activityActive = false;
      if (activityController === controller) activityController = undefined;
      if (!controller.signal.aborted && !document.hidden) activityReconnect = setTimeout(startActivity, 2500);
    }
  }
  function stopActivity() { if (typeof clearTimeout === 'function') clearTimeout(activityReconnect); activityController?.abort(); activityController = undefined; activityActive = false; }
  function renderDraft() {
    const intent = room && pending.get(room.key);
    if (intent) { $('message-text').value = intent.text; reply = intent.replyTo; }
    else { const draft = drafts.get(room?.key); $('message-text').value = draft?.text || ''; reply = draft?.replyTo; }
    $('message-text').readOnly = !!intent;
    $('send-message').disabled = !!room && (sending.has(room.key) || recoveryBlocked.has(room.key) || restoring?.version === epoch);
    text('send-message', intent ? 'Retry saved message' : 'Send message');
    const reconnect = intent?.detail === 'reauthorization-required';
    text('pending-message', !intent ? '' : reconnect
      ? 'Your message is saved. Your account is connected, but it has not granted permission to send messages here. Connect room access, then retry your saved message.'
      : intent.saved ? 'Your message is saved. It has not finished sending. Retry this message to continue.'
      : 'This message has not been confirmed as sent. Retry the same message to check and continue.');
    show('pending-message', !!intent);
    $('reconnect-room-access').href = '/api/connections/rooms?room=' + encodeURIComponent(room?.key || '');
    show('reconnect-room-access', reconnect);
    text('reply-context', reply ? 'Replying to this message.' : ''); show('reply-context', !!reply); show('cancel-reply', !!reply && !intent);
    size();
  }
  function rememberDraft() { if (room?.key && !pending.has(room.key)) drafts.set(room.key, { text: $('message-text').value, replyTo: reply }); }
  // Facet bridges for the composer (ADR 0008): the picker, the wire package and cleanup.
  window.TangentRooms = window.TangentRooms || {};
  window.TangentRooms.currentRoomKey = () => room?.key;
  window.TangentRooms.mentionables = async (key, prefix) => {
    const cacheKey = key + ':' + (prefix || '');
    const fresh = mentionCache.get(cacheKey);
    if (fresh && Date.now() - fresh.at < 30000) return fresh.list;
    const data = (await request('/api/v1/experience/topics/' + encodeURIComponent(key) + '/mentionables?prefix=' + encodeURIComponent(prefix || ''))).data;
    const list = data?.result?.data?.targets || [];
    mentionCache.set(cacheKey, { at: Date.now(), list });
    return list;
  };
  function size() { text('message-size', new TextEncoder().encode($('message-text').value).length + ' / 4096 bytes'); }
  async function mutateRoom(suffix, body, method) { const key = room.key; await request(roomPath(key) + suffix, body, method); status('Saved.'); await refreshRooms(); if (room?.key === key) await choose(key); }
  window.TangentRooms = window.TangentRooms || {};
  window.TangentRooms.size = size;
  window.addEventListener('tangent:welcome', event => {
    if (site?.participant?.did !== event.detail.participant?.did) {
      identityEpoch++; epoch++; resetMessages(); stopActivity(); pending.clear(); recoveryBlocked.clear(); drafts.clear(); sending.clear(); restoring = undefined; room = null; reply = undefined; sourceReadiness = undefined; activeTangentKey = undefined; routeTangent = routeTopics = undefined; activityByRoom.clear(); tangentByKey.clear();
      renderDraft(); show('room-content', false); show('choose-room', true); text('choose-room', 'Choose a room to see its topic and current access.'); status('');
    }
    site = event.detail;
    routeFailure = undefined;
    if (['sign-in', 'onboarding'].includes(route().kind)) return;
    show('place', true); document.body.classList.add('has-rooms'); refreshServer();
    show('site-setup', site.participant?.isOwner === true || currentTangents().some(t => t.canCreateTopic === true));
    const identity = identityEpoch;
    action(null, async () => {
      try {
      const legacy = new URL(location.href).searchParams.get('room');
      if (legacy && route().kind === 'home') {
        const topic = (await request(roomPath(legacy))).data;
        if (identity === identityEpoch) location.replace(topicUrl(topic.tangentKey, topic.key));
        return;
      }
      if (!site.tangents) site.tangents = (await request('/api/v1/tangents')).data;
      if (identity !== identityEpoch) return;
      renderTangents();
      if (route().tangent) {
        routeTangent = tangentByKey.get(route().tangent) || (await request(tangentPath(route().tangent))).data;
        if (identity !== identityEpoch) return;
        tangentByKey.set(routeTangent.key, routeTangent);
        selectTangent(routeTangent);
        await refreshRooms();
        if (identity !== identityEpoch) return;
        if (route().kind === 'topic') await choose(route().topic);
        else if (route().kind === 'post') await openPost();
      }
      updateHero(); startActivity();
      } catch (error) {
        if (identity === identityEpoch && [401, 403, 404].includes(error.status)) unavailableRoute(error.status);
        throw error;
      }
    });
    try { if (sessionStorage.getItem('tangent-created') === site.participant?.did) { sessionStorage.removeItem('tangent-created'); status('Your Tangent is ready. Make yourself at home.'); } } catch (_) { }
  });
  $('refresh-rooms').addEventListener('click', () => action($('refresh-rooms'), refreshRooms));
  $('return-to-tangents').addEventListener('click', () => {
    rememberDraft(); location.assign('/tangents/');
  });
  $('more-tangents').addEventListener('click', () => action($('more-tangents'), moreTangents));
  document.addEventListener('visibilitychange', () => { if (document.hidden) { liveController?.abort(); stopActivity(); } else { startLive(); startActivity(); } });
  $('more-rooms').addEventListener('click', () => action($('more-rooms'), () => refreshRooms(true)));
  $('more-messages').addEventListener('click', () => action($('more-messages'), () => historyPage(nextCursor)));
  $('message-text').addEventListener('input', () => { size(); rememberDraft(); });
  $('cancel-reply').addEventListener('click', () => { drafts.set(room.key, { text: $('message-text').value }); renderDraft(); });
  $('provision-room').addEventListener('click', () => action($('provision-room'), () => mutateRoom('/provision', {})));
  $('sync-room').addEventListener('click', () => action($('sync-room'), async () => { const key = room.key; status('Checking source repositories…'); await request(roomPath(key) + '/sync', {}); if (room?.key === key) { status(''); await choose(key); } }));
  $('acknowledge').addEventListener('click', () => action($('acknowledge'), async () => { if (route().kind === 'post' || !resumeCursor) return; await request(roomPath(room.key) + '/read-position', { cursor: resumeCursor }); status('Your read position is saved.'); }));
  $('create-room').addEventListener('submit', event => { event.preventDefault(); action(event.submitter, async () => {
    const body = Object.fromEntries(new FormData(event.target));
    if (activeTangentKey) {
      await request(tangentPath(activeTangentKey) + '/topics', body);
      event.target.reset(); location.assign(topicUrl(activeTangentKey, body.key));
      return;
    }
    await request('/api/rooms', body); event.target.reset(); const created = (await request(roomPath(body.key))).data; location.assign(topicUrl(created.tangentKey, created.key));
  }); });
  $('create-tangent').addEventListener('submit', event => { event.preventDefault(); action(event.submitter, async () => {
    const body = Object.fromEntries(new FormData(event.target));
    body.name = body.name.trim(); body.key = body.key.trim(); body.description = body.description.trim(); body.motto = body.motto.trim();
    if (!body.name || !body.key || !validArtwork(body.artwork)) throw new Error('Give this Tangent a name and stable address. Artwork must use HTTPS or a supplied sample.');
    await request('/api/v1/tangents', body); event.target.reset(); await refreshTangents();
    location.assign(tangentUrl(body.key));
  }); });
  $('create-tangent').addEventListener('input', renderCreatePreview);
  $('edit-tangent-form').addEventListener('input', renderEditorPreview);
  $('edit-tangent-form').addEventListener('submit', event => { event.preventDefault(); action(event.submitter, async () => {
    const tangent = tangentByKey.get(activeTangentKey); if (!tangent) throw new Error('Choose a Tangent before editing its card.');
    const body = Object.fromEntries(new FormData(event.target));
    body.name = body.name.trim(); body.description = body.description.trim(); body.motto = body.motto.trim(); body.artwork = body.artwork.trim();
    if (!body.name || !validArtwork(body.artwork)) throw new Error('Give this Tangent a name. Artwork must use HTTPS or a supplied sample.');
    await request(tangentPath(tangent.key), body, 'PATCH');
    await refreshTangents(); const updated = tangentByKey.get(tangent.key); if (updated) { activeTangentKey = updated.key; openTangentEditor(updated); }
    status('Tangent card saved.');
  }); });
  $('tangent-member-form').addEventListener('submit', event => { event.preventDefault(); action(event.submitter, async () => {
    const did = field('tangent-member-form', 'did').value.trim(), role = field('tangent-member-form', 'role').value;
    if (!activeTangentKey || !did) throw new Error('Choose a Tangent and enter a participant DID.');
    await request('/api/tangents/' + encodeURIComponent(activeTangentKey) + '/members/' + encodeURIComponent(did), { role }, 'PUT');
    field('tangent-member-form', 'did').value = ''; status('Community role saved.'); await refreshTangents();
  }); });
  document.querySelectorAll('.art-sample').forEach(button => button.addEventListener('click', () => {
    const artwork = button.dataset.artwork;
    if (typeof artwork === 'string') field('create-tangent', 'artwork').value = artwork;
  }));
  $('topic-form').addEventListener('submit', event => { event.preventDefault(); action(event.submitter, () => mutateRoom('/topic', { topic: field('topic-form', 'topic').value }, 'PUT')); });
  $('member-form').addEventListener('submit', event => { event.preventDefault(); action(event.submitter, () => mutateRoom('/members/' + encodeURIComponent(field('member-form', 'did').value.trim()), { role: field('member-form', 'role').value }, 'PUT')); });
  $('admission-form').addEventListener('submit', event => { event.preventDefault(); action(event.submitter, () => mutateRoom('/admission', { admission: field('admission-form', 'admission').value }, 'PUT')); });
  $('topic-settings-form').addEventListener('submit', event => { event.preventDefault(); action(event.submitter, () => mutateRoom('/settings', { allowPostEditing: field('topic-settings-form', 'allowPostEditing').checked, isLocked: field('topic-settings-form', 'isLocked').checked, title: room.title, topic: field('topic-form', 'topic').value }, 'PATCH')); });
  $('server-atmosphere-open').addEventListener('click', () => window.TangentAtmosphere?.open());
  $('server-settings-toggle').addEventListener('click', () => { const panel = $('server-settings'), button = $('server-settings-toggle'); panel.open = !panel.open; panel.hidden = !panel.open; button.setAttribute('aria-expanded', String(panel.open)); if (panel.open) $('server-settings-form').elements.namedItem('name').focus(); });
  $('server-settings-form').addEventListener('submit', event => { event.preventDefault(); action(event.submitter, async () => { const body = Object.fromEntries(new FormData(event.target)); body.allowAgentTangentOwnership = field('server-settings-form', 'allowAgentTangentOwnership').checked; await request('/api/server', body, 'PATCH'); await refreshServer(); status('Server settings saved.'); }); });
  $('message-form').addEventListener('submit', event => {
    event.preventDefault();
    if (!room?.canWrite || sending.has(room.key) || recoveryBlocked.has(room.key) || restoring?.version === epoch) return;
    action($('send-message'), async () => {
      const key = room.key, identity = identityEpoch, version = epoch;
      const current = () => identity === identityEpoch && version === epoch && room?.key === key;
      let intent = pending.get(key);
      if (!intent) {
        const body = $('message-text').value;
        if (!body.trim() || new TextEncoder().encode(body).length > 4096) throw new Error('Use a message of 1–4096 UTF-8 bytes.');
        intent = { operationId: crypto.randomUUID(), text: body, facets: window.TangentFacets?.wireFacets?.(key, body) || [], ...(reply ? { replyTo: reply } : {}) }; pending.set(key, intent);
      }
      sending.set(key, intent); renderDraft();
      try {
        const body = { operationId: intent.operationId, text: intent.text, ...(intent.facets?.length ? { facets: intent.facets } : {}), ...(intent.replyTo ? { replyTo: intent.replyTo } : {}) };
        const receipt = (await request(roomPath(key) + '/messages', body)).data;
        if (identity !== identityEpoch) return;
        if (receipt.state === 'pending') {
          intent.detail = receipt.detail; intent.saved = true;
          if (current()) renderDraft();
          return;
        }
        pending.delete(key);
        if (receipt.state === 'accepted') window.TangentFacets?.clearFacets?.(key);
        if (receipt.state === 'accepted') {
          const draft = drafts.get(key);
          if (draft?.text === intent.text && draft.replyTo?.uri === intent.replyTo?.uri && draft.replyTo?.cid === intent.replyTo?.cid) drafts.delete(key);
        } else drafts.set(key, { text: intent.text, replyTo: intent.replyTo });
        if (room?.key === key) {
          const completionVersion = epoch + 1;
          reply = undefined; renderDraft(); await choose(key);
          if (identity === identityEpoch && epoch === completionVersion && room?.key === key) status(receipt.state === 'accepted' ? 'Message sent.' : 'This message did not meet the room’s current rules.', receipt.state !== 'accepted');
        }
      } catch (error) { if (current()) throw error; }
      finally { if (sending.get(key) === intent) sending.delete(key); }
    });
  });
})();
