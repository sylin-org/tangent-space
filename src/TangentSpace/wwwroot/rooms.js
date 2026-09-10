'use strict';
(() => {
  const $ = id => document.getElementById(id);
  let site, room, epoch = 0, identityEpoch = 0, nextRoomPage, nextCursor, resumeCursor, reply, restoring;
  let liveController;
  let activityController, activityCursor, activityReconnect, activityActive = false;
  let sourceReadiness, activeTangentKey, activitySignature = '', tangentNextPage;
  const activityByRoom = new Map();
  const tangentByKey = new Map();
  const pending = new Map();
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
  async function request(path, body, method = 'POST', signal) {
    const response = await fetch(path, { method: body === undefined ? 'GET' : method, credentials: 'same-origin', cache: 'no-store',
      headers: { Accept: 'application/json', ...(site?.participant?.did ? { 'X-Tangent-Participant': site.participant.did } : {}),
        ...(body === undefined ? {} : { 'Content-Type': 'application/json' }) },
      body: body === undefined ? undefined : JSON.stringify(body), signal });
    const data = await response.json().catch(() => ({}));
    if (!response.ok && !['rejected', 'conflict'].includes(data.state)) { const error = new Error(data.reason || data.title || (response.status === 403 ? 'Your current access does not permit this action.' : 'This request could not be completed.')); error.status = response.status; throw error; }
    return { data, status: response.status };
  }
  async function action(button, work) {
    if (button) button.disabled = true;
    try { await work(); } catch (error) { status(error.message || 'The site could not be reached. Try again.', true); }
    finally { if (button === $('send-message')) renderDraft(); else if (button) button.disabled = false; }
  }
  function listRooms(listing, append = false) {
    if (!append) $('room-list').replaceChildren();
    for (const entry of listing.rooms || []) {
      const activity = activityFor(entry.key);
      const button = element('button', 'room-link', ''); button.type = 'button'; button.dataset.key = entry.key;
      const state = entry.spaceState === 'Pending' ? 'Setup pending' : entry.admission === 'InvitationOnly' ? 'By invitation' : 'Open to signed-in participants';
      const label = element('span', 'room-name', entry.title);
      if (activity.directReplies > 0) label.append(element('span', 'activity-diamond', '◆'));
      else if (activity.unreadCount > 0) label.append(element('span', 'activity-count', activity.unreadCountCapped || activity.unreadCount > 50 ? '50+' : String(activity.unreadCount)));
      button.append(label, element('span', 'room-state', state));
      button.addEventListener('click', () => action(button, () => choose(entry.key)));
      $('room-list').append(button);
    }
    nextRoomPage = listing.nextPage;
    show('more-rooms', !!nextRoomPage); show('no-rooms', !$('room-list').children.length);
  }
  async function refreshRooms() { listRooms((await request('/api/rooms')).data); }
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
    if (validArtwork(value.artwork)) { const image = document.createElement('img'); image.className = 'card-art'; image.src = value.artwork; image.alt = ''; face.append(image); }
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
    const key = preferredRoom || channels.find(channel => channel.canRead)?.key || channels[0]?.key;
    document.querySelectorAll('.tangent-card').forEach(card => card.setAttribute('aria-current', String(card.dataset.key === tangent.key)));
    listRooms({ rooms: channels });
    text('rooms-heading', tangent.name || 'Channels');
    show('community-settings', !!(site?.tangents?.setupRequired || site?.tangents?.canCreate || site?.participant?.isOwner || tangent.canManage || tangent.isOwner));
    show('tangent-members', !!(tangent.canManage || tangent.isOwner));
    openTangentEditor(tangent);
    if (key) action(null, () => choose(key));
    else { rememberDraft(); epoch++; resetMessages(); room = null; reply = undefined; document.body.classList.remove('conversation-open'); show('room-content', false); show('choose-room', true); text('choose-room', 'This Tangent has no channels yet. Add one when you know what it is for.'); }
  }
  function renderTangents() {
    const holder = $('tangent-list'); if (!holder) return;
    holder.replaceChildren(); tangentByKey.clear();
    const tangents = currentTangents();
    for (const tangent of tangents) {
      tangentByKey.set(tangent.key, tangent);
      const card = element('button', 'tangent-card', ''); card.type = 'button'; card.dataset.key = tangent.key;
      card.style.setProperty('--tangent-accent', safeAccent(tangent.accent)); cardArtwork(card, tangent);
      const channels = Array.isArray(tangent.channels) ? tangent.channels : [];
      const total = channels.reduce((sum, channel) => sum + (activityFor(channel.key).unreadCount || 0), 0);
      const cap = total > 50 || channels.some(channel => activityFor(channel.key).unreadCountCapped === true) ? '50+' : String(total);
      card.append(...cardContents(tangent, total, cap));
      card.addEventListener('click', () => selectTangent(tangent)); holder.append(card);
    }
    show('no-tangents', !tangents.length);
    show('tangent-setup', !!site?.tangents?.setupRequired || !!site?.tangents?.canCreate);
    const active = tangentByKey.get(activeTangentKey);
    show('community-settings', !!(site?.tangents?.setupRequired || site?.tangents?.canCreate || site?.participant?.isOwner || active?.canManage || active?.isOwner));
    configureCreateForm();
    tangentNextPage = site?.tangents?.nextPage;
    show('more-tangents', !!tangentNextPage);
    const incomplete = site?.tangents?.directoryIncomplete || site?.tangents?.channelsIncomplete;
    text('directory-note', !incomplete ? '' : site?.tangents?.directoryIncomplete ? 'The directory is bounded while the server checks what this account can see. Load more or narrow to a Tangent.' : 'Some channel lists are bounded. Open a Tangent to continue browsing its channels.');
    show('directory-note', !!incomplete);
  }
  async function refreshTangents() {
    const directory = (await request('/api/tangents')).data;
    if (!directory || !Array.isArray(directory.tangents)) throw new Error('Tangent directory could not be read.');
    site.tangents = directory; renderTangents();
  }
  async function moreTangents() {
    if (!tangentNextPage) return;
    const page = (await request('/api/tangents?page=' + encodeURIComponent(tangentNextPage))).data;
    if (!page || !Array.isArray(page.tangents)) throw new Error('More Tangents could not be loaded.');
    const known = new Set(currentTangents().map(tangent => tangent.key));
    site.tangents.tangents.push(...page.tangents.filter(tangent => !known.has(tangent.key)));
    site.tangents.nextPage = page.nextPage; site.tangents.directoryIncomplete = !!page.directoryIncomplete; site.tangents.channelsIncomplete = !!page.channelsIncomplete;
    renderTangents();
  }
  function resetMessages() { liveController?.abort(); rendered.clear(); $('messages').replaceChildren(); nextCursor = resumeCursor = undefined; show('more-messages', false); show('acknowledge', false); text('freshness', ''); }
  function revokeSelectedRoom() {
    const key = room?.key; if (!key) return;
    rememberDraft(); epoch++; resetMessages(); room = null; reply = undefined; sourceReadiness = undefined;
    for (const tangent of currentTangents()) tangent.channels = (tangent.channels || []).filter(channel => channel.key !== key);
    renderTangents(); const active = tangentByKey.get(activeTangentKey); listRooms({ rooms: active?.channels || [] });
    show('room-content', false); show('choose-room', true); text('choose-room', 'This channel is no longer available to this account.');
  }
  function renderRoom(value) {
    room = value;
    document.body.classList.add('conversation-open');
    text('tangent-return-title', 'Your Tangents');
    if (room.tangentKey) activeTangentKey = room.tangentKey;
    document.querySelectorAll('.room-link').forEach(button => button.setAttribute('aria-current', String(button.dataset.key === room.key)));
    text('room-title', room.title); text('room-topic', room.topic || 'No topic has been set yet.');
    const tangent = tangentByKey.get(room.tangentKey);
    text('rooms-heading', tangent?.name || 'Channels');
    const mark = $('room-tangent-mark');
    if (mark) { if (mark.style) mark.style.background = safeAccent(tangent?.accent); mark.textContent = tangent?.artwork ? '◈' : '✦'; mark.title = tangent?.name || ''; }
    text('room-admission', room.admission === 'InvitationOnly' ? 'By invitation' : 'Signed-in participants');
    const access = { 'sign-in-required': 'Sign in to enter this room.', 'invitation-required': 'A room manager can invite your DID to join.', removed: 'Your access to this room has been removed.', suspended: 'Your participation at this site is suspended.', 'space-pending': 'Room setup is pending. Its owner can finish connecting it.' };
    text('room-access', access[room.accessState] || (room.canWrite ? 'You can read and take part.' : room.canRead ? 'You can read this room.' : 'Content is not available under your current access.'));
    show('choose-room', false); show('room-content', true); show('room-admin', room.canManage);
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
  }
  async function choose(key) {
    rememberDraft();
    const version = ++epoch; resetMessages(); room = null; reply = undefined;
    show('room-content', false); text('choose-room', 'Opening room…'); show('choose-room', true); status('');
    const data = (await request(roomPath(key))).data;
    if (version !== epoch) return;
    renderRoom(data);
    history.replaceState(null, '', '/?room=' + encodeURIComponent(key));
    if (data.canWrite) await restorePending(version, key);
    if (version !== epoch) return;
    if (data.canRead) await historyPage(undefined, version, key, true);
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
    if (!key) return;
    try {
      const page = (await request(roomPath(key) + '/messages' + (cursor ? '?cursor=' + encodeURIComponent(cursor) : fromStart ? '?from=start' : ''))).data;
      if (version !== epoch || room?.key !== key) return;
      renderPage(page);
      startLive();
    } catch (error) {
      if (version !== epoch) return;
      if (error.status === 403 || error.status === 401 || error.status === 404) revokeSelectedRoom();
      throw error;
    }
  }
  async function refreshSourceReadiness() {
    const key = room?.key, identity = identityEpoch, version = epoch;
    if (!key || !site?.participant?.did) return;
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
  function renderPage(page) {
      for (const message of page.messages || []) {
        if (rendered.has(message.id)) continue;
        rendered.add(message.id);
        const li = element('li', 'message', '');
        const byline = element('div', 'message-byline', '');
        const isYou = message.authorDid === site.participant?.did;
        const handle = typeof page.authorHandles?.[message.authorDid] === 'string' ? page.authorHandles[message.authorDid] : '';
        const shown = isYou ? 'You' : handle ? '@' + handle.replace(/^@/, '') : message.authorDid;
        const initial = (handle || message.authorDid).replace(/^@/, '').slice(0, 1).toUpperCase() || '•';
        const avatar = element('span', 'message-avatar', initial); avatar.title = message.authorDid;
        const author = element('strong', 'message-author', shown); author.title = message.authorDid;
        byline.append(avatar, author, element('time', '', new Date(message.acceptedAt).toLocaleString()));
        li.append(byline);
        if (message.content.replyTo) li.append(element('p', 'hint reply-label', 'In reply to an earlier message'));
        li.append(element('p', 'message-text', message.content.text));
        const details = element('details', 'source-details', ''); details.append(element('summary', '', 'Source and identity'));
        details.append(element('p', 'did', message.authorDid), element('p', 'did', message.sourceUri), element('p', 'did', message.sourceCid)); li.append(details);
        if (room.canWrite) { const button = element('button', 'btn btn-quiet', 'Reply'); button.type = 'button'; button.addEventListener('click', () => { if (pending.has(room.key)) return status('Finish or retry the pending message first.'); drafts.set(room.key, { text: $('message-text').value, replyTo: { uri: message.sourceUri, cid: message.sourceCid } }); renderDraft(); $('message-text').focus(); }); li.append(button); }
        $('messages').append(li);
      }
      nextCursor = page.nextCursor; resumeCursor = page.resumeCursor;
      show('more-messages', !!nextCursor); show('acknowledge', rendered.size > 0);
      const freshness = { 'writer-checked': 'Latest notified writer verified against its source.', checked: 'Checked against source repositories.', 'catching-up': 'Catching up with source repositories.', unavailable: 'Source unavailable. Showing retained messages.', 'authority-reauthorization-required': 'The site connection needs to be renewed by its owner.', 'not-yet-checked': 'Source reconciliation has not completed yet.' };
      text('freshness', (freshness[page.freshness] || 'Source status is pending.') + (page.lastCheckedAt ? ' Last complete check: ' + new Date(page.lastCheckedAt).toLocaleString() : '') + (!rendered.size ? ' No messages here yet.' : ''));
  }
  async function startLive() {
    if (activityActive || document.hidden || !room?.canRead || !resumeCursor || nextCursor || (liveController && !liveController.signal.aborted)) return;
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
        revokeSelectedRoom();
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
    const state = snapshot.channelsIncomplete ? 'Live activity is connected. This overview is incomplete because the directory reached its current scan limit.' : snapshot.channelsTruncated ? 'Live activity is connected. This overview shows up to 100 visible channels.' : selectedTangent?.channelsIncomplete ? 'Live activity is connected. This Tangent’s channel directory is incomplete.' : selectedTangent?.nextChannelsPage ? 'Live activity is connected. This Tangent has more visible channels to load.' : snapshot.resetRequired ? 'Activity reconnected. Some earlier markers may need a fresh check.' : 'Live activity is connected.';
    const compactLive = state === 'Live activity is connected.';
    text('activity-status', compactLive ? 'Live' : state);
    $('activity-status').title = state;
    $('activity-status').setAttribute('aria-label', state);
    if (changed) {
      renderTangents();
      const activeChannels = currentTangents().flatMap(tangent => tangent.channels || []);
      const scope = room?.tangentKey || activeTangentKey;
      listRooms({ rooms: activeChannels.filter(channel => !scope || channel.tangentKey === scope) || [] });
      if (room) document.querySelectorAll('.room-link').forEach(button => button.setAttribute('aria-current', String(button.dataset.key === room.key)));
    }
    const selected = room ? activityByRoom.get(room.key) : undefined;
    const completeOverview = !snapshot.channelsTruncated && !snapshot.channelsHasMore && !snapshot.channelsIncomplete;
    if (room?.canRead && completeOverview && !selected) { revokeSelectedRoom(); refreshTangents().catch(() => {}); return; }
    const shouldRefresh = room && (snapshot.resetRequired || (Array.isArray(snapshot.events) && snapshot.events.some(event => event && event.roomKey === room.key)) || selected?.lastSequence !== previousSequence);
    if (shouldRefresh && room.canRead && !nextCursor && resumeCursor) action(null, () => historyPage(resumeCursor, epoch, room.key));
    if (Array.isArray(snapshot.events) && snapshot.events.some(event => ['TangentChanged', 'RoomChanged', 'MembershipChanged', 'ParticipantChanged'].includes(event?.kind))) {
      refreshTangents().then(() => {
        if (room && !currentTangents().some(tangent => (tangent.channels || []).some(channel => channel.key === room.key))) revokeSelectedRoom();
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
    if (typeof ReadableStream === 'undefined' || activityController || !site?.participant?.did || !site?.tangents || document.hidden) return;
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
  function size() { text('message-size', new TextEncoder().encode($('message-text').value).length + ' / 4096 bytes'); }
  async function mutateRoom(suffix, body, method) { const key = room.key; await request(roomPath(key) + suffix, body, method); status('Saved.'); await refreshRooms(); if (room?.key === key) await choose(key); }
  window.addEventListener('tangent:welcome', event => {
    if (site?.participant?.did !== event.detail.participant?.did) {
      identityEpoch++; epoch++; resetMessages(); stopActivity(); pending.clear(); recoveryBlocked.clear(); drafts.clear(); sending.clear(); restoring = undefined; room = null; reply = undefined; sourceReadiness = undefined; activeTangentKey = undefined; activityByRoom.clear(); tangentByKey.clear();
      renderDraft(); show('room-content', false); show('choose-room', true); text('choose-room', 'Choose a room to see its topic and current access.'); status('');
    }
    site = event.detail; show('place', true); document.body.classList.add('has-rooms');
    show('site-setup', site.participant?.isOwner === true);
    const key = new URL(location.href).searchParams.get('room');
    if (currentTangents().length) {
      renderTangents();
      const parent = currentTangents().find(tangent => (tangent.channels || []).some(channel => channel.key === key));
      if (parent) selectTangent(parent, key);
    } else {
      // Kept only for older servers during the API rollout. This is server data,
      // never an app-managed room fallback.
      listRooms(site.rooms || { rooms: [] });
      if (key && /^[a-z0-9-]{1,64}$/.test(key)) action(null, () => choose(key));
    }
    startActivity();
  });
  $('refresh-rooms').addEventListener('click', () => action($('refresh-rooms'), async () => { if (currentTangents().length) await refreshTangents(); else await refreshRooms(); }));
  $('return-to-tangents').addEventListener('click', () => {
    rememberDraft(); epoch++; resetMessages(); room = null; reply = undefined;
    document.body.classList.remove('conversation-open');
    text('tangent-return-title', 'Pick up a conversation');
    const active = tangentByKey.get(activeTangentKey); listRooms({ rooms: active?.channels || [] });
    show('room-content', false); show('choose-room', true); text('choose-room', 'Choose a channel, or pick another Tangent above.');
    history.replaceState(null, '', '/');
    const tangents = document.getElementById('tangent-return');
    tangents?.focus?.({ preventScroll: true });
    const reducedMotion = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches;
    tangents?.scrollIntoView?.({ behavior: reducedMotion ? 'auto' : 'smooth', block: 'start' });
  });
  $('more-tangents').addEventListener('click', () => action($('more-tangents'), moreTangents));
  document.addEventListener('visibilitychange', () => { if (document.hidden) { liveController?.abort(); stopActivity(); } else { startLive(); startActivity(); } });
  $('more-rooms').addEventListener('click', () => action($('more-rooms'), async () => listRooms((await request('/api/rooms?page=' + nextRoomPage)).data, true)));
  $('more-messages').addEventListener('click', () => action($('more-messages'), () => historyPage(nextCursor)));
  $('message-text').addEventListener('input', () => { size(); rememberDraft(); });
  $('cancel-reply').addEventListener('click', () => { drafts.set(room.key, { text: $('message-text').value }); renderDraft(); });
  $('provision-room').addEventListener('click', () => action($('provision-room'), () => mutateRoom('/provision', {})));
  $('sync-room').addEventListener('click', () => action($('sync-room'), async () => { const key = room.key; status('Checking source repositories…'); await request(roomPath(key) + '/sync', {}); if (room?.key === key) { status(''); await choose(key); } }));
  $('acknowledge').addEventListener('click', () => action($('acknowledge'), async () => { await request(roomPath(room.key) + '/read-position', { cursor: resumeCursor }); status('Your read position is saved.'); }));
  $('create-room').addEventListener('submit', event => { event.preventDefault(); action(event.submitter, async () => {
    const body = Object.fromEntries(new FormData(event.target));
    if (activeTangentKey && currentTangents().length) {
      await request('/api/tangents/' + encodeURIComponent(activeTangentKey) + '/channels', body);
      event.target.reset(); await refreshTangents(); await choose(body.key); status('Channel created. Finish its setup to open conversation.');
      return;
    }
    await request('/api/rooms', body); event.target.reset(); await refreshRooms(); await choose(body.key); status('Room created. Finish its setup to open conversation.');
  }); });
  $('create-tangent').addEventListener('submit', event => { event.preventDefault(); action(event.submitter, async () => {
    const body = Object.fromEntries(new FormData(event.target));
    body.name = body.name.trim(); body.key = body.key.trim(); body.description = body.description.trim(); body.motto = body.motto.trim();
    if (!body.name || !body.key || !validArtwork(body.artwork)) throw new Error('Give this Tangent a name and stable address. Artwork must use HTTPS or a supplied sample.');
    await request('/api/tangents', body); event.target.reset(); await refreshTangents();
    const tangent = currentTangents().find(value => value.key === body.key);
    if (tangent) selectTangent(tangent);
    status('Your Tangent is ready. Add a channel whenever you know what it is for.');
  }); });
  $('create-tangent').addEventListener('input', renderCreatePreview);
  $('edit-tangent-form').addEventListener('input', renderEditorPreview);
  $('edit-tangent-form').addEventListener('submit', event => { event.preventDefault(); action(event.submitter, async () => {
    const tangent = tangentByKey.get(activeTangentKey); if (!tangent) throw new Error('Choose a Tangent before editing its card.');
    const body = Object.fromEntries(new FormData(event.target));
    body.name = body.name.trim(); body.description = body.description.trim(); body.motto = body.motto.trim(); body.artwork = body.artwork.trim();
    if (!body.name || !validArtwork(body.artwork)) throw new Error('Give this Tangent a name. Artwork must use HTTPS or a supplied sample.');
    await request('/api/tangents/' + encodeURIComponent(tangent.key), body, 'PATCH');
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
        intent = { operationId: crypto.randomUUID(), text: body, ...(reply ? { replyTo: reply } : {}) }; pending.set(key, intent);
      }
      sending.set(key, intent); renderDraft();
      try {
        const body = { operationId: intent.operationId, text: intent.text, ...(intent.replyTo ? { replyTo: intent.replyTo } : {}) };
        const receipt = (await request(roomPath(key) + '/messages', body)).data;
        if (identity !== identityEpoch) return;
        if (receipt.state === 'pending') {
          intent.detail = receipt.detail; intent.saved = true;
          if (current()) renderDraft();
          return;
        }
        pending.delete(key);
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
