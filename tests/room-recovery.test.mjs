import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { randomUUID } from 'node:crypto';
import vm from 'node:vm';

const source = await readFile(new URL('../src/server/web/wwwroot/rooms.js', import.meta.url), 'utf8');
const activitySource = await readFile(new URL('../src/server/web/wwwroot/activity-transport.js', import.meta.url), 'utf8');
const activitySnapshot = (participantRef = 'ref-did:plc:alice') => ({ participantRef, checkpoint: 'c0', events: [], channels: [],
  hasMore: false, resetRequired: false, nextCursor: null, nextChannelCursor: null });
const settle = async () => { for (let turn = 0; turn < 5; turn++) await new Promise(resolve => setImmediate(resolve)); };
function memoryStorage(entries = new Map()) {
  return {
    get length() { return entries.size; },
    key(index) { return [...entries.keys()][index] ?? null; },
    getItem(key) { return entries.get(key) ?? null; },
    setItem(key, value) { entries.set(key, String(value)); },
    removeItem(key) { entries.delete(key); }
  };
}

// Exercise the browser script's event/request boundary without a live account.
// The DOM stub only supplies rendering primitives.
function fixture(respond = () => ({ messages: [] }), sessionStorage = memoryStorage(), roomOptions = {}) {
  class Element {
    constructor() {
      this.children = []; this.listeners = new Map(); this.value = ''; this.hidden = true; this.textContent = ''; this.dataset = {};
      this.classList = { add() {}, remove() {}, toggle() {} };
      this.style = { setProperty() {} };
      const fields = new Map();
      this.elements = { namedItem: name => { if (!fields.has(name)) fields.set(name, new Element()); return fields.get(name); } };
    }
    addEventListener(name, handler) { const all = this.listeners.get(name) || []; all.push(handler); this.listeners.set(name, all); }
    emit(name, values = {}) { for (const listener of this.listeners.get(name) || []) listener({ preventDefault() {}, ...values }); }
    append(...children) { this.children.push(...children); }
    replaceChildren(...children) { this.children = [...children]; }
    setAttribute(name, value) { this[name] = value; }
    click() { this.emit('click'); }
    focus() {}
    querySelector() { return null; }
    querySelectorAll() { return []; }
    contains(child) { return this.children.includes(child); }
  }
  const elements = new Map();
  const get = id => { if (!elements.has(id)) elements.set(id, new Element()); return elements.get(id); };
  const window = new Element(), document = new Element(), requests = [], timers = [];
  let timerId = 0;
  const setTimer = (fn, ms) => { const id = ++timerId; timers.push({ id, fn, ms }); return id; };
  const clearTimer = id => { const index = timers.findIndex(timer => timer.id === id); if (index >= 0) timers.splice(index, 1); };
  Object.assign(window, { setTimeout: setTimer, clearTimeout: clearTimer, ReadableStream });
  window.sessionStorage = sessionStorage;
  Object.assign(document, { hidden: true, body: new Element(), getElementById: get, createElement: () => new Element(), querySelectorAll: () => get('room-list').children });
  const route = { kind: 'topic', tangent: 'home', topic: 'lounge' };
  window.TangentPages = { route,
    tangentUrl: key => '/t/' + key + '/topics', topicUrl: (tangent, topic) => '/t/' + tangent + '/topics/' + topic,
    hero() {}, prepare() {}, unavailable() {} };
  const location = { href: 'http://127.0.0.1:5220/t/home/topics/lounge', assign: path => { location.href = new URL(path, location.href).href; }, replace: path => { location.href = new URL(path, location.href).href; } };
  const roomFields = key => typeof roomOptions === 'function' ? roomOptions(key) : roomOptions;
  class FormData { constructor() {} *[Symbol.iterator]() {} }
  vm.runInNewContext(activitySource + '\n' + source, { window, document, location, URL, TextEncoder, TextDecoder, AbortController, FormData, crypto: { randomUUID }, setImmediate,
    ReadableStream,
    setTimeout: setTimer, clearTimeout: clearTimer,
    history: { replaceState: (_state, _title, path) => { location.href = new URL(path, location.href).href; } },
    fetch: async (path, options) => {
      const request = { path, method: options.method, headers: options.headers, body: options.body && JSON.parse(options.body) }; requests.push(request);
      let data;
      const clean = path.split('?')[0], segments = clean.split('/');
      // The stubbed world answers the full route/load surface; `respond` stays the seam for writes.
      if (path === '/api/server') data = {};
      else if (path === '/api/v1/tangents') data = { tangents: [] };
      else if (/^\/api\/v1\/tangents\/[^/]+\/topics$/.test(clean)) data = { channels: [{ key: 'lounge', title: 'Lounge', tangentKey: 'home' }, { key: 'workshop', title: 'Workshop', tangentKey: 'home' }] };
      else if (/^\/api\/v1\/tangents\/[^/]+\/topics\/[^/]+$/.test(clean)) data = { key: segments.at(-1), title: segments.at(-1), tangentKey: segments.at(-2), canRead: true, canWrite: true, ...roomFields(segments.at(-1)) };
      else if (/^\/api\/v1\/tangents\/[^/]+$/.test(clean)) data = { key: segments.at(-1), name: 'T-' + segments.at(-1), channels: [] };
      else if (/^\/api\/rooms\/[^/]+$/.test(path)) data = { key: path.split('/').at(-1), title: path.split('/').at(-1), tangentKey: 'home', canRead: true, canWrite: true, ...roomFields(path.split('/').at(-1)) };
      else if (path.includes('/messages?')) data = { messages: [] };
      else data = await respond(request);
      if (data instanceof Response) return data;
      return new Response(JSON.stringify(data), { headers: { 'Content-Type': 'application/json' } });
    }
  });
  let lastDetail = null;
  return { get, requests, timers, document, window, welcome(did = 'did:plc:alice', key = 'lounge', tangents) {
    route.topic = key;
    location.href = 'http://127.0.0.1:5220/t/home/topics/' + key;
    lastDetail = { participant: { participantRef: 'ref-' + did, did }, rooms: { rooms: [{ key: 'lounge', title: 'Lounge' }, { key: 'workshop', title: 'Workshop' }] }, ...(tangents ? { tangents } : {}) };
    window.emit('tangent:welcome', { detail: lastDetail });
  }, choose(key) {
    // Room links navigate the browser: the page re-runs its welcome flow for the new route.
    route.topic = key;
    location.href = 'http://127.0.0.1:5220/t/home/topics/' + key;
    if (lastDetail) window.emit('tangent:welcome', { detail: lastDetail });
  },
  submit() { get('message-form').emit('submit'); },
  type(value) { get('message-text').value = value; get('message-text').emit('input'); } };
}

