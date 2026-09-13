'use strict';
(() => {
  const $ = id => document.getElementById(id);
  let site, room, epoch = 0, identityEpoch = 0, routeEpoch = 0, renderedRoute = '', nextRoomPage, nextCursor, resumeCursor, reply, restoring;
  const mentionCache = new Map();
  let activityTransport, activityActive = false, activityMode = 'stopped', activityOverviewNote = '', activityNeedsRefresh = false;
  let activityRecovering = false, activityRecoveryLabel = '', activityAutomaticRecoveries = 0, noticeTimer;
  let sourceReadiness, activeTangentKey, activitySignature = '', tangentNextPage;
  let routeTangent, routeTopics, routeFailure;
  const route = () => window.TangentPages?.route || { kind: 'home' };
  const tangentPath = key => '/api/v1/tangents/' + encodeURIComponent(key);
  const tangentUrl = key => window.TangentPages.tangentUrl(key);
  const topicUrl = (tangent, key) => window.TangentPages.topicUrl(tangent, key);
  const navigate = (url, replace = false) => window.TangentNavigation?.navigate(url, { replace }) || (replace ? location.replace(url) : location.assign(url));
  const emitWindow = (name, detail) => {
    if (typeof window.dispatchEvent === 'function' && typeof CustomEvent === 'function') window.dispatchEvent(new CustomEvent(name, { detail }));
    else window.emit?.(name, { detail });
  };
  const updateHero = () => routeFailure ? window.TangentPages?.unavailable(routeFailure) : window.TangentPages?.hero(site, route().tangent ? tangentByKey.get(route().tangent) || routeTangent : undefined, room);
  const activityByRoom = new Map();
  const tangentByKey = new Map();
  const pending = new Map();
  const postMutations = new Map();
  const recoveryBlocked = new Set();
  const drafts = new Map();
  const draftStoragePrefix = 'tangent-space:draft:v1:';
  const sending = new Map();
  const rendered = new Set();
  const visibleMessages = new Map();
  const newPosts = new Set();
  let acknowledgedSequence = -1, displayedSequence = 0, historyRevision = 0;
  let refreshingHistory = false;
  let queuedHistoryRefresh;
  const roomPath = key => '/api/rooms/' + encodeURIComponent(key);
  const text = (id, value) => { $(id).textContent = value ?? ''; };
  const show = (id, visible) => { $(id).hidden = !visible; };
  const field = (form, name) => $(form).elements.namedItem(name);
  function connectionStatus(message, state = activityActive ? activityMode === 'poll' ? 'polling' : 'live' : 'connecting') {
    const label = { live: 'Live', polling: 'Polling', reconnecting: 'Reconnecting…', paused: 'Paused', stopped: 'Offline', error: 'Offline' }[state] || 'Connecting…';
    const badgeLabel = { live: 'Live updates', polling: 'Polling for updates', reconnecting: 'Reconnecting…', paused: 'Updates paused', stopped: 'Offline', error: 'Offline' }[state] || 'Connecting…';
    const badgeText = $('activity-status-label');
    if (badgeText?.parentElement === $('activity-status')) badgeText.textContent = badgeLabel;
    else text('activity-status', label);
    $('activity-status').dataset.state = state;
    $('activity-status').title = message;
    $('activity-status').setAttribute('aria-label', message);
    text('conversation-connection', label);
    $('conversation-connection').dataset.state = state;
    $('conversation-connection').title = message;
  }
  function readingAnchor() {
    const composer = $('message-form');
    const focused = composer.contains?.(document.activeElement) ? composer : null;
    const node = focused || [...$('messages').children].find(item => { const rect = item.getBoundingClientRect?.(); return rect && rect.bottom > 0 && rect.top < window.innerHeight; });
    return node ? { id: node.id, top: node.getBoundingClientRect().top } : null;
  }
  function restoreReadingAnchor(anchor) {
    const node = anchor && document.getElementById(anchor.id);
    if (node?.getBoundingClientRect) window.scrollBy(0, node.getBoundingClientRect().top - anchor.top);
  }
  function updateNewPosts() {
    show('conversation-updates', newPosts.size > 0);
    text('new-posts-count', newPosts.size + (newPosts.size === 1 ? ' new post' : ' new posts'));
  }
  function checkpointState() {
    const visible = route().kind !== 'post' && rendered.size > 0 && !!resumeCursor;
    show('read-checkpoint', visible); show('acknowledge', visible);
    $('acknowledge').disabled = acknowledgedSequence >= displayedSequence;
    text('acknowledge', acknowledgedSequence >= displayedSequence ? 'Read position saved' : 'Mark these posts as read');
    text('read-state-note', nextCursor ? 'More posts follow. This marks only the posts loaded here.' : 'This marks only the posts loaded here.');
  }
  function status(message, error = false) { text('action-status', message); show('action-status', !!message); $('action-status').classList.toggle('error', error); }
  function notice(message) { status(message); if (noticeTimer) clearTimeout(noticeTimer); if (typeof setTimeout === 'function') noticeTimer = setTimeout(() => status(''), 8000); }
  function element(tag, className, value) { const node = document.createElement(tag); node.className = className; node.textContent = value; return node; }
  function safeAccent(value) { return /^#[0-9a-f]{6}$/i.test(value || '') ? value : '#d88957'; }
  function currentTangents() { return Array.isArray(site?.tangents?.tangents) ? site.tangents.tangents : []; }
  function activityFor(key) { return activityByRoom.get(key) || {}; }
  function can(subject, action) { const actions = subject?.permissions?.allowedActions || subject?.allowedActions; return Array.isArray(actions) && actions.includes(action); }
  async function request(path, body, method = 'POST', signal) {
    const response = await fetch(path, { method: body === undefined ? 'GET' : method, credentials: 'same-origin', cache: 'no-store',
      headers: { Accept: 'application/json', ...(body === undefined ? {} : { 'Content-Type': 'application/json' }) },
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
    show('server-settings-toggle', canManage && route().kind === 'home');
    show('server-settings', canManage && route().kind === 'settings');
    $('server-settings').open = canManage && route().kind === 'settings';
    show('settings-denied', route().kind === 'settings' && !canManage);
    if (canManage) {
      const form = $('server-settings-form');
      for (const key of ['name', 'byline', 'coverImageUrl', 'welcomeMessage', 'motd', 'creationPolicy']) if (form.elements.namedItem(key)) form.elements.namedItem(key).value = server[key] || (key === 'creationPolicy' ? 'owner_only' : '');
      form.elements.namedItem('allowAgentTangentOwnership').checked = server.allowAgentTangentOwnership === true;
      previewServerCard();
      text('server-permissions-summary', Array.isArray(server.permissions) ? server.permissions.map(p => [p.role, p.scope, (p.allowedActions || []).join(', ')].filter(Boolean).join(': ')).join(' · ') : '');
    }
    show('server-welcome', true);
    updateHero();
  }
  async function refreshServer(context) {
    const identity = identityEpoch, owner = site;
    if (context && !context.isCurrent()) return;
    try {
      const value = (await request('/api/server', undefined, 'GET', context?.signal)).data;
      if (identity !== identityEpoch || site !== owner || (context && !context.isCurrent())) return;
      if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('Server settings could not be read.');
      site.server = value; renderServerSettings(value);
    } catch (error) {
      // Activity may acknowledge an invalidation only after its dependent read succeeds.
      if (context) throw error;
    }
  }
  async function action(button, work) {
    const identity = identityEpoch;
    if (button) button.disabled = true;
    try { await work(); } catch (error) { if (identity === identityEpoch) status(error.message || 'The site could not be reached. Try again.', true); }
    finally { if (identity === identityEpoch) { if (button === $('send-message')) renderDraft(); else if (button === $('acknowledge')) checkpointState(); else if (button) button.disabled = false; } }
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
  async function refreshRooms(append = false, context) {
    const identity = identityEpoch, owner = site;
    if (context && !context.isCurrent()) return;
    if (activeTangentKey) {
      const key = activeTangentKey;
      const listing = (await request(tangentPath(key) + '/topics' + (append && nextRoomPage ? '?page=' + encodeURIComponent(nextRoomPage) : ''), undefined, 'GET', context?.signal)).data;
      if (identity !== identityEpoch || site !== owner || key !== activeTangentKey || (context && !context.isCurrent())) return;
      routeTopics = { ...listing, channels: append ? [...(routeTopics?.channels || []), ...(listing.channels || [])] : listing.channels || [] };
      listRooms(routeTopics);
    } else {
      const listing = (await request('/api/rooms', undefined, 'GET', context?.signal)).data;
      if (identity === identityEpoch && site === owner && !activeTangentKey && (!context || context.isCurrent())) listRooms(listing);
    }
  }
  function cardArtwork(card, tangent) {
    if (card.style) card.style.backgroundImage = '';
    card.classList.remove('has-artwork');
    if (typeof tangent.artwork === 'string' && tangent.artwork && validArtwork(tangent.artwork)) {
      card.classList.add('has-artwork');
    }
  }
  function validArtwork(value) { return !value || /^https:\/\//i.test(value) || /^\/tangent-art\/[a-z0-9-]+\.png$/i.test(value) || /^\/artwork\/[a-f0-9]{64}\.(png|jpg|webp)$/.test(value); }
  function cardContents(value, count = 0, countLabel = '·') {
    const face = element('span', 'tcg-art', '');
    if (value.artwork && validArtwork(value.artwork)) { const image = document.createElement('img'); image.src = value.artwork; image.alt = ''; image.loading = 'lazy'; face.append(image); }
    else face.append(element('span', 'tcg-emblem', '✦'));
    face.append(element('span', 'tcg-edition', 'TANGENT'));
    const stripe = element('span', 'tcg-divider', '');
    const disc = element('span', 'tcg-disc activity-disc', count ? countLabel : '✦'); disc.title = count ? countLabel + ' unread activity' : 'All caught up';
    const glass = element('span', 'tcg-pane', '');
    const unread = count === 1 ? '1 unread message' : count ? countLabel + ' unread messages' : 'No unread messages';
    glass.append(element('strong', 'tcg-name', value.name || value.key || 'Your Tangent'), element('span', 'tcg-description', value.description || 'A shared place for conversation.'));
    if (value.motto) glass.append(element('em', 'tcg-motto', '“' + value.motto + '”'));
    glass.append(element('span', 'tcg-foot tangent-card-footer', site?.participant ? unread : 'Explore this Tangent'));
    const foil = element('span', 'tcg-holo'), rainbow = element('span', 'tcg-holo2'), notch = element('span', 'tcg-notch');
    for (const decoration of [foil, rainbow, notch]) decoration.setAttribute('aria-hidden', 'true');
    return [face, stripe, disc, notch, glass, foil, rainbow];
  }
  function renderEditorPreview() {
    const form = $('edit-tangent-form'), preview = $('edit-card-preview'); if (!form || !preview) return;
    preview.classList.add('sylin-card');
    window.TangentArtwork?.refresh(form);
    const value = Object.fromEntries(new FormData(form)); preview.replaceChildren();
    if (preview.style) preview.style.setProperty('--tangent-accent', safeAccent(value.accent));
    cardArtwork(preview, value);
    preview.append(...cardContents({ ...value, name: value.name || 'Untitled Tangent' }));
  }
  function renderCreatePreview() {
    const form = $('create-tangent'), preview = $('create-card-preview'); if (!form || !preview) return;
    preview.classList.add('sylin-card');
    window.TangentArtwork?.refresh(form);
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
    $('edit-tangent-form').dataset.artworkKey = tangent.key;
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
    $('tangent-admin-choice').hidden = !tangent.isOwner;
    $('tangent-admin-choice').disabled = !tangent.isOwner;
    if (!tangent.isOwner && field('tangent-member-form', 'role').value === 'Admin') field('tangent-member-form', 'role').value = 'Member';
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
      const card = element('a', 'tangent-card sylin-card', ''); card.href = tangentUrl(tangent.key); card.dataset.key = tangent.key;
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
    show('create-tangent-open', !!site?.participant && !!site?.tangents?.canCreate && site?.onboarding === 'complete');
    const active = tangentByKey.get(activeTangentKey);
    show('community-settings', !!(site?.tangents?.setupRequired || site?.tangents?.canCreate || site?.participant?.isOwner || active?.canManage || active?.isOwner || active?.canCreateTopic === true));
    show('site-setup', site?.participant?.isOwner === true || active?.canCreateTopic === true || can(active, 'createTopic'));
    configureCreateForm();
    tangentNextPage = site?.tangents?.nextPage;
    show('more-tangents', !!tangentNextPage);
    const incomplete = site?.tangents?.directoryIncomplete || site?.tangents?.channelsIncomplete;
    text('directory-note', !incomplete ? '' : site?.tangents?.directoryIncomplete ? 'The directory is bounded while the server checks what this account can see. Load more or narrow to a Tangent.' : 'Some channel lists are bounded. Open a Tangent to continue browsing its channels.');
    show('directory-note', !!incomplete);
    renderCatchUp();
  }
  function previewServerCard() {
    if (!$('server-card-name')) return;
    window.TangentArtwork?.refresh($('server-settings-form'));
    const value = key => field('server-settings-form', key).value.trim();
    text('server-card-name', value('name') || 'Your place');
    text('server-card-byline', value('byline')); show('server-card-byline', !!value('byline'));
    text('server-card-description', value('welcomeMessage') || 'A place for your people and the conversations worth keeping.');
    text('server-card-motd', value('motd')); show('server-card-board', !!value('motd'));
    const image = $('server-card-image'), url = value('coverImageUrl');
    const valid = (/^https:\/\//i.test(url) || /^\/(?!\/)/.test(url)) && !/[\u0000-\u001f\\]/.test(url);
    if (valid && image.getAttribute('src') !== url) { image.hidden = false; image.onerror = () => { image.hidden = true; }; image.src = url; }
    else if (!valid) { image.hidden = true; image.removeAttribute('src'); }
  }
  function renderCatchUp() {
    const section = $('catch-up'), list = $('catch-up-list');
    if (!section || !list) return;
    const entries = currentTangents().flatMap(tangent => (tangent.channels || []).map(topic => ({ tangent, topic, activity: activityFor(topic.key) })))
      .filter(entry => entry.activity.unreadCount > 0 || entry.activity.directReplies > 0)
      .sort((a, b) => (b.activity.directReplies || 0) - (a.activity.directReplies || 0) || (b.activity.unreadCount || 0) - (a.activity.unreadCount || 0));
    section.hidden = route().kind !== 'home' || !site?.participant || !entries.length;
    list.replaceChildren();
    for (const { tangent, topic, activity } of entries.slice(0, 3)) {
      const item = document.createElement('li'), link = element('a', 'catch-up-link', '');
      link.href = topicUrl(tangent.key, topic.key);
      const context = element('span', 'catch-up-context', tangent.name);
      context.append(element('strong', '', topic.title));
      const count = activity.unreadCountCapped ? '50+' : String(activity.unreadCount || 0);
      const label = activity.directReplies > 0 ? activity.directReplies + (activity.directReplies === 1 ? ' reply to you' : ' replies to you') : count + (activity.unreadCount === 1 ? ' new post' : ' new posts');
      link.append(context, element('span', 'catch-up-count', label), element('span', 'catch-up-arrow', '↗')); item.append(link); list.append(item);
    }
    $('catch-up-more').hidden = entries.length <= 3;
  }
  async function refreshTangents(context) {
    const identity = identityEpoch, owner = site;
    if (context && !context.isCurrent()) return;
    const directory = (await request('/api/v1/tangents', undefined, 'GET', context?.signal)).data;
    if (identity !== identityEpoch || site !== owner || (context && !context.isCurrent())) return;
    if (!directory || !Array.isArray(directory.tangents)) throw new Error('Tangent directory could not be read.');
    site.tangents = directory; renderTangents();
    // A known unavailable route must not prevent global directory/activity recovery.
    if (route().tangent && !routeFailure) {
      const tangent = (await request(tangentPath(route().tangent), undefined, 'GET', context?.signal)).data;
      if (identity !== identityEpoch || site !== owner || (context && !context.isCurrent())) return;
      routeTangent = tangent;
      tangentByKey.set(routeTangent.key, routeTangent);
      await refreshRooms(false, context);
    }
    if (identity === identityEpoch && site === owner && (!context || context.isCurrent())) updateHero();
  }
  async function moreTangents() {
    if (!tangentNextPage) return;
    const identity = identityEpoch, owner = site;
    const page = (await request('/api/v1/tangents?page=' + encodeURIComponent(tangentNextPage))).data;
    if (identity !== identityEpoch || site !== owner) return;
    if (!page || !Array.isArray(page.tangents)) throw new Error('More Tangents could not be loaded.');
    const known = new Set(currentTangents().map(tangent => tangent.key));
    site.tangents.tangents.push(...page.tangents.filter(tangent => !known.has(tangent.key)));
    site.tangents.nextPage = page.nextPage; site.tangents.directoryIncomplete = !!page.directoryIncomplete; site.tangents.channelsIncomplete = !!page.channelsIncomplete;
    renderTangents();
  }
  function resetMessages() { historyRevision++; rendered.clear(); visibleMessages.clear(); newPosts.clear(); updateNewPosts(); $('messages').replaceChildren(); nextCursor = resumeCursor = undefined; show('more-messages', false); show('acknowledge', false); show('read-checkpoint', false); text('freshness', ''); }
  function unavailableRoute(code) {
    routeFailure = code; epoch++; resetMessages(); room = null; reply = undefined; sourceReadiness = undefined;
    window.TangentModeration?.topic?.(undefined, site?.participant);
    routeTangent = routeTopics = undefined;
    for (const id of ['room-title', 'room-topic', 'topic-permissions', 'room-access']) text(id, '');
    field('topic-form', 'topic').value = '';
    $('room-list').replaceChildren();
    show('room-content', false); show('more-rooms', false); show('choose-room', true);
    text('choose-room', code === 401 ? 'Sign in to continue.'
      : code === 403 ? 'You don’t have access to this conversation.'
      : 'This conversation isn’t available.');
    updateHero();
    startActivity(); // A missing/private route does not disconnect this participant's other activity.
  }
  function revokeSelectedRoom(code = 403) {
    const key = room?.key; if (!key) return;
    rememberDraft();
    for (const tangent of currentTangents()) tangent.channels = (tangent.channels || []).filter(channel => channel.key !== key);
    unavailableRoute(code);
  }
  function renderRoom(value) {
    room = value;
    window.TangentModeration?.topic?.(room, site?.participant);
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
    const version = ++epoch; resetMessages(); room = null; reply = undefined; acknowledgedSequence = -1;
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
  async function historyPage(cursor, version = epoch, key = room?.key, fromStart = false, context) {
    if (context && !context.isCurrent()) return false;
    if (route().kind === 'post') return refreshOpenHistory(false, context);
    if (!key) return;
    try {
      const revision = historyRevision, liveAppend = !!cursor && cursor === resumeCursor && !nextCursor;
      const page = (await request(roomPath(key) + '/messages' + (cursor ? '?cursor=' + encodeURIComponent(cursor) : fromStart ? '?from=start' : ''), undefined, 'GET', context?.signal)).data;
      if (context && !context.isCurrent()) return false;
      if (version !== epoch || room?.key !== key) return;
      if (revision !== historyRevision) {
        if (!nextCursor && resumeCursor) return historyPage(resumeCursor, version, key, false, context);
        return;
      }
      renderPage(page, true, liveAppend);
      startActivity();
      return true;
    } catch (error) {
      if (context && !context.isCurrent()) return false;
      if (version !== epoch) return;
      if (error.status === 403 || error.status === 401 || error.status === 404) revokeSelectedRoom(error.status);
      throw error;
    }
  }
  async function refreshOpenHistory(force = false, context) {
    if (context && !context.isCurrent()) return false;
    if (refreshingHistory) { if (context) throw new Error('History is already refreshing. Activity will retry.'); queuedHistoryRefresh = { key: room?.key, force: force || queuedHistoryRefresh?.force }; return; }
    if (!room?.canRead) return;
    if (!force && document.querySelector('.message-edit-form')) { if (context) throw new Error('Close the active edit to refresh activity.'); return; }
    const key = room.key, version = epoch, revision = historyRevision;
    refreshingHistory = true;
    try {
      let pages = [], topic;
      if (route().kind === 'post') {
        const result = (await request(tangentPath(route().tangent) + '/posts/' + encodeURIComponent(route().post), undefined, 'GET', context?.signal)).data;
        pages = [result.window]; topic = result.topic;
      } else {
        // Keep the loaded reading window, not just the first page, on an edit or removal.
        const count = rendered.size, cursors = new Set(); let cursor, loaded = 0;
        do {
          const page = (await request(roomPath(key) + '/messages' + (cursor ? '?cursor=' + encodeURIComponent(cursor) : '?from=start'), undefined, 'GET', context?.signal)).data;
          if (context && !context.isCurrent()) return false;
          pages.push(page); loaded += page.messages?.length || 0;
          cursor = page.nextCursor;
          if (!cursor || cursors.has(cursor) || !page.messages?.length) break;
          cursors.add(cursor);
        } while (loaded < count && version === epoch);
      }
      if (context && !context.isCurrent()) return false;
      if (version !== epoch || room?.key !== key) return;
      if (!force && document.querySelector('.message-edit-form')) { if (context) throw new Error('Close the active edit to refresh activity.'); return; }
      if (revision !== historyRevision) { queuedHistoryRefresh = { key, force }; return; }
      const anchor = readingAnchor(), previousPosts = new Set(rendered), previousNewPosts = new Set(newPosts);
      const open = [...$('messages').querySelectorAll('details[open]')].map(node => ({ post: node.closest('.message').id, kind: node.className }));
      resetMessages(); if (topic) renderRoom(topic);
      for (const page of pages) renderPage(page, route().kind !== 'post');
      for (const message of visibleMessages.values()) {
        if (previousNewPosts.has(message.id) || !previousPosts.has(message.id) && message.authorParticipantId !== site.participant?.participantRef && route().kind !== 'post') newPosts.add(message.id);
      }
      for (const id of newPosts) document.getElementById('post-' + id)?.classList.add('message-new');
      updateNewPosts();
      for (const state of open) {
        const detail = [...(document.getElementById(state.post)?.querySelectorAll('details') || [])].find(node => node.className === state.kind);
        if (detail) detail.open = true;
      }
      restoreReadingAnchor(anchor);
      startActivity();
      return true;
    } catch (error) {
      if (context && !context.isCurrent()) return false;
      if (version === epoch && [401, 403, 404].includes(error.status)) revokeSelectedRoom(error.status);
      throw error;
    } finally {
      refreshingHistory = false;
      const queued = queuedHistoryRefresh; queuedHistoryRefresh = undefined;
      if (queued?.key === room?.key && queued) action(null, () => refreshOpenHistory(queued.force));
    }
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
  function participantLabel(id, page) {
    const profile = page.resolved?.[id] || {};
    const handle = profile.handle || page.authorHandles?.[id] || '';
    return { ...profile, handle, name: profile.displayName || (handle ? '@' + handle.replace(/^@/, '') : 'Participant'),
      href: profile.profileUrl?.startsWith('/u/') ? profile.profileUrl : '/u/' + encodeURIComponent(profile.value || id) };
  }
  function replyPreview(reference) {
    const message = [...visibleMessages.values()].find(item => item.sourceUri === reference?.uri);
    return message ? { message, text: message.deleted || message.removed ? 'This post was removed.' : message.content?.text || 'Post' } : null;
  }
  function renderPage(page, nativeHistory = true, liveAppend = false) {
      const anchor = liveAppend ? readingAnchor() : null;
      historyRevision++;
      for (const message of page.messages || []) visibleMessages.set(message.id, message);
      for (const message of page.messages || []) {
        if (rendered.has(message.id)) continue;
        rendered.add(message.id);
        const li = element('li', 'message', '');
        li.id = 'post-' + message.id;
        if (route().kind === 'post' && message.id === route().post) { li.classList.add('message-anchor'); li.setAttribute('aria-current', 'true'); }
        const isYou = message.authorParticipantId === site.participant?.participantRef;
        if (liveAppend && !isYou) { newPosts.add(message.id); li.classList.add('message-new'); }
        const profile = participantLabel(message.authorParticipantId, page);
        const handle = profile.handle;
        const avatarLink = element('a', 'message-avatar', profile.name.replace(/^@/, '').slice(0, 1).toUpperCase());
        avatarLink.href = profile.href; avatarLink.setAttribute('aria-label', 'View ' + profile.name + '’s profile');
        avatarLink.dataset.profileDid = profile.value || ''; avatarLink.dataset.profilePart = 'avatar'; avatarLink.dataset.profileFallback = profile.name;
        if (typeof profile.avatar === 'string' && profile.avatar.startsWith('/api/profile-cache/avatar?')) {
          const image = document.createElement('img'); image.src = profile.avatar; image.alt = ''; image.loading = 'lazy'; image.referrerPolicy = 'no-referrer';
          image.addEventListener('error', () => image.remove(), { once: true }); avatarLink.append(image);
        }
        li.append(avatarLink);
        const byline = element('div', 'message-byline', '');
        const author = element('a', 'message-author author-link', profile.name); author.href = profile.href;
        author.dataset.profileDid = profile.value || ''; author.dataset.profilePart = 'name'; author.dataset.profileFallback = handle ? '@' + handle.replace(/^@/, '') : 'Participant';
        byline.append(author);
        if (isYou) byline.append(element('span', 'message-you', 'you'));
        if (handle && profile.displayName) byline.append(element('span', 'message-handle', '@' + handle.replace(/^@/, '')));
        if (profile.classification && !['Undeclared', 'Human'].includes(profile.classification)) byline.append(element('span', 'message-kind', profile.classification));
        const date = new Date(message.acceptedAt);
        const timestamp = element('time', 'message-time', date.toLocaleDateString(undefined, { month: 'short', day: 'numeric' }) + ' · ' + date.toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' }));
        timestamp.dateTime = message.acceptedAt; timestamp.title = date.toLocaleString(); byline.append(timestamp);
        li.append(byline);
        const content = message.content || {}, deleted = message.removed === true || message.deleted === true;
        if (content.replyTo) {
          const preview = replyPreview(content.replyTo);
          const label = preview ? participantLabel(preview.message.authorParticipantId, page).name + ' · ' + preview.text.replace(/\s+/g, ' ').slice(0, 150) : 'Reply to an earlier post';
          const context = element(preview ? 'a' : 'p', 'reply-label', '↳ ' + label);
          if (preview) { context.href = '#post-' + preview.message.id; context.title = preview.text; }
          li.append(context);
        }
        li.append(deleted ? element('p', 'message-text message-removed', 'This post was removed.')
          : window.TangentFacets?.renderFacetedText?.(content.text, message.facets, page.resolved)
            || element('p', 'message-text', content.text));
        const actions = message.permissions?.allowedActions || room.permissions?.allowedActions || [];
        const mayEdit = !deleted && actions.includes('editOwnPost') && isYou && !message._editing;
        const mayDelete = !deleted && (actions.includes('deleteOwnPost') && isYou || actions.includes('removePost'));
        const footer = element('div', 'message-footer', '');
        if (!deleted && (message.editedAt || message.changeId)) {
          const historyViewer = window.TangentHistory?.disclosure?.(message,
            { handles: page.authorHandles, resolved: page.resolved, viewerDid: site.participant?.participantRef });
          footer.append(historyViewer || element('span', 'message-edited', 'Edited'));
        }
        if (room.canWrite && !deleted) {
          const button = element('button', 'btn btn-quiet message-reply', 'Reply'); button.type = 'button';
          button.addEventListener('click', () => {
            if (pending.has(room.key)) return status('Finish or retry the pending message first.');
            saveDraft(room.key, { text: $('message-text').value, replyTo: { uri: message.sourceUri, cid: message.sourceCid } });
            renderDraft();
            if (!isYou) window.TangentFacets?.replyMention?.(profile.value || message.authorParticipantId, handle);
            rememberDraft(); $('message-text').focus();
          }); footer.append(button);
        }
        li.append(footer);
        const menu = element('details', 'message-menu', '');
        const summary = element('summary', '', '···'); summary.setAttribute('aria-label', 'Actions for post by ' + profile.name); summary.title = 'Post actions'; menu.append(summary);
        const controls = element('div', 'message-menu-panel', '');
        const permalink = element('a', 'post-permalink', 'Open post'); permalink.href = '/t/' + encodeURIComponent(room.tangentKey) + '/' + encodeURIComponent(message.id); controls.append(permalink);
        if (mayEdit) { const edit = element('button', 'btn btn-quiet', 'Edit post'); edit.type = 'button'; edit.addEventListener('click', () => { menu.open = false; beginEdit(li, message); }); controls.append(edit); }
        if (mayDelete) { const remove = element('button', 'btn btn-quiet message-danger', actions.includes('removePost') && !isYou ? 'Remove post' : 'Delete post'); remove.type = 'button'; remove.addEventListener('click', () => { menu.open = false; removeMessage(message); }); controls.append(remove); }
        window.TangentModeration?.postActions?.(controls, message, { room, menu, authorName: profile.name });
        const details = element('details', 'source-details', ''); details.append(element('summary', '', 'Source details'));
        for (const value of [message.authorParticipantId, message.sourceUri, message.sourceCid]) if (value) details.append(element('p', 'did', value));
        controls.append(details); menu.append(controls);
        menu.addEventListener('keydown', event => { if (event.key === 'Escape') { menu.open = false; summary.focus(); } });
        menu.addEventListener('toggle', () => { if (menu.open) document.querySelectorAll('.message-menu[open]').forEach(other => { if (other !== menu) other.open = false; }); });
        li.append(menu); $('messages').append(li);
      }
      nextCursor = nativeHistory ? page.nextCursor : undefined; resumeCursor = nativeHistory ? page.resumeCursor : undefined;
      displayedSequence = nextCursor ? Math.max(0, ...[...visibleMessages.values()].map(message => message.sequence || 0)) : page.boundary || 0;
      show('more-messages', !!nextCursor); checkpointState(); updateNewPosts();
      const freshness = { 'writer-checked': 'Latest source update received.', checked: 'Up to date with the source.', 'catching-up': 'Catching up with the source…', unavailable: 'The source is unavailable. You can still read saved posts.', 'authority-reauthorization-required': 'The owner needs to reconnect this Topic’s source.', 'not-yet-checked': 'Checking for source updates…' };
      const state = room.spaceState === 'Local' ? '' : freshness[page.freshness] || 'Checking for source updates…';
      text('freshness', state + (!rendered.size ? ' No posts yet. Start the conversation.' : ''));
      show('freshness', !!$('freshness').textContent.trim());
      restoreReadingAnchor(anchor);
  }
  function beginEdit(li, message) {
    const current = li.querySelector('.message-text'); if (!current) return;
    const key = room.key, mutationKey = (site.participant?.participantRef || site.participant.did) + ':' + key + ':' + message.id;
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
    const key = room.key, mutationKey = (site.participant?.participantRef || site.participant.did) + ':' + key + ':' + message.id;
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
  async function renderActivity(snapshot, context) {
    if (!context.isCurrent() || context.participant !== site?.participant?.participantRef) return false;
    const previousSequence = room ? activityFor(room.key).lastSequence : undefined;
    const signature = JSON.stringify({ channels: snapshot.channels, truncated: snapshot.channelsTruncated, reset: snapshot.resetRequired });
    const changed = signature !== activitySignature; activitySignature = signature;
    activityByRoom.clear();
    for (const channel of Array.isArray(snapshot.channels) ? snapshot.channels : []) {
      if (typeof channel.roomKey === 'string') activityByRoom.set(channel.roomKey, channel);
    }
    const selectedTangent = tangentByKey.get(room?.tangentKey || activeTangentKey);
    activityOverviewNote = snapshot.channelsIncomplete ? 'This overview is incomplete because the directory reached its current scan limit.' : snapshot.channelsTruncated ? 'This overview shows up to 100 visible topics.' : selectedTangent?.channelsIncomplete ? 'This Tangent’s topic directory is incomplete.' : selectedTangent?.nextChannelsPage ? 'This Tangent has more visible topics to load.' : snapshot.resetRequired ? 'Earlier activity markers have been reset.' : '';
    if (changed) {
      renderTangents();
      const activeChannels = currentTangents().flatMap(tangent => tangent.channels || []);
      const scope = room?.tangentKey || activeTangentKey;
      listRooms(routeTopics || { rooms: activeChannels.filter(channel => !scope || channel.tangentKey === scope) || [] });
      if (room) document.querySelectorAll('.room-link').forEach(button => button.setAttribute('aria-current', String(button.dataset.key === room.key)));
    }
    const selected = room ? activityByRoom.get(room.key) : undefined;
    const elsewhere = currentTangents().flatMap(tangent => (tangent.channels || []).map(topic => ({ tangent, topic, activity: activityFor(topic.key) })))
      .filter(entry => entry.topic.key !== room?.key && (entry.activity.unreadCount > 0 || entry.activity.directReplies > 0))
      .sort((a, b) => (b.activity.directReplies || 0) - (a.activity.directReplies || 0));
    show('conversation-elsewhere', elsewhere.length > 0);
    text('elsewhere-label', 'Elsewhere · ' + elsewhere.length + (elsewhere.length === 1 ? ' topic' : ' topics'));
    $('elsewhere-links').replaceChildren();
    for (const { tangent, topic, activity } of elsewhere.slice(0, 3)) {
      const count = activity.unreadCountCapped ? '50+' : activity.unreadCount;
      const link = element('a', '', tangent.name + ' / ' + topic.title + ' · ' + (activity.directReplies > 0 ? 'reply to you' : count + ' new'));
      link.href = topicUrl(tangent.key, topic.key); $('elsewhere-links').append(link);
    }
    if (elsewhere.length > 3) { const link = element('a', '', 'See all Tangents →'); link.href = '/tangents/'; $('elsewhere-links').append(link); }
    // Watch/mute policy also removes channels from activity. Absence is not an ACL result.
    if (room?.canRead && !selected && (changed || snapshot.resetRequired)) {
      const selectedRoom = room, version = epoch;
      try {
        const current = (await request(tangentPath(selectedRoom.tangentKey) + '/topics/' + encodeURIComponent(selectedRoom.key), undefined, 'GET', context.signal)).data;
        if (!context.isCurrent()) return false;
        if (version === epoch && room === selectedRoom) renderRoom(current);
      } catch (error) {
        if (!context.isCurrent()) return false;
        if (version === epoch && [401, 403, 404].includes(error.status)) revokeSelectedRoom(error.status);
        else throw error;
      }
    }
    const shouldRefresh = room && (snapshot.resetRequired || (Array.isArray(snapshot.events) && snapshot.events.some(event => event && event.roomKey === room.key)) || selected?.lastSequence !== previousSequence);
    const messageEvent = Array.isArray(snapshot.events) && snapshot.events.some(event => event && event.roomKey === room?.key && ['MessageChanged', 'MessageEdited', 'MessageDeleted', 'PostChanged', 'PostDeleted'].includes(event.kind));
    try {
      if ((messageEvent || snapshot.resetRequired || activityNeedsRefresh || route().kind === 'post' && shouldRefresh) && room?.canRead) {
        const refreshed = await refreshOpenHistory(false, context);
        if (room?.canRead && refreshed !== true) throw new Error('History changed during activity recovery. Retrying.');
      } else if (shouldRefresh && room.canRead && !nextCursor && resumeCursor) {
        const refreshed = await historyPage(resumeCursor, epoch, room.key, false, context);
        if (room?.canRead && refreshed !== true) throw new Error('History changed during activity recovery. Retrying.');
      }
    } catch (error) {
      // A known room denial has already cleared that route; it does not deny all activity.
      if (room || ![401, 403, 404].includes(error.status)) throw error;
    }
    if (!context.isCurrent()) return false;
    if (snapshot.resetRequired || snapshot.events?.some(event => event?.kind === 'ParticipantChanged')) await refreshServer(context);
    if (!context.isCurrent()) return false;
    if (snapshot.resetRequired || (Array.isArray(snapshot.events) && snapshot.events.some(event => ['TangentChanged', 'RoomChanged', 'MembershipChanged', 'ParticipantChanged'].includes(event?.kind)))) {
      await refreshTangents(context).then(async () => {
        if (!context.isCurrent()) return;
        if (room) {
          const key = room.key, version = epoch;
          try { const current = (await request(tangentPath(room.tangentKey) + '/topics/' + encodeURIComponent(key), undefined, 'GET', context.signal)).data; if (context.isCurrent() && version === epoch && room?.key === key) renderRoom(current); }
          catch (error) {
            if (!context.isCurrent() || version !== epoch) return;
            if ([401, 403, 404].includes(error.status)) revokeSelectedRoom(error.status);
            else throw error;
          }
        }
      });
    }
    return context.isCurrent();
  }
  function startActivity() {
    if (!site?.participant?.participantRef || activityRecovering || ['sign-in', 'onboarding'].includes(route().kind)) return;
    if (!activityTransport) {
      const activityFactory = window.TangentActivityCoordinator || window.TangentActivity;
      if (!activityFactory) { connectionStatus('Activity could not load. Reload this page to reconnect.', 'error'); return; }
      activityTransport = activityFactory.create({
        fetch: (path, options) => fetch(path, options),
        onSnapshot: async (snapshot, context) => {
          if (activityRecovering || !context.isCurrent() || context.participant !== site?.participant?.participantRef) return false;
          try {
            const accepted = await renderActivity(snapshot, context);
            if (accepted) { activityAutomaticRecoveries = 0; activityNeedsRefresh = false; }
            return accepted;
          } catch (error) {
            if (context.isCurrent()) { activityNeedsRefresh = true; activitySignature = ''; }
            throw error; // Failed invalidation must not advance the transport checkpoint.
          }
        },
        onIdentity: reloadIdentity,
        onState: state => {
          activityMode = state.mode;
          activityActive = state.status === 'live';
          if (activityRecovering) return;
          if (activityActive) {
            const message = state.mode === 'poll' ? 'Activity is connected using polling. Live streaming will be retried.' : 'Live activity is connected.';
            connectionStatus(message + (activityOverviewNote ? ' ' + activityOverviewNote : ''), state.mode === 'poll' ? 'polling' : 'live');
          } else if (state.status === 'paused') connectionStatus('Updates are paused while this page is hidden.', 'paused');
          else if (state.status === 'reconnecting') connectionStatus('Activity is reconnecting. Displayed information may be stale.', 'reconnecting');
          else if (state.status === 'identity') connectionStatus('Your session is being checked. Displayed information may be stale.', 'connecting');
          else connectionStatus('Activity is connecting. Displayed information may be stale.', state.status);
        }
      });
    }
    activityTransport.setVisible(!document.hidden);
    activityTransport.start({ participant: site.participant.participantRef });
  }
  function reloadIdentity(detail) {
    // Token-only sessions: the server pushed an identity_changed event for this session —
    // the browser signed in as another account somewhere. The browser holds only the
    // session token, so re-run the page's welcome flow; the tangent:welcome listener
    // re-fetches the world as the current identity and acknowledges it.
    if (activityRecovering) return;
    rememberDraft();
    activityRecovering = true;
    stopActivity();
    // Quarantine the old world immediately, before the asynchronous welcome request.
    // Retain the old actor on site solely so the welcome transition clears its drafts
    // and uncertain operations correctly; nothing is re-attributed or automatically sent.
    identityEpoch++; epoch++; resetMessages(); room = null; reply = undefined; sourceReadiness = undefined;
    routeTangent = routeTopics = undefined; activityByRoom.clear(); tangentByKey.clear();
    activitySignature = ''; activityOverviewNote = '';
    for (const id of ['room-content', 'place', 'community-settings', 'settings-page', 'server-welcome']) show(id, false);
    activityRecoveryLabel = typeof detail?.bestLabel === 'string' ? detail.bestLabel : '';
    connectionStatus('Your signed-in account changed. Updating…', 'connecting');
    if (++activityAutomaticRecoveries > 1) {
      connectionStatus('Your session or access needs attention. Sign in or retry to reconnect.', 'error');
      status('Activity could not verify your current access. Sign in or retry.', true);
      return;
    }
    const retry = document.getElementById('retry');
    if (retry && typeof retry.click === 'function') retry.click();
    else status('Your signed-in account changed. Reload this page.', true);
  }
  function stopActivity() { activityNeedsRefresh = true; activityTransport?.stop(); activityActive = false; }
  function storedDraftKey(key, actor = site?.participant?.participantRef) {
    return actor && key ? draftStoragePrefix + encodeURIComponent(actor) + ':' + encodeURIComponent(key) : '';
  }
  function readStoredDraft(key) {
    if (drafts.has(key)) return drafts.get(key);
    const storageKey = storedDraftKey(key); if (!storageKey) return undefined;
    try {
      const value = JSON.parse(window.sessionStorage.getItem(storageKey));
      const text = typeof value?.text === 'string' && value.text.length <= 4096 ? value.text : '';
      const replyTo = value?.replyTo && typeof value.replyTo.uri === 'string' && typeof value.replyTo.cid === 'string'
        ? { uri: value.replyTo.uri, cid: value.replyTo.cid } : undefined;
      if (!text && !replyTo) { window.sessionStorage.removeItem(storageKey); return undefined; }
      const draft = { text, ...(replyTo ? { replyTo } : {}) }; drafts.set(key, draft); return draft;
    } catch (_) { return undefined; }
  }
  function saveDraft(key, draft) {
    if (!key) return;
    drafts.set(key, draft);
    const storageKey = storedDraftKey(key); if (!storageKey) return;
    try {
      if (draft?.text || draft?.replyTo) window.sessionStorage.setItem(storageKey, JSON.stringify(draft));
      else window.sessionStorage.removeItem(storageKey);
    } catch (_) { /* A private or full browser store must not interrupt composing. */ }
  }
  function forgetDraft(key) {
    drafts.delete(key);
    const storageKey = storedDraftKey(key); if (!storageKey) return;
    try { window.sessionStorage.removeItem(storageKey); } catch (_) { }
  }
  function forgetStoredDrafts(actor) {
    if (!actor) return;
    const prefix = draftStoragePrefix + encodeURIComponent(actor) + ':';
    try {
      for (let index = window.sessionStorage.length - 1; index >= 0; index--) {
        const key = window.sessionStorage.key(index);
        if (key?.startsWith(prefix)) window.sessionStorage.removeItem(key);
      }
    } catch (_) { }
  }
  function renderDraft() {
    const intent = room && pending.get(room.key);
    if (intent) { $('message-text').value = intent.text; reply = intent.replyTo; }
    else { const draft = room && readStoredDraft(room.key); $('message-text').value = draft?.text || ''; reply = draft?.replyTo; }
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
    const preview = replyPreview(reply);
    text('reply-context', reply ? 'Replying to: ' + (preview?.text.replace(/\s+/g, ' ').slice(0, 140) || 'an earlier post') : ''); show('reply-context', !!reply); show('cancel-reply', !!reply && !intent);
    size();
  }
  function rememberDraft() { if (room?.key && !pending.has(room.key)) saveDraft(room.key, { text: $('message-text').value, replyTo: reply }); }
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
  // Administrative surfaces follow the token's permissions: a denial means this identity no
  // longer administers here, so route back to the landing page instead of a bare error.
  function administrativeDenied() { location.assign('/'); }
  async function mutateRoom(suffix, body, method) {
    const key = room.key;
    try { await request(roomPath(key) + suffix, body, method); }
    catch (error) { if (error.status === 403) { administrativeDenied(); return; } throw error; }
    status('Saved.'); await refreshRooms(); if (room?.key === key) await choose(key);
  }
  window.TangentRooms = window.TangentRooms || {};
  window.TangentRooms.size = size;
  function enterRoute(event) {
    let acknowledge = '';
    const nextRoute = route(), routeKey = [nextRoute.kind, nextRoute.tangent, nextRoute.topic, nextRoute.post, nextRoute.identifier].filter(Boolean).join(':');
    const routeChanged = !!renderedRoute && renderedRoute !== routeKey;
    renderedRoute = routeKey;
    const navigation = ++routeEpoch;
    if (site?.participant?.participantRef !== event.detail.participant?.participantRef) {
      // Identity events re-render the world as the new participant. Drafts and pending
      // outbound messages belong to the identity that wrote them; they are dropped here
      // rather than silently re-attributed to the new account.
      const recovered = activityRecovering, previousActor = site?.participant?.participantRef;
      forgetStoredDrafts(previousActor);
      identityEpoch++; epoch++; resetMessages(); stopActivity(); pending.clear(); recoveryBlocked.clear(); drafts.clear(); sending.clear(); restoring = undefined; room = null; reply = undefined; sourceReadiness = undefined; activeTangentKey = undefined; routeTangent = routeTopics = undefined; activityByRoom.clear(); tangentByKey.clear();
      window.TangentModeration?.topic?.(undefined, event.detail.participant);
      activityAutomaticRecoveries = 0;
      window.TangentFacets?.clearAll?.();
      mentionCache.clear(); postMutations.clear(); activitySignature = ''; activityOverviewNote = '';
      $('tangent-list')?.replaceChildren(); $('room-list')?.replaceChildren(); window.TangentProfile?.clear?.();
      renderDraft(); show('room-content', false); show('choose-room', true); text('choose-room', 'Choose a room to see its topic and current access.'); status('');
      if (recovered) acknowledge = 'Now viewing as ' + (event.detail.participant?.handle || activityRecoveryLabel || event.detail.participant?.did || event.detail.participant?.participantRef || 'another account') + '.';
    } else if (routeChanged) {
      rememberDraft(); epoch++; resetMessages(); room = null; reply = undefined; sourceReadiness = undefined;
      activeTangentKey = undefined; routeTangent = routeTopics = undefined; routeFailure = undefined;
      window.TangentModeration?.topic?.(undefined, event.detail.participant);
      document.body.classList.remove('conversation-open');
      renderDraft(); show('room-content', false); show('choose-room', true); status('');
    }
    activityRecovering = false;
    site = event.detail;
    routeFailure = undefined;
    if (['sign-in', 'onboarding'].includes(route().kind)) return;
    if (['participant', 'settings'].includes(route().kind)) { window.TangentModeration?.topic?.(undefined, site.participant); refreshServer(); show('place', false); startActivity(); return; }
    show('place', true); document.body.classList.add('has-rooms'); refreshServer();
    show('site-setup', site.participant?.isOwner === true || currentTangents().some(t => t.canCreateTopic === true));
    const identity = identityEpoch, welcome = site;
    const flow = action(null, async () => {
      try {
      const legacy = new URL(location.href).searchParams.get('room');
      if (legacy && route().kind === 'home') {
        const topic = (await request(roomPath(legacy))).data;
          if (identity === identityEpoch && navigation === routeEpoch) navigate(topicUrl(topic.tangentKey, topic.key), true);
          return;
      }
      if (!welcome.tangents) {
        const directory = (await request('/api/v1/tangents')).data;
        if (identity !== identityEpoch || navigation !== routeEpoch || site !== welcome) return;
        welcome.tangents = directory;
      }
      if (identity !== identityEpoch || navigation !== routeEpoch) return;
      renderTangents();
      if (route().tangent) {
        const tangent = tangentByKey.get(route().tangent) || (await request(tangentPath(route().tangent))).data;
        if (identity !== identityEpoch || navigation !== routeEpoch) return;
        routeTangent = tangent;
        tangentByKey.set(routeTangent.key, routeTangent);
        selectTangent(routeTangent);
        const routeContext = { isCurrent: () => identity === identityEpoch && navigation === routeEpoch && site === welcome };
        await refreshRooms(false, routeContext);
        if (identity !== identityEpoch || navigation !== routeEpoch) return;
        if (route().kind === 'topic') await choose(route().topic);
        else if (route().kind === 'post') await openPost();
        if (location.hash === '#tangent-members' && (routeTangent.canManage || routeTangent.isOwner)) {
          const participant = new URL(location.href).searchParams.get('participant');
          if (participant && participant.length <= 2048) {
            $('community-settings').open = true; $('tangent-members').open = true;
            field('tangent-member-form', 'did').value = participant;
            field('tangent-member-form', 'did').focus();
            $('tangent-members').scrollIntoView({ block: 'center' });
          }
        }
      }
      if (navigation === routeEpoch) updateHero();
      } catch (error) {
        if (identity === identityEpoch && navigation === routeEpoch && [401, 403, 404].includes(error.status)) unavailableRoute(error.status);
        throw error;
      }
    });
    startActivity();
    // The identity acknowledgment lands once the re-loaded world has settled, so opening
    // the route cannot clear it again.
    flow.then(() => {
      if (identity !== identityEpoch || navigation !== routeEpoch) return;
      if (acknowledge) notice(acknowledge);
      const href = location.pathname === undefined ? new URL(location.href).pathname : location.pathname + location.search + location.hash;
      emitWindow('tangent:route-ready', { href });
    });
    try { if (sessionStorage.getItem('tangent-created') === site.participant?.participantRef) { sessionStorage.removeItem('tangent-created'); status('Your Tangent is ready. Make yourself at home.'); } } catch (_) { }
  }
  window.addEventListener('tangent:welcome', enterRoute);
  window.addEventListener('tangent:route', enterRoute);
  $('refresh-rooms').addEventListener('click', () => action($('refresh-rooms'), refreshRooms));
  $('return-to-tangents').addEventListener('click', () => {
    rememberDraft(); navigate('/tangents/');
  });
  $('more-tangents').addEventListener('click', () => action($('more-tangents'), moreTangents));
  document.addEventListener('visibilitychange', () => { if (document.hidden) { activityNeedsRefresh = true; activityTransport?.setVisible(false); } else startActivity(); });
  window.addEventListener('pagehide', () => { rememberDraft(); stopActivity(); });
  window.addEventListener('pageshow', () => startActivity());
  $('more-rooms').addEventListener('click', () => action($('more-rooms'), () => refreshRooms(true)));
  $('more-messages').addEventListener('click', () => action($('more-messages'), () => historyPage(nextCursor)));
  $('view-new-posts').addEventListener('click', () => {
    const first = [...newPosts].map(id => document.getElementById('post-' + id)).find(Boolean);
    newPosts.clear(); updateNewPosts();
    if (first) { first.tabIndex = -1; first.focus({ preventScroll: true }); first.scrollIntoView({ block: 'start', behavior: 'auto' }); }
  });
  $('conversation-elsewhere').addEventListener('keydown', event => { if (event.key === 'Escape') { $('conversation-elsewhere').open = false; $('elsewhere-label').focus(); } });
  $('message-text').addEventListener('input', () => { size(); rememberDraft(); });
  $('cancel-reply').addEventListener('click', () => { saveDraft(room.key, { text: $('message-text').value }); renderDraft(); });
  $('provision-room').addEventListener('click', () => action($('provision-room'), () => mutateRoom('/provision', {})));
  $('sync-room').addEventListener('click', () => action($('sync-room'), async () => { const key = room.key; status(room.spaceState === 'Local' ? 'Checking for updates…' : 'Checking source repositories…'); await request(roomPath(key) + '/sync', {}); if (room?.key === key) { status(''); await choose(key); } }));
  $('acknowledge').addEventListener('click', () => action($('acknowledge'), async () => {
    if (route().kind === 'post' || !resumeCursor) return;
    const key = room.key, version = epoch, cursor = resumeCursor, sequence = displayedSequence;
    await request(roomPath(key) + '/read-position', { cursor });
    if (version !== epoch || room?.key !== key) return;
    acknowledgedSequence = Math.max(acknowledgedSequence, sequence);
    for (const id of newPosts) if ((visibleMessages.get(id)?.sequence ?? Infinity) <= sequence) newPosts.delete(id);
    for (const message of visibleMessages.values()) if (message.sequence <= sequence) document.getElementById('post-' + message.id)?.classList.remove('message-new');
    updateNewPosts(); checkpointState();
  }));
  $('create-room').addEventListener('submit', event => { event.preventDefault(); action(event.submitter, async () => {
    const body = Object.fromEntries(new FormData(event.target));
    if (activeTangentKey) {
      await request(tangentPath(activeTangentKey) + '/topics', body);
      event.target.reset(); navigate(topicUrl(activeTangentKey, body.key));
      return;
    }
    await request('/api/rooms', body); event.target.reset(); const created = (await request(roomPath(body.key))).data; navigate(topicUrl(created.tangentKey, created.key));
  }); });
  $('create-tangent').addEventListener('submit', event => { event.preventDefault(); action(event.submitter, async () => {
    const body = Object.fromEntries(new FormData(event.target));
    body.name = body.name.trim(); body.key = body.key.trim(); body.description = body.description.trim(); body.motto = body.motto.trim();
    if (!body.name || !body.key || !validArtwork(body.artwork)) throw new Error('Give this Tangent a name and stable address. Artwork must use HTTPS or a supplied sample.');
    await request('/api/v1/tangents', body); event.target.reset(); await refreshTangents();
    navigate(tangentUrl(body.key));
  }); });
  $('create-tangent').addEventListener('input', renderCreatePreview);
  $('edit-tangent-form').addEventListener('input', renderEditorPreview);
  $('edit-tangent-form').addEventListener('submit', event => { event.preventDefault(); action(event.submitter, async () => {
    const tangent = tangentByKey.get(activeTangentKey); if (!tangent) throw new Error('Choose a Tangent before editing its card.');
    const body = Object.fromEntries(new FormData(event.target));
    body.name = body.name.trim(); body.description = body.description.trim(); body.motto = body.motto.trim(); body.artwork = body.artwork.trim();
    if (!body.name || !validArtwork(body.artwork)) throw new Error('Give this Tangent a name. Artwork must use HTTPS or a supplied sample.');
    try { await request(tangentPath(tangent.key), body, 'PATCH'); }
    catch (error) { if (error.status === 403) { administrativeDenied(); return; } throw error; }
    await refreshTangents(); const updated = tangentByKey.get(tangent.key); if (updated) { activeTangentKey = updated.key; openTangentEditor(updated); }
    status('Tangent card saved.');
  }); });
  $('tangent-member-form').addEventListener('submit', event => { event.preventDefault(); action(event.submitter, async () => {
    const did = field('tangent-member-form', 'did').value.trim(), role = field('tangent-member-form', 'role').value;
    if (!activeTangentKey || !did) throw new Error('Choose a Tangent and enter an account handle or DID.');
    await request('/api/tangents/' + encodeURIComponent(activeTangentKey) + '/roles/' + encodeURIComponent(did), { role }, 'PUT');
    field('tangent-member-form', 'did').value = ''; status('Tangent role saved.'); await refreshTangents();
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
  $('server-settings-form').addEventListener('input', previewServerCard);
  $('server-settings-form').addEventListener('submit', event => { event.preventDefault(); action(event.submitter, async () => { const body = Object.fromEntries(new FormData(event.target)); body.allowAgentTangentOwnership = field('server-settings-form', 'allowAgentTangentOwnership').checked; try { await request('/api/server', body, 'PATCH'); } catch (error) { text('settings-status', error.status === 403 ? 'Your account can no longer change these settings.' : 'Settings could not be saved. Please try again.'); if (error.status === 403) { administrativeDenied(); return; } throw error; } await refreshServer(); text('settings-status', 'Server settings saved.'); }); });
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
          if (draft?.text === intent.text && draft.replyTo?.uri === intent.replyTo?.uri && draft.replyTo?.cid === intent.replyTo?.cid) forgetDraft(key);
        } else saveDraft(key, { text: intent.text, replyTo: intent.replyTo });
        if (room?.key === key) {
          const completionVersion = epoch;
          const completionCurrent = () => identity === identityEpoch && completionVersion === epoch && room?.key === key;
          reply = undefined; renderDraft();
          // Refresh in place. Reloading the Topic would discard the reading window and
          // move the composer. Recover the next saved intent without re-entering it.
          await restorePending(completionVersion, key);
          if (completionCurrent()) {
            status(receipt.state === 'accepted' ? 'Post sent.' : 'This post did not meet the topic’s current rules.', receipt.state !== 'accepted');
            try {
              if (route().kind === 'post') await refreshOpenHistory();
              else if (!nextCursor) await historyPage(resumeCursor, completionVersion, key, !resumeCursor);
            } catch (error) {
              if (completionCurrent() && receipt.state === 'accepted') status('Post sent. The conversation could not refresh; use Check for updates to see it.', true);
              else throw error;
            }
          }
        }
      } catch (error) { if (current()) throw error; }
      finally { if (sending.get(key) === intent) sending.delete(key); }
    });
  });
})();