const ownerRoom = {
  tangentKey: 'home', topic: 'Saved description', admission: 'InvitationOnly', readAudience: 'Restricted',
  canManage: true, canAppointManagers: true, allowPostEditing: false, isLocked: false
};
const audienceFields = f => ({
  form: f.get('read-audience-form'),
  audience: f.get('read-audience-form').elements.namedItem('audience'),
  confirmation: f.get('read-audience-form').elements.namedItem('publishExistingHistory')
});
const audienceWrites = f => f.requests.filter(request => request.path.endsWith('/read-audience') && request.method === 'PUT');

test('Topic managers can manage conversation settings without owner-only access or manager assignment controls', async () => {
  const f = fixture(undefined, undefined, { ...ownerRoom, canAppointManagers: false });
  f.get('member-form').elements.namedItem('role').value = 'Manager';
  f.welcome(); await settle();
  assert.equal(f.get('room-admin').hidden, false);
  assert.equal(f.get('topic-settings-form').hidden, false);
  assert.equal(f.get('topic-access-settings').hidden, true);
  assert.equal(f.get('admission-form').hidden, true);
  assert.equal(f.get('read-audience-form').hidden, true);
  assert.equal(f.get('manager-choice').hidden, true);
  assert.equal(f.get('manager-choice').disabled, true);
  assert.equal(f.get('member-form').elements.namedItem('role').value, 'Member');
});

test('owners see access controls and history confirmation only when selecting new public reading', async () => {
  const f = fixture(undefined, undefined, ownerRoom); f.welcome(); await settle();
  const { audience, confirmation } = audienceFields(f);
  assert.equal(f.get('topic-access-settings').hidden, false);
  assert.equal(f.get('admission-form').hidden, false);
  assert.equal(f.get('read-audience-form').hidden, false);
  assert.equal(f.get('manager-choice').disabled, false);
  assert.equal(audience.value, 'Restricted');
  assert.equal(f.get('publish-history-confirmation').hidden, true);
  assert.equal(confirmation.disabled, true);
  assert.equal(confirmation.required, false);
  audience.value = 'Public'; audience.emit('change');
  assert.equal(f.get('publish-history-confirmation').hidden, false);
  assert.equal(confirmation.disabled, false);
  assert.equal(confirmation.required, true);
  assert.match(f.get('read-audience-note').textContent, /without signing in/i);
  confirmation.checked = true;
  audience.value = 'Restricted'; audience.emit('change');
  assert.equal(f.get('publish-history-confirmation').hidden, true);
  assert.equal(confirmation.disabled, true);
  assert.equal(confirmation.required, false);
  assert.equal(confirmation.checked, false);
  audience.value = 'Public'; audience.emit('change');
  assert.equal(confirmation.checked, false, 'a new publication choice needs a fresh acknowledgement');
});

test('already-public reading and restricting it do not request another publication acknowledgement', async () => {
  const f = fixture(undefined, undefined, { ...ownerRoom, readAudience: 'Public' }); f.welcome(); await settle();
  const { audience, confirmation } = audienceFields(f);
  assert.equal(audience.value, 'Public');
  assert.equal(f.get('publish-history-confirmation').hidden, true);
  assert.equal(confirmation.required, false);
  audience.value = 'Restricted'; audience.emit('change');
  assert.equal(f.get('publish-history-confirmation').hidden, true);
  assert.equal(confirmation.disabled, true);
  assert.equal(confirmation.required, false);
  assert.match(f.get('read-audience-note').textContent, /copies|copied|archives/i);
});

test('publishing rejects an unconfirmed submit before sending any request', async () => {
  const f = fixture(undefined, undefined, ownerRoom); f.welcome(); await settle();
  const { form, audience } = audienceFields(f);
  audience.value = 'Public'; audience.emit('change');
  form.emit('submit', { submitter: f.get('audience-save') }); await settle();
  assert.equal(audienceWrites(f).length, 0);
  assert.match(f.get('action-status').textContent, /confirm|acknowledge/i);
  assert.equal(audience.value, 'Public', 'a validation error preserves the pending choice');
});

test('confirmed publication sends only the selected audience and explicit history acknowledgement', async () => {
  const f = fixture(undefined, undefined, ownerRoom); f.welcome(); await settle();
  const { form, audience, confirmation } = audienceFields(f);
  audience.value = 'Public'; audience.emit('change'); confirmation.checked = true;
  form.emit('submit', { submitter: f.get('audience-save') }); await settle();
  const writes = audienceWrites(f);
  assert.equal(writes.length, 1);
  assert.equal(writes[0].path, '/api/rooms/lounge/read-audience');
  assert.deepEqual(writes[0].body, { audience: 'Public', publishExistingHistory: true });
});

test('Topic and account transitions clear unsaved publication choices and acknowledgement', async () => {
  const f = fixture(undefined, undefined, key => ({ ...ownerRoom, readAudience: key === 'workshop' ? 'Public' : 'Restricted' }));
  f.welcome(); await settle();
  const { audience, confirmation } = audienceFields(f);
  audience.value = 'Public'; audience.emit('change'); confirmation.checked = true;
  f.choose('workshop'); await settle();
  assert.equal(audience.value, 'Public');
  assert.equal(confirmation.checked, false);
  assert.equal(f.get('publish-history-confirmation').hidden, true);
  f.choose('lounge'); await settle();
  assert.equal(audience.value, 'Restricted');
  assert.equal(confirmation.checked, false);
  assert.equal(confirmation.disabled, true);
  audience.value = 'Public'; audience.emit('change'); confirmation.checked = true;
  f.welcome('did:plc:bob'); await settle();
  assert.equal(audience.value, 'Restricted');
  assert.equal(confirmation.checked, false);
  assert.equal(f.get('publish-history-confirmation').hidden, true);
});

test('saving lock and editing controls never submits a different form\'s unsaved description or title', async () => {
  const f = fixture(undefined, undefined, ownerRoom); f.welcome(); await settle();
  f.get('topic-form').elements.namedItem('topic').value = 'Description still being written';
  const form = f.get('topic-settings-form');
  form.elements.namedItem('allowPostEditing').checked = true;
  form.elements.namedItem('isLocked').checked = true;
  form.emit('submit', { submitter: f.get('topic-settings-save') }); await settle();
  const writes = f.requests.filter(request => request.path.endsWith('/settings') && request.method === 'PATCH');
  assert.equal(writes.length, 1);
  assert.deepEqual(writes[0].body, { allowPostEditing: true, isLocked: true });
});

test('a failed audience save retains the choice and acknowledgement for a deliberate retry', async () => {
  const f = fixture(request => request.path.endsWith('/read-audience')
    ? new Response('{"reason":"Reading audience could not be saved."}', { status: 503 })
    : { messages: [] }, undefined, ownerRoom);
  f.welcome(); await settle();
  const { form, audience, confirmation } = audienceFields(f);
  audience.value = 'Public'; audience.emit('change'); confirmation.checked = true;
  form.emit('submit', { submitter: f.get('audience-save') }); await settle();
  assert.equal(audienceWrites(f).length, 1);
  assert.equal(audience.value, 'Public');
  assert.equal(confirmation.checked, true);
  assert.equal(f.get('action-status').hidden, false);
  assert.match(f.get('action-status').textContent, /could not be saved/i);
});

test('an unsent draft survives a reload for the same participant and is cleared after acceptance', async () => {
  const storage = memoryStorage();
  const first = fixture(() => ({ messages: [] }), storage);
  first.welcome(); await settle(); first.type('A thought worth keeping');

  const second = fixture(request => request.method === 'POST' ? { state: 'accepted' } : { messages: [] }, storage);
  second.welcome(); await settle();
  assert.equal(second.get('message-text').value, 'A thought worth keeping');
  second.submit(); await settle();
  assert.equal(second.get('message-text').value, '');

  const third = fixture(() => ({ messages: [] }), storage);
  third.welcome(); await settle();
  assert.equal(third.get('message-text').value, '');
});

test('changing participants removes the previous account reload draft', async () => {
  const storage = memoryStorage(), f = fixture(() => ({ messages: [] }), storage);
  f.welcome(); await settle(); f.type('Alice private draft');
  f.welcome('did:plc:bob'); await settle();
  f.welcome('did:plc:alice'); await settle();
  assert.equal(f.get('message-text').value, '');
});

test('a post that fails in transit retries with the same operation and clears once accepted', async () => {
  let attempts = 0;
  const f = fixture(request => {
    if (request.method !== 'POST') return { messages: [] };
    if (++attempts === 1) throw new TypeError('Failed to fetch');
    return { state: 'accepted' };
  });
  f.welcome(); await settle(); f.type('A new message'); f.submit(); await settle();
  assert.equal(f.get('message-text').readOnly, true);
  assert.match(f.get('pending-message').textContent, /not been confirmed as sent/);
  f.submit(); await settle();
  const posts = f.requests.filter(request => request.method === 'POST');
  assert.equal(posts.length, 2); assert.deepEqual(posts[0].body, posts[1].body);
  assert.deepEqual(Object.keys(posts[1].body).sort(), ['operationId', 'text']);
  assert.equal(f.get('message-text').value, '');
  assert.equal(f.get('pending-message').hidden, true);
});

test('an identity_changed push re-runs the welcome flow and acknowledges the new identity', async () => {
  const encoder = new TextEncoder();
  let clicks = 0, actor = 'ref-did:plc:alice', pushes = 0;
  const f = fixture(request => {
    if (request.path.startsWith('/api/activity/events')) {
      // One pushed event, then the stream parks like a quiet live connection.
      return new Response(new ReadableStream({ start(controller) {
        if (++pushes === 1) controller.enqueue(encoder.encode('event: identity_changed\ndata: {"participantRef":"ref-did:plc:bob","bestLabel":"bob.example"}\n\n'));
      } }), { status: 200, headers: { 'Content-Type': 'text/event-stream' } });
    }
    if (/^\/api\/activity(\?|$)/.test(request.path)) return activitySnapshot(actor);
    return { messages: [] };
  });
  f.document.hidden = false;
  f.get('retry').addEventListener('click', () => { clicks++; });
  f.welcome(); await settle();
  assert.equal(clicks, 1, 'the page re-runs its own welcome flow');
  assert.equal(f.get('activity-status').textContent, 'Connecting…');
  assert.match(f.get('activity-status').title, /Updating/);
  assert.ok(f.requests.some(request => /^\/api\/activity(\?|$)/.test(request.path)
    && request.headers['X-Tangent-Participant'] === undefined), 'the stream authenticates by token only');
  actor = 'ref-did:plc:bob'; f.welcome('did:plc:bob'); await settle();
  assert.match(f.get('action-status').textContent, /Now viewing as bob\.example/);
});

test('an SSE failure falls back to participant polling without a second topic connection or automatic writes', async () => {
  const f = fixture(request => {
    if (request.path.startsWith('/api/activity/events')) return new Response('{}', { status: 500 });
    if (request.path.startsWith('/api/activity/wait')) return activitySnapshot();
    if (/^\/api\/activity(\?|$)/.test(request.path)) return activitySnapshot();
    return { messages: [] };
  });
  f.document.hidden = false;
  f.welcome(); await settle();
  const retry = f.timers.find(timer => timer.ms >= 750 && timer.ms <= 1300);
  assert.ok(retry, 'bounded jittered reconnect is scheduled');
  f.timers.splice(f.timers.indexOf(retry), 1); retry.fn(); await settle();
  assert.ok(f.requests.some(request => request.path.startsWith('/api/activity/wait?') && request.path.includes('cursor=c0')));
  assert.equal(f.get('activity-status').textContent, 'Polling');
  assert.equal(f.requests.some(request => request.path.includes('/updates')), false);
  assert.equal(f.requests.some(request => request.method === 'POST'), false);
  f.window.emit('pagehide'); await settle();
  assert.equal(f.timers.length, 0, 'pagehide releases transport timers');
});
